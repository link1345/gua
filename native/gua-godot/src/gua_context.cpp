#include "gua/godot/gua_context.hpp"

#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/variant/utility_functions.hpp>

#include <string>
#include <vector>

namespace {

const char* event_type_name(int type)
{
    switch (type) {
    case GUA_EVENT_CLICK:
        return "click";
    case GUA_EVENT_FOCUS:
        return "focus";
    case GUA_EVENT_NONE:
    default:
        return "none";
    }
}

godot::String copy_runtime_json(gua_runtime_t* runtime, int (*copy_json)(gua_runtime_t*, char*, int))
{
    int required_size = copy_json(runtime, nullptr, 0);
    if (required_size <= 0) {
        return godot::String();
    }

    for (;;) {
        std::vector<char> buffer(static_cast<std::size_t>(required_size));
        const int actual_size = copy_json(runtime, buffer.data(), static_cast<int>(buffer.size()));
        if (actual_size <= 0) {
            return godot::String();
        }
        if (actual_size <= static_cast<int>(buffer.size())) {
            return godot::String::utf8(buffer.data());
        }
        required_size = actual_size;
    }
}

} // namespace

namespace godot {

GuaContext::GuaContext()
    : runtime_(gua_runtime_create())
{
    if (runtime_ == nullptr) {
        UtilityFunctions::push_error(
            "GuaContext failed to create a Gua runtime. Check that the Gua native library and dependent DLLs are available.");
    }
}

GuaContext::~GuaContext()
{
    gua_runtime_destroy(runtime_);
    runtime_ = nullptr;
}

void GuaContext::begin_frame(const String& screen)
{
    const CharString screen_utf8 = screen.utf8();
    gua_runtime_begin_frame(runtime_, screen_utf8.get_data());
}

void GuaContext::end_frame()
{
    gua_runtime_end_frame(runtime_);
}

void GuaContext::register_node(
    const String& id,
    const String& role,
    const String& label,
    const Rect2& bounds,
    bool visible,
    bool enabled)
{
    const CharString id_utf8 = id.utf8();
    const CharString role_utf8 = role.utf8();
    const CharString label_utf8 = label.utf8();

    gua_runtime_register_node(
        runtime_,
        id_utf8.get_data(),
        role_utf8.get_data(),
        label_utf8.get_data(),
        gua_bounds_t {
            static_cast<float>(bounds.position.x),
            static_cast<float>(bounds.position.y),
            static_cast<float>(bounds.size.x),
            static_cast<float>(bounds.size.y),
        },
        visible ? 1 : 0,
        enabled ? 1 : 0);
}

bool GuaContext::register_node_v2(const Dictionary& source)
{
    if (!source.has("id") || !source.has("role") || !source.has("bounds")) {
        UtilityFunctions::push_error("GuaContext.register_node_v2 requires id, role, and bounds.");
        return false;
    }

    const String id = source["id"];
    const String role = source["role"];
    const String label = source.get("label", String());
    const String parent_id = source.get("parent_id", String());
    const String text = source.get("text", String());
    const String value = source.get("value", String());
    const Rect2 bounds = source["bounds"];
    const CharString id_utf8 = id.utf8();
    const CharString parent_id_utf8 = parent_id.utf8();
    const CharString role_utf8 = role.utf8();
    const CharString label_utf8 = label.utf8();
    const CharString text_utf8 = text.utf8();
    const CharString value_utf8 = value.utf8();

    unsigned long long known_mask = 0;
    if (source.has("parent_id")) known_mask |= GUA_NODE_KNOWN_PARENT_ID;
    if (source.has("text")) known_mask |= GUA_NODE_KNOWN_TEXT;
    if (source.has("value")) known_mask |= GUA_NODE_KNOWN_VALUE;
    if (source.has("focused")) known_mask |= GUA_NODE_KNOWN_FOCUSED;
    if (source.has("hovered")) known_mask |= GUA_NODE_KNOWN_HOVERED;
    if (source.has("pressed")) known_mask |= GUA_NODE_KNOWN_PRESSED;
    if (source.has("checked")) known_mask |= GUA_NODE_KNOWN_CHECKED;
    if (source.has("selected")) known_mask |= GUA_NODE_KNOWN_SELECTED;

    const gua_node_descriptor_v2_t descriptor {
        sizeof(gua_node_descriptor_v2_t),
        known_mask,
        id_utf8.get_data(),
        source.has("parent_id") ? parent_id_utf8.get_data() : nullptr,
        role_utf8.get_data(),
        label_utf8.get_data(),
        source.has("text") ? text_utf8.get_data() : nullptr,
        source.has("value") ? value_utf8.get_data() : nullptr,
        gua_bounds_t {
            static_cast<float>(bounds.position.x), static_cast<float>(bounds.position.y),
            static_cast<float>(bounds.size.x), static_cast<float>(bounds.size.y),
        },
        source.get("visible", true) ? 1 : 0,
        source.get("enabled", true) ? 1 : 0,
        source.get("focused", false) ? 1 : 0,
        source.get("hovered", false) ? 1 : 0,
        source.get("pressed", false) ? 1 : 0,
        source.get("checked", false) ? 1 : 0,
        source.get("selected", false) ? 1 : 0,
    };
    return gua_runtime_register_node_v2(runtime_, &descriptor) != 0;
}

String GuaContext::get_ui_tree_json() const
{
    return copy_runtime_json(runtime_, gua_runtime_copy_ui_tree_json);
}

bool GuaContext::enqueue_click(const String& node_id)
{
    const CharString node_id_utf8 = node_id.utf8();
    return gua_runtime_enqueue_click(runtime_, node_id_utf8.get_data()) != 0;
}

bool GuaContext::consume_click_request(const String& node_id)
{
    const CharString node_id_utf8 = node_id.utf8();
    return gua_runtime_consume_click_request(runtime_, node_id_utf8.get_data()) != 0;
}

bool GuaContext::emit_click(const String& node_id)
{
    const CharString node_id_utf8 = node_id.utf8();
    return gua_runtime_emit_click(runtime_, node_id_utf8.get_data()) != 0;
}

Dictionary GuaContext::poll_event()
{
    gua_event_t event {};
    if (gua_runtime_poll_event(runtime_, &event) == 0) {
        return Dictionary();
    }

    Dictionary result;
    result["type"] = event_type_name(event.type);
    result["node_id"] = String::utf8(event.node_id);
    return result;
}

bool GuaContext::start_inspector_bridge(int port)
{
    return gua_runtime_start_inspector_bridge(runtime_, port) != 0;
}

void GuaContext::stop_inspector_bridge()
{
    gua_runtime_stop_inspector_bridge(runtime_);
}

bool GuaContext::inspector_bridge_running() const
{
    return gua_runtime_inspector_bridge_running(runtime_) != 0;
}

String GuaContext::inspector_bridge_url() const
{
    return String::utf8(gua_runtime_inspector_bridge_url(runtime_));
}

void GuaContext::publish_inspector_snapshot()
{
    gua_runtime_publish_inspector_snapshot(runtime_);
}

void GuaContext::_bind_methods()
{
    ClassDB::bind_method(D_METHOD("begin_frame", "screen"), &GuaContext::begin_frame);
    ClassDB::bind_method(D_METHOD("end_frame"), &GuaContext::end_frame);
    ClassDB::bind_method(
        D_METHOD("register_node", "id", "role", "label", "bounds", "visible", "enabled"),
        &GuaContext::register_node,
        DEFVAL(true),
        DEFVAL(true));
    ClassDB::bind_method(D_METHOD("register_node_v2", "descriptor"), &GuaContext::register_node_v2);
    ClassDB::bind_method(D_METHOD("get_ui_tree_json"), &GuaContext::get_ui_tree_json);
    ClassDB::bind_method(D_METHOD("enqueue_click", "node_id"), &GuaContext::enqueue_click);
    ClassDB::bind_method(D_METHOD("consume_click_request", "node_id"), &GuaContext::consume_click_request);
    ClassDB::bind_method(D_METHOD("emit_click", "node_id"), &GuaContext::emit_click);
    ClassDB::bind_method(D_METHOD("poll_event"), &GuaContext::poll_event);
    ClassDB::bind_method(D_METHOD("start_inspector_bridge", "port"), &GuaContext::start_inspector_bridge, DEFVAL(8765));
    ClassDB::bind_method(D_METHOD("stop_inspector_bridge"), &GuaContext::stop_inspector_bridge);
    ClassDB::bind_method(D_METHOD("inspector_bridge_running"), &GuaContext::inspector_bridge_running);
    ClassDB::bind_method(D_METHOD("inspector_bridge_url"), &GuaContext::inspector_bridge_url);
    ClassDB::bind_method(D_METHOD("publish_inspector_snapshot"), &GuaContext::publish_inspector_snapshot);
}

} // namespace godot
