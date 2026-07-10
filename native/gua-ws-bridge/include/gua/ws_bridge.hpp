#pragma once

#include <functional>
#include <memory>
#include <string>
#include <string_view>

namespace gua::ws {

struct QuerySelector {
    std::string id;
    int id_match = 0;
    std::string role;
    int role_match = 0;
    std::string name;
    int name_match = 0;
    std::string text;
    int text_match = 0;
    std::string parent_id;
    bool direct_child = false;
    int visible = 0;
    int enabled = 0;
};

struct BridgeHandlers {
    std::function<std::string()> get_ui_tree_json;
    std::function<std::string()> get_logs_json;
    std::function<std::string()> get_screenshot_json;
    std::function<std::string(const QuerySelector& selector)> query_nodes_json;
    std::function<bool(std::string_view node_id)> click_node;
    std::function<bool(std::string_view node_id)> focus_node;
    std::function<bool(std::string_view key)> press_key;
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
