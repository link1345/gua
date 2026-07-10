#include "gua/gua.h"

#include <algorithm>
#include <cstdio>
#include <deque>
#include <cstring>
#include <cmath>
#include <memory>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

namespace {

struct Node {
    std::string id;
    std::string role;
    std::string label;
    gua_bounds_t bounds;
    bool visible;
    bool enabled;
    unsigned long long known_mask = 0;
    std::string parent_id;
    std::string text;
    std::string value;
    bool focused = false;
    bool hovered = false;
    bool pressed = false;
    bool checked = false;
    bool selected = false;
};

struct Event {
    int action;
    std::string node_id;
    unsigned long long request_id = 0;
    int status = GUA_ACTION_STATUS_SUCCEEDED;
    int error_code = 0;
    std::string value;
    bool sensitive = false;
};

struct ActionRequest {
    unsigned long long request_id;
    int action;
    std::string node_id;
    std::string value;
    float delta_x;
    float delta_y;
    int bool_value;
    std::string key;
    unsigned int modifiers;
    bool sensitive;
    int scroll_unit;
};

struct LogEntry {
    int level;
    std::string message;
    unsigned long long sequence;
};

struct Screenshot {
    std::string data_uri;
    int width = 0;
    int height = 0;
};

const char* log_level_name(int level)
{
    switch (level) {
    case GUA_LOG_TRACE:
        return "trace";
    case GUA_LOG_DEBUG:
        return "debug";
    case GUA_LOG_INFO:
        return "info";
    case GUA_LOG_WARN:
        return "warn";
    case GUA_LOG_ERROR:
        return "error";
    default:
        return "info";
    }
}

std::string escape_json(const std::string& value)
{
    std::string out;
    out.reserve(value.size());
    for (const char ch : value) {
        switch (ch) {
        case '"':
            out += "\\\"";
            break;
        case '\\':
            out += "\\\\";
            break;
        case '\n':
            out += "\\n";
            break;
        case '\r':
            out += "\\r";
            break;
        case '\t':
            out += "\\t";
            break;
        default:
            out += ch;
            break;
        }
    }
    return out;
}

int write_node_id(const std::string& node_id, char* out_node_id, int out_node_id_size)
{
    if (out_node_id == nullptr || out_node_id_size <= 0) {
        return 0;
    }

    std::snprintf(out_node_id, static_cast<std::size_t>(out_node_id_size), "%s", node_id.c_str());
    return 1;
}

bool supports_action(const Node& node, int action)
{
    if (action == GUA_ACTION_PRESS_KEY) {
        return node.role == "textbox";
    }
    if (action == GUA_ACTION_FOCUS) {
        return node.role == "button" || node.role == "checkbox" || node.role == "radio" || node.role == "tab" ||
            node.role == "textbox" || node.role == "slider" || node.role == "combobox" || node.role == "list";
    }
    if (action == GUA_ACTION_CLICK) {
        return node.role == "button" || node.role == "checkbox" || node.role == "radio" || node.role == "tab";
    }
    if (action == GUA_ACTION_SET_VALUE) {
        return node.role == "textbox" || node.role == "slider";
    }
    if (action == GUA_ACTION_SET_CHECKED) {
        return node.role == "checkbox" || node.role == "radio";
    }
    if (action == GUA_ACTION_SELECT) {
        return node.role == "combobox" || node.role == "list" || node.role == "listitem" || node.role == "tablist" || node.role == "tab";
    }
    if (action == GUA_ACTION_SCROLL) {
        return node.role == "list" || node.role == "scrollarea";
    }
    return false;
}

const char* action_name(int action)
{
    switch (action) {
    case GUA_ACTION_CLICK: return "click";
    case GUA_ACTION_FOCUS: return "focus";
    case GUA_ACTION_SET_VALUE: return "set_value";
    case GUA_ACTION_SET_CHECKED: return "set_checked";
    case GUA_ACTION_SELECT: return "select";
    case GUA_ACTION_SCROLL: return "scroll";
    case GUA_ACTION_PRESS_KEY: return "press_key";
    default: return "";
    }
}

} // namespace

