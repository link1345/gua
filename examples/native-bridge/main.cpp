#include "gua/gua.hpp"
#include "gua/ws_bridge.hpp"

#include <chrono>
#include <csignal>
#include <cstdlib>
#include <iostream>
#include <mutex>
#include <sstream>
#include <string>
#include <string_view>
#include <thread>

namespace {

constexpr unsigned short default_port = 8765;
volatile std::sig_atomic_t stop_requested = 0;

void request_stop(int) { stop_requested = 1; }

std::string percent_encode(std::string_view value)
{
    constexpr char hex[] = "0123456789ABCDEF";
    std::string out;
    out.reserve(value.size());
    for (const unsigned char ch : value) {
        if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')
            || ch == '-' || ch == '_' || ch == '.' || ch == '~') {
            out.push_back(static_cast<char>(ch));
        } else {
            out.push_back('%');
            out.push_back(hex[(ch >> 4U) & 0x0fU]);
            out.push_back(hex[ch & 0x0fU]);
        }
    }
    return out;
}

class DemoRuntime {
public:
    DemoRuntime()
    {
        context_.log(gua::LogLevel::info, "Native demo runtime started.");
        context_.log(gua::LogLevel::debug, "Serving Gua core snapshots over WebSocket.");
        render_frame_unlocked();
    }

    [[nodiscard]] std::string ui_tree_json()
    {
        const std::lock_guard lock(mutex_);
        render_frame_unlocked();
        return context_.ui_tree_json();
    }

    [[nodiscard]] std::string logs_json()
    {
        const std::lock_guard lock(mutex_);
        return context_.logs_json();
    }

    [[nodiscard]] std::string screenshot_json()
    {
        const std::lock_guard lock(mutex_);
        render_frame_unlocked();
        return context_.screenshot_json();
    }

    [[nodiscard]] bool click_node(std::string_view node_id)
    {
        const std::lock_guard lock(mutex_);
        render_frame_unlocked();
        if (!context_.enqueue_click(node_id) || !context_.consume_click_request(node_id) || !context_.emit_click(node_id)) return false;

        context_.log(gua::LogLevel::info, "click_node(" + std::string(node_id) + ")");
        gua::Event event;
        while (context_.poll_event(event)) {
            if (event.type == gua::EventType::click && event.node_id == "start") {
                loading_ = true;
                context_.log(gua::LogLevel::info, "Screen changed to loading.");
            }
        }
        render_frame_unlocked();
        return true;
    }

    [[nodiscard]] bool focus_node(std::string_view node_id)
    {
        const std::lock_guard lock(mutex_);
        render_frame_unlocked();
        char found[128] {};
        const std::string id(node_id);
        if (gua_find_node_by_id(context_.native_handle(), id.c_str(), found, static_cast<int>(sizeof(found))) == 0) return false;
        focused_node_ = id;
        context_.log(gua::LogLevel::debug, "focus_node(" + id + ")");
        render_frame_unlocked();
        return true;
    }

    [[nodiscard]] bool press_key(std::string_view key)
    {
        const std::lock_guard lock(mutex_);
        if (key.empty()) return false;
        context_.log(gua::LogLevel::info, "press_key(" + std::string(key) + ")");
        return true;
    }

private:
    void render_frame_unlocked()
    {
        context_.begin_frame(loading_ ? "loading" : "title");
        if (loading_) {
            context_.node("root", "screen", "Loading Screen", { 0.0F, 0.0F, 1280.0F, 720.0F }, true, false);
            context_.text("loading", "Loading...", { 544.0F, 328.0F, 192.0F, 48.0F }, true);
        } else {
            context_.node("root", "screen", "Title Screen", { 0.0F, 0.0F, 1280.0F, 720.0F }, true, false);
            context_.panel("menu", "Main Menu", { 448.0F, 232.0F, 384.0F, 256.0F }, true);
            context_.button("start", "Start Game", { 512.0F, 312.0F, 256.0F, 56.0F }, true, true);
            context_.button("settings", "Settings", { 512.0F, 384.0F, 256.0F, 56.0F }, true, true);
        }
        context_.set_screenshot("data:image/svg+xml," + percent_encode(render_screenshot_svg_unlocked()), 1280, 720);
        context_.end_frame();
    }

    [[nodiscard]] std::string render_screenshot_svg_unlocked() const
    {
        const std::string title = loading_ ? "Loading..." : "Gua Native Runtime";
        const std::string subtitle = loading_ ? "Start command was received by the C++ bridge." : "Connected through gua-native-bridge-example.";
        std::ostringstream svg;
        svg << "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1280\" height=\"720\" viewBox=\"0 0 1280 720\">"
            << "<rect width=\"1280\" height=\"720\" fill=\"#101820\"/>"
            << "<rect x=\"448\" y=\"232\" width=\"384\" height=\"256\" fill=\"#1f2937\" stroke=\"#4b647f\" stroke-width=\"2\"/>"
            << "<text x=\"640\" y=\"284\" fill=\"#e8edf4\" font-family=\"Segoe UI, sans-serif\" font-size=\"34\" text-anchor=\"middle\">" << title << "</text>"
            << "<text x=\"640\" y=\"520\" fill=\"#91a4b7\" font-family=\"Segoe UI, sans-serif\" font-size=\"20\" text-anchor=\"middle\">" << subtitle << "</text>";
        if (!loading_) {
            render_button(svg, "Start Game", 512, 312, focused_node_ == "start");
            render_button(svg, "Settings", 512, 384, focused_node_ == "settings");
        }
        svg << "</svg>";
        return svg.str();
    }

    static void render_button(std::ostringstream& svg, std::string_view label, int x, int y, bool focused)
    {
        svg << "<rect x=\"" << x << "\" y=\"" << y << "\" width=\"256\" height=\"56\" fill=\"#253448\" stroke=\""
            << (focused ? "#f2c66d" : "#5d7288") << "\" stroke-width=\"2\"/>"
            << "<text x=\"" << (x + 128) << "\" y=\"" << (y + 36)
            << "\" fill=\"#f5f7fb\" font-family=\"Segoe UI, sans-serif\" font-size=\"22\" text-anchor=\"middle\">" << label << "</text>";
    }

    std::mutex mutex_;
    gua::Context context_;
    bool loading_ = false;
    std::string focused_node_ = "start";
};

} // namespace

int main(int argc, char** argv)
{
    const unsigned short port = argc > 1 ? static_cast<unsigned short>(std::stoi(argv[1])) : default_port;
    std::signal(SIGINT, request_stop);
    std::signal(SIGTERM, request_stop);

    try {
        DemoRuntime runtime;
        gua::ws::BridgeServer bridge(
            gua::ws::BridgeHandlers {
                .get_ui_tree_json = [&] { return runtime.ui_tree_json(); },
                .get_logs_json = [&] { return runtime.logs_json(); },
                .get_screenshot_json = [&] { return runtime.screenshot_json(); },
                .click_node = [&](std::string_view id) { return runtime.click_node(id); },
                .focus_node = [&](std::string_view id) { return runtime.focus_node(id); },
                .press_key = [&](std::string_view key) { return runtime.press_key(key); },
            },
            gua::ws::BridgeOptions { .port = port });
        bridge.start();
        if (!bridge.running()) return EXIT_FAILURE;
        while (stop_requested == 0) std::this_thread::sleep_for(std::chrono::milliseconds(100));
        bridge.stop();
        return EXIT_SUCCESS;
    } catch (const std::exception& error) {
        std::cerr << "Bridge failed: " << error.what() << std::endl;
        return EXIT_FAILURE;
    }
}
