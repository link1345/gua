#include "gua/runtime.h"

#include "gua/ws_bridge.hpp"

#include <cstdio>
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

int copy_json_string(const std::string& json, char* out_json, int out_json_size)
{
    const int required_size = static_cast<int>(json.size() + 1U);
    if (out_json != nullptr && out_json_size > 0) {
        std::snprintf(out_json, static_cast<std::size_t>(out_json_size), "%s", json.c_str());
    }
    return required_size;
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

extern "C" int gua_runtime_register_node_v2(gua_runtime_t* runtime, const gua_node_descriptor_v2_t* descriptor)
{
    if (!valid_runtime(runtime)) {
        return 0;
    }

    const std::lock_guard lock(runtime->context_mutex);
    return gua_register_node_v2(runtime->context, descriptor);
}

extern "C" const char* gua_runtime_get_ui_tree_json(gua_runtime_t* runtime)
{
    if (!valid_runtime(runtime)) {
        return "{}";
    }

    runtime->ui_tree_json = copy_ui_tree_json(runtime);
    return runtime->ui_tree_json.c_str();
}

extern "C" int gua_runtime_copy_ui_tree_json(gua_runtime_t* runtime, char* out_json, int out_json_size)
{
    if (!valid_runtime(runtime)) {
        return copy_json_string("{}", out_json, out_json_size);
    }

    return copy_json_string(copy_ui_tree_json(runtime), out_json, out_json_size);
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

extern "C" int gua_runtime_copy_logs_json(gua_runtime_t* runtime, char* out_json, int out_json_size)
{
    if (!valid_runtime(runtime)) {
        return copy_json_string("[]", out_json, out_json_size);
    }

    return copy_json_string(copy_logs_json(runtime), out_json, out_json_size);
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

extern "C" int gua_runtime_copy_screenshot_json(gua_runtime_t* runtime, char* out_json, int out_json_size)
{
    if (!valid_runtime(runtime)) {
        return copy_json_string("{\"dataUri\":\"\",\"width\":0,\"height\":0}", out_json, out_json_size);
    }

    return copy_json_string(copy_screenshot_json(runtime), out_json, out_json_size);
}

extern "C" int gua_runtime_get_node_state(gua_runtime_t* runtime, const char* node_id, gua_node_state_t* out_state)
{
    if (!valid_runtime(runtime)) {
        return 0;
    }

    const std::lock_guard lock(runtime->context_mutex);
    return gua_get_node_state(runtime->context, node_id, out_state);
}

extern "C" int gua_runtime_get_node_state_v2(gua_runtime_t* runtime, const char* node_id, gua_node_state_v2_t* out_state)
{
    if (!valid_runtime(runtime)) {
        return 0;
    }

    const std::lock_guard lock(runtime->context_mutex);
    return gua_get_node_state_v2(runtime->context, node_id, out_state);
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

extern "C" int gua_runtime_query_nodes_json(gua_runtime_t* runtime, const gua_selector_v1_t* selector, char* out_json, int out_json_size)
{
    if (!valid_runtime(runtime)) return 0;
    const std::lock_guard lock(runtime->context_mutex);
    return gua_query_nodes_json(runtime->context, selector, out_json, out_json_size);
}

extern "C" int gua_runtime_enqueue_click(gua_runtime_t* runtime, const char* node_id)
{
    if (!valid_runtime(runtime)) {
        return 0;
    }

    const std::lock_guard lock(runtime->context_mutex);
    return gua_enqueue_click(runtime->context, node_id);
}

extern "C" int gua_runtime_consume_click_request(gua_runtime_t* runtime, const char* node_id)
{
    if (!valid_runtime(runtime)) {
        return 0;
    }

    const std::lock_guard lock(runtime->context_mutex);
    return gua_consume_click_request(runtime->context, node_id);
}

extern "C" int gua_runtime_emit_click(gua_runtime_t* runtime, const char* node_id)
{
    if (!valid_runtime(runtime)) {
        return 0;
    }

    const std::lock_guard lock(runtime->context_mutex);
    return gua_emit_click(runtime->context, node_id);
}

extern "C" int gua_runtime_poll_event(gua_runtime_t* runtime, gua_event_t* out_event)
{
    if (!valid_runtime(runtime)) {
        return 0;
    }

    const std::lock_guard lock(runtime->context_mutex);
    return gua_poll_event(runtime->context, out_event);
}

std::string escape_json(std::string_view value)
{
    std::string escaped;
    for (char ch : value) {
        if (ch == '\\' || ch == '"') escaped.push_back('\\');
        escaped.push_back(ch);
    }
    return escaped;
}

extern "C" int gua_runtime_enqueue_action(gua_runtime_t* runtime, const gua_action_request_descriptor_t* descriptor, uint64_t* out_request_id)
{
    if (!valid_runtime(runtime)) return GUA_ACTION_ERROR_INVALID_ARGUMENT;
    const std::lock_guard lock(runtime->context_mutex);
    return gua_enqueue_action(runtime->context, descriptor, out_request_id);
}

extern "C" int gua_runtime_consume_action_request(gua_runtime_t* runtime, int action, const char* node_id, gua_action_request_t* out_request)
{
    if (!valid_runtime(runtime)) return 0;
    const std::lock_guard lock(runtime->context_mutex);
    return gua_consume_action_request(runtime->context, action, node_id, out_request);
}

extern "C" int gua_runtime_emit_action_result(gua_runtime_t* runtime, const gua_action_result_t* result)
{
    if (!valid_runtime(runtime)) return 0;
    const std::lock_guard lock(runtime->context_mutex);
    return gua_emit_action_result(runtime->context, result);
}

extern "C" int gua_runtime_poll_event_v2(gua_runtime_t* runtime, gua_event_v2_t* out_event)
{
    if (!valid_runtime(runtime)) return 0;
    const std::lock_guard lock(runtime->context_mutex);
    return gua_poll_event_v2(runtime->context, out_event);
}

extern "C" int gua_runtime_poll_event_v2_for_request(gua_runtime_t* runtime, uint64_t request_id, gua_event_v2_t* out_event)
{
    if (!valid_runtime(runtime)) return 0;
    const std::lock_guard lock(runtime->context_mutex);
    return gua_poll_event_v2_for_request(runtime->context, request_id, out_event);
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
        .query_nodes_json = [runtime](const gua::ws::QuerySelector& selector) {
            gua_selector_v1_t native {
                sizeof(gua_selector_v1_t),
                selector.id.empty() ? nullptr : selector.id.c_str(), selector.id_match,
                selector.role.empty() ? nullptr : selector.role.c_str(), selector.role_match,
                selector.name.empty() ? nullptr : selector.name.c_str(), selector.name_match,
                selector.text.empty() ? nullptr : selector.text.c_str(), selector.text_match,
                selector.parent_id.empty() ? nullptr : selector.parent_id.c_str(),
                selector.direct_child ? 1 : 0, selector.visible, selector.enabled,
            };
            const std::lock_guard lock(runtime->context_mutex);
            const int size = gua_query_nodes_json(runtime->context, &native, nullptr, 0);
            std::string json(static_cast<std::size_t>(size), '\0');
            gua_query_nodes_json(runtime->context, &native, json.data(), size);
            json.resize(static_cast<std::size_t>(size - 1));
            return json;
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
        .enqueue_action = [runtime](const gua::ws::ActionCommand& command) -> long long {
            int action = 0;
            if (command.type == "click_node") action = GUA_ACTION_CLICK;
            else if (command.type == "focus_node") action = GUA_ACTION_FOCUS;
            else if (command.type == "set_value") action = GUA_ACTION_SET_VALUE;
            else if (command.type == "set_checked") action = GUA_ACTION_SET_CHECKED;
            else if (command.type == "select") action = GUA_ACTION_SELECT;
            else if (command.type == "scroll") action = GUA_ACTION_SCROLL;
            else if (command.type == "press_key") action = GUA_ACTION_PRESS_KEY;
            const gua_action_request_descriptor_t descriptor {
                sizeof(gua_action_request_descriptor_t), action,
                command.node_id.empty() ? nullptr : command.node_id.c_str(),
                command.value.empty() ? nullptr : command.value.c_str(),
                command.delta_x, command.delta_y, command.bool_value ? 1 : 0,
                command.key.empty() ? nullptr : command.key.c_str(), command.modifiers,
                command.sensitive ? 1 : 0, command.scroll_unit
            };
            std::uint64_t request_id = 0;
            const std::lock_guard lock(runtime->context_mutex);
            const int result = gua_enqueue_action(runtime->context, &descriptor, &request_id);
            return result == GUA_ACTION_ACCEPTED ? static_cast<long long>(request_id) : static_cast<long long>(result);
        },
        .poll_action_event_json = [runtime](unsigned long long request_id) {
            gua_event_v2_t event { sizeof(gua_event_v2_t) };
            const std::lock_guard lock(runtime->context_mutex);
            const int found = request_id == 0
                ? gua_poll_event_v2(runtime->context, &event)
                : gua_poll_event_v2_for_request(runtime->context, request_id, &event);
            if (found == 0) return std::string("null");
            return std::string("{\"requestId\":") + std::to_string(event.request_id) +
                ",\"action\":" + std::to_string(event.action) +
                ",\"succeeded\":" + (event.status == GUA_ACTION_STATUS_SUCCEEDED ? "true" : "false") +
                ",\"error\":" + std::to_string(event.error_code) +
                ",\"nodeId\":\"" + escape_json(event.node_id) + "\"" +
                ",\"value\":\"" + escape_json(event.value) + "\"" +
                ",\"sensitive\":" + (event.sensitive != 0 ? "true" : "false") + "}";
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