struct gua_context_t {
    mutable std::mutex mutex;
    std::string screen = "unknown";
    std::vector<Node> nodes;
    std::deque<ActionRequest> action_requests;
    std::deque<ActionRequest> consumed_requests;
    std::deque<Event> events;
    std::vector<LogEntry> logs;
    Screenshot screenshot;
    unsigned long long next_log_sequence = 1;
    std::string json_cache;
    std::string logs_json_cache;
    std::string screenshot_json_cache;
    unsigned long long frame_sequence = 0;
    unsigned long long revision = 0;
    unsigned long long next_request_id = 1;
    std::string previous_semantic_snapshot;
};

namespace {

int copy_json_string(const std::string& json, char* out_json, int out_json_size)
{
    const int required_size = static_cast<int>(json.size() + 1U);
    if (out_json != nullptr && out_json_size > 0) {
        std::snprintf(out_json, static_cast<std::size_t>(out_json_size), "%s", json.c_str());
    }
    return required_size;
}

std::string build_semantic_snapshot_json(const gua_context_t& ctx)
{
    std::string json = "{\"screen\":\"" + escape_json(ctx.screen) + "\",\"nodes\":[";

    for (std::size_t i = 0; i < ctx.nodes.size(); ++i) {
        const Node& node = ctx.nodes[i];
        if (i > 0) {
            json += ",";
        }

        char bounds[160];
        std::snprintf(
            bounds,
            sizeof(bounds),
            "{\"x\":%.3f,\"y\":%.3f,\"w\":%.3f,\"h\":%.3f}",
            node.bounds.x,
            node.bounds.y,
            node.bounds.w,
            node.bounds.h);

        json += "{\"id\":\"" + escape_json(node.id) + "\"";
        if ((node.known_mask & GUA_NODE_KNOWN_PARENT_ID) != 0U) {
            json += ",\"parentId\":\"" + escape_json(node.parent_id) + "\"";
        }
        json += ",\"role\":\"" + escape_json(node.role) + "\"";
        json += ",\"label\":\"" + escape_json(node.label) + "\"";
        if ((node.known_mask & GUA_NODE_KNOWN_TEXT) != 0U) {
            json += ",\"text\":\"" + escape_json(node.text) + "\"";
        }
        if ((node.known_mask & GUA_NODE_KNOWN_VALUE) != 0U) {
            json += ",\"value\":\"" + escape_json(node.value) + "\"";
        }
        json += ",\"visible\":";
        json += node.visible ? "true" : "false";
        json += ",\"enabled\":";
        json += node.enabled ? "true" : "false";
        json += ",\"bounds\":";
        json += bounds;
        const unsigned long long boolean_state_mask =
            GUA_NODE_KNOWN_FOCUSED | GUA_NODE_KNOWN_HOVERED | GUA_NODE_KNOWN_PRESSED |
            GUA_NODE_KNOWN_CHECKED | GUA_NODE_KNOWN_SELECTED;
        if ((node.known_mask & boolean_state_mask) != 0U) {
            json += ",\"state\":{";
            bool wrote_state = false;
            const auto append_state = [&](const char* name, bool value) {
                if (wrote_state) {
                    json += ",";
                }
                json += "\"";
                json += name;
                json += "\":";
                json += value ? "true" : "false";
                wrote_state = true;
            };
            if ((node.known_mask & GUA_NODE_KNOWN_FOCUSED) != 0U) append_state("focused", node.focused);
            if ((node.known_mask & GUA_NODE_KNOWN_HOVERED) != 0U) append_state("hovered", node.hovered);
            if ((node.known_mask & GUA_NODE_KNOWN_PRESSED) != 0U) append_state("pressed", node.pressed);
            if ((node.known_mask & GUA_NODE_KNOWN_CHECKED) != 0U) append_state("checked", node.checked);
            if ((node.known_mask & GUA_NODE_KNOWN_SELECTED) != 0U) append_state("selected", node.selected);
            json += "}";
        }
        json += ",\"actions\":[";
        bool wrote_action = false;
        for (int action = GUA_ACTION_CLICK; action <= GUA_ACTION_PRESS_KEY; ++action) {
            if (!node.enabled || !supports_action(node, action)) continue;
            if (wrote_action) json += ",";
            json += "\"" + std::string(action_name(action)) + "\"";
            wrote_action = true;
        }
        json += "]}";
    }

    json += "]}";
    return json;
}

std::string build_ui_tree_json(const gua_context_t& ctx)
{
    std::string semantic = build_semantic_snapshot_json(ctx);
    semantic.erase(semantic.begin());
    return "{\"schemaVersion\":2,\"frameSequence\":" + std::to_string(ctx.frame_sequence) +
        ",\"revision\":" + std::to_string(ctx.revision) + "," + semantic;
}

std::string build_logs_json(const gua_context_t& ctx)
{
    std::string json = "[";
    for (std::size_t i = 0; i < ctx.logs.size(); ++i) {
        const LogEntry& entry = ctx.logs[i];
        if (i > 0) {
            json += ",";
        }

        json += "{\"sequence\":";
        json += std::to_string(entry.sequence);
        json += ",\"level\":\"";
        json += log_level_name(entry.level);
        json += "\",\"message\":\"";
        json += escape_json(entry.message);
        json += "\"}";
    }
    json += "]";
    return json;
}

std::string build_screenshot_json(const gua_context_t& ctx)
{
    std::string json = "{\"dataUri\":\"";
    json += escape_json(ctx.screenshot.data_uri);
    json += "\",\"width\":";
    json += std::to_string(ctx.screenshot.width);
    json += ",\"height\":";
    json += std::to_string(ctx.screenshot.height);
    json += "}";
    return json;
}

} // namespace

