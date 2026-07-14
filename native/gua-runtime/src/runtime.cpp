#include "gua/runtime.h"

#include "gua/ws_bridge.hpp"

#include <cstdio>
#include <chrono>
#include <algorithm>
#include <deque>
#include <thread>
#include <unordered_map>
#include <vector>
#include <memory>
#include <map>
#include <mutex>
#include <string>
#include <string_view>
#include <utility>

struct gua_runtime_t {
    struct ScreenshotBatch {
        uint64_t session_epoch = 0;
        std::vector<uint64_t> request_ids;
    };

    gua_context_t* context = nullptr;
    mutable std::mutex context_mutex;
    mutable std::mutex bridge_mutex;
    std::unique_ptr<gua::ws::BridgeServer> bridge;
    int bridge_port = 0;
    std::string bridge_url;
    std::string ui_tree_json;
    std::string logs_json;
    std::string screenshot_json;
    std::string diagnostics_json;
    std::string godot_plugin_version;
    std::map<std::string, std::string> adapter_versions;
    uint64_t next_screenshot_request_id = 1;
    std::deque<gua_screenshot_request_t> screenshot_requests;
    std::unordered_map<uint64_t, ScreenshotBatch> screenshot_batches;
    std::unordered_map<uint64_t, std::string> screenshot_results;
};

std::string escape_json(std::string_view value);

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

const char* screenshot_unavailable_name(int result)
{
    if (result == GUA_SCREENSHOT_UNAVAILABLE_HEADLESS) return "headless";
    if (result == GUA_SCREENSHOT_UNAVAILABLE_RENDERING_DISABLED) return "rendering_disabled";
    if (result == GUA_SCREENSHOT_UNAVAILABLE_STALE_SESSION) return "stale_session";
    return "unsupported";
}

std::string copy_diagnostics_json(gua_runtime_t* runtime)
{
    const std::lock_guard lock(runtime->context_mutex);
    std::string json = gua_get_diagnostics_json(runtime->context);
    if (!runtime->godot_plugin_version.empty()) {
        const std::string marker = "\"godotPluginVersion\":null";
        const auto position = json.find(marker);
        if (position != std::string::npos) {
            json.replace(position, marker.size(), "\"godotPluginVersion\":\"" + escape_json(runtime->godot_plugin_version) + "\"");
        }
    }
    const std::string adapter_marker = "\"adapterVersions\":{}";
    const auto adapter_position = json.find(adapter_marker);
    if (adapter_position != std::string::npos && !runtime->adapter_versions.empty()) {
        std::string adapters = "\"adapterVersions\":{";
        bool first = true;
        for (const auto& [name, version] : runtime->adapter_versions) {
            if (!first) adapters += ',';
            first = false;
            adapters += "\"" + escape_json(name) + "\":\"" + escape_json(version) + "\"";
        }
        adapters += '}';
        json.replace(adapter_position, adapter_marker.size(), adapters);
    }
    return json;
}

bool valid_adapter_name(std::string_view adapter)
{
    if (adapter.empty()) return false;
    for (const unsigned char ch : adapter) {
        if ((ch < 'a' || ch > 'z') && (ch < '0' || ch > '9') && ch != '_') return false;
    }
    return true;
}

std::string copy_version_json(gua_runtime_t* runtime)
{
    const std::lock_guard lock(runtime->context_mutex);
    char buffer[2048] {};
    gua_copy_version_json(buffer, static_cast<int>(sizeof(buffer)));
    std::string json = buffer;
    if (!runtime->godot_plugin_version.empty()) {
        const std::string marker = "\"godotPluginVersion\":null";
        const auto position = json.find(marker);
        if (position != std::string::npos) {
            json.replace(position, marker.size(), "\"godotPluginVersion\":\"" + escape_json(runtime->godot_plugin_version) + "\"");
        }
    }
    const std::string adapter_marker = "\"adapterVersions\":{}";
    const auto adapter_position = json.find(adapter_marker);
    if (adapter_position != std::string::npos && !runtime->adapter_versions.empty()) {
        std::string adapters = "\"adapterVersions\":{";
        bool first = true;
        for (const auto& [name, version] : runtime->adapter_versions) {
            if (!first) adapters += ',';
            first = false;
            adapters += "\"" + escape_json(name) + "\":\"" + escape_json(version) + "\"";
        }
        adapters += '}';
        json.replace(adapter_position, adapter_marker.size(), adapters);
    }
    return json;
}

