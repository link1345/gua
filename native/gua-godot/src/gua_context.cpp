#include "gua/godot/gua_context.hpp"

#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/classes/json.hpp>
#include <godot_cpp/variant/utility_functions.hpp>

#include <cstdint>
#include <string>
#include <vector>

namespace {

bool exactly_representable_as_double(std::int64_t value)
{
    const std::uint64_t magnitude = value < 0
        ? static_cast<std::uint64_t>(-(value + 1)) + 1U
        : static_cast<std::uint64_t>(value);
    constexpr std::uint64_t max_consecutive_integer = 1ULL << 53U;
    if (magnitude <= max_consecutive_integer) return true;

    std::uint64_t reduced = magnitude;
    unsigned discarded_bits = 0;
    while (reduced > max_consecutive_integer) {
        reduced >>= 1U;
        ++discarded_bits;
    }
    return (magnitude & ((1ULL << discarded_bits) - 1U)) == 0;
}

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

const char* clock_error_name(int result)
{
    switch (result) {
    case GUA_CLOCK_ERROR_NOT_INSTALLED: return "not_installed";
    case GUA_CLOCK_ERROR_INVALID_STATE: return "invalid_state";
    case GUA_CLOCK_ERROR_EXECUTION_LIMIT: return "execution_limit";
    case GUA_CLOCK_ERROR_INVALID_ARGUMENT: return "invalid_duration";
    default: return "";
    }
}

godot::Dictionary clock_result(gua_runtime_t* runtime, int result)
{
    gua_clock_status_t status { sizeof(gua_clock_status_t) };
    godot::Dictionary value;
    if (gua_runtime_clock_get_status(runtime, &status) != 0) {
        value["schema_version"] = 1;
        value["installed"] = status.installed != 0;
        value["state"] = status.paused != 0 ? "paused" : "running";
        value["now_ms"] = status.now_ms;
        value["default_step_ms"] = status.default_step_ms;
        value["pending_ms"] = status.pending_ms;
        value["generation"] = status.generation;
    }
    value["result"] = result;
    value["error"] = clock_error_name(result);
    return value;
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
    gua_runtime_set_godot_plugin_version(runtime_, GUA_GODOT_PLUGIN_VERSION);
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

void GuaContext::set_screenshot(const String& data_uri, int width, int height)
{
    const CharString data_uri_utf8 = data_uri.utf8();
    gua_runtime_set_screenshot(runtime_, data_uri_utf8.get_data(), width, height);
}

String GuaContext::get_screenshot_json() const
{
    return copy_runtime_json(runtime_, gua_runtime_copy_screenshot_json);
}

Dictionary GuaContext::consume_screenshot_request()
{
    gua_screenshot_request_t request { sizeof(gua_screenshot_request_t) };
    if (gua_runtime_consume_screenshot_request(runtime_, &request) == 0) return Dictionary();
    Dictionary result;
    result["request_id"] = request.request_id;
    result["session_epoch"] = request.session_epoch;
    result["after_frame_sequence"] = request.after_frame_sequence;
    return result;
}

bool GuaContext::complete_screenshot_request(const Dictionary& source)
{
    const String data_uri = source.get("data_uri", String());
    const CharString utf8 = data_uri.utf8();
    int result = GUA_SCREENSHOT_AVAILABLE;
    const String unavailable = source.get("unavailable", String());
    if (unavailable == "headless") result = GUA_SCREENSHOT_UNAVAILABLE_HEADLESS;
    else if (unavailable == "rendering_disabled") result = GUA_SCREENSHOT_UNAVAILABLE_RENDERING_DISABLED;
    else if (unavailable == "unsupported") result = GUA_SCREENSHOT_UNAVAILABLE_UNSUPPORTED;
    return gua_runtime_complete_screenshot_request(
        runtime_, source.get("request_id", 0), result, utf8.get_data(), source.get("width", 0), source.get("height", 0)) != 0;
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
    if (source.has("caret_position")) known_mask |= GUA_NODE_KNOWN_CARET_POSITION;
    if (source.has("selection_start") && source.has("selection_end")) known_mask |= GUA_NODE_KNOWN_SELECTION;
    if (source.has("scroll_x") && source.has("scroll_y")) known_mask |= GUA_NODE_KNOWN_SCROLL;
    if (source.has("scroll_max_x") && source.has("scroll_max_y")) known_mask |= GUA_NODE_KNOWN_SCROLL_MAX;
    if (source.has("range_value")) known_mask |= GUA_NODE_KNOWN_RANGE_VALUE;
    if (source.has("range_min")) known_mask |= GUA_NODE_KNOWN_RANGE_MIN;
    if (source.has("range_max")) known_mask |= GUA_NODE_KNOWN_RANGE_MAX;
    if (source.has("selected_index")) known_mask |= GUA_NODE_KNOWN_SELECTED_INDEX;

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
    const gua_node_descriptor_v3_t detailed {
        sizeof(gua_node_descriptor_v3_t), descriptor,
        source.get("caret_position", 0), source.get("selection_start", 0), source.get("selection_end", 0),
        source.get("scroll_x", 0.0), source.get("scroll_y", 0.0), source.get("scroll_max_x", 0.0), source.get("scroll_max_y", 0.0),
        source.get("range_value", 0.0), source.get("range_min", 0.0), source.get("range_max", 0.0), source.get("selected_index", -1)
    };
    return gua_runtime_register_node_v3(runtime_, &detailed) != 0;
}

bool GuaContext::begin_world_frame(const String& scene)
{
    const CharString value = scene.utf8();
    return gua_runtime_begin_world_frame(runtime_, value.get_data()) != 0;
}

bool GuaContext::register_world_object(const Dictionary& source)
{
    const auto reject = [this]() { gua_runtime_abort_world_frame(runtime_); return false; };
    if (!source.has("id") || !source.has("kind") || !source.has("label") || !source.has("position") || !source.has("space")) return reject();
    if ((source.has("visible_to_player") && static_cast<Variant>(source["visible_to_player"]).get_type() != Variant::BOOL) ||
        (source.has("active") && static_cast<Variant>(source["active"]).get_type() != Variant::BOOL)) return reject();
    const String id = source["id"], parent = source.get("parent_id", String()), kind = source["kind"], label = source["label"];
    const String description = source.get("description", String()), domain = source.get("domain_id", String()), related = source.get("related_ui_node_id", String());
    const CharString id8 = id.utf8(), parent8 = parent.utf8(), kind8 = kind.utf8(), label8 = label.utf8(), description8 = description.utf8(), domain8 = domain.utf8(), related8 = related.utf8();
    const Vector3 position = source["position"];
    const Array tags = source.get("tags", Array());
    std::vector<CharString> tag_strings; std::vector<const char*> tag_pointers;
    tag_strings.reserve(tags.size()); tag_pointers.reserve(tags.size());
    for (int i = 0; i < tags.size(); ++i) tag_strings.push_back(String(tags[i]).utf8());
    for (const auto& value : tag_strings) tag_pointers.push_back(value.get_data());
    const Dictionary state = source.get("state", Dictionary());
    const Array keys = state.keys();
    std::vector<CharString> state_keys, state_strings; std::vector<gua_world_state_value_v1_t> state_values;
    state_keys.reserve(keys.size()); state_strings.reserve(keys.size()); state_values.reserve(keys.size());
    for (int i = 0; i < keys.size(); ++i) { state_keys.push_back(String(keys[i]).utf8()); state_strings.emplace_back(); }
    for (int i = 0; i < keys.size(); ++i) {
        const Variant value = state[keys[i]]; gua_world_state_value_v1_t item { sizeof(gua_world_state_value_v1_t), state_keys[i].get_data() };
        if (value.get_type() == Variant::NIL) item.type = GUA_WORLD_VALUE_NULL;
        else if (value.get_type() == Variant::BOOL) { item.type = GUA_WORLD_VALUE_BOOLEAN; item.bool_value = static_cast<bool>(value) ? 1 : 0; }
        else if (value.get_type() == Variant::INT) {
            const auto integer = static_cast<std::int64_t>(value);
            if (!exactly_representable_as_double(integer)) return reject();
            item.type = GUA_WORLD_VALUE_NUMBER; item.number_value = static_cast<double>(integer);
        }
        else if (value.get_type() == Variant::FLOAT) { item.type = GUA_WORLD_VALUE_NUMBER; item.number_value = static_cast<double>(value); }
        else if (value.get_type() == Variant::STRING || value.get_type() == Variant::STRING_NAME) { state_strings[i] = String(value).utf8(); item.type = GUA_WORLD_VALUE_STRING; item.string_value = state_strings[i].get_data(); }
        else return reject();
        state_values.push_back(item);
    }
    const String space = source["space"], exposure = source.get("agent_exposure", String("auto"));
    if ((space != "world2d" && space != "world3d") || (exposure != "auto" && exposure != "private")) return reject();
    const gua_world_object_descriptor_v1_t descriptor { sizeof(gua_world_object_descriptor_v1_t), id8.get_data(), parent.is_empty() ? nullptr : parent8.get_data(), kind8.get_data(), label8.get_data(),
        description.is_empty() ? nullptr : description8.get_data(), space == "world3d" ? GUA_WORLD_SPACE_3D : GUA_WORLD_SPACE_2D,
        position.x, position.y, position.z, static_cast<bool>(source.get("visible_to_player", false)) ? 1 : 0,
        static_cast<bool>(source.get("active", true)) ? 1 : 0,
        exposure == "private" ? GUA_AGENT_EXPOSURE_PRIVATE : GUA_AGENT_EXPOSURE_AUTO,
        domain.is_empty() ? nullptr : domain8.get_data(), related.is_empty() ? nullptr : related8.get_data(),
        tag_pointers.data(), static_cast<uint32_t>(tag_pointers.size()), state_values.data(), static_cast<uint32_t>(state_values.size()) };
    return gua_runtime_register_world_object_v1(runtime_, &descriptor) != 0;
}

bool GuaContext::end_world_frame() { return gua_runtime_end_world_frame(runtime_) != 0; }
bool GuaContext::abort_world_frame() { return gua_runtime_abort_world_frame(runtime_) != 0; }
String GuaContext::get_world_object_tree_json() const { return copy_runtime_json(runtime_, gua_runtime_copy_world_object_tree_json); }
void GuaContext::enable_world_object_tree_adapter() { gua_runtime_set_world_object_tree_enabled(runtime_, 1); }

String GuaContext::get_ui_tree_json() const
{
    return copy_runtime_json(runtime_, gua_runtime_copy_ui_tree_json);
}

String GuaContext::get_version_json() const
{
    char json[2048] {};
    gua_runtime_copy_version_json(runtime_, json, static_cast<int>(sizeof(json)));
    return String::utf8(json);
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

Dictionary GuaContext::get_clock() const
{
    gua_clock_status_t status { sizeof(gua_clock_status_t) }; Dictionary result;
    if (gua_runtime_clock_get_status(runtime_, &status) == 0) return result;
    result["schema_version"] = 1; result["installed"] = status.installed != 0;
    result["state"] = status.paused != 0 ? "paused" : "running"; result["now_ms"] = status.now_ms;
    result["default_step_ms"] = status.default_step_ms; result["pending_ms"] = status.pending_ms; result["generation"] = status.generation;
    return result;
}
Dictionary GuaContext::clock_install(double initial_time_ms, double step_ms) { return clock_result(runtime_, gua_runtime_clock_install(runtime_, initial_time_ms, step_ms)); }
Dictionary GuaContext::clock_pause() { return clock_result(runtime_, gua_runtime_clock_pause(runtime_)); }
Dictionary GuaContext::clock_run_for(double duration_ms, const Variant& step_ms)
{
    const Dictionary status = get_clock();
    const double actual = step_ms.get_type() == Variant::NIL
        ? static_cast<double>(status["default_step_ms"])
        : static_cast<double>(step_ms);
    return clock_result(runtime_, gua_runtime_clock_run_for(runtime_, duration_ms, actual));
}
Dictionary GuaContext::clock_resume() { return clock_result(runtime_, gua_runtime_clock_resume(runtime_)); }
Dictionary GuaContext::clock_advance(double duration_ms) { return clock_result(runtime_, gua_runtime_clock_advance(runtime_, duration_ms)); }
Dictionary GuaContext::consume_clock_step()
{
    Dictionary result; gua_clock_step_t step { sizeof(gua_clock_step_t) };
    if (gua_runtime_clock_consume_step(runtime_, &step) == 0) return result;
    result["delta_ms"] = step.delta_ms; result["final"] = step.final_step != 0; result["generation"] = step.generation;
    return result;
}
Array GuaContext::consume_clock_steps()
{
    Array result; gua_clock_step_t step { sizeof(gua_clock_step_t) };
    while (gua_runtime_clock_consume_step(runtime_, &step) != 0) { Dictionary item; item["delta_ms"] = step.delta_ms; item["final"] = step.final_step != 0; item["generation"] = step.generation; result.push_back(item); step = gua_clock_step_t { sizeof(gua_clock_step_t) }; }
    return result;
}

void GuaContext::enable_virtual_clock_adapter()
{
    gua_runtime_set_virtual_clock_enabled(runtime_, 1);
}

bool GuaContext::publish_game_input_actions(const String& input_context, const Array& actions)
{
    const CharString context_utf8 = input_context.utf8();
    if (gua_runtime_begin_game_input_frame(runtime_, context_utf8.get_data()) == 0) return false;
    for (int index = 0; index < actions.size(); ++index) {
        if (actions[index].get_type() != Variant::DICTIONARY) {
            gua_runtime_abort_game_input_frame(runtime_);
            return false;
        }
        const Dictionary source = actions[index];
        const String id = source.get("id", String());
        const String description = source.get("description", String());
        const String type = source.get("value_type", String("button"));
        const String risk = source.get("risk", String("safe"));
        const String bindings_json = JSON::stringify(source.get("bindings", Array()));
        const CharString id_utf8 = id.utf8(), description_utf8 = description.utf8();
        const CharString bindings_utf8 = bindings_json.utf8(), risk_utf8 = risk.utf8();
        const int value_type = type == "axis1d" ? GUA_GAME_INPUT_AXIS1D :
            type == "vector2" ? GUA_GAME_INPUT_VECTOR2 : type == "text" ? GUA_GAME_INPUT_TEXT : GUA_GAME_INPUT_BUTTON;
        const bool has_range = source.has("minimum") || source.has("maximum");
        if (has_range && (!source.has("minimum") || !source.has("maximum"))) {
            gua_runtime_abort_game_input_frame(runtime_);
            return false;
        }
        const gua_game_input_action_descriptor_v1_t descriptor {
            sizeof(gua_game_input_action_descriptor_v1_t), id_utf8.get_data(), description_utf8.get_data(), value_type,
            source.get("minimum", 0.0), source.get("maximum", 0.0), has_range ? 1 : 0,
            static_cast<bool>(source.get("holdable", false)) ? 1 : 0,
            static_cast<bool>(source.get("active", true)) ? 1 : 0,
            bindings_utf8.get_data(), risk_utf8.get_data(),
            static_cast<bool>(source.get("requires_confirmation", false)) ? 1 : 0
        };
        if (gua_runtime_register_game_input_action_v1(runtime_, &descriptor) == 0) {
            gua_runtime_abort_game_input_frame(runtime_);
            return false;
        }
    }
    return gua_runtime_end_game_input_frame(runtime_) != 0;
}

String GuaContext::get_game_input_actions_json() const
{
    return copy_runtime_json(runtime_, gua_runtime_copy_game_input_actions_json);
}

void GuaContext::enable_game_input_adapter(int capabilities)
{
    gua_runtime_set_game_input_capabilities(runtime_, static_cast<uint32_t>(capabilities));
}

Dictionary GuaContext::consume_game_input_request()
{
    gua_game_input_request_v1_t request { sizeof(gua_game_input_request_v1_t) };
    if (gua_runtime_consume_game_input_request(runtime_, &request) == 0) return Dictionary();
    Dictionary result;
    result["request_id"] = request.request_id;
    result["owner_id"] = request.owner_id;
    result["kind"] = request.kind;
    result["operation"] = request.operation;
    result["target"] = String::utf8(request.target);
    result["value_json"] = String::utf8(request.value_json);
    result["x"] = request.x; result["y"] = request.y;
    result["lease_ms"] = request.lease_ms;
    result["device_index"] = request.device_index;
    result["sensitive"] = request.sensitive != 0;
    return result;
}

bool GuaContext::complete_game_input_request(const Dictionary& result)
{
    return gua_runtime_complete_game_input_request(runtime_, result.get("request_id", 0),
        static_cast<bool>(result.get("succeeded", false)) ? 1 : 0, result.get("error_code", 0)) != 0;
}

int GuaContext::tick_game_input_leases(double elapsed_ms)
{
    return gua_runtime_tick_game_input_leases(runtime_, elapsed_ms);
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
    result["world_frame_sequence"] = status.world_frame_sequence;
    result["world_revision"] = status.world_revision;
    result["world_object_count"] = status.world_object_count;
    return result;
}

Dictionary GuaContext::reset_context(const Dictionary& source)
{
    const gua_reset_options_t options {
        sizeof(gua_reset_options_t),
        static_cast<uint32_t>(static_cast<int64_t>(source.get("flags", GUA_RESET_DEFAULT_V3))),
        source.get("strict", false) ? 1 : 0,
        static_cast<uint64_t>(static_cast<int64_t>(source.get("expected_session_epoch", 0))),
        GUA_RESET_FLAGS_VERSION_CURRENT,
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
    result["discarded_world_object_count"] = report.discarded_world_object_count;
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
    ClassDB::bind_method(D_METHOD("begin_world_frame", "scene"), &GuaContext::begin_world_frame);
    ClassDB::bind_method(D_METHOD("register_world_object", "descriptor"), &GuaContext::register_world_object);
    ClassDB::bind_method(D_METHOD("end_world_frame"), &GuaContext::end_world_frame);
    ClassDB::bind_method(D_METHOD("abort_world_frame"), &GuaContext::abort_world_frame);
    ClassDB::bind_method(D_METHOD("get_world_object_tree_json"), &GuaContext::get_world_object_tree_json);
    ClassDB::bind_method(D_METHOD("enable_world_object_tree_adapter"), &GuaContext::enable_world_object_tree_adapter);
    ClassDB::bind_method(D_METHOD("get_ui_tree_json"), &GuaContext::get_ui_tree_json);
    ClassDB::bind_method(D_METHOD("get_version_json"), &GuaContext::get_version_json);
    ClassDB::bind_method(D_METHOD("set_screenshot", "data_uri", "width", "height"), &GuaContext::set_screenshot);
    ClassDB::bind_method(D_METHOD("get_screenshot_json"), &GuaContext::get_screenshot_json);
    ClassDB::bind_method(D_METHOD("consume_screenshot_request"), &GuaContext::consume_screenshot_request);
    ClassDB::bind_method(D_METHOD("complete_screenshot_request", "result"), &GuaContext::complete_screenshot_request);
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
    ClassDB::bind_method(D_METHOD("clock_install", "initial_time_ms", "step_ms"), &GuaContext::clock_install, DEFVAL(0.0), DEFVAL(1000.0 / 60.0));
    ClassDB::bind_method(D_METHOD("clock_pause"), &GuaContext::clock_pause);
    ClassDB::bind_method(D_METHOD("clock_run_for", "duration_ms", "step_ms"), &GuaContext::clock_run_for, DEFVAL(Variant()));
    ClassDB::bind_method(D_METHOD("clock_resume"), &GuaContext::clock_resume);
    ClassDB::bind_method(D_METHOD("clock_advance", "duration_ms"), &GuaContext::clock_advance);
    ClassDB::bind_method(D_METHOD("get_clock"), &GuaContext::get_clock);
    ClassDB::bind_method(D_METHOD("consume_clock_step"), &GuaContext::consume_clock_step);
    ClassDB::bind_method(D_METHOD("consume_clock_steps"), &GuaContext::consume_clock_steps);
    ClassDB::bind_method(D_METHOD("enable_virtual_clock_adapter"), &GuaContext::enable_virtual_clock_adapter);
    ClassDB::bind_method(D_METHOD("publish_game_input_actions", "input_context", "actions"), &GuaContext::publish_game_input_actions);
    ClassDB::bind_method(D_METHOD("get_game_input_actions_json"), &GuaContext::get_game_input_actions_json);
    ClassDB::bind_method(D_METHOD("enable_game_input_adapter", "capabilities"), &GuaContext::enable_game_input_adapter);
    ClassDB::bind_method(D_METHOD("consume_game_input_request"), &GuaContext::consume_game_input_request);
    ClassDB::bind_method(D_METHOD("complete_game_input_request", "result"), &GuaContext::complete_game_input_request);
    ClassDB::bind_method(D_METHOD("tick_game_input_leases", "elapsed_ms"), &GuaContext::tick_game_input_leases);
    ClassDB::bind_method(D_METHOD("start_inspector_bridge", "port"), &GuaContext::start_inspector_bridge, DEFVAL(8765));
    ClassDB::bind_method(D_METHOD("stop_inspector_bridge"), &GuaContext::stop_inspector_bridge);
    ClassDB::bind_method(D_METHOD("inspector_bridge_running"), &GuaContext::inspector_bridge_running);
    ClassDB::bind_method(D_METHOD("inspector_bridge_url"), &GuaContext::inspector_bridge_url);
    ClassDB::bind_method(D_METHOD("publish_inspector_snapshot"), &GuaContext::publish_inspector_snapshot);
}

} // namespace godot
