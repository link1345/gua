#include "gua/gua.h"

#include <algorithm>
#include <cstdio>
#include <deque>
#include <cstring>
#include <memory>
#include <mutex>
#include <regex>
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
    int type;
    std::string node_id;
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

bool matches_text(const std::string& actual, const char* expected, int mode, std::string& error)
{
    if (expected == nullptr || expected[0] == '\0') {
        return true;
    }
    switch (mode) {
    case GUA_MATCH_EXACT:
        return actual == expected;
    case GUA_MATCH_CONTAINS:
        return actual.find(expected) != std::string::npos;
    case GUA_MATCH_REGEX:
        try {
            return std::regex_search(actual, std::regex(expected, std::regex::ECMAScript));
        } catch (const std::regex_error& exception) {
            error = exception.what();
            return false;
        }
    default:
        error = "unknown match mode";
        return false;
    }
}

bool matches_filter(bool actual, int filter, std::string& error)
{
    if (filter == GUA_FILTER_ANY) return true;
    if (filter == GUA_FILTER_FALSE) return !actual;
    if (filter == GUA_FILTER_TRUE) return actual;
    error = "unknown state filter";
    return false;
}

bool is_in_scope(const std::vector<Node>& nodes, const Node& node, const char* parent_id, bool direct_child)
{
    if (parent_id == nullptr || parent_id[0] == '\0') return true;
    if (node.id == parent_id) return false;
    if (direct_child) return node.parent_id == parent_id;

    std::string current = node.parent_id;
    for (std::size_t depth = 0; !current.empty() && depth <= nodes.size(); ++depth) {
        if (current == parent_id) return true;
        const auto parent = std::find_if(nodes.begin(), nodes.end(), [&](const Node& candidate) { return candidate.id == current; });
        if (parent == nodes.end() || parent->parent_id == current) return false;
        current = parent->parent_id;
    }
    return false;
}

std::string build_query_json(const std::vector<Node>& nodes, const gua_selector_v1_t& selector)
{
    std::string error;
    std::vector<const Node*> matches;
    for (const Node& node : nodes) {
        if (!is_in_scope(nodes, node, selector.parent_id, selector.direct_child != 0)) continue;
        const std::string& text = (node.known_mask & GUA_NODE_KNOWN_TEXT) != 0U ? node.text : node.label;
        if (!matches_text(node.id, selector.id, selector.id_match, error) || !error.empty() ||
            !matches_text(node.role, selector.role, selector.role_match, error) || !error.empty() ||
            !matches_text(node.label, selector.name, selector.name_match, error) || !error.empty() ||
            !matches_text(text, selector.text, selector.text_match, error) || !error.empty() ||
            !matches_filter(node.visible, selector.visible, error) || !error.empty() ||
            !matches_filter(node.enabled, selector.enabled, error) || !error.empty()) {
            if (!error.empty()) break;
            continue;
        }
        matches.push_back(&node);
    }

    if (!error.empty()) {
        return "{\"valid\":false,\"error\":\"" + escape_json(error) + "\",\"matches\":[]}";
    }

    std::string json = "{\"valid\":true,\"matches\":[";
    for (std::size_t i = 0; i < matches.size(); ++i) {
        if (i > 0) json += ",";
        const Node& node = *matches[i];
        json += "{\"id\":\"" + escape_json(node.id) + "\",\"role\":\"" + escape_json(node.role) +
            "\",\"label\":\"" + escape_json(node.label) + "\",\"parentId\":";
        json += (node.known_mask & GUA_NODE_KNOWN_PARENT_ID) != 0U
            ? "\"" + escape_json(node.parent_id) + "\""
            : "null";
        json += "}";
    }
    json += "]}";
    return json;
}

} // namespace

struct gua_context_t {
    mutable std::mutex mutex;
    std::string screen = "unknown";
    std::vector<Node> nodes;
    std::deque<std::string> click_requests;
    std::deque<Event> events;
    std::vector<LogEntry> logs;
    Screenshot screenshot;
    unsigned long long next_log_sequence = 1;
    std::string json_cache;
    std::string logs_json_cache;
    std::string screenshot_json_cache;
    unsigned long long frame_sequence = 0;
    unsigned long long revision = 0;
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
        if ((node.role == "button" || node.role == "checkbox" || node.role == "radio" || node.role == "tab") && node.enabled) {
            json += ",\"actions\":[\"click\",\"focus\"]}";
        } else if ((node.role == "textbox" || node.role == "slider" || node.role == "combobox") && node.enabled) {
            json += ",\"actions\":[\"focus\",\"set_value\"]}";
        } else {
            json += ",\"actions\":[]}";
        }
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

extern "C" int gua_query_nodes_json(gua_context_t* ctx, const gua_selector_v1_t* selector, char* out_json, int out_json_size)
{
    if (ctx == nullptr || selector == nullptr || selector->struct_size < sizeof(gua_selector_v1_t)) {
        return copy_json_string("{\"valid\":false,\"error\":\"invalid selector struct\",\"matches\":[]}", out_json, out_json_size);
    }
    const std::lock_guard lock(ctx->mutex);
    return copy_json_string(build_query_json(ctx->nodes, *selector), out_json, out_json_size);
}

extern "C" int gua_enqueue_click(gua_context_t* ctx, const char* node_id)
{
    if (ctx == nullptr || node_id == nullptr) {
        return 0;
    }

    const std::lock_guard lock(ctx->mutex);
    const auto found = std::any_of(ctx->nodes.begin(), ctx->nodes.end(), [&](const Node& node) {
        return node.id == node_id && node.visible && node.enabled;
    });
    if (!found) {
        return 0;
    }

    ctx->click_requests.push_back(node_id);
    return 1;
}

extern "C" int gua_consume_click_request(gua_context_t* ctx, const char* node_id)
{
    if (ctx == nullptr || node_id == nullptr) {
        return 0;
    }

    const std::lock_guard lock(ctx->mutex);
    const auto node_found = std::any_of(ctx->nodes.begin(), ctx->nodes.end(), [&](const Node& node) {
        return node.id == node_id && node.visible && node.enabled;
    });
    if (!node_found) {
        return 0;
    }

    const auto request = std::find(ctx->click_requests.begin(), ctx->click_requests.end(), node_id);
    if (request == ctx->click_requests.end()) {
        return 0;
    }

    ctx->click_requests.erase(request);
    return 1;
}

extern "C" int gua_emit_click(gua_context_t* ctx, const char* node_id)
{
    if (ctx == nullptr || node_id == nullptr) {
        return 0;
    }

    const std::lock_guard lock(ctx->mutex);
    ctx->events.push_back(Event { GUA_EVENT_CLICK, node_id });
    return 1;
}

extern "C" int gua_poll_event(gua_context_t* ctx, gua_event_t* out_event)
{
    if (ctx == nullptr || out_event == nullptr) {
        return 0;
    }

    const std::lock_guard lock(ctx->mutex);
    if (ctx->events.empty()) {
        return 0;
    }

    const Event event = ctx->events.front();
    ctx->events.pop_front();

    out_event->type = event.type;
    std::snprintf(out_event->node_id, sizeof(out_event->node_id), "%s", event.node_id.c_str());
    return 1;
}
