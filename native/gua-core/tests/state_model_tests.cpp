#include "gua/gua.h"
#include "gua/gua.hpp"

#include <cassert>
#include <cstddef>
#include <cmath>
#include <cstring>
#include <string>
#include <cstdint>
#include <atomic>
#include <thread>
#include <vector>

namespace {

struct legacy_reset_options_t {
    uint32_t struct_size;
    uint32_t flags;
    int strict;
    uint64_t expected_session_epoch;
};

static_assert(sizeof(legacy_reset_options_t) == offsetof(gua_reset_options_t, flags_version));

void register_checkbox(gua_context_t* context, bool checked)
{
    const gua_node_descriptor_v2_t descriptor {
        sizeof(gua_node_descriptor_v2_t),
        GUA_NODE_KNOWN_PARENT_ID | GUA_NODE_KNOWN_TEXT | GUA_NODE_KNOWN_FOCUSED | GUA_NODE_KNOWN_CHECKED,
        "remember",
        "form",
        "checkbox",
        "Remember me",
        "Remember me",
        nullptr,
        { 10.0F, 20.0F, 100.0F, 24.0F },
        1,
        1,
        0,
        0,
        0,
        checked ? 1 : 0,
        0,
    };
    assert(gua_register_node_v2(context, &descriptor) == 1);
}

} // namespace

