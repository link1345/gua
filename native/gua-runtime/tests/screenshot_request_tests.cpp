#include "gua/runtime.h"

#include <array>
#include <cassert>
#include <cstdint>
#include <thread>
#include <string>
#include <vector>

namespace {

std::string poll(gua_runtime_t* runtime, uint64_t request_id)
{
    const int size = gua_runtime_poll_screenshot_result_json(runtime, request_id, nullptr, 0);
    if (size == 0) return {};
    std::vector<char> buffer(static_cast<std::size_t>(size));
    assert(gua_runtime_poll_screenshot_result_json(runtime, request_id, buffer.data(), size) == size);
    return buffer.data();
}

std::string version(gua_runtime_t* runtime)
{
    const int size = gua_runtime_copy_version_json(runtime, nullptr, 0);
    std::vector<char> buffer(static_cast<std::size_t>(size));
    assert(gua_runtime_copy_version_json(runtime, buffer.data(), size) == size);
    return buffer.data();
}

std::string diagnostics(gua_runtime_t* runtime)
{
    const int size = gua_runtime_copy_diagnostics_json(runtime, nullptr, 0);
    std::vector<char> buffer(static_cast<std::size_t>(size));
    assert(gua_runtime_copy_diagnostics_json(runtime, buffer.data(), size) == size);
    return buffer.data();
}

std::string ui_tree(gua_runtime_t* runtime, bool player)
{
    const auto copy = player ? gua_runtime_copy_player_ui_tree_json : gua_runtime_copy_ui_tree_json;
    const int size = copy(runtime, nullptr, 0);
    std::vector<char> buffer(static_cast<std::size_t>(size));
    assert(copy(runtime, buffer.data(), size) == size);
    return buffer.data();
}

} // namespace

