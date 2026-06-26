#include "gua/gua.h"

#include <algorithm>
#include <cstdio>
#include <deque>
#include <cstring>
#include <memory>
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
};

struct Event {
    int type;
    std::string node_id;
};

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

} // namespace

struct gua_context_t {
    std::string screen = "unknown";
    std::vector<Node> nodes;
    std::deque<Event> events;
    std::string json_cache;
};

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

    ctx->screen = screen != nullptr ? screen : "unknown";
    ctx->nodes.clear();
}

extern "C" void gua_end_frame(gua_context_t* ctx)
{
    if (ctx == nullptr) {
        return;
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

    ctx->nodes.push_back(Node {
        id,
        role,
        label != nullptr ? label : "",
        bounds,
        visible != 0,
        enabled != 0,
    });
}

extern "C" const char* gua_get_ui_tree_json(gua_context_t* ctx)
{
    if (ctx == nullptr) {
        return "{}";
    }

    std::string json = "{\"screen\":\"" + escape_json(ctx->screen) + "\",\"nodes\":[";

    for (std::size_t i = 0; i < ctx->nodes.size(); ++i) {
        const Node& node = ctx->nodes[i];
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
        json += ",\"role\":\"" + escape_json(node.role) + "\"";
        json += ",\"label\":\"" + escape_json(node.label) + "\"";
        json += ",\"visible\":";
        json += node.visible ? "true" : "false";
        json += ",\"enabled\":";
        json += node.enabled ? "true" : "false";
        json += ",\"bounds\":";
        json += bounds;
        if (node.role == "button" && node.enabled) {
            json += ",\"actions\":[\"click\",\"focus\"]}";
        } else {
            json += ",\"actions\":[]}";
        }
    }

    json += "]}";
    ctx->json_cache = std::move(json);
    return ctx->json_cache.c_str();
}

extern "C" int gua_get_node_state(gua_context_t* ctx, const char* node_id, gua_node_state_t* out_state)
{
    if (ctx == nullptr || node_id == nullptr || out_state == nullptr) {
        return 0;
    }

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

extern "C" int gua_find_node_by_id(gua_context_t* ctx, const char* node_id, char* out_node_id, int out_node_id_size)
{
    if (ctx == nullptr || node_id == nullptr) {
        return 0;
    }

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
    if (ctx == nullptr || node_id == nullptr) {
        return 0;
    }

    const auto found = std::any_of(ctx->nodes.begin(), ctx->nodes.end(), [&](const Node& node) {
        return node.id == node_id && node.visible && node.enabled;
    });
    if (!found) {
        return 0;
    }

    ctx->events.push_back(Event { GUA_EVENT_CLICK, node_id });
    return 1;
}

extern "C" int gua_poll_event(gua_context_t* ctx, gua_event_t* out_event)
{
    if (ctx == nullptr || out_event == nullptr || ctx->events.empty()) {
        return 0;
    }

    const Event event = ctx->events.front();
    ctx->events.pop_front();

    out_event->type = event.type;
    std::snprintf(out_event->node_id, sizeof(out_event->node_id), "%s", event.node_id.c_str());
    return 1;
}