extern "C" gua_context_t* gua_create_context(void)
{
    return new gua_context_t();
}

extern "C" void gua_destroy_context(gua_context_t* ctx)
{
    delete ctx;
}

extern "C" void gua_begin_frame(gua_context_t* ctx, const char* screen)
{
    if (ctx == nullptr) {
        return;
    }

    const std::lock_guard lock(ctx->mutex);
    ctx->screen = screen != nullptr ? screen : "unknown";
    ctx->nodes.clear();
}

extern "C" void gua_end_frame(gua_context_t* ctx)
{
    if (ctx == nullptr) {
        return;
    }

    const std::lock_guard lock(ctx->mutex);
    const std::string semantic_snapshot = build_semantic_snapshot_json(*ctx);
    ++ctx->frame_sequence;
    if (semantic_snapshot != ctx->previous_semantic_snapshot) {
        ++ctx->revision;
        ctx->previous_semantic_snapshot = semantic_snapshot;
    }
    ctx->json_cache.clear();
}

extern "C" void gua_register_node(
    gua_context_t* ctx,
    const char* id,
    const char* role,
    const char* label,
    gua_bounds_t bounds,
    int visible,
    int enabled)
{
    if (ctx == nullptr || id == nullptr || role == nullptr) {
        return;
    }

    const std::lock_guard lock(ctx->mutex);
    ctx->nodes.push_back(Node {
        id,
        role,
        label != nullptr ? label : "",
        bounds,
        visible != 0,
        enabled != 0,
    });
}

extern "C" int gua_register_node_v2(gua_context_t* ctx, const gua_node_descriptor_v2_t* descriptor)
{
    if (ctx == nullptr || descriptor == nullptr || descriptor->struct_size < sizeof(gua_node_descriptor_v2_t) ||
        descriptor->id == nullptr || descriptor->role == nullptr) {
        return 0;
    }

    const std::lock_guard lock(ctx->mutex);
    ctx->nodes.push_back(Node {
        descriptor->id,
        descriptor->role,
        descriptor->label != nullptr ? descriptor->label : "",
        descriptor->bounds,
        descriptor->visible != 0,
        descriptor->enabled != 0,
        descriptor->known_mask,
        descriptor->parent_id != nullptr ? descriptor->parent_id : "",
        descriptor->text != nullptr ? descriptor->text : "",
        descriptor->value != nullptr ? descriptor->value : "",
        descriptor->focused != 0,
        descriptor->hovered != 0,
        descriptor->pressed != 0,
        descriptor->checked != 0,
        descriptor->selected != 0,
    });
    return 1;
}

