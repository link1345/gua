#include "gua/gua.h"

#include <algorithm>
#include <cctype>
#include <chrono>
#include <cstddef>
#include <cstdlib>
#include <cstdio>
#include <deque>
#include <cstring>
#include <cmath>
#include <memory>
#include <mutex>
#include <iomanip>
#include <limits>
#include <locale>
#include <regex>
#include <sstream>
#include <string>
#include <utility>
#include <unordered_map>
#include <unordered_set>
#include <vector>
#include <unordered_set>

namespace {

std::string escape_json(const std::string& value);
constexpr double default_clock_step_ms = 1000.0 / 60.0;

#ifndef GUA_VERSION
#define GUA_VERSION "0.0.0-development"
#endif
#ifndef GUA_BUILD_ID
#define GUA_BUILD_ID "development"
#endif

std::string build_version_json(const char* godot_plugin_version = nullptr)
{
    const std::string plugin = godot_plugin_version == nullptr
        ? "null"
        : "\"" + escape_json(godot_plugin_version) + "\"";
    return "{\"protocolSchemaVersion\":\"2\",\"coreVersion\":\"" GUA_VERSION
        "\",\"runtimeVersion\":\"" GUA_VERSION "\",\"godotPluginVersion\":" + plugin + ",\"adapterVersions\":{}" +
        ",\"abiVersion\":1,\"buildId\":\"" GUA_BUILD_ID
        "\",\"capabilities\":[\"semantic_ui_tree_v2\",\"detailed_semantic_state_v1\",\"semantic_actions_v2\",\"context_reset_v1\",\"diagnostics_v1\",\"version_v1\",\"capture_screenshot_v1\",\"virtual_clock_v1\",\"semantic_game_input_v1\",\"raw_keyboard_input_v1\",\"raw_pointer_input_v1\",\"raw_gamepad_input_v1\",\"text_input_v1\",\"game_input_lease_v1\",\"world_object_tree_v1\"]}";
}

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
    long long caret_position = 0, selection_start = 0, selection_end = 0;
    double scroll_x = 0, scroll_y = 0, scroll_max_x = 0, scroll_max_y = 0;
    double range_value = 0, range_min = 0, range_max = 0;
    long long selected_index = -1;
};

struct WorldStateValue {
    std::string key;
    int type = GUA_WORLD_VALUE_NULL;
    std::string string_value;
    double number_value = 0;
    bool bool_value = false;
};

struct WorldObject {
    std::string id, parent_id, kind, label, description, domain_id, related_ui_node_id;
    int space = GUA_WORLD_SPACE_2D;
    double x = 0, y = 0, z = 0;
    bool visible_to_player = false;
    bool active = true;
    int agent_exposure = GUA_AGENT_EXPOSURE_AUTO;
    std::vector<std::string> tags;
    std::vector<WorldStateValue> state;
};