std::string status_json(gua_runtime_t* runtime)
{
    gua_context_status_t status { sizeof(gua_context_status_t) };
    const std::lock_guard lock(runtime->context_mutex);
    if (gua_get_context_status(runtime->context, &status) == 0) return "null";
    return "{\"sessionEpoch\":" + std::to_string(status.session_epoch) +
        ",\"frameSequence\":" + std::to_string(status.frame_sequence) +
        ",\"revision\":" + std::to_string(status.revision) +
        ",\"nodeCount\":" + std::to_string(status.node_count) +
        ",\"pendingRequestCount\":" + std::to_string(status.pending_request_count) +
        ",\"inFlightRequestCount\":" + std::to_string(status.in_flight_request_count) +
        ",\"unconsumedEventCount\":" + std::to_string(status.unconsumed_event_count) +
        ",\"logCount\":" + std::to_string(status.log_count) +
        ",\"hasScreenshot\":" + (status.has_screenshot != 0 ? "true" : "false") +
        ",\"firstPendingAction\":" + std::to_string(status.first_pending_action) +
        ",\"firstPendingNodeId\":\"" + escape_json(status.first_pending_node_id) + "\"" +
        ",\"firstEventAction\":" + std::to_string(status.first_event_action) +
        ",\"firstEventNodeId\":\"" + escape_json(status.first_event_node_id) + "\"}";
}

std::string stale_screenshot_json(uint64_t request_id, const gua_context_status_t& status)
{
    return "{\"requestId\":" + std::to_string(request_id) +
        ",\"sessionEpoch\":" + std::to_string(status.session_epoch) +
        ",\"frameSequence\":" + std::to_string(status.frame_sequence) +
        ",\"unavailable\":\"stale_session\"}";
}

void invalidate_screenshot_requests(gua_runtime_t* runtime)
{
    gua_context_status_t status { sizeof(gua_context_status_t) };
    if (gua_get_context_status(runtime->context, &status) == 0) return;
    for (const auto& request : runtime->screenshot_requests)
        runtime->screenshot_results[request.request_id] = stale_screenshot_json(request.request_id, status);
    runtime->screenshot_requests.clear();
    for (const auto& [leader, batch] : runtime->screenshot_batches) {
        (void)leader;
        for (const auto request_id : batch.request_ids)
            runtime->screenshot_results[request_id] = stale_screenshot_json(request_id, status);
    }
    runtime->screenshot_batches.clear();
    for (auto& [request_id, result] : runtime->screenshot_results)
        result = stale_screenshot_json(request_id, status);
}