extern "C" const char* gua_get_ui_tree_json(gua_context_t* ctx)
{
    if (ctx == nullptr) {
        return "{}";
    }

    const std::lock_guard lock(ctx->mutex);
    ctx->json_cache = build_ui_tree_json(*ctx);
    return ctx->json_cache.c_str();
}

extern "C" int gua_copy_ui_tree_json(gua_context_t* ctx, char* out_json, int out_json_size)
{
    if (ctx == nullptr) {
        return copy_json_string("{}", out_json, out_json_size);
    }

    const std::lock_guard lock(ctx->mutex);
    return copy_json_string(build_ui_tree_json(*ctx), out_json, out_json_size);
}

extern "C" void gua_add_log(gua_context_t* ctx, int level, const char* message)
{
    if (ctx == nullptr || message == nullptr) {
        return;
    }

    const std::lock_guard lock(ctx->mutex);
    ctx->logs.push_back(LogEntry {
        level,
        message,
        ctx->next_log_sequence++,
    });
    ctx->logs_json_cache.clear();
}

extern "C" const char* gua_get_logs_json(gua_context_t* ctx)
{
    if (ctx == nullptr) {
        return "[]";
    }

    const std::lock_guard lock(ctx->mutex);
    ctx->logs_json_cache = build_logs_json(*ctx);
    return ctx->logs_json_cache.c_str();
}

extern "C" int gua_copy_logs_json(gua_context_t* ctx, char* out_json, int out_json_size)
{
    if (ctx == nullptr) {
        return copy_json_string("[]", out_json, out_json_size);
    }

    const std::lock_guard lock(ctx->mutex);
    return copy_json_string(build_logs_json(*ctx), out_json, out_json_size);
}

extern "C" void gua_set_screenshot(gua_context_t* ctx, const char* data_uri, int width, int height)
{
    if (ctx == nullptr) {
        return;
    }

    const std::lock_guard lock(ctx->mutex);
    ctx->screenshot.data_uri = data_uri != nullptr ? data_uri : "";
    ctx->screenshot.width = std::max(0, width);
    ctx->screenshot.height = std::max(0, height);
    ctx->screenshot_json_cache.clear();
}

extern "C" const char* gua_get_screenshot_json(gua_context_t* ctx)
{
    if (ctx == nullptr) {
        return "{\"dataUri\":\"\",\"width\":0,\"height\":0}";
    }

    const std::lock_guard lock(ctx->mutex);
    ctx->screenshot_json_cache = build_screenshot_json(*ctx);
    return ctx->screenshot_json_cache.c_str();
}

extern "C" int gua_copy_screenshot_json(gua_context_t* ctx, char* out_json, int out_json_size)
{
    if (ctx == nullptr) {
        return copy_json_string("{\"dataUri\":\"\",\"width\":0,\"height\":0}", out_json, out_json_size);
    }

    const std::lock_guard lock(ctx->mutex);
    return copy_json_string(build_screenshot_json(*ctx), out_json, out_json_size);
}

extern "C" int gua_get_node_state(gua_context_t* ctx, const char* node_id, gua_node_state_t* out_state)
{
    if (ctx == nullptr || node_id == nullptr || out_state == nullptr) {
        return 0;
    }

    const std::lock_guard lock(ctx->mutex);
    const auto found = std::find_if(ctx->nodes.begin(), ctx->nodes.end(), [&](const Node& node) {
        return node.id == node_id;
    });
    if (found == ctx->nodes.end()) {
        return 0;
    }

    out_state->visible = found->visible ? 1 : 0;
    out_state->enabled = found->enabled ? 1 : 0;
    return 1;
}

