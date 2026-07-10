#pragma once

#include <functional>
#include <memory>
#include <string>
#include <string_view>

namespace gua::ws {

struct ActionCommand {
    std::string type;
    std::string node_id;
    std::string value;
    float delta_x = 0;
    float delta_y = 0;
    bool bool_value = false;
    std::string key;
    unsigned int modifiers = 0;
    bool sensitive = false;
    int scroll_unit = 0;
};

struct BridgeHandlers {
    std::function<std::string()> get_ui_tree_json;
    std::function<std::string()> get_logs_json;
    std::function<std::string()> get_screenshot_json;
    std::function<bool(std::string_view node_id)> click_node;
    std::function<bool(std::string_view node_id)> focus_node;
    std::function<bool(std::string_view key)> press_key;
    std::function<long long(const ActionCommand& command)> enqueue_action;
    std::function<std::string(unsigned long long request_id)> poll_action_event_json;
};

struct BridgeOptions {
    unsigned short port = 8765;
};

class BridgeServer {
public:
    BridgeServer(BridgeHandlers handlers, BridgeOptions options = {});
    BridgeServer(const BridgeServer&) = delete;
    BridgeServer& operator=(const BridgeServer&) = delete;
    BridgeServer(BridgeServer&&) noexcept;
    BridgeServer& operator=(BridgeServer&&) noexcept;
    ~BridgeServer();

    void start();
    void stop();
    void publish_snapshot();
    [[nodiscard]] bool running() const;
    [[nodiscard]] unsigned short port() const;

private:
    class Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace gua::ws