std::string reset_report_json(gua_runtime_t* runtime, unsigned long long expected_epoch, unsigned int flags, bool strict)
{
    gua_reset_options_t options { sizeof(gua_reset_options_t), flags, strict ? 1 : 0, expected_epoch };
    gua_reset_report_t report { sizeof(gua_reset_report_t) };
    const std::lock_guard lock(runtime->context_mutex);
    const int result = gua_reset_context(runtime->context, &options, &report);
    if (result == GUA_RESET_SUCCEEDED) invalidate_screenshot_requests(runtime);
    return "{\"result\":" + std::to_string(result) +
        ",\"previousSessionEpoch\":" + std::to_string(report.previous_session_epoch) +
        ",\"sessionEpoch\":" + std::to_string(report.session_epoch) +
        ",\"pendingRequestCount\":" + std::to_string(report.pending_request_count) +
        ",\"inFlightRequestCount\":" + std::to_string(report.in_flight_request_count) +
        ",\"unconsumedEventCount\":" + std::to_string(report.unconsumed_event_count) +
        ",\"discardedNodeCount\":" + std::to_string(report.discarded_node_count) +
        ",\"discardedPendingRequestCount\":" + std::to_string(report.discarded_pending_request_count) +
        ",\"discardedInFlightRequestCount\":" + std::to_string(report.discarded_in_flight_request_count) +
        ",\"discardedEventCount\":" + std::to_string(report.discarded_event_count) +
        ",\"discardedLogCount\":" + std::to_string(report.discarded_log_count) +
        ",\"discardedScreenshot\":" + (report.discarded_screenshot != 0 ? "true" : "false") +
        ",\"firstPendingAction\":" + std::to_string(report.first_pending_action) +
        ",\"firstPendingNodeId\":\"" + escape_json(report.first_pending_node_id) + "\"" +
        ",\"firstEventAction\":" + std::to_string(report.first_event_action) +
        ",\"firstEventNodeId\":\"" + escape_json(report.first_event_node_id) + "\"}";
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

extern "C" int gua_runtime_register_node_v3(gua_runtime_t* runtime, const gua_node_descriptor_v3_t* descriptor)
{
    return runtime != nullptr ? gua_register_node_v3(runtime->context, descriptor) : 0;
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

extern "C" int gua_runtime_enqueue_screenshot_request(gua_runtime_t* runtime, uint64_t after_frame_sequence, uint64_t* out_request_id)
{
    if (!valid_runtime(runtime) || out_request_id == nullptr) return 0;
    const std::lock_guard lock(runtime->context_mutex);
    gua_context_status_t status { sizeof(gua_context_status_t) };
    if (gua_get_context_status(runtime->context, &status) == 0) return 0;
    const uint64_t id = runtime->next_screenshot_request_id++;
    runtime->screenshot_requests.push_back({ sizeof(gua_screenshot_request_t), id, status.session_epoch, after_frame_sequence });
    *out_request_id = id;
    return 1;
}

extern "C" int gua_runtime_consume_screenshot_request(gua_runtime_t* runtime, gua_screenshot_request_t* out_request)
{
    if (!valid_runtime(runtime) || out_request == nullptr || out_request->struct_size < sizeof(gua_screenshot_request_t)) return 0;
    const std::lock_guard lock(runtime->context_mutex);
    gua_context_status_t status { sizeof(gua_context_status_t) };
    gua_get_context_status(runtime->context, &status);
    while (!runtime->screenshot_requests.empty() && runtime->screenshot_requests.front().session_epoch != status.session_epoch) {
        const auto stale = runtime->screenshot_requests.front();
        runtime->screenshot_requests.pop_front();
        runtime->screenshot_results[stale.request_id] = "{\"requestId\":" + std::to_string(stale.request_id) +
            ",\"sessionEpoch\":" + std::to_string(status.session_epoch) +
            ",\"frameSequence\":" + std::to_string(status.frame_sequence) + ",\"unavailable\":\"stale_session\"}";
    }
    const auto first_ready = std::find_if(runtime->screenshot_requests.begin(), runtime->screenshot_requests.end(),
        [&status](const auto& pending) {
            return pending.session_epoch == status.session_epoch && status.frame_sequence > pending.after_frame_sequence;
        });
    if (first_ready == runtime->screenshot_requests.end()) return 0;
    auto first = *first_ready;
    uint64_t after = first.after_frame_sequence;
    std::vector<uint64_t> batch;
    for (auto pending = runtime->screenshot_requests.begin(); pending != runtime->screenshot_requests.end();) {
        if (pending->session_epoch == first.session_epoch && status.frame_sequence > pending->after_frame_sequence) {
            batch.push_back(pending->request_id);
            after = std::max(after, pending->after_frame_sequence);
            pending = runtime->screenshot_requests.erase(pending);
        } else {
            ++pending;
        }
    }
    first.after_frame_sequence = after;
    runtime->screenshot_batches[first.request_id] = { first.session_epoch, std::move(batch) };
    *out_request = first;
    return 1;
}

extern "C" int gua_runtime_complete_screenshot_request(gua_runtime_t* runtime, uint64_t request_id, int result, const char* data_uri, int width, int height)
{
    if (!valid_runtime(runtime) || request_id == 0) return 0;
    const std::lock_guard lock(runtime->context_mutex);
    const auto batch = runtime->screenshot_batches.find(request_id);
    if (batch == runtime->screenshot_batches.end()) return 0;
    gua_context_status_t status { sizeof(gua_context_status_t) };
    gua_get_context_status(runtime->context, &status);
    const bool stale_session = batch->second.session_epoch != status.session_epoch;
    if (!stale_session && result == GUA_SCREENSHOT_AVAILABLE) gua_set_screenshot(runtime->context, data_uri, width, height);
    for (const auto id : batch->second.request_ids) {
        if (stale_session) {
            runtime->screenshot_results[id] = "{\"requestId\":" + std::to_string(id) +
                ",\"sessionEpoch\":" + std::to_string(status.session_epoch) +
                ",\"frameSequence\":" + std::to_string(status.frame_sequence) + ",\"unavailable\":\"stale_session\"}";
        } else if (result == GUA_SCREENSHOT_AVAILABLE) {
            runtime->screenshot_results[id] = "{\"requestId\":" + std::to_string(id) +
                ",\"sessionEpoch\":" + std::to_string(status.session_epoch) +
                ",\"frameSequence\":" + std::to_string(status.frame_sequence) +
                ",\"width\":" + std::to_string(std::max(0, width)) + ",\"height\":" + std::to_string(std::max(0, height)) +
                ",\"dataUri\":\"" + escape_json(data_uri == nullptr ? "" : data_uri) + "\"}";
        } else {
            runtime->screenshot_results[id] = "{\"requestId\":" + std::to_string(id) +
                ",\"sessionEpoch\":" + std::to_string(status.session_epoch) +
                ",\"frameSequence\":" + std::to_string(status.frame_sequence) +
                ",\"unavailable\":\"" + screenshot_unavailable_name(result) + "\"}";
        }
    }
    runtime->screenshot_batches.erase(batch);
    return 1;
}

extern "C" int gua_runtime_poll_screenshot_result_json(gua_runtime_t* runtime, uint64_t request_id, char* out_json, int out_json_size)
{
    if (!valid_runtime(runtime) || request_id == 0) return 0;
    const std::lock_guard lock(runtime->context_mutex);
    const auto found = runtime->screenshot_results.find(request_id);
    if (found == runtime->screenshot_results.end()) return 0;
    const int size = copy_json_string(found->second, out_json, out_json_size);
    if (out_json != nullptr && out_json_size >= size) runtime->screenshot_results.erase(found);
    return size;
}

extern "C" int gua_runtime_cancel_screenshot_request(gua_runtime_t* runtime, uint64_t request_id)
{
    if (!valid_runtime(runtime) || request_id == 0) return 0;
    const std::lock_guard lock(runtime->context_mutex);
    bool removed = runtime->screenshot_results.erase(request_id) != 0;
    const auto before = runtime->screenshot_requests.size();
    runtime->screenshot_requests.erase(
        std::remove_if(runtime->screenshot_requests.begin(), runtime->screenshot_requests.end(),
            [request_id](const auto& request) { return request.request_id == request_id; }),
        runtime->screenshot_requests.end());
    removed = removed || before != runtime->screenshot_requests.size();
    for (auto& [leader, batch] : runtime->screenshot_batches) {
        (void)leader;
        const auto batch_before = batch.request_ids.size();
        batch.request_ids.erase(std::remove(batch.request_ids.begin(), batch.request_ids.end(), request_id), batch.request_ids.end());
        removed = removed || batch_before != batch.request_ids.size();
    }
    return removed ? 1 : 0;
}

extern "C" int gua_runtime_set_diagnostics_history_limit(gua_runtime_t* runtime, uint32_t history_limit)
{
    if (!valid_runtime(runtime)) return 0;
    const std::lock_guard lock(runtime->context_mutex);
    return gua_set_diagnostics_history_limit(runtime->context, history_limit);
}

extern "C" int gua_runtime_set_diagnostics_environment_json(gua_runtime_t* runtime, const char* environment_json)
{
    if (!valid_runtime(runtime)) return 0;
    const std::lock_guard lock(runtime->context_mutex);
    return gua_set_diagnostics_environment_json(runtime->context, environment_json);
}

extern "C" const char* gua_runtime_get_diagnostics_json(gua_runtime_t* runtime)
{
    if (!valid_runtime(runtime)) return "{}";
    runtime->diagnostics_json = copy_diagnostics_json(runtime);
    return runtime->diagnostics_json.c_str();
}

extern "C" int gua_runtime_copy_diagnostics_json(gua_runtime_t* runtime, char* out_json, int out_json_size)
{
    if (!valid_runtime(runtime)) return copy_json_string("{}", out_json, out_json_size);
    return copy_json_string(copy_diagnostics_json(runtime), out_json, out_json_size);
}

extern "C" int gua_runtime_copy_version_json(gua_runtime_t* runtime, char* out_json, int out_json_size)
{
    if (!valid_runtime(runtime)) return copy_json_string("{}", out_json, out_json_size);
    return copy_json_string(copy_version_json(runtime), out_json, out_json_size);
}

extern "C" void gua_runtime_set_godot_plugin_version(gua_runtime_t* runtime, const char* version)
{
    if (!valid_runtime(runtime)) return;
    const std::lock_guard lock(runtime->context_mutex);
    runtime->godot_plugin_version = version == nullptr ? "" : version;
    if (runtime->godot_plugin_version.empty()) runtime->adapter_versions.erase("godot");
    else runtime->adapter_versions["godot"] = runtime->godot_plugin_version;
}

extern "C" void gua_runtime_set_adapter_version(gua_runtime_t* runtime, const char* adapter, const char* version)
{
    if (!valid_runtime(runtime) || adapter == nullptr || !valid_adapter_name(adapter)) return;
    const std::lock_guard lock(runtime->context_mutex);
    if (version == nullptr || version[0] == '\0') runtime->adapter_versions.erase(adapter);
    else runtime->adapter_versions[adapter] = version;
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
        switch (ch) {
        case '\\': escaped += "\\\\"; break;
        case '"': escaped += "\\\""; break;
        case '\n': escaped += "\\n"; break;
        case '\r': escaped += "\\r"; break;
        case '\t': escaped += "\\t"; break;
        default:
            if (static_cast<unsigned char>(ch) < 0x20U) {
                constexpr char hex[] = "0123456789abcdef";
                const unsigned char byte_value = static_cast<unsigned char>(ch);
                escaped += "\\u00";
                escaped += hex[byte_value >> 4U];
                escaped += hex[byte_value & 0x0fU];
            } else {
                escaped.push_back(ch);
            }
            break;
        }
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

extern "C" int gua_runtime_poll_event_v3(gua_runtime_t* runtime, gua_event_v3_t* out_event)
{
    return runtime != nullptr ? gua_poll_event_v3(runtime->context, out_event) : 0;
}

extern "C" int gua_runtime_poll_event_v3_for_request(gua_runtime_t* runtime, uint64_t request_id, gua_event_v3_t* out_event)
{
    return runtime != nullptr ? gua_poll_event_v3_for_request(runtime->context, request_id, out_event) : 0;
}

extern "C" int gua_runtime_get_context_status(gua_runtime_t* runtime, gua_context_status_t* out_status)
{
    if (!valid_runtime(runtime)) return 0;
    const std::lock_guard lock(runtime->context_mutex);
    return gua_get_context_status(runtime->context, out_status);
}

extern "C" int gua_runtime_reset_context(gua_runtime_t* runtime, const gua_reset_options_t* options, gua_reset_report_t* out_report)
{
    if (!valid_runtime(runtime)) return GUA_RESET_ERROR_INVALID_ARGUMENT;
    const std::lock_guard lock(runtime->context_mutex);
    const int result = gua_reset_context(runtime->context, options, out_report);
    if (result == GUA_RESET_SUCCEEDED) invalidate_screenshot_requests(runtime);
    return result;
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
        .capture_screenshot = [runtime](unsigned long long after_frame_sequence, unsigned int timeout_ms) {
            uint64_t request_id = 0;
            if (gua_runtime_enqueue_screenshot_request(runtime, after_frame_sequence, &request_id) == 0)
                return gua::ws::CommandResult { false, {}, "capture_screenshot request could not be queued" };
            const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeout_ms == 0 ? 1 : timeout_ms);
            while (std::chrono::steady_clock::now() < deadline) {
                const int size = gua_runtime_poll_screenshot_result_json(runtime, request_id, nullptr, 0);
                if (size > 0) {
                    std::string json(static_cast<std::size_t>(size), '\0');
                    gua_runtime_poll_screenshot_result_json(runtime, request_id, json.data(), size);
                    json.resize(static_cast<std::size_t>(size - 1));
                    if (json.find("\"unavailable\"") != std::string::npos)
                        return gua::ws::CommandResult { false, {}, json };
                    return gua::ws::CommandResult { true, std::move(json), {} };
                }
                std::this_thread::sleep_for(std::chrono::milliseconds(5));
            }
            gua_runtime_cancel_screenshot_request(runtime, request_id);
            return gua::ws::CommandResult { false, {}, "capture_screenshot timed out" };
        },
        .get_diagnostics_json = [runtime] {
            return copy_diagnostics_json(runtime);
        },
        .get_version_json = [runtime] {
            return copy_version_json(runtime);
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
        .get_context_status_json = [runtime] { return status_json(runtime); },
        .reset_context_json = [runtime](unsigned long long expected_epoch, unsigned int flags, bool strict) {
            return reset_report_json(runtime, expected_epoch, flags, strict);
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
            gua_event_v3_t event { sizeof(gua_event_v3_t), { sizeof(gua_event_v2_t) } };
            const std::lock_guard lock(runtime->context_mutex);
            const int found = request_id == 0
                ? gua_poll_event_v3(runtime->context, &event)
                : gua_poll_event_v3_for_request(runtime->context, request_id, &event);
            if (found == 0) return std::string("null");
            return std::string("{\"requestId\":") + std::to_string(event.base.request_id) +
                ",\"action\":" + std::to_string(event.base.action) +
                ",\"succeeded\":" + (event.base.status == GUA_ACTION_STATUS_SUCCEEDED ? "true" : "false") +
                ",\"error\":" + std::to_string(event.base.error_code) +
                ",\"nodeId\":\"" + escape_json(event.base.node_id) + "\"" +
                ",\"value\":\"" + escape_json(event.base.value) + "\"" +
                ",\"sensitive\":" + (event.base.sensitive != 0 ? "true" : "false") +
                ",\"sessionEpoch\":" + std::to_string(event.session_epoch) +
                ",\"frameSequence\":" + std::to_string(event.frame_sequence) +
                ",\"revision\":" + std::to_string(event.revision) + "}";
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