extern "C" int gua_get_node_state_v2(gua_context_t* ctx, const char* node_id, gua_node_state_v2_t* out_state)
{
    if (ctx == nullptr || node_id == nullptr || out_state == nullptr || out_state->struct_size < sizeof(gua_node_state_v2_t)) {
        return 0;
    }

    const std::lock_guard lock(ctx->mutex);
    const auto found = std::find_if(ctx->nodes.begin(), ctx->nodes.end(), [&](const Node& node) { return node.id == node_id; });
    if (found == ctx->nodes.end()) {
        return 0;
    }

    out_state->known_mask = found->known_mask;
    out_state->visible = found->visible ? 1 : 0;
    out_state->enabled = found->enabled ? 1 : 0;
    out_state->focused = found->focused ? 1 : 0;
    out_state->hovered = found->hovered ? 1 : 0;
    out_state->pressed = found->pressed ? 1 : 0;
    out_state->checked = found->checked ? 1 : 0;
    out_state->selected = found->selected ? 1 : 0;
    std::snprintf(out_state->parent_id, sizeof(out_state->parent_id), "%s", found->parent_id.c_str());
    std::snprintf(out_state->text, sizeof(out_state->text), "%s", found->text.c_str());
    std::snprintf(out_state->value, sizeof(out_state->value), "%s", found->value.c_str());
    return 1;
}

extern "C" int gua_find_node_by_id(gua_context_t* ctx, const char* node_id, char* out_node_id, int out_node_id_size)
{
    if (ctx == nullptr || node_id == nullptr) {
        return 0;
    }

    const std::lock_guard lock(ctx->mutex);
    const auto found = std::find_if(ctx->nodes.begin(), ctx->nodes.end(), [&](const Node& node) {
        return node.id == node_id;
    });
    if (found == ctx->nodes.end()) {
        return 0;
    }

    return write_node_id(found->id, out_node_id, out_node_id_size);
}

extern "C" int gua_find_node_by_role(gua_context_t* ctx, const char* role, const char* name, char* out_node_id, int out_node_id_size)
{
    if (ctx == nullptr || role == nullptr) {
        return 0;
    }

    const std::lock_guard lock(ctx->mutex);
    const auto found = std::find_if(ctx->nodes.begin(), ctx->nodes.end(), [&](const Node& node) {
        if (node.role != role) {
            return false;
        }

        return name == nullptr || std::strlen(name) == 0 || node.label == name;
    });
    if (found == ctx->nodes.end()) {
        return 0;
    }

    return write_node_id(found->id, out_node_id, out_node_id_size);
}

extern "C" int gua_find_node_by_text(gua_context_t* ctx, const char* text, char* out_node_id, int out_node_id_size)
{
    if (ctx == nullptr || text == nullptr) {
        return 0;
    }

    const std::lock_guard lock(ctx->mutex);
    const auto found = std::find_if(ctx->nodes.begin(), ctx->nodes.end(), [&](const Node& node) {
        return node.label == text;
    });
    if (found == ctx->nodes.end()) {
        return 0;
    }

    return write_node_id(found->id, out_node_id, out_node_id_size);
}

extern "C" int gua_enqueue_click(gua_context_t* ctx, const char* node_id)
{
    const gua_action_request_descriptor_t descriptor {
        sizeof(gua_action_request_descriptor_t), GUA_ACTION_CLICK, node_id, nullptr, 0, 0, 0, nullptr, 0, 0, 0
    };
    return gua_enqueue_action(ctx, &descriptor, nullptr) == GUA_ACTION_ACCEPTED ? 1 : 0;
}

extern "C" int gua_consume_click_request(gua_context_t* ctx, const char* node_id)
{
    if (ctx == nullptr || node_id == nullptr) {
        return 0;
    }

    gua_action_request_t request { sizeof(gua_action_request_t) };
    return gua_consume_action_request(ctx, GUA_ACTION_CLICK, node_id, &request);
}

