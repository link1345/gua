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

int action_type(const godot::String& name)
{
    if (name == "click") return GUA_ACTION_CLICK;
    if (name == "focus") return GUA_ACTION_FOCUS;
    if (name == "set_value") return GUA_ACTION_SET_VALUE;
    if (name == "set_checked") return GUA_ACTION_SET_CHECKED;
    if (name == "select") return GUA_ACTION_SELECT;
    if (name == "scroll") return GUA_ACTION_SCROLL;
    if (name == "press_key") return GUA_ACTION_PRESS_KEY;
    return 0;
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
    default: return "none";
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

Dictionary GuaContext::enqueue_action(const Dictionary& source)
{
    const String action = source.get("action", String());
    const String node_id = source.get("node_id", String());
    const String value = source.get("value", String());
    const String key = source.get("key", String());
    const CharString node_utf8 = node_id.utf8();
    const CharString value_utf8 = value.utf8();
    const CharString key_utf8 = key.utf8();
    const gua_action_request_descriptor_t request {
        sizeof(gua_action_request_descriptor_t), action_type(action), node_id.is_empty() ? nullptr : node_utf8.get_data(),
        value.is_empty() ? nullptr : value_utf8.get_data(), source.get("delta_x", 0.0), source.get("delta_y", 0.0),
        source.get("bool_value", false) ? 1 : 0, key.is_empty() ? nullptr : key_utf8.get_data(),
        static_cast<uint32_t>(static_cast<int64_t>(source.get("modifiers", 0))), source.get("sensitive", false) ? 1 : 0,
        source.get("scroll_unit", 0)
    };
    uint64_t request_id = 0;
    const int code = gua_runtime_enqueue_action(runtime_, &request, &request_id);
    Dictionary result;
    result["error_code"] = code == GUA_ACTION_ACCEPTED ? 0 : code;
    result["request_id"] = request_id;
    return result;
}

Dictionary GuaContext::consume_action_request(const String& action, const String& node_id)
{
    const CharString node_utf8 = node_id.utf8();
    gua_action_request_t request { sizeof(gua_action_request_t) };
    if (gua_runtime_consume_action_request(runtime_, action_type(action), node_utf8.get_data(), &request) == 0) return Dictionary();
    Dictionary result;
    result["request_id"] = request.request_id;
    result["action"] = action_name(request.action);
    result["node_id"] = String::utf8(request.node_id);
    result["value"] = String::utf8(request.value);
    result["delta_x"] = request.delta_x;
    result["delta_y"] = request.delta_y;
    result["bool_value"] = request.bool_value != 0;
    result["key"] = String::utf8(request.key);
    result["modifiers"] = request.modifiers;
    result["sensitive"] = request.sensitive != 0;
    result["scroll_unit"] = request.scroll_unit;
    return result;
}

bool GuaContext::emit_action_result(const Dictionary& source)
{
    const String node_id = source.get("node_id", String());
    const String value = source.get("value", String());
    const CharString node_utf8 = node_id.utf8();
    const CharString value_utf8 = value.utf8();
    const gua_action_result_t result {
        sizeof(gua_action_result_t), source.get("request_id", 0), action_type(source.get("action", String())),
        source.get("succeeded", true) ? GUA_ACTION_STATUS_SUCCEEDED : GUA_ACTION_STATUS_FAILED,
        source.get("error_code", 0), node_utf8.get_data(), value.is_empty() ? nullptr : value_utf8.get_data(),
        source.get("sensitive", false) ? 1 : 0
    };
    return gua_runtime_emit_action_result(runtime_, &result) != 0;
}

Dictionary GuaContext::poll_event_v2()
{
    gua_event_v2_t event { sizeof(gua_event_v2_t) };
    if (gua_runtime_poll_event_v2(runtime_, &event) == 0) return Dictionary();
    Dictionary result;
    result["request_id"] = event.request_id;
    result["action"] = action_name(event.action);
    result["succeeded"] = event.status == GUA_ACTION_STATUS_SUCCEEDED;
    result["error_code"] = event.error_code;
    result["node_id"] = String::utf8(event.node_id);
    result["value"] = String::utf8(event.value);
    result["sensitive"] = event.sensitive != 0;
    return result;
}

Dictionary GuaContext::get_context_status() const
{
    gua_context_status_t status { sizeof(gua_context_status_t) };
    if (gua_runtime_get_context_status(runtime_, &status) == 0) return Dictionary();
    Dictionary result;
    result["session_epoch"] = status.session_epoch;
    result["frame_sequence"] = status.frame_sequence;
    result["revision"] = status.revision;
    result["node_count"] = status.node_count;
    result["pending_request_count"] = status.pending_request_count;
    result["in_flight_request_count"] = status.in_flight_request_count;
    result["unconsumed_event_count"] = status.unconsumed_event_count;
    result["log_count"] = status.log_count;
    result["has_screenshot"] = status.has_screenshot != 0;
    result["first_pending_action"] = action_name(status.first_pending_action);
    result["first_pending_node_id"] = String::utf8(status.first_pending_node_id);
    result["first_event_action"] = action_name(status.first_event_action);
    result["first_event_node_id"] = String::utf8(status.first_event_node_id);
    return result;
}

Dictionary GuaContext::reset_context(const Dictionary& source)
{
    const gua_reset_options_t options {
        sizeof(gua_reset_options_t),
        static_cast<uint32_t>(static_cast<int64_t>(source.get("flags", GUA_RESET_DEFAULT))),
        source.get("strict", false) ? 1 : 0,
        static_cast<uint64_t>(static_cast<int64_t>(source.get("expected_session_epoch", 0))),
    };
    gua_reset_report_t report { sizeof(gua_reset_report_t) };
    const int code = gua_runtime_reset_context(runtime_, &options, &report);
    Dictionary result;
    result["result"] = code;
    result["previous_session_epoch"] = report.previous_session_epoch;
    result["session_epoch"] = report.session_epoch;
    result["pending_request_count"] = report.pending_request_count;
    result["in_flight_request_count"] = report.in_flight_request_count;
    result["unconsumed_event_count"] = report.unconsumed_event_count;
    result["discarded_node_count"] = report.discarded_node_count;
    result["discarded_pending_request_count"] = report.discarded_pending_request_count;
    result["discarded_in_flight_request_count"] = report.discarded_in_flight_request_count;
    result["discarded_event_count"] = report.discarded_event_count;
    result["discarded_log_count"] = report.discarded_log_count;
    result["discarded_screenshot"] = report.discarded_screenshot != 0;
    result["first_pending_action"] = action_name(report.first_pending_action);
    result["first_pending_node_id"] = String::utf8(report.first_pending_node_id);
    result["first_event_action"] = action_name(report.first_event_action);
    result["first_event_node_id"] = String::utf8(report.first_event_node_id);
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
    ClassDB::bind_method(D_METHOD("enqueue_action", "request"), &GuaContext::enqueue_action);
    ClassDB::bind_method(D_METHOD("consume_action_request", "action", "node_id"), &GuaContext::consume_action_request);
    ClassDB::bind_method(D_METHOD("emit_action_result", "result"), &GuaContext::emit_action_result);
    ClassDB::bind_method(D_METHOD("poll_event_v2"), &GuaContext::poll_event_v2);
    ClassDB::bind_method(D_METHOD("get_context_status"), &GuaContext::get_context_status);
    ClassDB::bind_method(D_METHOD("reset_context", "options"), &GuaContext::reset_context, DEFVAL(Dictionary()));
    ClassDB::bind_method(D_METHOD("start_inspector_bridge", "port"), &GuaContext::start_inspector_bridge, DEFVAL(8765));
    ClassDB::bind_method(D_METHOD("stop_inspector_bridge"), &GuaContext::stop_inspector_bridge);
    ClassDB::bind_method(D_METHOD("inspector_bridge_running"), &GuaContext::inspector_bridge_running);
    ClassDB::bind_method(D_METHOD("inspector_bridge_url"), &GuaContext::inspector_bridge_url);
    ClassDB::bind_method(D_METHOD("publish_inspector_snapshot"), &GuaContext::publish_inspector_snapshot);
}

} // namespace godot
