#include "gua/runtime.h"

#include "gua/ws_bridge.hpp"

#include <memory>
#include <mutex>
#include <string>
#include <string_view>
#include <utility>

struct gua_runtime_t {
    gua_context_t* context = nullptr;
    mutable std::mutex context_mutex;
    mutable std::mutex bridge_mutex;
    std::unique_ptr<gua::ws::BridgeServer> bridge;
    int bridge_port = 0;
    std::string bridge_url;
    std::string ui_tree_json;
    std::string logs_json;
    std::string screenshot_json;
};

namespace {

bool valid_runtime(gua_runtime_t* runtime)
{
    return runtime != nullptr && runtime->context != nullptr;
}

std::string copy_ui_tree_json(gua_runtime_t* runtime)
{
    const std::lock_guard lock(runtime->context_mutex);
    return gua_get_ui_tree_json(runtime->context);
}

std::string copy_logs_json(gua_runtime_t* runtime)
{
    const std::lock_guard lock(runtime->context_mutex);
    return gua_get_logs_json(runtime->context);
}

std::string copy_screenshot_json(gua_runtime_t* runtime)
{
    const std::lock_guard lock(runtime->context_mutex);
    return gua_get_screenshot_json(runtime->context);
}

} // namespace

extern "C" gua_runtime_t* gua_runtime_create(void)
{
    auto runtime = std::make_unique<gua_runtime_t>();
    runtime->context = gua_create_context();
    if (runtime->context == nullptr) {
        return nullptr;
    }

    return runtime.release();
}

extern "C" void gua_runtime_destroy(gua_runtime_t* runtime)
{
    if (runtime == nullptr) {
        return;
    }

    gua_runtime_stop_inspector_bridge(runtime);
    {
        const std::lock_guard lock(runtime->context_mutex);
        gua_destroy_context(runtime->context);
        runtime->context = nullptr;
    }

    delete runtime;
}

extern "C" void gua_runtime_begin_frame(gua_runtime_t* runtime, const char* screen)
{
    if (!valid_runtime(runtime)) {
        return;
    }

    const std::lock_guard lock(runtime->context_mutex);
    gua_begin_frame(runtime->context, screen);
}

extern "C" void gua_runtime_end_frame(gua_runtime_t* runtime)
{
    if (!valid_runtime(runtime)) {
        return;
    }

    {
        const std::lock_guard lock(runtime->context_mutex);
        gua_end_frame(runtime->context);
    }

    gua_runtime_publish_inspector_snapshot(runtime);
}

extern "C" void gua_runtime_register_node(
    gua_runtime_t* runtime,
    const char* id,
    const char* role,
    const char* label,
    gua_bounds_t bounds,
    int visible,
    int enabled)
{
    if (!valid_runtime(runtime)) {
        return;
    }

    const std::lock_guard lock(runtime->context_mutex);
    gua_register_node(runtime->context, id, role, label, bounds, visible, enabled);
}

extern "C" const char* gua_runtime_get_ui_tree_json(gua_runtime_t* runtime)
{
    if (!valid_runtime(runtime)) {
        return "{}";
    }

    runtime->ui_tree_json = copy_ui_tree_json(runtime);
    return runtime->ui_tree_json.c_str();
}

extern "C" void gua_runtime_add_log(gua_runtime_t* runtime, int level, const char* message)
{
    if (!valid_runtime(runtime)) {
        return;
    }

    const std::lock_guard lock(runtime->context_mutex);
    gua_add_log(runtime->context, level, message);
}

extern "C" const char* gua_runtime_get_logs_json(gua_runtime_t* runtime)
{
    if (!valid_runtime(runtime)) {
        return "[]";
    }

    runtime->logs_json = copy_logs_json(runtime);
    return runtime->logs_json.c_str();
}

extern "C" void gua_runtime_set_screenshot(gua_runtime_t* runtime, const char* data_uri, int width, int height)
{
    if (!valid_runtime(runtime)) {
        return;
    }

    const std::lock_guard lock(runtime->context_mutex);
    gua_set_screenshot(runtime->context, data_uri, width, height);
}

extern "C" const char* gua_runtime_get_screenshot_json(gua_runtime_t* runtime)
{
    if (!valid_runtime(runtime)) {
        return "{\"dataUri\":\"\",\"width\":0,\"height\":0}";
    }

    runtime->screenshot_json = copy_screenshot_json(runtime);
    return runtime->screenshot_json.c_str();
}

extern "C" int gua_runtime_get_node_state(gua_runtime_t* runtime, const char* node_id, gua_node_state_t* out_state)
{
    if (!valid_runtime(runtime)) {
        return 0;
    }

    const std::lock_guard lock(runtime->context_mutex);
    return gua_get_node_state(runtime->context, node_id, out_state);
}

extern "C" int gua_runtime_find_node_by_id(gua_runtime_t* runtime, const char* node_id, char* out_node_id, int out_node_id_size)
{
    if (!valid_runtime(runtime)) {
        return 0;
    }

    const std::lock_guard lock(runtime->context_mutex);
    return gua_find_node_by_id(runtime->context, node_id, out_node_id, out_node_id_size);
}