struct Event {
    int action;
    std::string node_id;
    unsigned long long request_id = 0;
    int status = GUA_ACTION_STATUS_SUCCEEDED;
    int error_code = 0;
    std::string value;
    bool sensitive = false;
    unsigned long long session_epoch = 0;
    unsigned long long frame_sequence = 0;
    unsigned long long revision = 0;
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

struct HistoryEntry {
    unsigned long long sequence;
    unsigned long long elapsed_milliseconds;
    unsigned long long revision;
    std::string phase;
    unsigned long long request_id;
    int action;
    std::string node_id;
    int status;
    int error_code;
    std::string value;
    bool sensitive;
    float delta_x;
    float delta_y;
    int bool_value;
    std::string key;
    unsigned int modifiers;
    int scroll_unit;
};

struct GameInputAction {
    std::string id;
    std::string description;
    int value_type = 0;
    double minimum = 0.0;
    double maximum = 0.0;
    bool has_range = false;
    bool holdable = false;
    bool active = false;
    std::string bindings_json = "[]";
    std::string risk = "safe";
    bool requires_confirmation = false;
};

struct GameInputRequest {
    unsigned long long request_id = 0;
    unsigned long long owner_id = 0;
    int kind = 0;
    int operation = 0;
    std::string target;
    std::string value_json;
    double x = 0.0;
    double y = 0.0;
    unsigned int lease_ms = 0;
    int device_index = 0;
    bool sensitive = false;
};

struct HeldGameInput {
    unsigned long long owner_id = 0;
    int kind = 0;
    std::string target;
    int device_index = 0;
    std::string value_json;
    double remaining_ms = 0.0;
    bool sensitive = false;
};

struct GameInputResult {
    unsigned long long request_id = 0;
    unsigned long long owner_id = 0;
    bool succeeded = false;
    int error_code = 0;
};

constexpr std::size_t max_game_input_results = 1024;

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
            if (static_cast<unsigned char>(ch) < 0x20U) {
                constexpr char hex[] = "0123456789abcdef";
                const unsigned char byte_value = static_cast<unsigned char>(ch);
                out += "\\u00";
                out += hex[byte_value >> 4U];
                out += hex[byte_value & 0x0fU];
            } else {
                out += ch;
            }
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

bool validate_text_criterion(const char* expected, int mode, std::string& error)
{
    if (expected == nullptr || expected[0] == '\0') return true;
    if (mode != GUA_MATCH_EXACT && mode != GUA_MATCH_CONTAINS && mode != GUA_MATCH_REGEX) {
        error = "unknown match mode";
        return false;
    }
    if (mode != GUA_MATCH_REGEX) return true;
    try {
        (void)std::regex(expected, std::regex::ECMAScript);
        return true;
    } catch (const std::regex_error& exception) {
        error = exception.what();
        return false;
    }
}

bool validate_filter(int filter, std::string& error)
{
    if (filter == GUA_FILTER_ANY || filter == GUA_FILTER_FALSE || filter == GUA_FILTER_TRUE) return true;
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

std::string build_query_json(const std::vector<Node>& nodes, const gua_selector_v1_t& selector)
{
    std::string error;
    if (!validate_text_criterion(selector.id, selector.id_match, error) ||
        !validate_text_criterion(selector.role, selector.role_match, error) ||
        !validate_text_criterion(selector.name, selector.name_match, error) ||
        !validate_text_criterion(selector.text, selector.text_match, error) ||
        !validate_filter(selector.visible, error) || !validate_filter(selector.enabled, error))
        return "{\"valid\":false,\"error\":\"" + escape_json(error) + "\",\"matches\":[]}";
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

std::string json_number(double value)
{
    if (value == 0.0) return "0";
    std::ostringstream stream;
    stream.imbue(std::locale::classic());
    stream << std::setprecision(17) << value;
    return stream.str();
}

std::string world_value_json(const WorldStateValue& value)
{
    if (value.type == GUA_WORLD_VALUE_STRING) return "\"" + escape_json(value.string_value) + "\"";
    if (value.type == GUA_WORLD_VALUE_NUMBER) return json_number(value.number_value);
    if (value.type == GUA_WORLD_VALUE_BOOLEAN) return value.bool_value ? "true" : "false";
    return "null";
}

std::string world_object_json(const WorldObject& object)
{
    std::string json = "{\"id\":\"" + escape_json(object.id) + "\"";
    if (!object.parent_id.empty()) json += ",\"parentId\":\"" + escape_json(object.parent_id) + "\"";
    json += ",\"kind\":\"" + escape_json(object.kind) + "\",\"label\":\"" + escape_json(object.label) + "\"";
    if (!object.description.empty()) json += ",\"description\":\"" + escape_json(object.description) + "\"";
    json += ",\"space\":\"" + std::string(object.space == GUA_WORLD_SPACE_3D ? "world3d" : "world2d") + "\",\"position\":{";
    json += "\"x\":" + json_number(object.x) + ",\"y\":" + json_number(object.y);
    if (object.space == GUA_WORLD_SPACE_3D) json += ",\"z\":" + json_number(object.z);
    json += "},\"visibleToPlayer\":" + std::string(object.visible_to_player ? "true" : "false") +
        ",\"active\":" + std::string(object.active ? "true" : "false") +
        ",\"agentExposure\":\"" + std::string(object.agent_exposure == GUA_AGENT_EXPOSURE_PRIVATE ? "private" : "auto") + "\"";
    if (!object.domain_id.empty()) json += ",\"domainId\":\"" + escape_json(object.domain_id) + "\"";
    if (!object.related_ui_node_id.empty()) json += ",\"relatedUiNodeId\":\"" + escape_json(object.related_ui_node_id) + "\"";
    json += ",\"tags\":[";
    for (std::size_t i = 0; i < object.tags.size(); ++i) { if (i != 0) json += ','; json += "\"" + escape_json(object.tags[i]) + "\""; }
    json += "],\"state\":{";
    for (std::size_t i = 0; i < object.state.size(); ++i) { if (i != 0) json += ','; json += "\"" + escape_json(object.state[i].key) + "\":" + world_value_json(object.state[i]); }
    return json + "}}";
}

std::vector<WorldObject> project_world_objects(const std::vector<WorldObject>& objects, int profile)
{
    if (profile == GUA_OBSERVATION_PROFILE_DEBUG) return objects;
    std::unordered_map<std::string, const WorldObject*> by_id;
    for (const auto& object : objects) by_id.emplace(object.id, &object);
    std::vector<WorldObject> result;
    for (const auto& object : objects) {
        bool observable = true;
        const WorldObject* current = &object;
        for (std::size_t depth = 0; current != nullptr && depth <= objects.size(); ++depth) {
            if (!current->visible_to_player || current->agent_exposure == GUA_AGENT_EXPOSURE_PRIVATE) {
                observable = false;
                break;
            }
            if (current->parent_id.empty()) break;
            const auto parent = by_id.find(current->parent_id);
            if (parent == by_id.end()) { observable = false; break; }
            current = parent->second;
        }
        if (!observable) continue;
        result.push_back(object);
    }
    return result;
}

std::string build_world_semantic_json(const std::string& scene, const std::vector<WorldObject>& objects)
{
    std::string json = "{\"scene\":\"" + escape_json(scene) + "\",\"objects\":[";
    for (std::size_t i = 0; i < objects.size(); ++i) { if (i != 0) json += ','; json += world_object_json(objects[i]); }
    return json + "]}";
}

std::string build_world_tree_json(const std::string& scene, const std::vector<WorldObject>& source,
    unsigned long long session_epoch, unsigned long long frame_sequence, unsigned long long revision, int profile)
{
    const auto objects = project_world_objects(source, profile);
    std::string semantic = build_world_semantic_json(scene, objects);
    semantic.erase(semantic.begin());
    return "{\"schemaVersion\":1,\"sessionEpoch\":" + std::to_string(session_epoch) +
        ",\"frameSequence\":" + std::to_string(frame_sequence) +
        ",\"revision\":" + std::to_string(revision) + "," + semantic;
}

bool world_in_scope(const std::vector<WorldObject>& objects, const WorldObject& object, const char* parent_id, bool direct)
{
    if (parent_id == nullptr || parent_id[0] == '\0') return true;
    if (object.id == parent_id) return false;
    if (direct) return object.parent_id == parent_id;
    std::string current = object.parent_id;
    for (std::size_t depth = 0; !current.empty() && depth <= objects.size(); ++depth) {
        if (current == parent_id) return true;
        const auto parent = std::find_if(objects.begin(), objects.end(), [&](const auto& candidate) { return candidate.id == current; });
        if (parent == objects.end()) return false;
        current = parent->parent_id;
    }
    return false;
}

bool world_state_matches(const WorldObject& object, const gua_world_state_value_v1_t* expected)
{
    if (expected == nullptr) return true;
    const auto found = std::find_if(object.state.begin(), object.state.end(), [&](const auto& item) { return item.key == (expected->key == nullptr ? "" : expected->key); });
    if (found == object.state.end() || found->type != expected->type) return false;
    if (found->type == GUA_WORLD_VALUE_STRING) return found->string_value == (expected->string_value == nullptr ? "" : expected->string_value);
    if (found->type == GUA_WORLD_VALUE_NUMBER) return found->number_value == expected->number_value;
    if (found->type == GUA_WORLD_VALUE_BOOLEAN) return found->bool_value == (expected->bool_value != 0);
    return true;
}

std::string build_world_query_json(const std::vector<WorldObject>& source, const gua_world_selector_v1_t& selector, int profile)
{
    std::string error;
    if (!validate_text_criterion(selector.id, selector.id_match, error) ||
        !validate_text_criterion(selector.kind, selector.kind_match, error) ||
        !validate_text_criterion(selector.label, selector.label_match, error) ||
        !validate_text_criterion(selector.tag, selector.tag_match, error) ||
        !validate_filter(selector.visible_to_player, error) || !validate_filter(selector.active, error))
        return "{\"valid\":false,\"error\":\"" + escape_json(error) + "\",\"matches\":[]}";
    const auto objects = project_world_objects(source, profile);
    std::string json = "{\"valid\":true,\"matches\":[";
    bool comma = false;
    for (const auto& object : objects) {
        if (!world_in_scope(objects, object, selector.parent_id, selector.direct_child != 0)) continue;
        bool tag_match = selector.tag == nullptr || selector.tag[0] == '\0';
        for (const auto& tag : object.tags) if (matches_text(tag, selector.tag, selector.tag_match, error)) { tag_match = true; break; }
        if (!error.empty()) return "{\"valid\":false,\"error\":\"" + escape_json(error) + "\",\"matches\":[]}";
        if (!matches_text(object.id, selector.id, selector.id_match, error) ||
            !matches_text(object.kind, selector.kind, selector.kind_match, error) ||
            !matches_text(object.label, selector.label, selector.label_match, error) || !tag_match ||
            !matches_filter(object.visible_to_player, selector.visible_to_player, error) ||
            !matches_filter(object.active, selector.active, error) || !world_state_matches(object, selector.state)) continue;
        if (comma) json += ',';
        json += world_object_json(object);
        comma = true;
    }
    if (!error.empty()) return "{\"valid\":false,\"error\":\"" + escape_json(error) + "\",\"matches\":[]}";
    return json + "]}";
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
    std::string staging_screen = "unknown";
    std::vector<Node> staging_nodes;
    bool frame_in_progress = false;
    bool staging_valid = true;
    std::deque<ActionRequest> action_requests;
    std::deque<ActionRequest> consumed_requests;
    std::deque<Event> events;
    std::vector<LogEntry> logs;
    Screenshot screenshot;
    unsigned long long next_log_sequence = 1;
    std::string json_cache;
    std::string logs_json_cache;
    std::string screenshot_json_cache;
    std::string diagnostics_json_cache;
    unsigned long long frame_sequence = 0;
    unsigned long long revision = 0;
    unsigned long long next_request_id = 1;
    unsigned long long session_epoch = 1;
    std::string previous_semantic_snapshot;
    std::string world_scene = "unknown";
    std::vector<WorldObject> world_objects;
    std::string staging_world_scene = "unknown";
    std::vector<WorldObject> staging_world_objects;
    bool world_frame_in_progress = false;
    bool staging_world_valid = true;
    unsigned long long world_frame_sequence = 0;
    unsigned long long world_revision = 0;
    unsigned long long player_world_revision = 0;
    std::string previous_world_snapshot;
    std::string previous_player_world_snapshot;
    std::string world_json_cache_debug;
    std::string world_json_cache_player;
    std::deque<HistoryEntry> operation_history;
    std::deque<HistoryEntry> event_history;
    std::size_t diagnostics_history_limit = 100;
    unsigned long long next_history_sequence = 1;
    std::chrono::steady_clock::time_point diagnostics_history_started_at = std::chrono::steady_clock::now();
    std::string diagnostics_environment_json = "{}";
    bool clock_installed = false;
    bool clock_paused = false;
    double clock_now_ms = 0.0;
    double clock_default_step_ms = default_clock_step_ms;
    double clock_pending_ms = 0.0;
    double clock_pending_step_ms = 1000.0 / 60.0;
    double clock_pending_total_ms = 0.0;
    double clock_pending_start_ms = 0.0;
    double clock_pending_elapsed_ms = 0.0;
    double clock_pending_target_ms = 0.0;
    unsigned long long clock_pending_step_count = 0;
    unsigned long long clock_pending_step_index = 0;
    unsigned long long next_clock_operation_sequence = 1;
    unsigned long long clock_pending_operation_sequence = 0;
    unsigned long long clock_awaiting_frame_operation_sequence = 0;
    unsigned long long clock_completed_operation_sequence = 0;
    unsigned long long clock_generation = 0;
    std::string game_input_context = "unknown";
    std::vector<GameInputAction> game_input_actions;
    std::string staging_game_input_context = "unknown";
    std::vector<GameInputAction> staging_game_input_actions;
    bool game_input_frame_in_progress = false;
    bool game_input_staging_valid = true;
    unsigned long long game_input_revision = 0;
    std::string previous_game_input_snapshot;
    unsigned long long next_game_input_owner_id = 1;
    std::unordered_set<unsigned long long> game_input_owners;
    unsigned long long next_game_input_request_id = 1;
    std::deque<GameInputRequest> game_input_cleanup_requests;
    std::deque<GameInputRequest> game_input_requests;
    std::deque<GameInputRequest> consumed_game_input_requests;
    std::vector<HeldGameInput> held_game_inputs;
    std::deque<GameInputResult> game_input_results;
};

namespace {

bool prepare_clock_operation(
    double now_ms, double duration_ms, double step_ms, double& target_ms, unsigned long long& step_count)
{
    target_ms = now_ms + duration_ms;
    if (!std::isfinite(target_ms)) return false;
    if (duration_ms == 0.0) {
        const double next_step_ms = now_ms + step_ms;
        if (!std::isfinite(next_step_ms) || next_step_ms <= now_ms) return false;
        step_count = 0;
        return true;
    }
    const double emitted_step_ms = std::min(duration_ms, step_ms);
    if (target_ms <= now_ms || now_ms + emitted_step_ms <= now_ms ||
        target_ms - emitted_step_ms >= target_ms) return false;
    double quotient = duration_ms / step_ms;
    const double nearest = std::round(quotient);
    const double quotient_tolerance = std::numeric_limits<double>::epsilon() * 8.0 * std::max(1.0, std::abs(quotient));
    if (nearest >= 1.0 && std::abs(quotient - nearest) <= quotient_tolerance) quotient = nearest;
    step_count = std::max(1ULL, static_cast<unsigned long long>(std::ceil(quotient)));
    while (step_count > 1) {
        const double previous_elapsed_ms = std::min(duration_ms, step_ms * static_cast<double>(step_count - 1));
        if (now_ms + previous_elapsed_ms < target_ms) break;
        --step_count;
    }
    double previous_boundary_ms = now_ms;
    for (unsigned long long index = 1; index <= step_count; ++index) {
        const double elapsed_ms = index == step_count ? duration_ms :
            std::min(duration_ms, step_ms * static_cast<double>(index));
        const double boundary_ms = index == step_count ? target_ms : now_ms + elapsed_ms;
        if (!std::isfinite(boundary_ms) || boundary_ms <= previous_boundary_ms) return false;
        previous_boundary_ms = boundary_ms;
    }
    return true;
}

int copy_json_string(const std::string& json, char* out_json, int out_json_size)
{
    const int required_size = static_cast<int>(json.size() + 1U);
    if (out_json != nullptr && out_json_size > 0) {
        std::snprintf(out_json, static_cast<std::size_t>(out_json_size), "%s", json.c_str());
    }
    return required_size;
}

bool valid_game_input_id(std::string_view value)
{
    if (value.empty() || value.size() >= 128 || value.front() < 'a' || value.front() > 'z') return false;
    return std::all_of(value.begin(), value.end(), [](char ch) {
        return (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '.' || ch == '-';
    });
}

void skip_json_whitespace(std::string_view value, std::size_t& index)
{
    while (index < value.size() && std::isspace(static_cast<unsigned char>(value[index])) != 0) ++index;
}

bool parse_json_value(std::string_view value, std::size_t& index, int depth);

bool parse_json_string(std::string_view value, std::size_t& index)
{
    if (index == value.size() || value[index++] != '"') return false;
    while (index < value.size()) {
        const unsigned char character = static_cast<unsigned char>(value[index++]);
        if (character == '"') return true;
        if (character < 0x20) return false;
        if (character != '\\') continue;
        if (index == value.size()) return false;
        const char escape = value[index++];
        if (escape == 'u') {
            for (int digit = 0; digit < 4; ++digit) {
                if (index == value.size() || std::isxdigit(static_cast<unsigned char>(value[index++])) == 0) return false;
            }
        } else if (std::string_view("\"\\/bfnrt").find(escape) == std::string_view::npos) {
            return false;
        }
    }
    return false;
}

bool parse_json_number(std::string_view value, std::size_t& index)
{
    const std::size_t start = index;
    if (index < value.size() && value[index] == '-') ++index;
    if (index == value.size()) return false;
    if (value[index] == '0') {
        ++index;
        if (index < value.size() && std::isdigit(static_cast<unsigned char>(value[index])) != 0) return false;
    } else {
        if (value[index] < '1' || value[index] > '9') return false;
        while (index < value.size() && std::isdigit(static_cast<unsigned char>(value[index])) != 0) ++index;
    }
    if (index < value.size() && value[index] == '.') {
        ++index;
        if (index == value.size() || std::isdigit(static_cast<unsigned char>(value[index])) == 0) return false;
        while (index < value.size() && std::isdigit(static_cast<unsigned char>(value[index])) != 0) ++index;
    }
    if (index < value.size() && (value[index] == 'e' || value[index] == 'E')) {
        ++index;
        if (index < value.size() && (value[index] == '+' || value[index] == '-')) ++index;
        if (index == value.size() || std::isdigit(static_cast<unsigned char>(value[index])) == 0) return false;
        while (index < value.size() && std::isdigit(static_cast<unsigned char>(value[index])) != 0) ++index;
    }
    return index > start;
}

bool parse_json_value(std::string_view value, std::size_t& index, int depth)
{
    if (depth > 32) return false;
    skip_json_whitespace(value, index);
    if (index == value.size()) return false;
    if (value[index] == '"') return parse_json_string(value, index);
    if (value.substr(index, 4) == "true" || value.substr(index, 4) == "null") { index += 4; return true; }
    if (value.substr(index, 5) == "false") { index += 5; return true; }
    if (value[index] == '[') {
        ++index;
        skip_json_whitespace(value, index);
        if (index < value.size() && value[index] == ']') { ++index; return true; }
        while (parse_json_value(value, index, depth + 1)) {
            skip_json_whitespace(value, index);
            if (index < value.size() && value[index] == ']') { ++index; return true; }
            if (index == value.size() || value[index++] != ',') return false;
        }
        return false;
    }
    if (value[index] == '{') {
        ++index;
        skip_json_whitespace(value, index);
        if (index < value.size() && value[index] == '}') { ++index; return true; }
        while (true) {
            if (!parse_json_string(value, index)) return false;
            skip_json_whitespace(value, index);
            if (index == value.size() || value[index++] != ':') return false;
            if (!parse_json_value(value, index, depth + 1)) return false;
            skip_json_whitespace(value, index);
            if (index < value.size() && value[index] == '}') { ++index; return true; }
            if (index == value.size() || value[index++] != ',') return false;
            skip_json_whitespace(value, index);
        }
    }
    return parse_json_number(value, index);
}

bool valid_json_value(std::string_view value)
{
    if (value.empty() || value.size() >= 512) return false;
    std::size_t index = 0;
    if (!parse_json_value(value, index, 0)) return false;
    skip_json_whitespace(value, index);
    return index == value.size();
}

bool valid_json_string_array(std::string_view value)
{
    std::size_t index = 0;
    const auto skip_whitespace = [&] {
        while (index < value.size() && std::isspace(static_cast<unsigned char>(value[index])) != 0) ++index;
    };
    skip_whitespace();
    if (index == value.size() || value[index++] != '[') return false;
    skip_whitespace();
    if (index < value.size() && value[index] == ']') {
        ++index;
        skip_whitespace();
        return index == value.size();
    }
    while (index < value.size()) {
        if (value[index++] != '"') return false;
        bool closed = false;
        while (index < value.size()) {
            const unsigned char character = static_cast<unsigned char>(value[index++]);
            if (character == '"') {
                closed = true;
                break;
            }
            if (character < 0x20) return false;
            if (character != '\\') continue;
            if (index == value.size()) return false;
            const char escape = value[index++];
            if (escape == 'u') {
                for (int digit = 0; digit < 4; ++digit) {
                    if (index == value.size() || std::isxdigit(static_cast<unsigned char>(value[index++])) == 0) return false;
                }
            } else if (std::string_view("\"\\/bfnrt").find(escape) == std::string_view::npos) {
                return false;
            }
        }
        if (!closed) return false;
        skip_whitespace();
        if (index == value.size()) return false;
        if (value[index] == ']') {
            ++index;
            skip_whitespace();
            return index == value.size();
        }
        if (value[index++] != ',') return false;
        skip_whitespace();
    }
    return false;
}

bool json_object_number(std::string_view json, std::string_view name, double& result)
{
    const std::regex pattern("\\\"" + std::string(name) + "\\\"\\s*:\\s*([-+]?(?:[0-9]+(?:\\.[0-9]*)?|\\.[0-9]+)(?:[eE][-+]?[0-9]+)?)");
    std::cmatch match;
    const std::string copy(json);
    if (!std::regex_search(copy.c_str(), match, pattern)) return false;
    char* end = nullptr;
    result = std::strtod(match[1].first, &end);
    return end == match[1].second && std::isfinite(result);
}

bool valid_keyboard_code(std::string_view code)
{
    static const std::unordered_set<std::string> named {
        "Backquote", "Backslash", "Backspace", "BracketLeft", "BracketRight", "CapsLock", "Comma",
        "ContextMenu", "Delete", "End", "Enter", "Equal", "Escape", "Home", "Insert", "MetaLeft",
        "MetaRight", "Minus", "NumLock", "PageDown", "PageUp", "Pause", "Period", "Quote",
        "ScrollLock", "Semicolon", "ShiftLeft", "ShiftRight", "Slash", "Space", "Tab", "ControlLeft",
        "ControlRight", "AltLeft", "AltRight", "ArrowDown", "ArrowLeft", "ArrowRight", "ArrowUp"
    };
    if (named.contains(std::string(code))) return true;
    return std::regex_match(code.begin(), code.end(), std::regex("(?:Key[A-Z]|Digit[0-9]|F(?:[1-9]|1[0-9]|2[0-4])|Numpad[0-9])"));
}

bool one_of(std::string_view value, std::initializer_list<std::string_view> allowed)
{
    return std::find(allowed.begin(), allowed.end(), value) != allowed.end();
}

bool one_of(int value, std::initializer_list<int> allowed)
{
    return std::find(allowed.begin(), allowed.end(), value) != allowed.end();
}

int validate_semantic_game_input(const std::vector<GameInputAction>& actions, int operation,
    std::string_view target, std::string_view value)
{
    if (operation == GUA_GAME_INPUT_RELEASE)
        return valid_game_input_id(target) ? GUA_GAME_INPUT_OK : GUA_GAME_INPUT_ERROR_INVALID_VALUE;
    const auto action = std::find_if(actions.begin(), actions.end(),
        [&](const auto& item) { return item.id == target; });
    if (action == actions.end()) return GUA_GAME_INPUT_ERROR_ACTION_NOT_FOUND;
    if (!action->active) return GUA_GAME_INPUT_ERROR_INACTIVE;
    if (operation == GUA_GAME_INPUT_PRESS && action->value_type != GUA_GAME_INPUT_BUTTON)
        return GUA_GAME_INPUT_ERROR_UNSUPPORTED;
    if (operation == GUA_GAME_INPUT_SET && action->value_type == GUA_GAME_INPUT_BUTTON && !action->holdable)
        return GUA_GAME_INPUT_ERROR_UNSUPPORTED;
    if (action->value_type == GUA_GAME_INPUT_BUTTON && operation == GUA_GAME_INPUT_SET &&
        value != "true" && value != "false") return GUA_GAME_INPUT_ERROR_INVALID_VALUE;
    if (action->value_type == GUA_GAME_INPUT_AXIS1D && operation == GUA_GAME_INPUT_SET) {
        char* end = nullptr;
        const std::string copy(value);
        const double number = std::strtod(copy.c_str(), &end);
        if (end != copy.c_str() + copy.size() || !std::isfinite(number) ||
            (action->has_range && (number < action->minimum || number > action->maximum)))
            return GUA_GAME_INPUT_ERROR_INVALID_VALUE;
    }
    if (action->value_type == GUA_GAME_INPUT_VECTOR2 && operation == GUA_GAME_INPUT_SET) {
        double x = 0.0, y = 0.0;
        if (value.empty() || value.front() != '{' || !json_object_number(value, "x", x) || !json_object_number(value, "y", y) ||
            (action->has_range && (x < action->minimum || x > action->maximum || y < action->minimum || y > action->maximum)))
            return GUA_GAME_INPUT_ERROR_INVALID_VALUE;
    }
    if (action->value_type == GUA_GAME_INPUT_TEXT && operation == GUA_GAME_INPUT_SET &&
        (value.empty() || value.front() != '"')) return GUA_GAME_INPUT_ERROR_INVALID_VALUE;
    return GUA_GAME_INPUT_OK;
}

std::string build_game_input_semantic_snapshot(const std::string& context, const std::vector<GameInputAction>& actions)
{
    std::string json = "{\"context\":\"" + escape_json(context) + "\",\"actions\":[";
    for (std::size_t index = 0; index < actions.size(); ++index) {
        const auto& action = actions[index];
        if (index != 0) json += ",";
        const char* type = action.value_type == GUA_GAME_INPUT_BUTTON ? "button" :
            action.value_type == GUA_GAME_INPUT_AXIS1D ? "axis1d" :
            action.value_type == GUA_GAME_INPUT_VECTOR2 ? "vector2" : "text";
        json += "{\"id\":\"" + escape_json(action.id) + "\",\"description\":\"" + escape_json(action.description) +
            "\",\"valueType\":\"" + type + "\"";
        if (action.has_range) json += ",\"range\":{\"minimum\":" + std::to_string(action.minimum) + ",\"maximum\":" + std::to_string(action.maximum) + "}";
        json += ",\"holdable\":" + std::string(action.holdable ? "true" : "false") +
            ",\"active\":" + std::string(action.active ? "true" : "false") +
            ",\"bindings\":" + action.bindings_json + ",\"risk\":\"" + escape_json(action.risk) +
            "\",\"requiresConfirmation\":" + (action.requires_confirmation ? "true" : "false") + "}";
    }
    return json + "]}";
}

std::string build_game_input_actions_json(const gua_context_t& ctx)
{
    std::string semantic = build_game_input_semantic_snapshot(ctx.game_input_context, ctx.game_input_actions);
    semantic.erase(semantic.begin());
    return "{\"schemaVersion\":1,\"sessionEpoch\":" + std::to_string(ctx.session_epoch) +
        ",\"revision\":" + std::to_string(ctx.game_input_revision) + "," + semantic;
}

bool held_key_matches(const HeldGameInput& held, unsigned long long owner_id, int kind,
    std::string_view target, int device_index)
{
    return held.owner_id == owner_id && held.kind == kind && held.target == target && held.device_index == device_index;
}

bool request_creates_hold(const GameInputRequest& request, const std::vector<GameInputAction>& actions)
{
    if (request.kind == GUA_GAME_INPUT_SEMANTIC) {
        if (request.operation != GUA_GAME_INPUT_SET) return false;
        const auto action = std::find_if(actions.begin(), actions.end(),
            [&](const auto& candidate) { return candidate.id == request.target; });
        return action != actions.end() && action->value_type != GUA_GAME_INPUT_TEXT;
    }
    if (request.kind == GUA_GAME_INPUT_KEYBOARD || request.kind == GUA_GAME_INPUT_POINTER || request.kind == GUA_GAME_INPUT_GAMEPAD)
        return request.operation == GUA_GAME_INPUT_DOWN || request.operation == GUA_GAME_INPUT_SET;
    return false;
}

bool request_releases_hold(const GameInputRequest& request)
{
    return request.operation == GUA_GAME_INPUT_RELEASE || request.operation == GUA_GAME_INPUT_UP ||
        request.operation == GUA_GAME_INPUT_RESET;
}

std::string build_semantic_snapshot_json(const std::string& screen, const std::vector<Node>& nodes)
{
    std::string json = "{\"screen\":\"" + escape_json(screen) + "\",\"nodes\":[";

    for (std::size_t i = 0; i < nodes.size(); ++i) {
        const Node& node = nodes[i];
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
        const unsigned long long detailed_state_mask = GUA_NODE_KNOWN_CARET_POSITION | GUA_NODE_KNOWN_SELECTION |
            GUA_NODE_KNOWN_SCROLL | GUA_NODE_KNOWN_SCROLL_MAX | GUA_NODE_KNOWN_RANGE_VALUE |
            GUA_NODE_KNOWN_RANGE_MIN | GUA_NODE_KNOWN_RANGE_MAX | GUA_NODE_KNOWN_SELECTED_INDEX;
        if ((node.known_mask & (boolean_state_mask | detailed_state_mask)) != 0U) {
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
            const auto append_number = [&](const char* name, auto value) {
                if (wrote_state) json += ",";
                json += "\"" + std::string(name) + "\":" + std::to_string(value);
                wrote_state = true;
            };
            if ((node.known_mask & GUA_NODE_KNOWN_CARET_POSITION) != 0U) append_number("caretPosition", node.caret_position);
            if ((node.known_mask & GUA_NODE_KNOWN_SELECTION) != 0U) { append_number("selectionStart", node.selection_start); append_number("selectionEnd", node.selection_end); }
            if ((node.known_mask & GUA_NODE_KNOWN_SCROLL) != 0U) { append_number("scrollX", node.scroll_x); append_number("scrollY", node.scroll_y); }
            if ((node.known_mask & GUA_NODE_KNOWN_SCROLL_MAX) != 0U) { append_number("scrollMaxX", node.scroll_max_x); append_number("scrollMaxY", node.scroll_max_y); }
            if ((node.known_mask & GUA_NODE_KNOWN_RANGE_VALUE) != 0U) append_number("rangeValue", node.range_value);
            if ((node.known_mask & GUA_NODE_KNOWN_RANGE_MIN) != 0U) append_number("rangeMin", node.range_min);
            if ((node.known_mask & GUA_NODE_KNOWN_RANGE_MAX) != 0U) append_number("rangeMax", node.range_max);
            if ((node.known_mask & GUA_NODE_KNOWN_SELECTED_INDEX) != 0U) append_number("selectedIndex", node.selected_index);
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

std::string build_semantic_snapshot_json(const gua_context_t& ctx)
{
    return build_semantic_snapshot_json(ctx.screen, ctx.nodes);
}

std::string build_ui_tree_json(const gua_context_t& ctx)
{
    std::string semantic = build_semantic_snapshot_json(ctx);
    semantic.erase(semantic.begin());
    return "{\"schemaVersion\":2,\"sessionEpoch\":" + std::to_string(ctx.session_epoch) +
        ",\"frameSequence\":" + std::to_string(ctx.frame_sequence) +
        ",\"revision\":" + std::to_string(ctx.revision) + "," + semantic;
}

void fill_reset_summary(const gua_context_t& ctx, gua_reset_report_t& report)
{
    const ActionRequest* request = !ctx.action_requests.empty() ? &ctx.action_requests.front()
        : (!ctx.consumed_requests.empty() ? &ctx.consumed_requests.front() : nullptr);
    if (request != nullptr) {
        report.first_pending_action = request->action;
        std::snprintf(report.first_pending_node_id, sizeof(report.first_pending_node_id), "%s", request->node_id.c_str());
    }
    if (!ctx.events.empty()) {
        report.first_event_action = ctx.events.front().action;
        std::snprintf(report.first_event_node_id, sizeof(report.first_event_node_id), "%s", ctx.events.front().node_id.c_str());
    }
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

void trim_history(std::deque<HistoryEntry>& history, std::size_t limit)
{
    while (history.size() > limit) history.pop_front();
}

void append_history(gua_context_t& ctx, std::deque<HistoryEntry>& history, std::string phase,
    unsigned long long request_id, int action, const std::string& node_id, int status,
    int error_code, const std::string& value, bool sensitive, float delta_x = 0, float delta_y = 0,
    int bool_value = 0, const std::string& key = {}, unsigned int modifiers = 0, int scroll_unit = 0)
{
    if (ctx.diagnostics_history_limit == 0) return;
    const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now() - ctx.diagnostics_history_started_at).count();
    history.push_back(HistoryEntry { ctx.next_history_sequence++, static_cast<unsigned long long>(elapsed),
        ctx.revision, std::move(phase), request_id, action, node_id, status, error_code,
        sensitive ? "" : value, sensitive, delta_x, delta_y, bool_value,
        sensitive ? "" : key, modifiers, scroll_unit });
    trim_history(history, ctx.diagnostics_history_limit);
    ctx.diagnostics_json_cache.clear();
}

std::string build_request_json(const ActionRequest& request)
{
    return "{\"requestId\":" + std::to_string(request.request_id) +
        ",\"action\":\"" + action_name(request.action) + "\",\"nodeId\":\"" + escape_json(request.node_id) +
        "\",\"value\":\"" + escape_json(request.sensitive ? "" : request.value) +
        "\",\"sensitive\":" + (request.sensitive ? "true" : "false") + "}";
}

std::string build_history_json(const std::deque<HistoryEntry>& history)
{
    std::string json = "[";
    for (std::size_t i = 0; i < history.size(); ++i) {
        if (i > 0) json += ",";
        const auto& entry = history[i];
        json += "{\"sequence\":" + std::to_string(entry.sequence) +
            ",\"elapsedMilliseconds\":" + std::to_string(entry.elapsed_milliseconds) +
            ",\"revision\":" + std::to_string(entry.revision) +
            ",\"phase\":\"" + escape_json(entry.phase) +
            "\",\"requestId\":" + std::to_string(entry.request_id) + ",\"action\":\"" + action_name(entry.action) +
            "\",\"nodeId\":\"" + escape_json(entry.node_id) + "\",\"status\":" + std::to_string(entry.status) +
            ",\"errorCode\":" + std::to_string(entry.error_code) + ",\"value\":\"" + escape_json(entry.value) +
            "\",\"sensitive\":" + (entry.sensitive ? "true" : "false") +
            ",\"deltaX\":" + std::to_string(entry.delta_x) + ",\"deltaY\":" + std::to_string(entry.delta_y) +
            ",\"boolValue\":" + (entry.bool_value != 0 ? "true" : "false") +
            ",\"key\":\"" + escape_json(entry.key) + "\",\"modifiers\":" + std::to_string(entry.modifiers) +
            ",\"scrollUnit\":" + std::to_string(entry.scroll_unit) + "}";
    }
    return json + "]";
}

std::string build_diagnostics_json(const gua_context_t& ctx)
{
    std::string pending = "[";
    bool comma = false;
    for (const auto& request : ctx.action_requests) {
        if (comma) pending += ",";
        pending += build_request_json(request);
        comma = true;
    }
    for (const auto& request : ctx.consumed_requests) {
        if (comma) pending += ",";
        pending += build_request_json(request);
        comma = true;
    }
    pending += "]";
    return "{\"schemaVersion\":1,\"sessionEpoch\":" + std::to_string(ctx.session_epoch) +
        ",\"frameSequence\":" + std::to_string(ctx.frame_sequence) + ",\"revision\":" + std::to_string(ctx.revision) +
        ",\"historyLimit\":" + std::to_string(ctx.diagnostics_history_limit) +
        ",\"pendingRequestCount\":" + std::to_string(ctx.action_requests.size()) +
        ",\"inFlightRequestCount\":" + std::to_string(ctx.consumed_requests.size()) +
        ",\"unconsumedEventCount\":" + std::to_string(ctx.events.size()) +
        ",\"environment\":" + ctx.diagnostics_environment_json + ",\"version\":" + build_version_json() +
        ",\"uiTree\":" + build_ui_tree_json(ctx) + ",\"pendingRequests\":" + pending +
        ",\"operations\":" + build_history_json(ctx.operation_history) +
        ",\"events\":" + build_history_json(ctx.event_history) +
        ",\"logs\":" + build_logs_json(ctx) + ",\"screenshot\":" +
        (ctx.screenshot.data_uri.empty() ? "null" : build_screenshot_json(ctx)) + "}";
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
    ctx->staging_screen = screen != nullptr ? screen : "unknown";
    ctx->staging_nodes.clear();
    ctx->frame_in_progress = true;
    ctx->staging_valid = true;
}

extern "C" void gua_end_frame(gua_context_t* ctx)
{
    if (ctx == nullptr) {
        return;
    }

    const std::lock_guard lock(ctx->mutex);
    if (!ctx->frame_in_progress || !ctx->staging_valid) {
        ctx->staging_nodes.clear();
        ctx->frame_in_progress = false;
        ctx->staging_valid = true;
        return;
    }
    const auto focused_count = std::count_if(ctx->staging_nodes.begin(), ctx->staging_nodes.end(), [](const Node& node) {
        return (node.known_mask & GUA_NODE_KNOWN_FOCUSED) != 0U && node.focused;
    });
    if (focused_count > 1) {
        ctx->staging_nodes.clear();
        ctx->frame_in_progress = false;
        return;
    }
    const std::string semantic_snapshot = build_semantic_snapshot_json(ctx->staging_screen, ctx->staging_nodes);
    ctx->screen.swap(ctx->staging_screen);
    ctx->nodes.swap(ctx->staging_nodes);
    ctx->staging_nodes.clear();
    ctx->frame_in_progress = false;
    ++ctx->frame_sequence;
    if (ctx->clock_awaiting_frame_operation_sequence != 0) {
        ctx->clock_completed_operation_sequence = std::max(
            ctx->clock_completed_operation_sequence, ctx->clock_awaiting_frame_operation_sequence);
        ctx->clock_awaiting_frame_operation_sequence = 0;
    }
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
    if (ctx == nullptr) {
        return;
    }

    const std::lock_guard lock(ctx->mutex);
    if (!ctx->frame_in_progress || id == nullptr || role == nullptr) {
        ctx->staging_valid = false;
        return;
    }
    ctx->staging_nodes.push_back(Node {
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
    if (ctx == nullptr) {
        return 0;
    }

    const std::lock_guard lock(ctx->mutex);
    if (!ctx->frame_in_progress || descriptor == nullptr || descriptor->struct_size < sizeof(gua_node_descriptor_v2_t) ||
        descriptor->id == nullptr || descriptor->role == nullptr) {
        ctx->staging_valid = false;
        return 0;
    }
    ctx->staging_nodes.push_back(Node {
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

extern "C" int gua_register_node_v3(gua_context_t* ctx, const gua_node_descriptor_v3_t* descriptor)
{
    if (ctx == nullptr) return 0;

    const std::lock_guard lock(ctx->mutex);
    if (!ctx->frame_in_progress || descriptor == nullptr || descriptor->struct_size < sizeof(gua_node_descriptor_v3_t) ||
        descriptor->base.struct_size < sizeof(gua_node_descriptor_v2_t) || descriptor->base.id == nullptr ||
        descriptor->base.role == nullptr) {
        ctx->staging_valid = false;
        return 0;
    }
    const auto& base = descriptor->base;
    ctx->staging_nodes.push_back(Node {
        base.id,
        base.role,
        base.label != nullptr ? base.label : "",
        base.bounds,
        base.visible != 0,
        base.enabled != 0,
        base.known_mask,
        base.parent_id != nullptr ? base.parent_id : "",
        base.text != nullptr ? base.text : "",
        base.value != nullptr ? base.value : "",
        base.focused != 0,
        base.hovered != 0,
        base.pressed != 0,
        base.checked != 0,
        base.selected != 0,
    });
    Node& node = ctx->staging_nodes.back();
    node.caret_position = descriptor->caret_position; node.selection_start = descriptor->selection_start; node.selection_end = descriptor->selection_end;
    node.scroll_x = descriptor->scroll_x; node.scroll_y = descriptor->scroll_y; node.scroll_max_x = descriptor->scroll_max_x; node.scroll_max_y = descriptor->scroll_max_y;
    node.range_value = descriptor->range_value; node.range_min = descriptor->range_min; node.range_max = descriptor->range_max;
    node.selected_index = descriptor->selected_index;
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

extern "C" int gua_set_diagnostics_history_limit(gua_context_t* ctx, uint32_t history_limit)
{
    if (ctx == nullptr) return 0;
    const std::lock_guard lock(ctx->mutex);
    ctx->diagnostics_history_limit = history_limit;
    trim_history(ctx->operation_history, history_limit);
    trim_history(ctx->event_history, history_limit);
    ctx->diagnostics_json_cache.clear();
    return 1;
}

extern "C" int gua_set_diagnostics_environment_json(gua_context_t* ctx, const char* environment_json)
{
    if (ctx == nullptr || environment_json == nullptr) return 0;
    const std::string value(environment_json);
    const auto first = value.find_first_not_of(" \t\r\n");
    const auto last = value.find_last_not_of(" \t\r\n");
    if (first == std::string::npos || value[first] != '{' || value[last] != '}') return 0;
    const std::lock_guard lock(ctx->mutex);
    ctx->diagnostics_environment_json = value;
    ctx->diagnostics_json_cache.clear();
    return 1;
}

extern "C" const char* gua_get_diagnostics_json(gua_context_t* ctx)
{
    if (ctx == nullptr) return "{}";
    const std::lock_guard lock(ctx->mutex);
    ctx->diagnostics_json_cache = build_diagnostics_json(*ctx);
    return ctx->diagnostics_json_cache.c_str();
}

extern "C" int gua_copy_diagnostics_json(gua_context_t* ctx, char* out_json, int out_json_size)
{
    if (ctx == nullptr) return copy_json_string("{}", out_json, out_json_size);
    const std::lock_guard lock(ctx->mutex);
    return copy_json_string(build_diagnostics_json(*ctx), out_json, out_json_size);
}

extern "C" int gua_copy_version_json(char* out_json, int out_json_size)
{
    return copy_json_string(build_version_json(), out_json, out_json_size);
}

extern "C" int gua_clock_install(gua_context_t* ctx, double initial_time_ms, double step_ms)
{
    if (ctx == nullptr || !std::isfinite(initial_time_ms) || initial_time_ms < 0.0 ||
        !std::isfinite(step_ms) || step_ms <= 0.0 || !std::isfinite(initial_time_ms + step_ms) ||
        initial_time_ms + step_ms <= initial_time_ms)
        return GUA_CLOCK_ERROR_INVALID_ARGUMENT;
    const std::lock_guard lock(ctx->mutex);
    if (ctx->clock_installed) return GUA_CLOCK_ERROR_INVALID_STATE;
    ctx->clock_installed = true;
    ctx->clock_paused = false;
    ctx->clock_now_ms = initial_time_ms;
    ctx->clock_default_step_ms = step_ms;
    ctx->clock_pending_ms = 0.0;
    ctx->clock_pending_step_ms = step_ms;
    ctx->clock_pending_total_ms = 0.0;
    ctx->clock_pending_start_ms = initial_time_ms;
    ctx->clock_pending_elapsed_ms = 0.0;
    ctx->clock_pending_target_ms = initial_time_ms;
    ctx->clock_pending_step_count = 0;
    ctx->clock_pending_step_index = 0;
    ++ctx->clock_generation;
    return GUA_CLOCK_OK;
}

extern "C" int gua_clock_pause(gua_context_t* ctx)
{
    if (ctx == nullptr) return GUA_CLOCK_ERROR_INVALID_ARGUMENT;
    const std::lock_guard lock(ctx->mutex);
    if (!ctx->clock_installed) return GUA_CLOCK_ERROR_NOT_INSTALLED;
    if (ctx->clock_pending_ms > 0.0) return GUA_CLOCK_ERROR_INVALID_STATE;
    ctx->clock_paused = true;
    return GUA_CLOCK_OK;
}

extern "C" int gua_clock_run_for(gua_context_t* ctx, double duration_ms, double step_ms)
{
    if (ctx == nullptr || !std::isfinite(duration_ms) || duration_ms < 0.0 ||
        !std::isfinite(step_ms) || step_ms <= 0.0) return GUA_CLOCK_ERROR_INVALID_ARGUMENT;
    const std::lock_guard lock(ctx->mutex);
    if (!ctx->clock_installed) return GUA_CLOCK_ERROR_NOT_INSTALLED;
    if (!ctx->clock_paused || ctx->clock_pending_ms > 0.0) return GUA_CLOCK_ERROR_INVALID_STATE;
    if (duration_ms / step_ms > 1000000.0) return GUA_CLOCK_ERROR_EXECUTION_LIMIT;
    double target_ms = 0.0;
    unsigned long long step_count = 0;
    if (!prepare_clock_operation(ctx->clock_now_ms, duration_ms, step_ms, target_ms, step_count))
        return GUA_CLOCK_ERROR_INVALID_ARGUMENT;
    ctx->clock_pending_ms = duration_ms;
    ctx->clock_pending_step_ms = step_ms;
    ctx->clock_pending_total_ms = duration_ms;
    ctx->clock_pending_start_ms = ctx->clock_now_ms;
    ctx->clock_pending_elapsed_ms = 0.0;
    ctx->clock_pending_target_ms = target_ms;
    ctx->clock_pending_step_count = step_count;
    ctx->clock_pending_step_index = 0;
    ctx->clock_pending_operation_sequence = ctx->next_clock_operation_sequence++;
    if (duration_ms == 0.0) {
        ctx->clock_awaiting_frame_operation_sequence = std::max(
            ctx->clock_awaiting_frame_operation_sequence, ctx->clock_pending_operation_sequence);
        ctx->clock_pending_operation_sequence = 0;
    }
    return GUA_CLOCK_OK;
}

extern "C" int gua_clock_resume(gua_context_t* ctx)
{
    if (ctx == nullptr) return GUA_CLOCK_ERROR_INVALID_ARGUMENT;
    const std::lock_guard lock(ctx->mutex);
    if (!ctx->clock_installed) return GUA_CLOCK_ERROR_NOT_INSTALLED;
    if (ctx->clock_pending_ms > 0.0) return GUA_CLOCK_ERROR_INVALID_STATE;
    ctx->clock_paused = false;
    return GUA_CLOCK_OK;
}

extern "C" int gua_clock_advance(gua_context_t* ctx, double duration_ms)
{
    if (ctx == nullptr || !std::isfinite(duration_ms) || duration_ms < 0.0) return GUA_CLOCK_ERROR_INVALID_ARGUMENT;
    const std::lock_guard lock(ctx->mutex);
    if (!ctx->clock_installed) return GUA_CLOCK_ERROR_NOT_INSTALLED;
    if (ctx->clock_paused || ctx->clock_pending_ms > 0.0) return GUA_CLOCK_ERROR_INVALID_STATE;
    if (duration_ms / ctx->clock_default_step_ms > 1000000.0) return GUA_CLOCK_ERROR_EXECUTION_LIMIT;
    double target_ms = 0.0;
    unsigned long long step_count = 0;
    if (!prepare_clock_operation(
            ctx->clock_now_ms, duration_ms, ctx->clock_default_step_ms, target_ms, step_count))
        return GUA_CLOCK_ERROR_INVALID_ARGUMENT;
    ctx->clock_pending_ms = duration_ms;
    ctx->clock_pending_step_ms = ctx->clock_default_step_ms;
    ctx->clock_pending_total_ms = duration_ms;
    ctx->clock_pending_start_ms = ctx->clock_now_ms;
    ctx->clock_pending_elapsed_ms = 0.0;
    ctx->clock_pending_target_ms = target_ms;
    ctx->clock_pending_step_count = step_count;
    ctx->clock_pending_step_index = 0;
    ctx->clock_pending_operation_sequence = ctx->next_clock_operation_sequence++;
    if (duration_ms == 0.0) {
        ctx->clock_awaiting_frame_operation_sequence = std::max(
            ctx->clock_awaiting_frame_operation_sequence, ctx->clock_pending_operation_sequence);
        ctx->clock_pending_operation_sequence = 0;
    }
    return GUA_CLOCK_OK;
}

extern "C" int gua_clock_get_status(gua_context_t* ctx, gua_clock_status_t* out_status)
{
    if (ctx == nullptr || out_status == nullptr || out_status->struct_size < sizeof(gua_clock_status_t)) return 0;
    const std::lock_guard lock(ctx->mutex);
    *out_status = gua_clock_status_t { sizeof(gua_clock_status_t), ctx->clock_installed ? 1 : 0,
        ctx->clock_paused ? 1 : 0, ctx->clock_now_ms, ctx->clock_default_step_ms,
        ctx->clock_pending_ms, ctx->clock_generation };
    return 1;
}

extern "C" int gua_clock_get_operation_status(gua_context_t* ctx, gua_clock_operation_status_t* out_status)
{
    if (ctx == nullptr || out_status == nullptr || out_status->struct_size < sizeof(gua_clock_operation_status_t)) return 0;
    const std::lock_guard lock(ctx->mutex);
    *out_status = gua_clock_operation_status_t { sizeof(gua_clock_operation_status_t),
        ctx->next_clock_operation_sequence - 1, ctx->clock_pending_operation_sequence,
        ctx->clock_completed_operation_sequence };
    return 1;
}

extern "C" int gua_clock_copy_status_json(gua_context_t* ctx, char* out_json, int out_json_size)
{
    if (ctx == nullptr) return copy_json_string("{}", out_json, out_json_size);
    const std::lock_guard lock(ctx->mutex);
    std::ostringstream json;
    json.imbue(std::locale::classic());
    json << std::setprecision(std::numeric_limits<double>::max_digits10)
         << "{\"schemaVersion\":1,\"installed\":" << (ctx->clock_installed ? "true" : "false")
         << ",\"state\":\"" << (ctx->clock_paused ? "paused" : "running")
         << "\",\"nowMs\":" << ctx->clock_now_ms
         << ",\"defaultStepMs\":" << ctx->clock_default_step_ms
         << ",\"pendingMs\":" << ctx->clock_pending_ms
         << ",\"generation\":" << ctx->clock_generation
         << ",\"completedOperationSequence\":" << ctx->clock_completed_operation_sequence << '}';
    return copy_json_string(json.str(), out_json, out_json_size);
}

extern "C" int gua_clock_consume_step(gua_context_t* ctx, gua_clock_step_t* out_step)
{
    if (ctx == nullptr || out_step == nullptr || out_step->struct_size < sizeof(gua_clock_step_t)) return 0;
    const std::lock_guard lock(ctx->mutex);
    if (!ctx->clock_installed || ctx->clock_pending_ms <= 0.0) return 0;
    ++ctx->clock_pending_step_index;
    const bool final_step = ctx->clock_pending_step_index >= ctx->clock_pending_step_count;
    const double next_elapsed_ms = final_step ? ctx->clock_pending_total_ms :
        std::min(ctx->clock_pending_total_ms,
            ctx->clock_pending_step_ms * static_cast<double>(ctx->clock_pending_step_index));
    const double next_now_ms = final_step ? ctx->clock_pending_target_ms : ctx->clock_pending_start_ms + next_elapsed_ms;
    const double delta = next_now_ms - ctx->clock_now_ms;
    ctx->clock_pending_elapsed_ms = next_elapsed_ms;
    ctx->clock_pending_ms = final_step ? 0.0 : ctx->clock_pending_total_ms - next_elapsed_ms;
    ctx->clock_now_ms = next_now_ms;
    if (final_step) {
        ctx->clock_pending_total_ms = 0.0;
        ctx->clock_awaiting_frame_operation_sequence = std::max(
            ctx->clock_awaiting_frame_operation_sequence, ctx->clock_pending_operation_sequence);
        ctx->clock_pending_operation_sequence = 0;
    }
    *out_step = gua_clock_step_t { sizeof(gua_clock_step_t), delta, final_step ? 1 : 0, ctx->clock_generation };
    return 1;
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

    if (found->parent_id.size() >= sizeof(out_state->parent_id) ||
        found->text.size() >= sizeof(out_state->text) ||
        found->value.size() >= sizeof(out_state->value)) {
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

extern "C" int gua_get_node_state_v3(gua_context_t* ctx, const char* node_id, gua_node_state_v3_t* out_state)
{
    if (ctx == nullptr || node_id == nullptr || out_state == nullptr || out_state->struct_size < sizeof(gua_node_state_v3_t)) return 0;
    out_state->base.struct_size = sizeof(gua_node_state_v2_t);
    if (gua_get_node_state_v2(ctx, node_id, &out_state->base) == 0) return 0;
    const std::lock_guard lock(ctx->mutex);
    const auto found = std::find_if(ctx->nodes.begin(), ctx->nodes.end(), [&](const Node& node) { return node.id == node_id; });
    if (found == ctx->nodes.end()) return 0;
    out_state->caret_position = found->caret_position; out_state->selection_start = found->selection_start; out_state->selection_end = found->selection_end;
    out_state->scroll_x = found->scroll_x; out_state->scroll_y = found->scroll_y; out_state->scroll_max_x = found->scroll_max_x; out_state->scroll_max_y = found->scroll_max_y;
    out_state->range_value = found->range_value; out_state->range_min = found->range_min; out_state->range_max = found->range_max; out_state->selected_index = found->selected_index;
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
        return node.label == text ||
            ((node.known_mask & GUA_NODE_KNOWN_TEXT) != 0U && node.text == text);
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

extern "C" int gua_begin_world_frame(gua_context_t* ctx, const char* scene)
{
    if (ctx == nullptr || scene == nullptr || scene[0] == '\0') return 0;
    const std::lock_guard lock(ctx->mutex);
    if (ctx->world_frame_in_progress) return 0;
    ctx->staging_world_scene = scene;
    ctx->staging_world_objects.clear();
    ctx->world_frame_in_progress = true;
    ctx->staging_world_valid = true;
    return 1;
}

extern "C" int gua_register_world_object_v1(gua_context_t* ctx, const gua_world_object_descriptor_v1_t* descriptor)
{
    if (ctx == nullptr) return 0;
    const std::lock_guard lock(ctx->mutex);
    if (descriptor == nullptr || descriptor->struct_size < sizeof(gua_world_object_descriptor_v1_t)) {
        if (ctx->world_frame_in_progress) ctx->staging_world_valid = false;
        return 0;
    }
    const auto valid_kind = [](const char* kind) {
        if (kind == nullptr || kind[0] == '\0' || kind[0] < 'a' || kind[0] > 'z') return false;
        for (const char* current = kind + 1; *current != '\0'; ++current) {
            if (!((*current >= 'a' && *current <= 'z') || (*current >= '0' && *current <= '9') || *current == '_' || *current == '-' || *current == '.')) return false;
        }
        return true;
    };
    if (!ctx->world_frame_in_progress || !ctx->staging_world_valid || descriptor->id == nullptr || descriptor->id[0] == '\0' ||
        (descriptor->parent_id != nullptr && descriptor->parent_id[0] == '\0') ||
        (descriptor->domain_id != nullptr && descriptor->domain_id[0] == '\0') ||
        (descriptor->related_ui_node_id != nullptr && descriptor->related_ui_node_id[0] == '\0') ||
        !valid_kind(descriptor->kind) || descriptor->label == nullptr ||
        (descriptor->space != GUA_WORLD_SPACE_2D && descriptor->space != GUA_WORLD_SPACE_3D) ||
        !std::isfinite(descriptor->position_x) || !std::isfinite(descriptor->position_y) || !std::isfinite(descriptor->position_z) ||
        (descriptor->agent_exposure != GUA_AGENT_EXPOSURE_AUTO && descriptor->agent_exposure != GUA_AGENT_EXPOSURE_PRIVATE) ||
        (descriptor->tag_count != 0 && descriptor->tags == nullptr) ||
        (descriptor->state_value_count != 0 && descriptor->state_values == nullptr)) {
        ctx->staging_world_valid = false;
        return 0;
    }
    if (std::any_of(ctx->staging_world_objects.begin(), ctx->staging_world_objects.end(), [&](const auto& object) { return object.id == descriptor->id; })) {
        ctx->staging_world_valid = false;
        return 0;
    }
    WorldObject object;
    object.id = descriptor->id;
    object.parent_id = descriptor->parent_id == nullptr ? "" : descriptor->parent_id;
    object.kind = descriptor->kind;
    object.label = descriptor->label;
    object.description = descriptor->description == nullptr ? "" : descriptor->description;
    object.space = descriptor->space;
    object.x = descriptor->position_x; object.y = descriptor->position_y; object.z = descriptor->position_z;
    object.visible_to_player = descriptor->visible_to_player != 0;
    object.active = descriptor->active != 0;
    object.agent_exposure = descriptor->agent_exposure;
    object.domain_id = descriptor->domain_id == nullptr ? "" : descriptor->domain_id;
    object.related_ui_node_id = descriptor->related_ui_node_id == nullptr ? "" : descriptor->related_ui_node_id;
    std::unordered_set<std::string> unique;
    for (uint32_t i = 0; i < descriptor->tag_count; ++i) {
        if (descriptor->tags[i] == nullptr || descriptor->tags[i][0] == '\0' || !unique.insert(descriptor->tags[i]).second) { ctx->staging_world_valid = false; return 0; }
        object.tags.emplace_back(descriptor->tags[i]);
    }
    unique.clear();
    for (uint32_t i = 0; i < descriptor->state_value_count; ++i) {
        const auto& value = descriptor->state_values[i];
        if (value.struct_size < sizeof(gua_world_state_value_v1_t) || value.key == nullptr || value.key[0] == '\0' ||
            value.type < GUA_WORLD_VALUE_NULL || value.type > GUA_WORLD_VALUE_BOOLEAN ||
            (value.type == GUA_WORLD_VALUE_STRING && value.string_value == nullptr) ||
            (value.type == GUA_WORLD_VALUE_NUMBER && !std::isfinite(value.number_value)) || !unique.insert(value.key).second) {
            ctx->staging_world_valid = false; return 0;
        }
        object.state.push_back(WorldStateValue { value.key, value.type, value.string_value == nullptr ? "" : value.string_value,
            value.number_value, value.bool_value != 0 });
    }
    std::sort(object.state.begin(), object.state.end(), [](const auto& left, const auto& right) {
        return left.key < right.key;
    });
    ctx->staging_world_objects.push_back(std::move(object));
    return 1;
}

extern "C" int gua_end_world_frame(gua_context_t* ctx)
{
    if (ctx == nullptr) return 0;
    const std::lock_guard lock(ctx->mutex);
    if (!ctx->world_frame_in_progress || !ctx->staging_world_valid) {
        ctx->staging_world_objects.clear(); ctx->world_frame_in_progress = false; ctx->staging_world_valid = true; return 0;
    }
    const auto reject_frame = [&]() {
        ctx->staging_world_objects.clear();
        ctx->world_frame_in_progress = false;
        return 0;
    };
    std::unordered_map<std::string, std::size_t> world_indices;
    world_indices.reserve(ctx->staging_world_objects.size());
    for (std::size_t index = 0; index < ctx->staging_world_objects.size(); ++index)
        world_indices.emplace(ctx->staging_world_objects[index].id, index);
    std::vector<unsigned char> visit_state(ctx->staging_world_objects.size(), 0);
    std::vector<std::size_t> path;
    path.reserve(ctx->staging_world_objects.size());
    for (std::size_t start = 0; start < ctx->staging_world_objects.size(); ++start) {
        if (visit_state[start] == 2) continue;
        path.clear();
        std::size_t current = start;
        while (true) {
            if (visit_state[current] == 1) return reject_frame();
            if (visit_state[current] == 2) break;
            visit_state[current] = 1;
            path.push_back(current);
            const auto& parent_id = ctx->staging_world_objects[current].parent_id;
            if (parent_id.empty()) break;
            const auto parent = world_indices.find(parent_id);
            if (parent == world_indices.end()) return reject_frame();
            current = parent->second;
        }
        for (const auto index : path) visit_state[index] = 2;
    }
    const std::string semantic = build_world_semantic_json(ctx->staging_world_scene, ctx->staging_world_objects);
    const std::string player_semantic = build_world_semantic_json(ctx->staging_world_scene,
        project_world_objects(ctx->staging_world_objects, GUA_OBSERVATION_PROFILE_PLAYER));
    ctx->world_scene.swap(ctx->staging_world_scene);
    ctx->world_objects.swap(ctx->staging_world_objects);
    ctx->staging_world_objects.clear(); ctx->world_frame_in_progress = false;
    ++ctx->world_frame_sequence;
    if (semantic != ctx->previous_world_snapshot) { ++ctx->world_revision; ctx->previous_world_snapshot = semantic; }
    if (player_semantic != ctx->previous_player_world_snapshot) {
        ++ctx->player_world_revision;
        ctx->previous_player_world_snapshot = player_semantic;
    }
    ctx->world_json_cache_debug.clear(); ctx->world_json_cache_player.clear();
    return 1;
}

extern "C" int gua_abort_world_frame(gua_context_t* ctx)
{
    if (ctx == nullptr) return 0;
    const std::lock_guard lock(ctx->mutex);
    if (!ctx->world_frame_in_progress) return 0;
    ctx->staging_world_objects.clear();
    ctx->staging_world_scene = "unknown";
    ctx->world_frame_in_progress = false;
    ctx->staging_world_valid = true;
    return 1;
}

extern "C" int gua_copy_world_object_tree_json(gua_context_t* ctx, int observation_profile, char* out_json, int out_json_size)
{
    if (ctx == nullptr || (observation_profile != GUA_OBSERVATION_PROFILE_DEBUG && observation_profile != GUA_OBSERVATION_PROFILE_PLAYER)) return 0;
    const std::lock_guard lock(ctx->mutex);
    return copy_json_string(build_world_tree_json(ctx->world_scene, ctx->world_objects, ctx->session_epoch,
        ctx->world_frame_sequence, observation_profile == GUA_OBSERVATION_PROFILE_PLAYER ? ctx->player_world_revision : ctx->world_revision,
        observation_profile), out_json, out_json_size);
}

extern "C" int gua_query_world_objects_json(gua_context_t* ctx, const gua_world_selector_v1_t* selector, int observation_profile, char* out_json, int out_json_size)
{
    if (ctx == nullptr || selector == nullptr || selector->struct_size < sizeof(gua_world_selector_v1_t) ||
        (observation_profile != GUA_OBSERVATION_PROFILE_DEBUG && observation_profile != GUA_OBSERVATION_PROFILE_PLAYER) ||
        (selector->id != nullptr && selector->id[0] == '\0') ||
        (selector->kind != nullptr && selector->kind[0] == '\0') ||
        (selector->label != nullptr && selector->label[0] == '\0') ||
        (selector->tag != nullptr && selector->tag[0] == '\0') ||
        (selector->parent_id != nullptr && selector->parent_id[0] == '\0') ||
        (selector->direct_child != 0 && selector->direct_child != 1) ||
        (selector->direct_child == 1 && (selector->parent_id == nullptr || selector->parent_id[0] == '\0')))
        return copy_json_string("{\"valid\":false,\"error\":\"invalid world selector\",\"matches\":[]}", out_json, out_json_size);
    if (selector->state != nullptr && (selector->state->struct_size < sizeof(gua_world_state_value_v1_t) ||
        selector->state->key == nullptr || selector->state->key[0] == '\0' ||
        selector->state->type < GUA_WORLD_VALUE_NULL || selector->state->type > GUA_WORLD_VALUE_BOOLEAN ||
        (selector->state->type == GUA_WORLD_VALUE_STRING && selector->state->string_value == nullptr) ||
        (selector->state->type == GUA_WORLD_VALUE_NUMBER && !std::isfinite(selector->state->number_value))))
        return copy_json_string("{\"valid\":false,\"error\":\"invalid world selector state\",\"matches\":[]}", out_json, out_json_size);
    const std::lock_guard lock(ctx->mutex);
    return copy_json_string(build_world_query_json(ctx->world_objects, *selector, observation_profile), out_json, out_json_size);
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
    while (true) {
        const auto legacy_event = std::find_if(ctx->events.begin(), ctx->events.end(), [](const Event& event) {
            return event.action == GUA_ACTION_CLICK || event.action == GUA_ACTION_FOCUS;
        });
        if (legacy_event == ctx->events.end()) {
            return 0;
        }

        const Event event = *legacy_event;
        ctx->events.erase(legacy_event);
        if (event.status != GUA_ACTION_STATUS_SUCCEEDED) {
            continue;
        }

        out_event->type = event.action;
        std::snprintf(out_event->node_id, sizeof(out_event->node_id), "%s", event.node_id.c_str());
        return 1;
    }
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
		(descriptor->action == GUA_ACTION_SCROLL && (!std::isfinite(descriptor->delta_x) || !std::isfinite(descriptor->delta_y)))) {
        return GUA_ACTION_ERROR_INVALID_VALUE;
    }

    const std::lock_guard lock(ctx->mutex);
    if (!node_id.empty()) {
		const auto node = std::find_if(ctx->nodes.begin(), ctx->nodes.end(), [&](const Node& candidate) { return candidate.id == node_id; });
		if (node == ctx->nodes.end()) return GUA_ACTION_ERROR_NODE_NOT_FOUND;
		if (descriptor->action == GUA_ACTION_SELECT && value.empty() && node->role != "listitem" && node->role != "tab") {
			return GUA_ACTION_ERROR_INVALID_VALUE;
		}
        if (!node->visible) return GUA_ACTION_ERROR_HIDDEN;
        if (!node->enabled) return GUA_ACTION_ERROR_DISABLED;
        if (!supports_action(*node, descriptor->action)) return GUA_ACTION_ERROR_UNSUPPORTED;
    }

    const unsigned long long request_id = ctx->next_request_id++;
    ctx->action_requests.push_back(ActionRequest {
        request_id, descriptor->action, node_id, value, descriptor->delta_x, descriptor->delta_y,
        descriptor->bool_value, key, descriptor->modifiers, descriptor->sensitive != 0, descriptor->scroll_unit
    });
    append_history(*ctx, ctx->operation_history, "enqueued", request_id, descriptor->action, node_id,
        GUA_ACTION_ACCEPTED, 0, value, descriptor->sensitive != 0, descriptor->delta_x, descriptor->delta_y,
        descriptor->bool_value, key, descriptor->modifiers, descriptor->scroll_unit);
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
    append_history(*ctx, ctx->operation_history, "consumed", value.request_id, value.action, value.node_id,
        GUA_ACTION_ACCEPTED, 0, value.value, value.sensitive, value.delta_x, value.delta_y,
        value.bool_value, value.key, value.modifiers, value.scroll_unit);
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
        ctx->session_epoch,
        ctx->frame_sequence,
        ctx->revision,
    });
    append_history(*ctx, ctx->event_history, "observed", result->request_id, result->action,
        result->node_id != nullptr ? result->node_id : "", result->status, result->error_code,
        result->value != nullptr ? result->value : "", result->sensitive != 0);
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

extern "C" int gua_poll_event_v3(gua_context_t* ctx, gua_event_v3_t* out_event)
{
    if (ctx == nullptr || out_event == nullptr || out_event->struct_size < sizeof(gua_event_v3_t)) return 0;
    const std::lock_guard lock(ctx->mutex);
    if (ctx->events.empty()) return 0;
    const Event event = ctx->events.front(); ctx->events.pop_front();
    out_event->base.struct_size = sizeof(gua_event_v2_t);
    out_event->base.request_id = event.request_id; out_event->base.action = event.action; out_event->base.status = event.status; out_event->base.error_code = event.error_code;
    std::snprintf(out_event->base.node_id, sizeof(out_event->base.node_id), "%s", event.node_id.c_str());
    std::snprintf(out_event->base.value, sizeof(out_event->base.value), "%s", event.value.c_str()); out_event->base.sensitive = event.sensitive ? 1 : 0;
    out_event->session_epoch = event.session_epoch; out_event->frame_sequence = event.frame_sequence; out_event->revision = event.revision;
    return 1;
}

extern "C" int gua_poll_event_v3_for_request(gua_context_t* ctx, uint64_t request_id, gua_event_v3_t* out_event)
{
    if (ctx == nullptr || request_id == 0 || out_event == nullptr || out_event->struct_size < sizeof(gua_event_v3_t)) return 0;
    const std::lock_guard lock(ctx->mutex);
    const auto found = std::find_if(ctx->events.begin(), ctx->events.end(), [&](const Event& event) { return event.request_id == request_id; });
    if (found == ctx->events.end()) return 0;
    const Event event = *found; ctx->events.erase(found);
    out_event->base.struct_size = sizeof(gua_event_v2_t);
    out_event->base.request_id = event.request_id; out_event->base.action = event.action; out_event->base.status = event.status; out_event->base.error_code = event.error_code;
    std::snprintf(out_event->base.node_id, sizeof(out_event->base.node_id), "%s", event.node_id.c_str());
    std::snprintf(out_event->base.value, sizeof(out_event->base.value), "%s", event.value.c_str()); out_event->base.sensitive = event.sensitive ? 1 : 0;
    out_event->session_epoch = event.session_epoch; out_event->frame_sequence = event.frame_sequence; out_event->revision = event.revision;
    return 1;
}

extern "C" int gua_get_context_status(gua_context_t* ctx, gua_context_status_t* out_status)
{
    constexpr uint32_t legacy_size = static_cast<uint32_t>(offsetof(gua_context_status_t, world_frame_sequence));
    if (ctx == nullptr || out_status == nullptr || out_status->struct_size < legacy_size) return 0;
    const uint32_t output_size = out_status->struct_size;
    const std::lock_guard lock(ctx->mutex);
    out_status->session_epoch = ctx->session_epoch;
    out_status->frame_sequence = ctx->frame_sequence;
    out_status->revision = ctx->revision;
    out_status->node_count = static_cast<uint32_t>(ctx->nodes.size());
    out_status->pending_request_count = static_cast<uint32_t>(ctx->action_requests.size());
    out_status->in_flight_request_count = static_cast<uint32_t>(ctx->consumed_requests.size());
    out_status->unconsumed_event_count = static_cast<uint32_t>(ctx->events.size());
    out_status->log_count = static_cast<uint32_t>(ctx->logs.size());
    out_status->has_screenshot = ctx->screenshot.data_uri.empty() ? 0 : 1;
    out_status->first_pending_action = 0;
    out_status->first_pending_node_id[0] = '\0';
    out_status->first_event_action = 0;
    out_status->first_event_node_id[0] = '\0';
    gua_reset_report_t summary { sizeof(gua_reset_report_t) };
    fill_reset_summary(*ctx, summary);
    out_status->first_pending_action = summary.first_pending_action;
    std::snprintf(out_status->first_pending_node_id, sizeof(out_status->first_pending_node_id), "%s", summary.first_pending_node_id);
    out_status->first_event_action = summary.first_event_action;
    std::snprintf(out_status->first_event_node_id, sizeof(out_status->first_event_node_id), "%s", summary.first_event_node_id);
    if (output_size >= sizeof(gua_context_status_t)) {
        out_status->world_frame_sequence = ctx->world_frame_sequence;
        out_status->world_revision = ctx->world_revision;
        out_status->world_object_count = static_cast<uint32_t>(ctx->world_objects.size());
    }
    return 1;
}

extern "C" int gua_reset_context(gua_context_t* ctx, const gua_reset_options_t* options, gua_reset_report_t* out_report)
{
    constexpr uint32_t legacy_options_size = static_cast<uint32_t>(offsetof(gua_reset_options_t, flags_version));
    constexpr uint32_t flags_version_size = static_cast<uint32_t>(offsetof(gua_reset_options_t, flags_version) + sizeof(uint32_t));
    constexpr uint32_t legacy_report_size = static_cast<uint32_t>(offsetof(gua_reset_report_t, discarded_world_object_count));
    if (ctx == nullptr || options == nullptr || options->struct_size < legacy_options_size ||
        out_report == nullptr || out_report->struct_size < legacy_report_size) return GUA_RESET_ERROR_INVALID_ARGUMENT;
    const uint32_t flags_version = options->struct_size >= flags_version_size
        ? options->flags_version : GUA_RESET_FLAGS_VERSION_LEGACY;
    if (flags_version > GUA_RESET_FLAGS_VERSION_CURRENT) return GUA_RESET_ERROR_INVALID_ARGUMENT;
    uint32_t reset_flags = options->flags;
    const uint32_t legacy_all = GUA_RESET_DEFAULT | GUA_RESET_LOGS | GUA_RESET_SCREENSHOT;
    const uint32_t clock_all = GUA_RESET_DEFAULT_V2 | GUA_RESET_LOGS | GUA_RESET_SCREENSHOT;
    if (flags_version == GUA_RESET_FLAGS_VERSION_LEGACY) {
        if (reset_flags == GUA_RESET_DEFAULT || reset_flags == legacy_all) reset_flags |= GUA_RESET_CLOCK;
        if (reset_flags == GUA_RESET_DEFAULT_V2 || reset_flags == clock_all) reset_flags |= GUA_RESET_WORLD_OBJECTS;
    }
    if (flags_version == GUA_RESET_FLAGS_VERSION_V1 &&
        (reset_flags == GUA_RESET_DEFAULT_V2 || reset_flags == clock_all))
        reset_flags |= GUA_RESET_WORLD_OBJECTS;
    const uint32_t known_flags = GUA_RESET_NODES | GUA_RESET_REQUESTS | GUA_RESET_EVENTS | GUA_RESET_HISTORY | GUA_RESET_LOGS | GUA_RESET_SCREENSHOT | GUA_RESET_CLOCK | GUA_RESET_WORLD_OBJECTS;
    if ((reset_flags & ~known_flags) != 0U) return GUA_RESET_ERROR_INVALID_ARGUMENT;

    const std::lock_guard lock(ctx->mutex);
    const uint32_t output_size = out_report->struct_size;
    std::memset(out_report, 0, output_size);
    out_report->struct_size = output_size;
    out_report->previous_session_epoch = ctx->session_epoch;
    out_report->session_epoch = ctx->session_epoch;
    out_report->pending_request_count = static_cast<uint32_t>(ctx->action_requests.size());
    out_report->in_flight_request_count = static_cast<uint32_t>(ctx->consumed_requests.size());
    out_report->unconsumed_event_count = static_cast<uint32_t>(ctx->events.size());
    fill_reset_summary(*ctx, *out_report);

    if (options->expected_session_epoch != 0 && options->expected_session_epoch != ctx->session_epoch) {
        out_report->result = GUA_RESET_ERROR_STALE_EPOCH;
        return out_report->result;
    }
    const bool dirty_requests = (reset_flags & GUA_RESET_REQUESTS) != 0U &&
        (!ctx->action_requests.empty() || !ctx->consumed_requests.empty());
    const bool dirty_events = (reset_flags & GUA_RESET_EVENTS) != 0U && !ctx->events.empty();
    if (options->strict != 0 && (dirty_requests || dirty_events)) {
        out_report->result = GUA_RESET_ERROR_DIRTY;
        return out_report->result;
    }

    out_report->discarded_node_count = (reset_flags & GUA_RESET_NODES) != 0U ? static_cast<uint32_t>(ctx->nodes.size()) : 0;
    out_report->discarded_pending_request_count = (reset_flags & GUA_RESET_REQUESTS) != 0U ? static_cast<uint32_t>(ctx->action_requests.size()) : 0;
    out_report->discarded_in_flight_request_count = (reset_flags & GUA_RESET_REQUESTS) != 0U ? static_cast<uint32_t>(ctx->consumed_requests.size()) : 0;
    out_report->discarded_event_count = (reset_flags & GUA_RESET_EVENTS) != 0U ? static_cast<uint32_t>(ctx->events.size()) : 0;
    out_report->discarded_log_count = (reset_flags & GUA_RESET_LOGS) != 0U ? static_cast<uint32_t>(ctx->logs.size()) : 0;
    out_report->discarded_screenshot = (reset_flags & GUA_RESET_SCREENSHOT) != 0U && !ctx->screenshot.data_uri.empty() ? 1 : 0;
    if (output_size >= sizeof(gua_reset_report_t))
        out_report->discarded_world_object_count = (reset_flags & GUA_RESET_WORLD_OBJECTS) != 0U ? static_cast<uint32_t>(ctx->world_objects.size()) : 0;

    if ((reset_flags & GUA_RESET_NODES) != 0U) {
        ctx->screen = "unknown";
        ctx->nodes.clear();
        ctx->staging_screen = "unknown";
        ctx->staging_nodes.clear();
        ctx->frame_in_progress = false;
        ctx->staging_valid = true;
    }
    if ((reset_flags & GUA_RESET_REQUESTS) != 0U) {
        ctx->action_requests.clear();
        ctx->consumed_requests.clear();
    }
    if ((reset_flags & GUA_RESET_EVENTS) != 0U) ctx->events.clear();
    if ((reset_flags & GUA_RESET_HISTORY) != 0U) {
        ctx->operation_history.clear();
        ctx->event_history.clear();
        ctx->next_history_sequence = 1;
        ctx->diagnostics_history_started_at = std::chrono::steady_clock::now();
        ctx->diagnostics_json_cache.clear();
    }
    if ((reset_flags & GUA_RESET_LOGS) != 0U) {
        ctx->logs.clear();
        ctx->next_log_sequence = 1;
        ctx->logs_json_cache.clear();
    }
    if ((reset_flags & GUA_RESET_SCREENSHOT) != 0U) {
        ctx->screenshot = Screenshot {};
        ctx->screenshot_json_cache.clear();
    }
    if ((reset_flags & GUA_RESET_CLOCK) != 0U) {
        ctx->clock_installed = false; ctx->clock_paused = false; ctx->clock_now_ms = 0.0;
        ctx->clock_default_step_ms = default_clock_step_ms;
        ctx->clock_pending_ms = 0.0; ctx->clock_pending_total_ms = 0.0; ctx->clock_pending_elapsed_ms = 0.0;
        ctx->clock_pending_target_ms = 0.0; ctx->clock_pending_step_count = 0; ctx->clock_pending_step_index = 0;
        ctx->clock_pending_operation_sequence = 0; ctx->clock_awaiting_frame_operation_sequence = 0;
        ctx->clock_completed_operation_sequence = ctx->next_clock_operation_sequence - 1;
        ++ctx->clock_generation;
    }
    ctx->game_input_requests.clear();
    ctx->game_input_cleanup_requests.clear();
    for (const auto owner_id : ctx->game_input_owners) {
        ctx->game_input_cleanup_requests.push_back(GameInputRequest {
            ctx->next_game_input_request_id++, owner_id, GUA_GAME_INPUT_CLEANUP, GUA_GAME_INPUT_RELEASE_ALL,
            "all", "null", 0.0, 0.0, 0, 0, false });
    }
    ctx->held_game_inputs.clear();
    ctx->consumed_game_input_requests.clear();
    ctx->game_input_results.clear();
    if ((reset_flags & GUA_RESET_WORLD_OBJECTS) != 0U) {
        ctx->world_scene = "unknown"; ctx->world_objects.clear(); ctx->staging_world_scene = "unknown";
        ctx->staging_world_objects.clear(); ctx->world_frame_in_progress = false; ctx->staging_world_valid = true;
        ctx->world_frame_sequence = 0; ctx->world_revision = 0; ctx->player_world_revision = 0;
        ctx->previous_world_snapshot.clear(); ctx->previous_player_world_snapshot.clear();
        ctx->world_json_cache_debug.clear(); ctx->world_json_cache_player.clear();
    }
    ctx->frame_sequence = 0;
    ctx->revision = 0;
    ctx->previous_semantic_snapshot.clear();
    ctx->json_cache.clear();
    ctx->world_frame_sequence = 0;
    ctx->world_revision = 0;
    ctx->player_world_revision = 0;
    ctx->world_json_cache_debug.clear();
    ctx->world_json_cache_player.clear();
    ++ctx->session_epoch;
    ctx->next_request_id = 1;
    out_report->session_epoch = ctx->session_epoch;
    out_report->result = GUA_RESET_SUCCEEDED;
    return out_report->result;
}

extern "C" int gua_begin_game_input_frame(gua_context_t* ctx, const char* input_context)
{
    if (ctx == nullptr || input_context == nullptr || input_context[0] == '\0') return 0;
    const std::lock_guard lock(ctx->mutex);
    ctx->staging_game_input_context = input_context;
    ctx->staging_game_input_actions.clear();
    ctx->game_input_frame_in_progress = true;
    ctx->game_input_staging_valid = true;
    return 1;
}

extern "C" int gua_register_game_input_action_v1(gua_context_t* ctx, const gua_game_input_action_descriptor_v1_t* descriptor)
{
    if (ctx == nullptr || descriptor == nullptr || descriptor->struct_size < sizeof(*descriptor)) return 0;
    const std::lock_guard lock(ctx->mutex);
    if (!ctx->game_input_frame_in_progress || descriptor->id == nullptr || !valid_game_input_id(descriptor->id) ||
        descriptor->value_type < GUA_GAME_INPUT_BUTTON || descriptor->value_type > GUA_GAME_INPUT_TEXT ||
        (descriptor->has_range != 0 && (!std::isfinite(descriptor->minimum) || !std::isfinite(descriptor->maximum) || descriptor->minimum > descriptor->maximum))) {
        ctx->game_input_staging_valid = false;
        return 0;
    }
    if (std::any_of(ctx->staging_game_input_actions.begin(), ctx->staging_game_input_actions.end(),
        [&](const auto& action) { return action.id == descriptor->id; })) {
        ctx->game_input_staging_valid = false;
        return 0;
    }
    const std::string bindings = descriptor->bindings_json != nullptr ? descriptor->bindings_json : "[]";
    if (!valid_json_string_array(bindings)) {
        ctx->game_input_staging_valid = false;
        return 0;
    }
    const std::string risk = descriptor->risk != nullptr ? descriptor->risk : "safe";
    if (risk != "safe" && risk != "caution" && risk != "dangerous") {
        ctx->game_input_staging_valid = false;
        return 0;
    }
    ctx->staging_game_input_actions.push_back(GameInputAction {
        descriptor->id, descriptor->description != nullptr ? descriptor->description : "", descriptor->value_type,
        descriptor->minimum, descriptor->maximum, descriptor->has_range != 0, descriptor->holdable != 0,
        descriptor->active != 0, bindings, risk, descriptor->requires_confirmation != 0 });
    return 1;
}

extern "C" int gua_end_game_input_frame(gua_context_t* ctx)
{
    if (ctx == nullptr) return 0;
    const std::lock_guard lock(ctx->mutex);
    if (!ctx->game_input_frame_in_progress || !ctx->game_input_staging_valid) {
        ctx->staging_game_input_actions.clear();
        ctx->game_input_frame_in_progress = false;
        ctx->game_input_staging_valid = true;
        return 0;
    }
    const std::string snapshot = build_game_input_semantic_snapshot(
        ctx->staging_game_input_context, ctx->staging_game_input_actions);
    ctx->game_input_context.swap(ctx->staging_game_input_context);
    ctx->game_input_actions.swap(ctx->staging_game_input_actions);
    ctx->staging_game_input_actions.clear();
    ctx->game_input_frame_in_progress = false;
    if (snapshot != ctx->previous_game_input_snapshot) {
        ++ctx->game_input_revision;
        ctx->previous_game_input_snapshot = snapshot;
    }
    return 1;
}

extern "C" int gua_abort_game_input_frame(gua_context_t* ctx)
{
    if (ctx == nullptr) return 0;
    const std::lock_guard lock(ctx->mutex);
    if (!ctx->game_input_frame_in_progress) return 0;
    ctx->staging_game_input_actions.clear();
    ctx->game_input_frame_in_progress = false;
    ctx->game_input_staging_valid = true;
    return 1;
}

extern "C" int gua_copy_game_input_actions_json(gua_context_t* ctx, char* out_json, int out_json_size)
{
    if (ctx == nullptr) return 0;
    const std::lock_guard lock(ctx->mutex);
    return copy_json_string(build_game_input_actions_json(*ctx), out_json, out_json_size);
}

extern "C" uint64_t gua_create_game_input_owner(gua_context_t* ctx)
{
    if (ctx == nullptr) return 0;
    const std::lock_guard lock(ctx->mutex);
    const auto owner = ctx->next_game_input_owner_id++;
    ctx->game_input_owners.insert(owner);
    return owner;
}

extern "C" int gua_release_game_input_owner(gua_context_t* ctx, uint64_t owner_id)
{
    if (ctx == nullptr || owner_id == 0) return 0;
    const std::lock_guard lock(ctx->mutex);
    if (ctx->game_input_owners.erase(owner_id) == 0) return 0;
    ctx->game_input_requests.erase(std::remove_if(ctx->game_input_requests.begin(), ctx->game_input_requests.end(),
        [&](const auto& request) { return request.owner_id == owner_id; }), ctx->game_input_requests.end());
    ctx->game_input_cleanup_requests.erase(std::remove_if(ctx->game_input_cleanup_requests.begin(), ctx->game_input_cleanup_requests.end(),
        [&](const auto& request) { return request.owner_id == owner_id; }), ctx->game_input_cleanup_requests.end());
    ctx->consumed_game_input_requests.erase(std::remove_if(ctx->consumed_game_input_requests.begin(), ctx->consumed_game_input_requests.end(),
        [&](const auto& request) { return request.owner_id == owner_id; }), ctx->consumed_game_input_requests.end());
    ctx->game_input_results.erase(std::remove_if(ctx->game_input_results.begin(), ctx->game_input_results.end(),
        [&](const auto& result) { return result.owner_id == owner_id; }), ctx->game_input_results.end());
    ctx->game_input_cleanup_requests.push_back(GameInputRequest { ctx->next_game_input_request_id++, owner_id,
        GUA_GAME_INPUT_CLEANUP, GUA_GAME_INPUT_RELEASE_ALL, "all", "null", 0, 0, 0, 0, false });
    ctx->held_game_inputs.erase(std::remove_if(ctx->held_game_inputs.begin(), ctx->held_game_inputs.end(),
        [&](const auto& held) { return held.owner_id == owner_id; }), ctx->held_game_inputs.end());
    return 1;
}

extern "C" int gua_enqueue_game_input(gua_context_t* ctx,
    const gua_game_input_request_descriptor_v1_t* descriptor, uint64_t* out_request_id)
{
    if (ctx == nullptr || descriptor == nullptr || descriptor->struct_size < sizeof(*descriptor) ||
        descriptor->owner_id == 0 || descriptor->kind < GUA_GAME_INPUT_SEMANTIC || descriptor->kind > GUA_GAME_INPUT_CLEANUP ||
        descriptor->operation < GUA_GAME_INPUT_PRESS || descriptor->operation > GUA_GAME_INPUT_RELEASE_ALL ||
        descriptor->lease_ms > 60000 || !std::isfinite(descriptor->x) || !std::isfinite(descriptor->y))
        return GUA_GAME_INPUT_ERROR_INVALID_ARGUMENT;
    const std::lock_guard lock(ctx->mutex);
    if (!ctx->game_input_owners.contains(descriptor->owner_id)) return GUA_GAME_INPUT_ERROR_INVALID_ARGUMENT;
    const std::string target = descriptor->target != nullptr ? descriptor->target : "";
    const std::string value = descriptor->value_json != nullptr ? descriptor->value_json : "null";
    if (target.size() >= 128 || !valid_json_value(value)) return GUA_GAME_INPUT_ERROR_INVALID_VALUE;
    if (descriptor->kind == GUA_GAME_INPUT_SEMANTIC) {
        const int validation = validate_semantic_game_input(ctx->game_input_actions, descriptor->operation, target, value);
        if (validation != GUA_GAME_INPUT_OK) return validation;
    } else if (descriptor->kind == GUA_GAME_INPUT_KEYBOARD) {
        if (!one_of(descriptor->operation, { GUA_GAME_INPUT_PRESS, GUA_GAME_INPUT_DOWN, GUA_GAME_INPUT_UP }) ||
            !valid_keyboard_code(target)) return GUA_GAME_INPUT_ERROR_INVALID_VALUE;
    } else if (descriptor->kind == GUA_GAME_INPUT_POINTER) {
        if ((descriptor->operation == GUA_GAME_INPUT_DOWN || descriptor->operation == GUA_GAME_INPUT_UP) &&
            !one_of(target, { "primary", "secondary", "auxiliary", "back", "forward" }))
            return GUA_GAME_INPUT_ERROR_INVALID_VALUE;
        if (descriptor->operation == GUA_GAME_INPUT_MOVE_ABSOLUTE && target == "absolute:viewport_normalized" &&
            (descriptor->x < 0.0 || descriptor->x > 1.0 || descriptor->y < 0.0 || descriptor->y > 1.0))
            return GUA_GAME_INPUT_ERROR_INVALID_VALUE;
    } else if (descriptor->kind == GUA_GAME_INPUT_GAMEPAD) {
        if ((descriptor->operation == GUA_GAME_INPUT_DOWN || descriptor->operation == GUA_GAME_INPUT_UP) &&
            !one_of(target, { "south", "east", "west", "north", "left_shoulder", "right_shoulder", "left_trigger",
                "right_trigger", "back", "start", "left_stick", "right_stick", "dpad_up", "dpad_down", "dpad_left", "dpad_right" }))
            return GUA_GAME_INPUT_ERROR_INVALID_VALUE;
        if (descriptor->operation == GUA_GAME_INPUT_SET) {
            if (!one_of(target, { "left_stick_x", "left_stick_y", "right_stick_x", "right_stick_y", "left_trigger", "right_trigger" }))
                return GUA_GAME_INPUT_ERROR_INVALID_VALUE;
            char* end = nullptr; const double axis = std::strtod(value.c_str(), &end);
            if (end != value.c_str() + value.size() || !std::isfinite(axis) || axis < -1.0 || axis > 1.0)
                return GUA_GAME_INPUT_ERROR_INVALID_VALUE;
        }
    }
    const unsigned int lease = descriptor->lease_ms == 0 ? 5000U : descriptor->lease_ms;
    const auto request_id = ctx->next_game_input_request_id++;
    ctx->game_input_requests.push_back(GameInputRequest { request_id, descriptor->owner_id, descriptor->kind,
        descriptor->operation, target, value, descriptor->x, descriptor->y,
        lease, descriptor->device_index, descriptor->sensitive != 0 });
    if (out_request_id != nullptr) *out_request_id = request_id;
    return GUA_GAME_INPUT_OK;
}

extern "C" int gua_consume_game_input_request(gua_context_t* ctx, gua_game_input_request_v1_t* out_request)
{
    if (ctx == nullptr || out_request == nullptr || out_request->struct_size < sizeof(*out_request)) return 0;
    const std::lock_guard lock(ctx->mutex);
    while (!ctx->game_input_requests.empty()) {
        const auto& request = ctx->game_input_requests.front();
        if (request.kind != GUA_GAME_INPUT_SEMANTIC) break;
        const int validation = validate_semantic_game_input(ctx->game_input_actions, request.operation,
            request.target, request.value_json);
        if (validation == GUA_GAME_INPUT_OK) break;
        if (ctx->game_input_results.size() >= max_game_input_results) ctx->game_input_results.pop_front();
        ctx->game_input_results.push_back(GameInputResult { request.request_id, request.owner_id, false, validation });
        ctx->game_input_requests.pop_front();
    }
    if (ctx->game_input_cleanup_requests.empty() && ctx->game_input_requests.empty()) return 0;
    auto& queue = ctx->game_input_cleanup_requests.empty() ? ctx->game_input_requests : ctx->game_input_cleanup_requests;
    GameInputRequest request = std::move(queue.front());
    queue.pop_front();
    ctx->consumed_game_input_requests.push_back(request);
    *out_request = gua_game_input_request_v1_t { sizeof(*out_request) };
    out_request->request_id = request.request_id; out_request->owner_id = request.owner_id;
    out_request->kind = request.kind; out_request->operation = request.operation;
    std::snprintf(out_request->target, sizeof(out_request->target), "%s", request.target.c_str());
    std::snprintf(out_request->value_json, sizeof(out_request->value_json), "%s", request.value_json.c_str());
    out_request->x = request.x; out_request->y = request.y; out_request->lease_ms = request.lease_ms;
    out_request->device_index = request.device_index; out_request->sensitive = request.sensitive ? 1 : 0;
    return 1;
}

extern "C" int gua_complete_game_input_request(gua_context_t* ctx, uint64_t request_id, int succeeded, int error_code)
{
    if (ctx == nullptr || request_id == 0) return 0;
    const std::lock_guard lock(ctx->mutex);
    const auto iterator = std::find_if(ctx->consumed_game_input_requests.begin(), ctx->consumed_game_input_requests.end(),
        [&](const auto& request) { return request.request_id == request_id; });
    if (iterator == ctx->consumed_game_input_requests.end()) return 0;
    const GameInputRequest request = *iterator;
    ctx->consumed_game_input_requests.erase(iterator);
    if (succeeded != 0) {
        if (request.operation == GUA_GAME_INPUT_RELEASE_ALL) {
            ctx->held_game_inputs.erase(std::remove_if(ctx->held_game_inputs.begin(), ctx->held_game_inputs.end(),
                [&](const auto& held) { return held.owner_id == request.owner_id; }), ctx->held_game_inputs.end());
        } else if (request.kind == GUA_GAME_INPUT_GAMEPAD && request.operation == GUA_GAME_INPUT_RESET) {
            ctx->held_game_inputs.erase(std::remove_if(ctx->held_game_inputs.begin(), ctx->held_game_inputs.end(),
                [&](const auto& held) {
                    return held.owner_id == request.owner_id && held.kind == GUA_GAME_INPUT_GAMEPAD &&
                        held.device_index == request.device_index;
                }), ctx->held_game_inputs.end());
        } else if (request_releases_hold(request)) {
            ctx->held_game_inputs.erase(std::remove_if(ctx->held_game_inputs.begin(), ctx->held_game_inputs.end(),
                [&](const auto& held) { return held_key_matches(held, request.owner_id, request.kind, request.target, request.device_index); }),
                ctx->held_game_inputs.end());
        } else if (request_creates_hold(request, ctx->game_input_actions)) {
            auto held = std::find_if(ctx->held_game_inputs.begin(), ctx->held_game_inputs.end(),
                [&](const auto& item) { return held_key_matches(item, request.owner_id, request.kind, request.target, request.device_index); });
            if (held == ctx->held_game_inputs.end()) ctx->held_game_inputs.push_back(HeldGameInput {
                request.owner_id, request.kind, request.target, request.device_index, request.value_json,
                static_cast<double>(request.lease_ms), request.sensitive });
            else { held->value_json = request.value_json; held->remaining_ms = request.lease_ms; held->sensitive = request.sensitive; }
        }
    }
    if (ctx->game_input_owners.contains(request.owner_id)) {
        if (ctx->game_input_results.size() >= max_game_input_results) ctx->game_input_results.pop_front();
        ctx->game_input_results.push_back(GameInputResult { request.request_id, request.owner_id, succeeded != 0, error_code });
    }
    return 1;
}

extern "C" int gua_tick_game_input_leases(gua_context_t* ctx, double elapsed_ms)
{
    if (ctx == nullptr || !std::isfinite(elapsed_ms) || elapsed_ms < 0) return 0;
    const std::lock_guard lock(ctx->mutex);
    std::vector<HeldGameInput> expired;
    for (auto& held : ctx->held_game_inputs) {
        held.remaining_ms -= elapsed_ms;
        if (held.remaining_ms <= 0) expired.push_back(held);
    }
    for (const auto& held : expired) {
        ctx->game_input_cleanup_requests.push_back(GameInputRequest { ctx->next_game_input_request_id++, held.owner_id,
            held.kind, GUA_GAME_INPUT_RELEASE, held.target, "null", 0, 0, 0, held.device_index, false });
    }
    ctx->held_game_inputs.erase(std::remove_if(ctx->held_game_inputs.begin(), ctx->held_game_inputs.end(),
        [](const auto& held) { return held.remaining_ms <= 0; }), ctx->held_game_inputs.end());
    return static_cast<int>(expired.size());
}

extern "C" int gua_copy_game_input_state_json(gua_context_t* ctx, uint64_t owner_id, char* out_json, int out_json_size)
{
    if (ctx == nullptr || owner_id == 0) return 0;
    const std::lock_guard lock(ctx->mutex);
    if (!ctx->game_input_owners.contains(owner_id)) return 0;
    std::string json = "{\"schemaVersion\":1,\"held\":[";
    bool comma = false;
    for (const auto& held : ctx->held_game_inputs) {
        if (held.owner_id != owner_id) continue;
        if (comma) json += ",";
        json += "{\"kind\":" + std::to_string(held.kind) + ",\"target\":\"" + escape_json(held.target) +
            "\",\"deviceIndex\":" + std::to_string(held.device_index) + ",\"value\":" +
            (held.sensitive ? "null" : held.value_json) +
            ",\"remainingLeaseMs\":" + std::to_string(std::max(0.0, held.remaining_ms)) + "}";
        comma = true;
    }
    return copy_json_string(json + "]}", out_json, out_json_size);
}

extern "C" int gua_copy_game_input_result_json(gua_context_t* ctx, uint64_t owner_id, uint64_t request_id,
    char* out_json, int out_json_size)
{
    if (ctx == nullptr || owner_id == 0 || request_id == 0) return 0;
    const std::lock_guard lock(ctx->mutex);
    const auto result = std::find_if(ctx->game_input_results.begin(), ctx->game_input_results.end(),
        [&](const auto& item) { return item.owner_id == owner_id && item.request_id == request_id; });
    const std::string json = result == ctx->game_input_results.end()
        ? "{\"completed\":false}"
        : "{\"completed\":true,\"requestId\":" + std::to_string(result->request_id) +
            ",\"succeeded\":" + (result->succeeded ? "true" : "false") +
            ",\"errorCode\":" + std::to_string(result->error_code) + "}";
    const int required_size = copy_json_string(json, out_json, out_json_size);
    if (result != ctx->game_input_results.end() && out_json != nullptr && out_json_size >= required_size)
        ctx->game_input_results.erase(result);
    return required_size;
}
