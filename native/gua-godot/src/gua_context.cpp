#include "gua/godot/gua_context.hpp"

#include <godot_cpp/core/class_db.hpp>

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

} // namespace

namespace godot {

GuaContext::GuaContext()
    : runtime_(gua_runtime_create())
{
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

String GuaContext::get_ui_tree_json() const
{
    return String::utf8(gua_runtime_get_ui_tree_json(runtime_));
}

bool GuaContext::enqueue_click(const String& node_id)
{
    const CharString node_id_utf8 = node_id.utf8();
    return gua_runtime_enqueue_click(runtime_, node_id_utf8.get_data()) != 0;
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
    ClassDB::bind_method(D_METHOD("get_ui_tree_json"), &GuaContext::get_ui_tree_json);
    ClassDB::bind_method(D_METHOD("enqueue_click", "node_id"), &GuaContext::enqueue_click);
    ClassDB::bind_method(D_METHOD("poll_event"), &GuaContext::poll_event);
    ClassDB::bind_method(D_METHOD("start_inspector_bridge", "port"), &GuaContext::start_inspector_bridge, DEFVAL(8765));
    ClassDB::bind_method(D_METHOD("stop_inspector_bridge"), &GuaContext::stop_inspector_bridge);
    ClassDB::bind_method(D_METHOD("inspector_bridge_running"), &GuaContext::inspector_bridge_running);
    ClassDB::bind_method(D_METHOD("inspector_bridge_url"), &GuaContext::inspector_bridge_url);
    ClassDB::bind_method(D_METHOD("publish_inspector_snapshot"), &GuaContext::publish_inspector_snapshot);
}

} // namespace godot