extern "C" int gua_emit_click(gua_context_t* ctx, const char* node_id)
{
    if (ctx == nullptr || node_id == nullptr) {
        return 0;
    }

    unsigned long long request_id = 0;
    {
        const std::lock_guard lock(ctx->mutex);
        const auto consumed = std::find_if(ctx->consumed_requests.begin(), ctx->consumed_requests.end(), [&](const ActionRequest& request) {
            return request.action == GUA_ACTION_CLICK && request.node_id == node_id;
        });
        if (consumed != ctx->consumed_requests.end()) {
            request_id = consumed->request_id;
        }
    }
    const gua_action_result_t result {
        sizeof(gua_action_result_t), request_id, GUA_ACTION_CLICK, GUA_ACTION_STATUS_SUCCEEDED, 0, node_id, nullptr, 0
    };
    return gua_emit_action_result(ctx, &result);
}

extern "C" int gua_poll_event(gua_context_t* ctx, gua_event_t* out_event)
{
    if (ctx == nullptr || out_event == nullptr) {
        return 0;
    }

    const std::lock_guard lock(ctx->mutex);
    const auto legacy_event = std::find_if(ctx->events.begin(), ctx->events.end(), [](const Event& event) {
        return event.action == GUA_ACTION_CLICK || event.action == GUA_ACTION_FOCUS;
    });
    if (legacy_event == ctx->events.end()) {
        return 0;
    }

    const Event event = *legacy_event;
    ctx->events.erase(legacy_event);

    out_event->type = event.action;
    std::snprintf(out_event->node_id, sizeof(out_event->node_id), "%s", event.node_id.c_str());
    return 1;
}

extern "C" int gua_enqueue_action(gua_context_t* ctx, const gua_action_request_descriptor_t* descriptor, uint64_t* out_request_id)
{
    if (ctx == nullptr || descriptor == nullptr || descriptor->struct_size < sizeof(gua_action_request_descriptor_t) ||
        descriptor->action < GUA_ACTION_CLICK || descriptor->action > GUA_ACTION_PRESS_KEY) {
        return GUA_ACTION_ERROR_INVALID_ARGUMENT;
    }

    const std::string node_id = descriptor->node_id != nullptr ? descriptor->node_id : "";
    const std::string value = descriptor->value != nullptr ? descriptor->value : "";
    const std::string key = descriptor->key != nullptr ? descriptor->key : "";
    if ((descriptor->action != GUA_ACTION_PRESS_KEY && node_id.empty()) ||
        (descriptor->action == GUA_ACTION_PRESS_KEY && key.empty()) ||
        (descriptor->action == GUA_ACTION_SELECT && value.empty()) ||
        (descriptor->action == GUA_ACTION_SCROLL && (!std::isfinite(descriptor->delta_x) || !std::isfinite(descriptor->delta_y)))) {
        return GUA_ACTION_ERROR_INVALID_VALUE;
    }

    const std::lock_guard lock(ctx->mutex);
    if (!node_id.empty()) {
        const auto node = std::find_if(ctx->nodes.begin(), ctx->nodes.end(), [&](const Node& candidate) { return candidate.id == node_id; });
        if (node == ctx->nodes.end()) return GUA_ACTION_ERROR_NODE_NOT_FOUND;
        if (!node->visible) return GUA_ACTION_ERROR_HIDDEN;
        if (!node->enabled) return GUA_ACTION_ERROR_DISABLED;
        if (!supports_action(*node, descriptor->action)) return GUA_ACTION_ERROR_UNSUPPORTED;
    }

    const unsigned long long request_id = ctx->next_request_id++;
    ctx->action_requests.push_back(ActionRequest {
        request_id, descriptor->action, node_id, value, descriptor->delta_x, descriptor->delta_y,
        descriptor->bool_value, key, descriptor->modifiers, descriptor->sensitive != 0, descriptor->scroll_unit
    });
    if (out_request_id != nullptr) *out_request_id = request_id;
    return GUA_ACTION_ACCEPTED;
}