int main()
{
    const int version_size = gua_copy_version_json(nullptr, 0);
    assert(version_size > 1);
    std::vector<char> version(static_cast<std::size_t>(version_size));
    assert(gua_copy_version_json(version.data(), version_size) == version_size);
    assert(std::string(version.data()).find("\"godotPluginVersion\":null") != std::string::npos);
    assert(std::string(version.data()).find("\"version_v1\"") != std::string::npos);
    gua_context_t* detailed_context = gua_create_context();
    gua_begin_frame(detailed_context, "details");
    const gua_node_descriptor_v3_t detailed {
        sizeof(gua_node_descriptor_v3_t),
        { sizeof(gua_node_descriptor_v2_t), GUA_NODE_KNOWN_FOCUSED | GUA_NODE_KNOWN_CARET_POSITION |
            GUA_NODE_KNOWN_SELECTION | GUA_NODE_KNOWN_SCROLL | GUA_NODE_KNOWN_SCROLL_MAX |
            GUA_NODE_KNOWN_RANGE_VALUE | GUA_NODE_KNOWN_RANGE_MIN | GUA_NODE_KNOWN_RANGE_MAX |
            GUA_NODE_KNOWN_SELECTED_INDEX, "detail", nullptr, "textbox", "Detail", nullptr, nullptr,
            {0, 0, 10, 10}, 1, 1, 1, 0, 0, 0, 0 },
        0, 0, 0, 0, 12, 0, 100, 5, 0, 10, 0
    };
    assert(gua_register_node_v3(detailed_context, &detailed) == 1);
    gua_end_frame(detailed_context);
    const std::string detailed_json = gua_get_ui_tree_json(detailed_context);
    assert(detailed_json.find("\"caretPosition\":0") != std::string::npos);
    assert(detailed_json.find("\"scrollY\":12.000000") != std::string::npos);
    assert(detailed_json.find("\"rangeValue\":5.000000") != std::string::npos);
    gua_node_state_v3_t detailed_state { sizeof(gua_node_state_v3_t) };
    assert(gua_get_node_state_v3(detailed_context, "detail", &detailed_state) == 1);
    assert(detailed_state.caret_position == 0 && detailed_state.scroll_y == 12 && detailed_state.range_value == 5);

    gua_begin_frame(detailed_context, "invalid-focus");
    auto focused = detailed.base;
    focused.id = "one"; assert(gua_register_node_v2(detailed_context, &focused) == 1);
    focused.id = "two"; assert(gua_register_node_v2(detailed_context, &focused) == 1);
    gua_end_frame(detailed_context);
    assert(std::string(gua_get_ui_tree_json(detailed_context)) == detailed_json);
    gua_destroy_context(detailed_context);
    gua_context_t* context = gua_create_context();
    assert(context != nullptr);

    assert(gua_clock_pause(context) == GUA_CLOCK_ERROR_NOT_INSTALLED);
    assert(gua_clock_install(context, 0.0, 10.0) == GUA_CLOCK_OK);
    assert(gua_clock_pause(context) == GUA_CLOCK_OK);
    assert(gua_clock_run_for(context, 25.0, 10.0) == GUA_CLOCK_OK);
    gua_clock_step_t clock_step { sizeof(gua_clock_step_t) };
    assert(gua_clock_consume_step(context, &clock_step) == 1 && clock_step.delta_ms == 10.0 && clock_step.final_step == 0);
    clock_step = gua_clock_step_t { sizeof(gua_clock_step_t) };
    assert(gua_clock_consume_step(context, &clock_step) == 1 && clock_step.delta_ms == 10.0);
    clock_step = gua_clock_step_t { sizeof(gua_clock_step_t) };
    assert(gua_clock_consume_step(context, &clock_step) == 1 && clock_step.delta_ms == 5.0 && clock_step.final_step == 1);
    gua_clock_status_t clock_status { sizeof(gua_clock_status_t) };
    assert(gua_clock_get_status(context, &clock_status) == 1 && clock_status.now_ms == 25.0 && clock_status.paused == 1);
    assert(gua_clock_resume(context) == GUA_CLOCK_OK);
    assert(gua_clock_advance(context, 4.0) == GUA_CLOCK_OK);
    clock_step = gua_clock_step_t { sizeof(gua_clock_step_t) };
    assert(gua_clock_consume_step(context, &clock_step) == 1 && clock_step.delta_ms == 4.0);
    assert(gua_clock_install(context, 0.0, 1.0) == GUA_CLOCK_ERROR_INVALID_STATE);
    assert(gua_clock_get_status(context, &clock_status) == 1 && clock_status.now_ms == 29.0);
    assert(gua_clock_advance(context, 10000001.0) == GUA_CLOCK_ERROR_EXECUTION_LIMIT);
    assert(gua_clock_get_status(context, &clock_status) == 1 && clock_status.pending_ms == 0.0);

    gua_context_t* pause_context = gua_create_context();
    assert(gua_clock_install(pause_context, 0.0, 250.0) == GUA_CLOCK_OK);
    assert(gua_clock_advance(pause_context, 10.0) == GUA_CLOCK_OK);
    assert(gua_clock_pause(pause_context) == GUA_CLOCK_ERROR_INVALID_STATE);
    clock_step = gua_clock_step_t { sizeof(gua_clock_step_t) };
    assert(gua_clock_consume_step(pause_context, &clock_step) == 1);
    assert(gua_clock_pause(pause_context) == GUA_CLOCK_OK);
    assert(gua_clock_run_for(pause_context, 10.0, 10.0) == GUA_CLOCK_OK);
    gua_reset_options_t clock_reset { sizeof(gua_reset_options_t), GUA_RESET_DEFAULT_V2, 0, 0, GUA_RESET_FLAGS_VERSION_CURRENT };
    gua_reset_report_t clock_reset_report { sizeof(gua_reset_report_t) };
    assert(gua_reset_context(pause_context, &clock_reset, &clock_reset_report) == GUA_RESET_SUCCEEDED);
    gua_clock_status_t reset_clock_status { sizeof(gua_clock_status_t) };
    assert(gua_clock_get_status(pause_context, &reset_clock_status) == 1);
    assert(reset_clock_status.installed == 0 && reset_clock_status.paused == 0);
    assert(reset_clock_status.now_ms == 0.0 && reset_clock_status.pending_ms == 0.0);
    assert(reset_clock_status.default_step_ms == 1000.0 / 60.0);
    gua_clock_operation_status_t reset_operation_status { sizeof(gua_clock_operation_status_t) };
    assert(gua_clock_get_operation_status(pause_context, &reset_operation_status) == 1);
    assert(reset_operation_status.latest_operation_sequence == reset_operation_status.completed_operation_sequence);
    gua_destroy_context(pause_context);

    gua_context_t* explicit_reset_context = gua_create_context();
    assert(gua_clock_install(explicit_reset_context, 0.0, 250.0) == GUA_CLOCK_OK);
    gua_clock_status_t explicit_clock_status { sizeof(gua_clock_status_t) };
    assert(gua_clock_get_status(explicit_reset_context, &explicit_clock_status) == 1);
    const auto explicit_generation = explicit_clock_status.generation;
    gua_reset_options_t explicit_reset {
        sizeof(gua_reset_options_t), GUA_RESET_DEFAULT, 0, 0, GUA_RESET_FLAGS_VERSION_CURRENT
    };
    gua_reset_report_t explicit_reset_report { sizeof(gua_reset_report_t) };
    assert(gua_reset_context(explicit_reset_context, &explicit_reset, &explicit_reset_report) == GUA_RESET_SUCCEEDED);
    assert(gua_clock_get_status(explicit_reset_context, &explicit_clock_status) == 1);
    assert(explicit_clock_status.installed == 1 && explicit_clock_status.generation == explicit_generation);
    gua_destroy_context(explicit_reset_context);

    gua_context_t* legacy_reset_context = gua_create_context();
    assert(gua_clock_install(legacy_reset_context, 0.0, 250.0) == GUA_CLOCK_OK);
    legacy_reset_options_t legacy_reset {
        sizeof(legacy_reset_options_t), GUA_RESET_DEFAULT, 0, 0
    };
    gua_reset_report_t legacy_reset_report { sizeof(gua_reset_report_t) };
    assert(gua_reset_context(legacy_reset_context,
        reinterpret_cast<const gua_reset_options_t*>(&legacy_reset), &legacy_reset_report) == GUA_RESET_SUCCEEDED);
    gua_clock_status_t legacy_clock_status { sizeof(gua_clock_status_t) };
    assert(gua_clock_get_status(legacy_reset_context, &legacy_clock_status) == 1 && legacy_clock_status.installed == 0);
    assert(gua_clock_install(legacy_reset_context, 0.0, 250.0) == GUA_CLOCK_OK);
    legacy_reset.flags = GUA_RESET_DEFAULT | GUA_RESET_LOGS | GUA_RESET_SCREENSHOT;
    legacy_reset_report = gua_reset_report_t { sizeof(gua_reset_report_t) };
    assert(gua_reset_context(legacy_reset_context,
        reinterpret_cast<const gua_reset_options_t*>(&legacy_reset), &legacy_reset_report) == GUA_RESET_SUCCEEDED);
    assert(gua_clock_get_status(legacy_reset_context, &legacy_clock_status) == 1 && legacy_clock_status.installed == 0);
    gua_destroy_context(legacy_reset_context);

    gua_context_t* overflow_context = gua_create_context();
    assert(gua_clock_install(overflow_context, 1e308, 1e308) == GUA_CLOCK_ERROR_INVALID_ARGUMENT);
    assert(gua_clock_get_status(overflow_context, &clock_status) == 1 &&
        std::isfinite(clock_status.now_ms) && clock_status.pending_ms == 0.0);
    gua_destroy_context(overflow_context);

    gua_context_t* precision_context = gua_create_context();
    assert(gua_clock_install(precision_context, 0.0, 1e-7) == GUA_CLOCK_OK);
    assert(gua_clock_pause(precision_context) == GUA_CLOCK_OK);
    assert(gua_clock_run_for(precision_context, 2e-7, 1e-7) == GUA_CLOCK_OK);
    std::vector<char> clock_json(static_cast<std::size_t>(gua_clock_copy_status_json(precision_context, nullptr, 0)));
    gua_clock_copy_status_json(precision_context, clock_json.data(), static_cast<int>(clock_json.size()));
    const std::string precise_status(clock_json.data());
    const auto number_after = [&](const std::string& field) {
        const auto offset = precise_status.find(field);
        assert(offset != std::string::npos);
        return std::stod(precise_status.substr(offset + field.size()));
    };
    assert(number_after("\"defaultStepMs\":") == 1e-7);
    assert(number_after("\"pendingMs\":") == 2e-7);
    gua_destroy_context(precision_context);

    const auto count_clock_steps = [](double duration, double step) {
        gua_context_t* rounding_context = gua_create_context();
        assert(gua_clock_install(rounding_context, 0.0, step) == GUA_CLOCK_OK);
        assert(gua_clock_pause(rounding_context) == GUA_CLOCK_OK);
        assert(gua_clock_run_for(rounding_context, duration, step) == GUA_CLOCK_OK);
        int count = 0;
        gua_clock_step_t rounding_step { sizeof(gua_clock_step_t) };
        while (gua_clock_consume_step(rounding_context, &rounding_step) == 1) ++count;
        gua_clock_status_t rounding_status { sizeof(gua_clock_status_t) };
        assert(gua_clock_get_status(rounding_context, &rounding_status) == 1);
        assert(rounding_status.now_ms == duration && rounding_status.pending_ms == 0.0);
        gua_destroy_context(rounding_context);
        return count;
    };
    assert(count_clock_steps(1.0, 0.1) == 10);
    assert(count_clock_steps(1000.0, 1000.0 / 60.0) == 60);
    assert(count_clock_steps(1e-16, 1.0) == 1);

    gua_context_t* underflow_context = gua_create_context();
    assert(gua_clock_install(underflow_context, 0.0, 1e308) == GUA_CLOCK_OK);
    assert(gua_clock_pause(underflow_context) == GUA_CLOCK_OK);
    assert(gua_clock_run_for(underflow_context, 1e-300, 1e308) == GUA_CLOCK_OK);
    gua_clock_step_t underflow_step { sizeof(gua_clock_step_t) };
    assert(gua_clock_consume_step(underflow_context, &underflow_step) == 1);
    assert(underflow_step.delta_ms == 1e-300 && underflow_step.final_step == 1);
    gua_clock_status_t underflow_status { sizeof(gua_clock_status_t) };
    assert(gua_clock_get_status(underflow_context, &underflow_status) == 1);
    assert(underflow_status.now_ms == 1e-300 && underflow_status.pending_ms == 0.0);
    assert(gua_clock_consume_step(underflow_context, &underflow_step) == 0);
    gua_destroy_context(underflow_context);

    gua_context_t* zero_duration_context = gua_create_context();
    assert(gua_clock_install(zero_duration_context, 0.0, 10.0) == GUA_CLOCK_OK);
    assert(gua_clock_pause(zero_duration_context) == GUA_CLOCK_OK);
    assert(gua_clock_run_for(zero_duration_context, 0.0, 10.0) == GUA_CLOCK_OK);
    gua_clock_step_t zero_duration_step { sizeof(gua_clock_step_t) };
    assert(gua_clock_consume_step(zero_duration_context, &zero_duration_step) == 0);
    gua_destroy_context(zero_duration_context);

    gua_context_t* magnitude_context = gua_create_context();
    assert(gua_clock_install(magnitude_context, 1e16, 1.0) == GUA_CLOCK_ERROR_INVALID_ARGUMENT);
    assert(gua_clock_install(magnitude_context, 1e16, 3.0) == GUA_CLOCK_OK);
    assert(gua_clock_pause(magnitude_context) == GUA_CLOCK_OK);
    assert(gua_clock_run_for(magnitude_context, 0.0, 1.0) == GUA_CLOCK_ERROR_INVALID_ARGUMENT);
    assert(gua_clock_run_for(magnitude_context, 100.0, 3.0) == GUA_CLOCK_OK);
    gua_clock_status_t magnitude_status { sizeof(gua_clock_status_t) };
    double previous_now = 1e16;
    int magnitude_steps = 0;
    gua_clock_step_t magnitude_step { sizeof(gua_clock_step_t) };
    while (gua_clock_consume_step(magnitude_context, &magnitude_step) == 1) {
        assert(gua_clock_get_status(magnitude_context, &magnitude_status) == 1);
        assert(magnitude_status.now_ms > previous_now);
        assert(magnitude_step.delta_ms == magnitude_status.now_ms - previous_now);
        previous_now = magnitude_status.now_ms;
        ++magnitude_steps;
        magnitude_step = gua_clock_step_t { sizeof(gua_clock_step_t) };
    }
    assert(gua_clock_get_status(magnitude_context, &magnitude_status) == 1);
    assert(magnitude_steps == 33 && magnitude_status.now_ms == 1e16 + 100.0 && magnitude_status.pending_ms == 0.0);
    gua_clock_operation_status_t operation_status { sizeof(gua_clock_operation_status_t) };
    assert(gua_clock_get_operation_status(magnitude_context, &operation_status) == 1);
    assert(operation_status.latest_operation_sequence == 1 && operation_status.completed_operation_sequence == 0);
    gua_begin_frame(magnitude_context, "clock-complete"); gua_end_frame(magnitude_context);
    assert(gua_clock_get_operation_status(magnitude_context, &operation_status) == 1);
    assert(operation_status.completed_operation_sequence == 1);
    gua_destroy_context(magnitude_context);

    magnitude_context = gua_create_context();
    assert(gua_clock_install(magnitude_context, 1e16, 3.0) == GUA_CLOCK_OK);
    assert(gua_clock_advance(magnitude_context, 100.0) == GUA_CLOCK_OK);
    magnitude_step = gua_clock_step_t { sizeof(gua_clock_step_t) };
    while (gua_clock_consume_step(magnitude_context, &magnitude_step) == 1)
        magnitude_step = gua_clock_step_t { sizeof(gua_clock_step_t) };
    assert(gua_clock_get_status(magnitude_context, &magnitude_status) == 1 && magnitude_status.now_ms == 1e16 + 100.0);
    gua_destroy_context(magnitude_context);

    gua::Context cpp_context;
    cpp_context.install_clock(0.0, 10.0);
    cpp_context.pause_clock();
    cpp_context.run_clock_for(25.0);
    gua_clock_step_t cpp_step { sizeof(gua_clock_step_t) };
    assert(cpp_context.poll_clock_step(cpp_step) && cpp_step.delta_ms == 10.0);
    assert(cpp_context.poll_clock_step(cpp_step) && cpp_step.delta_ms == 10.0);
    assert(cpp_context.poll_clock_step(cpp_step) && cpp_step.delta_ms == 5.0);

    // A frame is private until end_frame atomically publishes it.
    gua_context_t* atomic_context = gua_create_context();
    gua_begin_frame(atomic_context, "initial-staging");
    gua_register_node(atomic_context, "private", "button", "Private", { 0, 0, 1, 1 }, 1, 1);
    const std::string before_first_publish = gua_get_ui_tree_json(atomic_context);
    assert(before_first_publish.find("\"screen\":\"unknown\"") != std::string::npos);
    assert(before_first_publish.find("private") == std::string::npos);
    gua_end_frame(atomic_context);

    gua_begin_frame(atomic_context, "second-staging");
    gua_register_node(atomic_context, "partial", "button", "Partial", { 0, 0, 1, 1 }, 1, 1);
    const std::string during_second_frame = gua_get_ui_tree_json(atomic_context);
    assert(during_second_frame.find("initial-staging") != std::string::npos);
    assert(during_second_frame.find("partial") == std::string::npos);
    const gua_selector_v1_t private_selector { sizeof(gua_selector_v1_t), "private" };
    char query_json[512] {};
    gua_query_nodes_json(atomic_context, &private_selector, query_json, sizeof(query_json));
    assert(std::string(query_json).find("private") != std::string::npos);
    assert(std::string(gua_get_diagnostics_json(atomic_context)).find("initial-staging") != std::string::npos);
    const gua_action_request_descriptor_t published_click { sizeof(gua_action_request_descriptor_t), GUA_ACTION_CLICK, "private" };
    const gua_action_request_descriptor_t staging_click { sizeof(gua_action_request_descriptor_t), GUA_ACTION_CLICK, "partial" };
    assert(gua_enqueue_action(atomic_context, &published_click, nullptr) == GUA_ACTION_ACCEPTED);
    assert(gua_enqueue_action(atomic_context, &staging_click, nullptr) == GUA_ACTION_ERROR_NODE_NOT_FOUND);
    gua_register_node(atomic_context, nullptr, "button", "Invalid", { 0, 0, 1, 1 }, 1, 1);
    gua_end_frame(atomic_context);
    assert(std::string(gua_get_ui_tree_json(atomic_context)) == during_second_frame);
    gua_destroy_context(atomic_context);

    gua_begin_frame(context, "settings");
    register_checkbox(context, false);
    gua_end_frame(context);
    const std::string first = gua_get_ui_tree_json(context);
    assert(first.find("\"schemaVersion\":2") != std::string::npos);
    assert(first.find("\"sessionEpoch\":1") != std::string::npos);
    assert(first.find("\"frameSequence\":1") != std::string::npos);
    assert(first.find("\"revision\":1") != std::string::npos);
    assert(first.find("\"parentId\":\"form\"") != std::string::npos);
    assert(first.find("\"checked\":false") != std::string::npos);
    assert(first.find("\"selected\"") == std::string::npos);

    gua_add_log(context, GUA_LOG_INFO, "control\bcharacter");
    const std::string escaped_logs = gua_get_logs_json(context);
    assert(escaped_logs.find("control\\u0008character") != std::string::npos);
    assert(escaped_logs.find('\b') == std::string::npos);

    gua_node_state_v2_t state {};
    state.struct_size = sizeof(state);
    assert(gua_get_node_state_v2(context, "remember", &state) == 1);
    assert((state.known_mask & GUA_NODE_KNOWN_CHECKED) != 0U);
    assert(state.checked == 0);
    assert(std::strcmp(state.parent_id, "form") == 0);

    const std::string long_text(300, 'x');
    const gua_node_descriptor_v2_t long_text_node {
        sizeof(gua_node_descriptor_v2_t), GUA_NODE_KNOWN_TEXT, "long-text", nullptr, "text", "Long text",
        long_text.c_str(), nullptr, { 0, 0, 1, 1 }, 1, 1, 0, 0, 0, 0, 0,
    };
    gua_context_t* oversized_context = gua_create_context();
    gua_begin_frame(oversized_context, "settings");
    assert(gua_register_node_v2(oversized_context, &long_text_node) == 1);
    gua_end_frame(oversized_context);
    gua_node_state_v2_t oversized_state {};
    oversized_state.struct_size = sizeof(oversized_state);
    assert(gua_get_node_state_v2(oversized_context, "long-text", &oversized_state) == 0);
    gua_destroy_context(oversized_context);

    gua_begin_frame(context, "settings");
    register_checkbox(context, false);
    gua_end_frame(context);
    const std::string stable = gua_get_ui_tree_json(context);
    assert(stable.find("\"frameSequence\":2") != std::string::npos);
    assert(stable.find("\"revision\":1") != std::string::npos);

    gua_begin_frame(context, "settings");
    register_checkbox(context, true);
    gua_end_frame(context);
    const std::string changed = gua_get_ui_tree_json(context);
    assert(changed.find("\"frameSequence\":3") != std::string::npos);
    assert(changed.find("\"revision\":2") != std::string::npos);
    assert(changed.find("\"checked\":true") != std::string::npos);

    gua_begin_frame(context, "settings");
    gua_register_node(context, "legacy", "button", "Legacy", { 0, 0, 1, 1 }, 1, 1);
    gua_end_frame(context);
    gua_node_state_t legacy {};
    assert(gua_get_node_state(context, "legacy", &legacy) == 1);
    assert(legacy.visible == 1 && legacy.enabled == 1);

    gua_begin_frame(context, "actions");
    gua_register_node(context, "hidden", "button", "Hidden", { 0, 0, 1, 1 }, 0, 1);
    gua_register_node(context, "disabled", "button", "Disabled", { 0, 0, 1, 1 }, 1, 0);
    const gua_node_descriptor_v2_t textbox {
        sizeof(gua_node_descriptor_v2_t), GUA_NODE_KNOWN_VALUE, "name", nullptr, "textbox", "Name", nullptr, "",
        { 0, 0, 100, 20 }, 1, 1, 0, 0, 0, 0, 0
    };
    assert(gua_register_node_v2(context, &textbox) == 1);
    register_checkbox(context, false);
	gua_register_node(context, "difficulty", "combobox", "Difficulty", { 0, 0, 100, 20 }, 1, 1);
	gua_register_node(context, "difficulty$item:0", "listitem", "Easy", { 0, 0, 100, 20 }, 1, 1);
    gua_register_node(context, "content", "scrollarea", "Content", { 0, 0, 100, 100 }, 1, 1);
    gua_end_frame(context);

    const gua_action_request_descriptor_t hidden_click { sizeof(gua_action_request_descriptor_t), GUA_ACTION_CLICK, "hidden" };
    const gua_action_request_descriptor_t disabled_click { sizeof(gua_action_request_descriptor_t), GUA_ACTION_CLICK, "disabled" };
    const gua_action_request_descriptor_t unsupported { sizeof(gua_action_request_descriptor_t), GUA_ACTION_SELECT, "name", "x" };
    const gua_action_request_descriptor_t missing { sizeof(gua_action_request_descriptor_t), GUA_ACTION_CLICK, "missing" };
    const gua_action_request_descriptor_t invalid_value { sizeof(gua_action_request_descriptor_t), GUA_ACTION_SELECT, "remember", "" };
    assert(gua_enqueue_action(context, &hidden_click, nullptr) == GUA_ACTION_ERROR_HIDDEN);
    assert(gua_enqueue_action(context, &disabled_click, nullptr) == GUA_ACTION_ERROR_DISABLED);
    assert(gua_enqueue_action(context, &unsupported, nullptr) == GUA_ACTION_ERROR_UNSUPPORTED);
    assert(gua_enqueue_action(context, &missing, nullptr) == GUA_ACTION_ERROR_NODE_NOT_FOUND);
    assert(gua_enqueue_action(context, &invalid_value, nullptr) == GUA_ACTION_ERROR_INVALID_VALUE);

    const gua_action_request_descriptor_t focus { sizeof(gua_action_request_descriptor_t), GUA_ACTION_FOCUS, "name" };
    const gua_action_request_descriptor_t checked { sizeof(gua_action_request_descriptor_t), GUA_ACTION_SET_CHECKED, "remember", nullptr, 0, 0, 1 };
	const gua_action_request_descriptor_t select { sizeof(gua_action_request_descriptor_t), GUA_ACTION_SELECT, "difficulty", "hard" };
	const gua_action_request_descriptor_t select_item { sizeof(gua_action_request_descriptor_t), GUA_ACTION_SELECT, "difficulty$item:0" };
    const gua_action_request_descriptor_t scroll { sizeof(gua_action_request_descriptor_t), GUA_ACTION_SCROLL, "content", nullptr, 2, 3 };
    const gua_action_request_descriptor_t key { sizeof(gua_action_request_descriptor_t), GUA_ACTION_PRESS_KEY, "name", nullptr, 0, 0, 0, "A" };
    std::uint64_t action_ids[5] {};
    assert(gua_enqueue_action(context, &focus, &action_ids[0]) == GUA_ACTION_ACCEPTED);
    assert(gua_enqueue_action(context, &checked, &action_ids[1]) == GUA_ACTION_ACCEPTED);
	assert(gua_enqueue_action(context, &select, &action_ids[2]) == GUA_ACTION_ACCEPTED);
	assert(gua_enqueue_action(context, &select_item, nullptr) == GUA_ACTION_ACCEPTED);
    assert(gua_enqueue_action(context, &scroll, &action_ids[3]) == GUA_ACTION_ACCEPTED);
    assert(gua_enqueue_action(context, &key, &action_ids[4]) == GUA_ACTION_ACCEPTED);
    for (std::size_t i = 1; i < 5; ++i) assert(action_ids[i] > action_ids[i - 1]);

    const gua_action_request_descriptor_t secret { sizeof(gua_action_request_descriptor_t), GUA_ACTION_SET_VALUE, "name", "secret-marker", 0, 0, 0, nullptr, 0, 1 };
    std::uint64_t request_id = 0;
    assert(gua_enqueue_action(context, &secret, &request_id) == GUA_ACTION_ACCEPTED);
    assert(request_id > 0);
    gua_action_request_t consumed { sizeof(gua_action_request_t) };
    assert(gua_consume_action_request(context, GUA_ACTION_SET_VALUE, "name", &consumed) == 1);
    assert(consumed.request_id == request_id);
    assert(std::strcmp(consumed.value, "secret-marker") == 0);
    const gua_action_result_t result { sizeof(gua_action_result_t), request_id, GUA_ACTION_SET_VALUE,
        GUA_ACTION_STATUS_SUCCEEDED, 0, "name", "secret-marker", 1 };
    assert(gua_emit_action_result(context, &result) == 1);
    assert(gua_enqueue_click(context, "remember") == 1);
    assert(gua_consume_click_request(context, "remember") == 1);
    assert(gua_emit_click(context, "remember") == 1);
    gua_event_t legacy_event {};
    assert(gua_poll_event(context, &legacy_event) == 1);
    assert(legacy_event.type == GUA_EVENT_CLICK);

    gua_event_v3_t event { sizeof(gua_event_v3_t), { sizeof(gua_event_v2_t) } };
    assert(gua_poll_event_v3_for_request(context, request_id, &event) == 1);
    assert(event.base.request_id == request_id);
    assert(event.base.action == GUA_ACTION_SET_VALUE);
    assert(event.base.status == GUA_ACTION_STATUS_SUCCEEDED);
    assert(event.base.sensitive == 1);
    assert(std::strlen(event.base.value) == 0);
    assert(event.session_epoch == 1 && event.frame_sequence > 0 && event.revision > 0);

    const std::string complete_diagnostics = gua_get_diagnostics_json(context);
    assert(complete_diagnostics.find("\"elapsedMilliseconds\":") != std::string::npos);
    assert(complete_diagnostics.find("\"revision\":") != std::string::npos);
    assert(complete_diagnostics.find("\"deltaX\":2.000000") != std::string::npos);
    assert(complete_diagnostics.find("\"deltaY\":3.000000") != std::string::npos);
    assert(complete_diagnostics.find("\"boolValue\":true") != std::string::npos);
    assert(complete_diagnostics.find("\"key\":\"A\"") != std::string::npos);
    assert(complete_diagnostics.find("secret-marker") == std::string::npos);

    assert(gua_set_diagnostics_history_limit(context, 2) == 1);
    assert(gua_set_diagnostics_environment_json(context, "{\"testName\":\"native-state\"}") == 1);
    const std::string diagnostics = gua_get_diagnostics_json(context);
    assert(diagnostics.find("\"schemaVersion\":1") != std::string::npos);
    assert(diagnostics.find("\"historyLimit\":2") != std::string::npos);
    assert(diagnostics.find("\"testName\":\"native-state\"") != std::string::npos);
    assert(diagnostics.find("\"screenshot\":null") != std::string::npos);
    assert(diagnostics.find("secret-marker") == std::string::npos);
    assert(diagnostics.find("\"sensitive\":true") != std::string::npos);

	const gua_action_request_descriptor_t failed_click { sizeof(gua_action_request_descriptor_t), GUA_ACTION_CLICK, "remember" };
	std::uint64_t failed_click_id = 0;
	assert(gua_enqueue_action(context, &failed_click, &failed_click_id) == GUA_ACTION_ACCEPTED);
	assert(gua_consume_action_request(context, GUA_ACTION_CLICK, "remember", &consumed) == 1);
	const gua_action_result_t failed_click_result { sizeof(gua_action_result_t), failed_click_id, GUA_ACTION_CLICK,
		GUA_ACTION_STATUS_FAILED, GUA_ACTION_ERROR_DISABLED, "remember", nullptr, 0 };
	assert(gua_emit_action_result(context, &failed_click_result) == 1);
	assert(gua_poll_event(context, &legacy_event) == 0);
	gua_context_status_t failed_click_status { sizeof(gua_context_status_t) };
	assert(gua_get_context_status(context, &failed_click_status) == 1);
	assert(failed_click_status.unconsumed_event_count == 0);
	gua_event_v2_t failed_click_event { sizeof(gua_event_v2_t) };
	assert(gua_poll_event_v2_for_request(context, failed_click_id, &failed_click_event) == 0);

    gua_action_request_t in_flight { sizeof(gua_action_request_t) };
    assert(gua_consume_action_request(context, GUA_ACTION_FOCUS, "name", &in_flight) == 1);

    gua_context_status_t status { sizeof(gua_context_status_t) };
    assert(gua_get_context_status(context, &status) == 1);
    assert(status.session_epoch == 1);
    assert(status.pending_request_count == 5);
    assert(status.in_flight_request_count == 1);
    assert(status.unconsumed_event_count == 0);
    assert(status.first_pending_action == GUA_ACTION_SET_CHECKED);
    assert(std::strcmp(status.first_pending_node_id, "remember") == 0);

    gua_reset_report_t report { sizeof(gua_reset_report_t) };
    const gua_reset_options_t strict_reset { sizeof(gua_reset_options_t), GUA_RESET_DEFAULT_V2, 1, 1, GUA_RESET_FLAGS_VERSION_CURRENT };
    assert(gua_reset_context(context, &strict_reset, &report) == GUA_RESET_ERROR_DIRTY);
    assert(report.session_epoch == 1);
    assert(report.pending_request_count == 5);
    assert(report.in_flight_request_count == 1);
    assert(report.discarded_pending_request_count == 0);
    assert(gua_get_context_status(context, &status) == 1);
    assert(status.pending_request_count == 5);
    assert(status.in_flight_request_count == 1);

    gua_context_t* other = gua_create_context();
    gua_begin_frame(other, "other");
    gua_register_node(other, "other", "button", "Other", { 0, 0, 1, 1 }, 1, 1);
    gua_end_frame(other);

    report = gua_reset_report_t { sizeof(gua_reset_report_t) };
    const gua_reset_options_t stale_reset { sizeof(gua_reset_options_t), GUA_RESET_DEFAULT_V2, 0, 2, GUA_RESET_FLAGS_VERSION_CURRENT };
    assert(gua_reset_context(context, &stale_reset, &report) == GUA_RESET_ERROR_STALE_EPOCH);
    assert(report.session_epoch == 1);

    report = gua_reset_report_t { sizeof(gua_reset_report_t) };
    const gua_reset_options_t reset { sizeof(gua_reset_options_t), GUA_RESET_DEFAULT_V2, 0, 1, GUA_RESET_FLAGS_VERSION_CURRENT };
    assert(gua_reset_context(context, &reset, &report) == GUA_RESET_SUCCEEDED);
    assert(report.previous_session_epoch == 1 && report.session_epoch == 2);
    assert(report.discarded_pending_request_count == 5);
    assert(report.discarded_in_flight_request_count == 1);
    assert(gua_get_context_status(context, &status) == 1);
    assert(status.session_epoch == 2 && status.frame_sequence == 0 && status.revision == 0);
    assert(status.node_count == 0 && status.pending_request_count == 0 && status.unconsumed_event_count == 0);
    clock_status = gua_clock_status_t { sizeof(gua_clock_status_t) };
    assert(gua_clock_get_status(context, &clock_status) == 1 && clock_status.installed == 0);
    const std::string reset_diagnostics = gua_get_diagnostics_json(context);
    assert(reset_diagnostics.find("\"operations\":[]") != std::string::npos);
    assert(reset_diagnostics.find("\"events\":[]") != std::string::npos);
    assert(std::string(gua_get_ui_tree_json(context)).find("\"sessionEpoch\":2") != std::string::npos);
    char other_id[16] {};
    assert(gua_find_node_by_id(other, "other", other_id, sizeof(other_id)) == 1);

    const gua_action_result_t unsolicited { sizeof(gua_action_result_t), 0, GUA_ACTION_FOCUS,
        GUA_ACTION_STATUS_SUCCEEDED, 0, "focus-target", nullptr, 0 };
    assert(gua_emit_action_result(context, &unsolicited) == 1);
    report = gua_reset_report_t { sizeof(gua_reset_report_t) };
    const gua_reset_options_t strict_events { sizeof(gua_reset_options_t), GUA_RESET_EVENTS, 1, 2, GUA_RESET_FLAGS_VERSION_CURRENT };
    assert(gua_reset_context(context, &strict_events, &report) == GUA_RESET_ERROR_DIRTY);
    assert(report.unconsumed_event_count == 1);
    assert(report.discarded_event_count == 0);
    assert(report.first_event_action == GUA_ACTION_FOCUS);
    assert(std::strcmp(report.first_event_node_id, "focus-target") == 0);
    gua_event_v2_t preserved_event { sizeof(gua_event_v2_t) };
    assert(gua_poll_event_v2(context, &preserved_event) == 1);
    assert(preserved_event.action == GUA_ACTION_FOCUS);

    gua_destroy_context(other);

    // Readers may observe the old or new complete frame, never a partial node count.
    gua_context_t* concurrent = gua_create_context();
    gua_begin_frame(concurrent, "stress");
    for (int i = 0; i < 8; ++i) {
        const std::string id = "old-" + std::to_string(i);
        gua_register_node(concurrent, id.c_str(), "text", id.c_str(), { 0, 0, 1, 1 }, 1, 1);
    }
    gua_end_frame(concurrent);
    std::atomic<bool> stop { false };
    std::atomic<bool> invalid_count { false };
    std::vector<std::thread> readers;
    for (int reader = 0; reader < 4; ++reader) {
        readers.emplace_back([&] {
            while (!stop.load()) {
                gua_context_status_t concurrent_status { sizeof(gua_context_status_t) };
                assert(gua_get_context_status(concurrent, &concurrent_status) == 1);
                if (concurrent_status.node_count != 8 && concurrent_status.node_count != 64) invalid_count = true;
            }
        });
    }
    for (int frame = 0; frame < 100; ++frame) {
        const int count = (frame % 2 == 0) ? 64 : 8;
        gua_begin_frame(concurrent, "stress");
        for (int i = 0; i < count; ++i) {
            const std::string id = "node-" + std::to_string(i);
            gua_register_node(concurrent, id.c_str(), "text", id.c_str(), { 0, 0, 1, 1 }, 1, 1);
            if ((i % 8) == 0) std::this_thread::yield();
        }
        gua_end_frame(concurrent);
    }
    stop = true;
    for (auto& reader : readers) reader.join();
    assert(!invalid_count.load());
    gua_destroy_context(concurrent);

    // Game input maps commit atomically and held state is isolated by owner.
    assert(gua_begin_game_input_frame(context, "gameplay") == 1);
    const gua_game_input_action_descriptor_v1_t move {
        sizeof(gua_game_input_action_descriptor_v1_t), "move", "Move player", GUA_GAME_INPUT_VECTOR2,
        -1.0, 1.0, 1, 1, 1, "[\"KeyW/KeyA/KeyS/KeyD\"]", "safe", 0
    };
    const gua_game_input_action_descriptor_v1_t interact {
        sizeof(gua_game_input_action_descriptor_v1_t), "interact", "Interact", GUA_GAME_INPUT_BUTTON,
        0.0, 0.0, 0, 0, 1, "[\"KeyE\"]", "safe", 0
    };
    assert(gua_register_game_input_action_v1(context, &move) == 1);
    assert(gua_register_game_input_action_v1(context, &interact) == 1);
    assert(gua_end_game_input_frame(context) == 1);
    const int map_size = gua_copy_game_input_actions_json(context, nullptr, 0);
    std::string map(static_cast<std::size_t>(map_size), '\0');
    gua_copy_game_input_actions_json(context, map.data(), map_size);
    assert(map.find("\"context\":\"gameplay\"") != std::string::npos);

    const uint64_t owner_a = gua_create_game_input_owner(context);
    const uint64_t owner_b = gua_create_game_input_owner(context);
    uint64_t move_request_id = 0;
    const gua_game_input_request_descriptor_v1_t invalid_hold {
        sizeof(gua_game_input_request_descriptor_v1_t), owner_a, GUA_GAME_INPUT_SEMANTIC, GUA_GAME_INPUT_SET,
        "interact", "true", 0, 0, 5000, 0, 0
    };
    assert(gua_enqueue_game_input(context, &invalid_hold, nullptr) == GUA_GAME_INPUT_ERROR_UNSUPPORTED);
    const gua_game_input_request_descriptor_v1_t invalid_vector {
        sizeof(gua_game_input_request_descriptor_v1_t), owner_a, GUA_GAME_INPUT_SEMANTIC, GUA_GAME_INPUT_SET,
        "move", "{\"x\":2,\"y\":0}", 0, 0, 5000, 0, 0
    };
    assert(gua_enqueue_game_input(context, &invalid_vector, nullptr) == GUA_GAME_INPUT_ERROR_INVALID_VALUE);
    const gua_game_input_request_descriptor_v1_t invalid_key {
        sizeof(gua_game_input_request_descriptor_v1_t), owner_a, GUA_GAME_INPUT_KEYBOARD, GUA_GAME_INPUT_DOWN,
        "NotAKey", "true", 0, 0, 5000, 0, 0
    };
    assert(gua_enqueue_game_input(context, &invalid_key, nullptr) == GUA_GAME_INPUT_ERROR_INVALID_VALUE);
    auto invalid_lease = invalid_key;
    invalid_lease.target = "Space"; invalid_lease.lease_ms = 60001;
    assert(gua_enqueue_game_input(context, &invalid_lease, nullptr) == GUA_GAME_INPUT_ERROR_INVALID_ARGUMENT);
    const gua_game_input_request_descriptor_v1_t set_move {
        sizeof(gua_game_input_request_descriptor_v1_t), owner_a, GUA_GAME_INPUT_SEMANTIC, GUA_GAME_INPUT_SET,
        "move", "{\"x\":1,\"y\":0}", 0, 0, 5000, 0, 0
    };
    assert(gua_enqueue_game_input(context, &set_move, &move_request_id) == GUA_GAME_INPUT_OK);
    gua_game_input_request_v1_t consumed_input { sizeof(gua_game_input_request_v1_t) };
    assert(gua_consume_game_input_request(context, &consumed_input) == 1);
    assert(consumed_input.request_id == move_request_id && consumed_input.owner_id == owner_a);
    assert(gua_complete_game_input_request(context, move_request_id, 1, 0) == 1);
    int state_size = gua_copy_game_input_state_json(context, owner_a, nullptr, 0);
    std::string game_input_state(static_cast<std::size_t>(state_size), '\0');
    gua_copy_game_input_state_json(context, owner_a, game_input_state.data(), state_size);
    assert(game_input_state.find("\"target\":\"move\"") != std::string::npos);
    state_size = gua_copy_game_input_state_json(context, owner_b, nullptr, 0);
    game_input_state.assign(static_cast<std::size_t>(state_size), '\0');
    gua_copy_game_input_state_json(context, owner_b, game_input_state.data(), state_size);
    assert(game_input_state.find("\"target\":\"move\"") == std::string::npos);
    assert(gua_tick_game_input_leases(context, 5000.0) == 1);
    assert(gua_consume_game_input_request(context, &consumed_input) == 1);
    assert(consumed_input.operation == GUA_GAME_INPUT_RELEASE && consumed_input.owner_id == owner_a);
    assert(gua_complete_game_input_request(context, consumed_input.request_id, 1, 0) == 1);
    assert(gua_release_game_input_owner(context, owner_a) == 1);
    assert(gua_release_game_input_owner(context, owner_b) == 1);

    gua_destroy_context(context);
    return 0;
}