extern "C" int gua_runtime_find_node_by_role(
    gua_runtime_t* runtime,
    const char* role,
    const char* name,
    char* out_node_id,
    int out_node_id_size)
{
    if (!valid_runtime(runtime)) {
        return 0;
    }

    const std::lock_guard lock(runtime->context_mutex);
    return gua_find_node_by_role(runtime->context, role, name, out_node_id, out_node_id_size);
}

extern "C" int gua_runtime_find_node_by_text(gua_runtime_t* runtime, const char* text, char* out_node_id, int out_node_id_size)
{
    if (!valid_runtime(runtime)) {
        return 0;
    }

    const std::lock_guard lock(runtime->context_mutex);
    return gua_find_node_by_text(runtime->context, text, out_node_id, out_node_id_size);
}

extern "C" int gua_runtime_enqueue_click(gua_runtime_t* runtime, const char* node_id)
{
    if (!valid_runtime(runtime)) {
        return 0;
    }

    const std::lock_guard lock(runtime->context_mutex);
    return gua_enqueue_click(runtime->context, node_id);
}

extern "C" int gua_runtime_poll_event(gua_runtime_t* runtime, gua_event_t* out_event)
{
    if (!valid_runtime(runtime)) {
        return 0;
    }

    const std::lock_guard lock(runtime->context_mutex);
    return gua_poll_event(runtime->context, out_event);
}

extern "C" int gua_runtime_start_inspector_bridge(gua_runtime_t* runtime, int port)
{
    if (!valid_runtime(runtime) || port <= 0 || port > 65535) {
        return 0;
    }

    const std::lock_guard bridge_lock(runtime->bridge_mutex);
    if (runtime->bridge != nullptr && runtime->bridge->running()) {
        return runtime->bridge->port() == static_cast<unsigned short>(port) ? 1 : 0;
    }

    gua::ws::BridgeHandlers handlers {
        .get_ui_tree_json = [runtime] {
            return copy_ui_tree_json(runtime);
        },
        .get_logs_json = [runtime] {
            return copy_logs_json(runtime);
        },
        .get_screenshot_json = [runtime] {
            return copy_screenshot_json(runtime);
        },
        .click_node = [runtime](std::string_view node_id) {
            const std::string id(node_id);
            const std::lock_guard lock(runtime->context_mutex);
            return gua_enqueue_click(runtime->context, id.c_str()) != 0;
        },
        .focus_node = [runtime](std::string_view node_id) {
            const std::string id(node_id);
            char found[128] {};
            const std::lock_guard lock(runtime->context_mutex);
            if (gua_find_node_by_id(runtime->context, id.c_str(), found, static_cast<int>(sizeof(found))) == 0) {
                return false;
            }
            const std::string message = "focus_node(" + id + ")";
            gua_add_log(runtime->context, GUA_LOG_DEBUG, message.c_str());
            return true;
        },
        .press_key = [runtime](std::string_view key) {
            const std::string key_string(key);
            const std::lock_guard lock(runtime->context_mutex);
            const std::string message = "press_key(" + key_string + ")";
            gua_add_log(runtime->context, GUA_LOG_INFO, message.c_str());
            return !key.empty();
        },
    };

    auto bridge = std::make_unique<gua::ws::BridgeServer>(
        std::move(handlers),
        gua::ws::BridgeOptions { .port = static_cast<unsigned short>(port) });

    bridge->start();
    if (!bridge->running()) {
        const std::lock_guard lock(runtime->context_mutex);
        gua_add_log(runtime->context, GUA_LOG_ERROR, "Inspector bridge failed to listen.");
        return 0;
    }

    runtime->bridge_port = port;
    runtime->bridge_url = "ws://127.0.0.1:" + std::to_string(port);
    runtime->bridge = std::move(bridge);

    {
        const std::lock_guard lock(runtime->context_mutex);
        const std::string message = "Inspector bridge listening on " + runtime->bridge_url;
        gua_add_log(runtime->context, GUA_LOG_INFO, message.c_str());
    }

    return 1;
}

extern "C" void gua_runtime_stop_inspector_bridge(gua_runtime_t* runtime)
{
    if (runtime == nullptr) {
        return;
    }

    std::unique_ptr<gua::ws::BridgeServer> bridge;
    {
        const std::lock_guard bridge_lock(runtime->bridge_mutex);
        bridge = std::move(runtime->bridge);
        runtime->bridge_port = 0;
        runtime->bridge_url.clear();
    }

    if (bridge != nullptr) {
        bridge->stop();
    }
}

extern "C" int gua_runtime_inspector_bridge_running(gua_runtime_t* runtime)
{
    if (runtime == nullptr) {
        return 0;
    }

    const std::lock_guard bridge_lock(runtime->bridge_mutex);
    return runtime->bridge != nullptr && runtime->bridge->running() ? 1 : 0;
}

extern "C" const char* gua_runtime_inspector_bridge_url(gua_runtime_t* runtime)
{
    if (runtime == nullptr) {
        return "";
    }

    const std::lock_guard bridge_lock(runtime->bridge_mutex);
    return runtime->bridge_url.c_str();
}

extern "C" void gua_runtime_publish_inspector_snapshot(gua_runtime_t* runtime)
{
    if (runtime == nullptr) {
        return;
    }

    const std::lock_guard bridge_lock(runtime->bridge_mutex);
    if (runtime->bridge != nullptr && runtime->bridge->running()) {
        runtime->bridge->publish_snapshot();
    }
}