extern "C" int gua_consume_action_request(gua_context_t* ctx, int action, const char* node_id, gua_action_request_t* out_request)
{
    if (ctx == nullptr || out_request == nullptr || out_request->struct_size < sizeof(gua_action_request_t)) return 0;
    const std::string target = node_id != nullptr ? node_id : "";
    const std::lock_guard lock(ctx->mutex);
    const auto request = std::find_if(ctx->action_requests.begin(), ctx->action_requests.end(), [&](const ActionRequest& candidate) {
        return candidate.action == action && candidate.node_id == target;
    });
    if (request == ctx->action_requests.end()) return 0;
    const ActionRequest value = *request;
    ctx->action_requests.erase(request);
    ctx->consumed_requests.push_back(value);
    out_request->request_id = value.request_id;
    out_request->action = value.action;
    std::snprintf(out_request->node_id, sizeof(out_request->node_id), "%s", value.node_id.c_str());
    std::snprintf(out_request->value, sizeof(out_request->value), "%s", value.value.c_str());
    out_request->delta_x = value.delta_x;
    out_request->delta_y = value.delta_y;
    out_request->bool_value = value.bool_value;
    std::snprintf(out_request->key, sizeof(out_request->key), "%s", value.key.c_str());
    out_request->modifiers = value.modifiers;
    out_request->sensitive = value.sensitive ? 1 : 0;
    out_request->scroll_unit = value.scroll_unit;
    return 1;
}

extern "C" int gua_emit_action_result(gua_context_t* ctx, const gua_action_result_t* result)
{
    if (ctx == nullptr || result == nullptr || result->struct_size < sizeof(gua_action_result_t) ||
        result->action < GUA_ACTION_CLICK || result->action > GUA_ACTION_PRESS_KEY) return 0;
    const std::lock_guard lock(ctx->mutex);
    auto consumed = ctx->consumed_requests.end();
    if (result->request_id != 0) {
        consumed = std::find_if(ctx->consumed_requests.begin(), ctx->consumed_requests.end(), [&](const ActionRequest& request) {
            return request.request_id == result->request_id && request.action == result->action &&
                request.node_id == (result->node_id != nullptr ? result->node_id : "");
        });
        if (consumed == ctx->consumed_requests.end()) return 0;
    }
    ctx->events.push_back(Event {
        result->action,
        result->node_id != nullptr ? result->node_id : "",
        result->request_id,
        result->status,
        result->error_code,
        result->sensitive != 0 ? "" : (result->value != nullptr ? result->value : ""),
        result->sensitive != 0,
    });
    if (consumed != ctx->consumed_requests.end()) ctx->consumed_requests.erase(consumed);
    return 1;
}

extern "C" int gua_poll_event_v2(gua_context_t* ctx, gua_event_v2_t* out_event)
{
    if (ctx == nullptr || out_event == nullptr || out_event->struct_size < sizeof(gua_event_v2_t)) return 0;
    const std::lock_guard lock(ctx->mutex);
    if (ctx->events.empty()) return 0;
    const Event event = ctx->events.front();
    ctx->events.pop_front();
    out_event->request_id = event.request_id;
    out_event->action = event.action;
    out_event->status = event.status;
    out_event->error_code = event.error_code;
    std::snprintf(out_event->node_id, sizeof(out_event->node_id), "%s", event.node_id.c_str());
    std::snprintf(out_event->value, sizeof(out_event->value), "%s", event.value.c_str());
    out_event->sensitive = event.sensitive ? 1 : 0;
    return 1;
}

extern "C" int gua_poll_event_v2_for_request(gua_context_t* ctx, uint64_t request_id, gua_event_v2_t* out_event)
{
    if (ctx == nullptr || request_id == 0 || out_event == nullptr || out_event->struct_size < sizeof(gua_event_v2_t)) return 0;
    const std::lock_guard lock(ctx->mutex);
    const auto found = std::find_if(ctx->events.begin(), ctx->events.end(), [&](const Event& event) { return event.request_id == request_id; });
    if (found == ctx->events.end()) return 0;
    const Event event = *found;
    ctx->events.erase(found);
    out_event->request_id = event.request_id;
    out_event->action = event.action;
    out_event->status = event.status;
    out_event->error_code = event.error_code;
    std::snprintf(out_event->node_id, sizeof(out_event->node_id), "%s", event.node_id.c_str());
    std::snprintf(out_event->value, sizeof(out_event->value), "%s", event.value.c_str());
    out_event->sensitive = event.sensitive ? 1 : 0;
    return 1;
}