int main()
{
    gua_runtime_t* runtime = gua_runtime_create();
    assert(runtime != nullptr);
    assert(gua_runtime_get_observation_profile(runtime) == GUA_OBSERVATION_PROFILE_DEBUG);
    assert(version(runtime).find("virtual_clock_v1") == std::string::npos);
    assert(gua_runtime_set_diagnostics_environment_json(
        runtime, "{\"tags\":[\"test\",\"virtual_clock_v1\"]}") == 1);
    const std::string diagnostics_json = diagnostics(runtime);
    assert(diagnostics_json.find("\"tags\":[\"test\",\"virtual_clock_v1\"]") != std::string::npos);
    const auto version_position = diagnostics_json.find("\"version\":");
    const auto ui_tree_position = diagnostics_json.find(",\"uiTree\":", version_position);
    assert(version_position != std::string::npos && ui_tree_position != std::string::npos);
    assert(diagnostics_json.substr(version_position, ui_tree_position - version_position)
        .find("virtual_clock_v1") == std::string::npos);
    gua_runtime_set_virtual_clock_enabled(runtime, 1);
    gua_runtime_set_adapter_version(runtime, "unity", "0.5.0-preview.3");
    gua_runtime_set_adapter_version(runtime, "Unity", "invalid");
    gua_runtime_set_adapter_version(runtime, "ui-toolkit", "invalid");
    gua_runtime_set_godot_plugin_version(runtime, "0.4.0");
    const std::string version_json = version(runtime);
    assert(version_json.find("\"adapterVersions\":{\"godot\":\"0.4.0\",\"unity\":\"0.5.0-preview.3\"}") != std::string::npos);
    assert(version_json.find("\"godotPluginVersion\":\"0.4.0\"") != std::string::npos);
    assert(version_json.find("Unity") == std::string::npos);
    assert(version_json.find("ui-toolkit") == std::string::npos);
    assert(version_json.find("virtual_clock_v1") != std::string::npos);
    std::thread version_writer([runtime] {
        for (int index = 0; index < 1000; ++index)
            gua_runtime_set_adapter_version(runtime, "unity", index % 2 == 0 ? "0.5.0-preview.3" : "0.5.0-preview.4");
    });
    std::thread version_reader([runtime] {
        for (int index = 0; index < 1000; ++index)
            assert(version(runtime).find("\"adapterVersions\":{") != std::string::npos);
    });
    version_writer.join();
    version_reader.join();
    const gua_world_selector_v1_t empty_world_selector { sizeof(gua_world_selector_v1_t) };
    std::thread world_capability_writer([runtime] {
        for (int index = 0; index < 20'000; ++index)
            gua_runtime_set_world_object_tree_enabled(runtime, index % 2);
    });
    std::thread world_capability_reader([runtime, &empty_world_selector] {
        char buffer[2048] {};
        for (int index = 0; index < 20'000; ++index) {
            assert(gua_runtime_copy_world_object_tree_json(runtime, buffer, sizeof(buffer)) > 0 && buffer[0] == '{');
            assert(gua_runtime_query_world_objects_json(runtime, &empty_world_selector, buffer, sizeof(buffer)) > 0 && buffer[0] == '{');
        }
    });
    world_capability_writer.join();
    world_capability_reader.join();
    gua_runtime_set_world_object_tree_enabled(runtime, 0);
    const gua_agent_policy_v1_t browser_public_policy {
        sizeof(gua_agent_policy_v1_t), GUA_AGENT_EXPOSURE_AUTO, 1,
        1ULL << GUA_ACTION_FOCUS, nullptr, 0 };
    const gua_agent_policy_v1_t browser_private_policy {
        sizeof(gua_agent_policy_v1_t), GUA_AGENT_EXPOSURE_PRIVATE, 0, 0, nullptr, 0 };
    const gua_node_descriptor_v2_t browser_public_base {
        sizeof(gua_node_descriptor_v2_t), 0, "public", nullptr, "button", "Public", nullptr, nullptr,
        { 0, 0, 10, 10 }, 1, 1, 0, 0, 0, 0, 0 };
    const gua_node_descriptor_v2_t browser_private_base {
        sizeof(gua_node_descriptor_v2_t), 0, "private", nullptr, "button", "Private", nullptr, nullptr,
        { 0, 0, 10, 10 }, 1, 1, 0, 0, 0, 0, 0 };
    const gua_node_descriptor_v3_t browser_public_detail {
        sizeof(gua_node_descriptor_v3_t), browser_public_base, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1 };
    const gua_node_descriptor_v3_t browser_private_detail {
        sizeof(gua_node_descriptor_v3_t), browser_private_base, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1 };
    const gua_node_descriptor_v4_t browser_public_node {
        sizeof(gua_node_descriptor_v4_t), browser_public_detail, browser_public_policy };
    const gua_node_descriptor_v4_t browser_private_node {
        sizeof(gua_node_descriptor_v4_t), browser_private_detail, browser_private_policy };
    gua_runtime_begin_frame(runtime, "title");
    assert(gua_runtime_register_node_v4(runtime, &browser_public_node) == 1);
    assert(gua_runtime_register_node_v4(runtime, &browser_private_node) == 1);
    gua_runtime_end_frame(runtime);
    assert(ui_tree(runtime, false).find("Private") != std::string::npos);
    assert(ui_tree(runtime, true).find("Private") == std::string::npos);
    const gua_action_request_descriptor_t click {
        sizeof(gua_action_request_descriptor_t), GUA_ACTION_CLICK, "public" };
    uint64_t debug_action = 0;
    assert(gua_runtime_enqueue_action(runtime, &click, &debug_action) == GUA_ACTION_ACCEPTED);
    assert(gua_runtime_get_action_request_observation_profile(runtime, debug_action) == GUA_OBSERVATION_PROFILE_DEBUG);
    assert(gua_runtime_cancel_action_request(runtime, debug_action) == GUA_ACTION_CANCELLED);
    assert(gua_runtime_get_action_request_observation_profile(runtime, debug_action) == -1);
    assert(gua_runtime_enqueue_player_action(runtime, &click, nullptr) == GUA_ACTION_ERROR_UNSUPPORTED);
    const gua_action_request_descriptor_t private_click {
        sizeof(gua_action_request_descriptor_t), GUA_ACTION_CLICK, "private" };
    assert(gua_runtime_enqueue_player_action(runtime, &private_click, nullptr) == GUA_ACTION_ERROR_NODE_NOT_FOUND);

    assert(gua_runtime_clock_install(runtime, 0.0, 10.0) == GUA_CLOCK_OK);
    assert(gua_runtime_clock_pause(runtime) == GUA_CLOCK_OK);
    assert(gua_runtime_clock_run_for(runtime, 25.0, 0.0) == GUA_CLOCK_ERROR_INVALID_ARGUMENT);
    assert(gua_runtime_clock_run_for(runtime, 25.0, 10.0) == GUA_CLOCK_OK);
    gua_clock_step_t clock_step { sizeof(gua_clock_step_t) };
    assert(gua_runtime_clock_consume_step(runtime, &clock_step) == 1 && clock_step.delta_ms == 10.0);
    clock_step = { sizeof(gua_clock_step_t) };
    assert(gua_runtime_clock_consume_step(runtime, &clock_step) == 1 && clock_step.delta_ms == 10.0);
    clock_step = { sizeof(gua_clock_step_t) };
    assert(gua_runtime_clock_consume_step(runtime, &clock_step) == 1 && clock_step.delta_ms == 5.0 && clock_step.final_step == 1);
    assert(gua_runtime_clock_run_for(runtime, 1.0, -1.0) == GUA_CLOCK_ERROR_INVALID_ARGUMENT);

    uint64_t first = 0;
    uint64_t second = 0;
    assert(gua_runtime_enqueue_screenshot_request(runtime, 1, &first) == 1);
    assert(gua_runtime_enqueue_screenshot_request(runtime, 3, &second) == 1);
    assert(first != second);
    assert(poll(runtime, first).empty());

    gua_screenshot_request_t request { sizeof(gua_screenshot_request_t) };
    assert(gua_runtime_consume_screenshot_request(runtime, &request) == 0);
    gua_runtime_begin_frame(runtime, "title");
    gua_runtime_end_frame(runtime);
    assert(gua_runtime_consume_screenshot_request(runtime, &request) == 1);
    assert(request.request_id == first);
    assert(request.session_epoch == 1);
    assert(request.after_frame_sequence == 1);
    assert(gua_runtime_consume_screenshot_request(runtime, &request) == 0);

    assert(gua_runtime_complete_screenshot_request(
        runtime, first, GUA_SCREENSHOT_AVAILABLE, "data:image/png;base64,iVBORw0KGgo=", 2, 3) == 1);
    const std::string first_json = poll(runtime, first);
    assert(first_json.find("\"requestId\":" + std::to_string(first)) != std::string::npos);
    assert(poll(runtime, second).empty());
    assert(first_json.find("\"frameSequence\":2") != std::string::npos);
    assert(first_json.find("\"width\":2,\"height\":3") != std::string::npos);

    for (int frame = 0; frame < 2; ++frame) {
        gua_runtime_begin_frame(runtime, "title");
        gua_runtime_end_frame(runtime);
    }
    request = { sizeof(gua_screenshot_request_t) };
    assert(gua_runtime_consume_screenshot_request(runtime, &request) == 1);
    assert(request.request_id == second);
    assert(request.after_frame_sequence == 3);
    assert(gua_runtime_complete_screenshot_request(
        runtime, second, GUA_SCREENSHOT_AVAILABLE, "data:image/png;base64,aGVsbG8=", 4, 5) == 1);
    assert(poll(runtime, second).find("\"frameSequence\":4") != std::string::npos);

    uint64_t unavailable = 0;
    assert(gua_runtime_enqueue_screenshot_request(runtime, 4, &unavailable) == 1);
    request = { sizeof(gua_screenshot_request_t) };
    assert(gua_runtime_consume_screenshot_request(runtime, &request) == 0);
    gua_runtime_begin_frame(runtime, "title");
    gua_runtime_end_frame(runtime);
    assert(gua_runtime_consume_screenshot_request(runtime, &request) == 1);
    assert(gua_runtime_complete_screenshot_request(runtime, request.request_id, GUA_SCREENSHOT_UNAVAILABLE_HEADLESS, nullptr, 0, 0) == 1);
    assert(poll(runtime, unavailable).find("\"unavailable\":\"headless\"") != std::string::npos);

    uint64_t stale_after_consume = 0;
    assert(gua_runtime_enqueue_screenshot_request(runtime, 5, &stale_after_consume) == 1);
    request = { sizeof(gua_screenshot_request_t) };
    assert(gua_runtime_consume_screenshot_request(runtime, &request) == 0);
    gua_runtime_begin_frame(runtime, "title");
    gua_runtime_end_frame(runtime);
    assert(gua_runtime_consume_screenshot_request(runtime, &request) == 1);
    assert(request.session_epoch == 1);
    gua_reset_options_t reset { sizeof(gua_reset_options_t), GUA_RESET_DEFAULT_V2, 0, 1, GUA_RESET_FLAGS_VERSION_CURRENT };
    gua_reset_report_t reset_report { sizeof(gua_reset_report_t) };
    assert(gua_runtime_reset_context(runtime, &reset, &reset_report) == 1);
    assert(reset_report.result == GUA_RESET_SUCCEEDED);
    assert(reset_report.session_epoch == 2);
    assert(gua_runtime_complete_screenshot_request(
        runtime, request.request_id, GUA_SCREENSHOT_AVAILABLE, "data:image/png;base64,c3RhbGU=", 4, 5) == 0);
    const std::string stale_json = poll(runtime, stale_after_consume);
    assert(stale_json.find("\"sessionEpoch\":2") != std::string::npos);
    assert(stale_json.find("\"unavailable\":\"stale_session\"") != std::string::npos);
    assert(stale_json.find("dataUri") == std::string::npos);

    uint64_t completed_before_reset = 0;
    assert(gua_runtime_enqueue_screenshot_request(runtime, 0, &completed_before_reset) == 1);
    gua_runtime_begin_frame(runtime, "title");
    gua_runtime_end_frame(runtime);
    request = { sizeof(gua_screenshot_request_t) };
    assert(gua_runtime_consume_screenshot_request(runtime, &request) == 1);
    assert(gua_runtime_complete_screenshot_request(
        runtime, request.request_id, GUA_SCREENSHOT_AVAILABLE, "data:image/png;base64,U0VDUkVU", 1, 1) == 1);
    reset = { sizeof(gua_reset_options_t), GUA_RESET_DEFAULT_V2, 0, 2, GUA_RESET_FLAGS_VERSION_CURRENT };
    reset_report = { sizeof(gua_reset_report_t) };
    assert(gua_runtime_reset_context(runtime, &reset, &reset_report) == GUA_RESET_SUCCEEDED);
    const std::string reset_completed_json = poll(runtime, completed_before_reset);
    assert(reset_completed_json.find("\"sessionEpoch\":3") != std::string::npos);
    assert(reset_completed_json.find("\"unavailable\":\"stale_session\"") != std::string::npos);
    assert(reset_completed_json.find("U0VDUkVU") == std::string::npos);

    uint64_t cancelled = 0;
    assert(gua_runtime_enqueue_screenshot_request(runtime, 2, &cancelled) == 1);
    assert(gua_runtime_cancel_screenshot_request(runtime, cancelled) == 1);
    request = { sizeof(gua_screenshot_request_t) };
    assert(gua_runtime_consume_screenshot_request(runtime, &request) == 0);

    uint64_t cancelled_in_flight = 0;
    assert(gua_runtime_enqueue_screenshot_request(runtime, 0, &cancelled_in_flight) == 1);
    gua_runtime_begin_frame(runtime, "title");
    gua_runtime_end_frame(runtime);
    request = { sizeof(gua_screenshot_request_t) };
    assert(gua_runtime_consume_screenshot_request(runtime, &request) == 1);
    assert(request.request_id == cancelled_in_flight);
    assert(gua_runtime_cancel_screenshot_request(runtime, cancelled_in_flight) == 1);
    assert(gua_runtime_complete_screenshot_request(
        runtime, cancelled_in_flight, GUA_SCREENSHOT_AVAILABLE, "data:image/png;base64,bGF0ZQ==", 1, 1) == 0);

    const gua_node_descriptor_v3_t concurrent_node {
        sizeof(gua_node_descriptor_v3_t),
        { sizeof(gua_node_descriptor_v2_t), GUA_NODE_KNOWN_RANGE_VALUE,
            "concurrent", nullptr, "slider", "Concurrent", nullptr, nullptr,
            { 0, 0, 1, 1 }, 1, 1, 0, 0, 0, 0, 0 },
        0, 0, 0, 0, 0, 0, 0, 1, 0, 10, 0
    };
    gua_runtime_begin_frame(runtime, "legacy-state");
    assert(gua_runtime_register_node_v3(runtime, &concurrent_node) == 1);
    gua_runtime_end_frame(runtime);
    assert(gua_runtime_get_node_state(runtime, "concurrent", nullptr) == 0);
    std::thread node_writer([runtime, &concurrent_node] {
        for (int index = 0; index < 20'000; ++index) {
            gua_runtime_begin_frame(runtime, "concurrent");
            gua_runtime_register_node_v3(runtime, &concurrent_node);
            gua_runtime_end_frame(runtime);
        }
    });
    std::thread context_resetter([runtime] {
        for (int index = 0; index < 2'000; ++index) {
            const gua_reset_options_t options { sizeof(gua_reset_options_t), GUA_RESET_DEFAULT_V2, 0, 0, GUA_RESET_FLAGS_VERSION_CURRENT };
            gua_reset_report_t report { sizeof(gua_reset_report_t) };
            assert(gua_runtime_reset_context(runtime, &options, &report) == GUA_RESET_SUCCEEDED);
        }
    });
    node_writer.join();
    context_resetter.join();

    gua_runtime_destroy(runtime);

    runtime = gua_runtime_create();
    assert(gua_runtime_set_observation_profile(runtime, GUA_OBSERVATION_PROFILE_PLAYER) == 1);
    assert(gua_runtime_get_observation_profile(runtime) == GUA_OBSERVATION_PROFILE_PLAYER);
    uint64_t player_request = 0;
    assert(gua_runtime_enqueue_screenshot_request(runtime, 0, &player_request) == 0);
    assert(gua_runtime_set_player_screenshot_enabled(runtime, 1) == 1);
    assert(gua_runtime_enqueue_screenshot_request(runtime, 0, &player_request) == 1);
    const gua_node_descriptor_v2_t private_base { sizeof(gua_node_descriptor_v2_t), 0, "private", nullptr, "button", "Secret", nullptr, nullptr,
        { 0, 0, 1, 1 }, 1, 1, 0, 0, 0, 0, 0 };
    const gua_node_descriptor_v3_t private_detail { sizeof(gua_node_descriptor_v3_t), private_base };
    const gua_agent_policy_v1_t private_policy { sizeof(gua_agent_policy_v1_t), GUA_AGENT_EXPOSURE_PRIVATE };
    const gua_node_descriptor_v4_t private_node { sizeof(gua_node_descriptor_v4_t), private_detail, private_policy };
    gua_runtime_begin_frame(runtime, "player");
    gua_runtime_register_node(runtime, "visible", "button", "Visible", { 0, 0, 1, 1 }, 1, 1);
    assert(gua_runtime_register_node_v4(runtime, &private_node) == 1);
    gua_runtime_end_frame(runtime);
    char found[128] {};
    assert(gua_runtime_find_node_by_id(runtime, "private", found, sizeof(found)) == 0);
    gua_context_status_t player_status { sizeof(gua_context_status_t) };
    assert(gua_runtime_get_context_status(runtime, &player_status) == 1 && player_status.node_count == 1);
    assert(gua_runtime_emit_click(runtime, "visible") == 1);
    gua_event_t observed {};
    assert(gua_runtime_poll_event(runtime, &observed) == 1 && std::string(observed.node_id) == "visible");
    assert(gua_runtime_emit_click(runtime, "private") == 1);
    observed = {};
    assert(gua_runtime_poll_event(runtime, &observed) == 0);
    alignas(gua_reset_report_t) std::array<unsigned char, sizeof(gua_reset_report_t)> undersized_report_storage;
    undersized_report_storage.fill(0xA5);
    auto* undersized_report = reinterpret_cast<gua_reset_report_t*>(undersized_report_storage.data());
    undersized_report->struct_size = sizeof(uint32_t);
    const gua_reset_options_t player_reset {
        sizeof(gua_reset_options_t), GUA_RESET_DEFAULT_V2, 0, 0, GUA_RESET_FLAGS_VERSION_CURRENT };
    assert(gua_runtime_reset_context(runtime, &player_reset, undersized_report) == GUA_RESET_ERROR_INVALID_ARGUMENT);
    for (std::size_t index = sizeof(uint32_t); index < undersized_report_storage.size(); ++index) {
        assert(undersized_report_storage[index] == 0xA5);
    }
    gua_runtime_destroy(runtime);

    runtime = gua_runtime_create();
    gua_runtime_set_game_input_capabilities(runtime, GUA_RUNTIME_GAME_INPUT_KEYBOARD);
    assert(gua_runtime_get_game_input_capabilities(runtime, GUA_OBSERVATION_PROFILE_DEBUG) == GUA_RUNTIME_GAME_INPUT_KEYBOARD);
    assert(gua_runtime_get_game_input_capabilities(runtime, GUA_OBSERVATION_PROFILE_PLAYER) == 0);
    const uint64_t input_owner = gua_runtime_create_game_input_owner(runtime);
    const gua_game_input_request_descriptor_v2_t key_down {
        sizeof(gua_game_input_request_descriptor_v2_t), input_owner, GUA_GAME_INPUT_KEYBOARD,
        GUA_GAME_INPUT_DOWN, "KeyA", "null", 0, 0, 5000, 0, 0, 0
    };
    uint64_t input_request = 0;
    assert(gua_runtime_enqueue_game_input_for_profile_v2(runtime, &key_down,
        GUA_OBSERVATION_PROFILE_PLAYER, &input_request) == GUA_GAME_INPUT_ERROR_UNSUPPORTED);
    gua_runtime_set_player_game_input_capabilities(runtime, GUA_RUNTIME_GAME_INPUT_KEYBOARD);
    assert(gua_runtime_enqueue_game_input_for_profile_v2(runtime, &key_down,
        GUA_OBSERVATION_PROFILE_PLAYER, &input_request) == GUA_GAME_INPUT_OK);
    gua_runtime_set_player_game_input_capabilities(runtime, 0);
    gua_game_input_request_v1_t input { sizeof(gua_game_input_request_v1_t) };
    assert(gua_runtime_consume_game_input_request(runtime, &input) == 0);
    const int result_size = gua_runtime_copy_game_input_result_json(runtime, input_owner, input_request, nullptr, 0);
    std::string input_result(static_cast<std::size_t>(result_size), '\0');
    gua_runtime_copy_game_input_result_json(runtime, input_owner, input_request, input_result.data(), result_size);
    assert(input_result.find("\"succeeded\":false") != std::string::npos);
    assert(input_result.find("\"errorCode\":-4") != std::string::npos);
    const gua_game_input_request_descriptor_v2_t release_all {
        sizeof(gua_game_input_request_descriptor_v2_t), input_owner, GUA_GAME_INPUT_CLEANUP,
        GUA_GAME_INPUT_RELEASE_ALL, "all", "null", 0, 0, 5000, 0, 0, 0
    };
    uint64_t cleanup_request = 0;
    assert(gua_runtime_enqueue_game_input_for_profile_v2(runtime, &release_all,
        GUA_OBSERVATION_PROFILE_PLAYER, &cleanup_request) == GUA_GAME_INPUT_OK);
    input = { sizeof(gua_game_input_request_v1_t) };
    assert(gua_runtime_consume_game_input_request(runtime, &input) == 1 && input.request_id == cleanup_request);
    assert(gua_runtime_complete_game_input_request(runtime, cleanup_request, 1, 0) == 1);
    uint64_t debug_request = 0;
    assert(gua_runtime_enqueue_game_input_for_profile_v2(runtime, &key_down,
        GUA_OBSERVATION_PROFILE_DEBUG, &debug_request) == GUA_GAME_INPUT_OK);
    input = { sizeof(gua_game_input_request_v1_t) };
    assert(gua_runtime_consume_game_input_request(runtime, &input) == 1 && input.request_id == debug_request);
    gua_runtime_destroy(runtime);
    return 0;
}
