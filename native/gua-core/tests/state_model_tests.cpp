#include "gua/gua.h"

#include <cassert>
#include <cstring>
#include <string>
#include <cstdint>
#include <atomic>
#include <thread>
#include <vector>

namespace {

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
    gua_context_t* context = gua_create_context();
    assert(context != nullptr);

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

    gua_event_v2_t event { sizeof(gua_event_v2_t) };
    assert(gua_poll_event_v2_for_request(context, request_id, &event) == 1);
    assert(event.request_id == request_id);
    assert(event.action == GUA_ACTION_SET_VALUE);
    assert(event.status == GUA_ACTION_STATUS_SUCCEEDED);
    assert(event.sensitive == 1);
    assert(std::strlen(event.value) == 0);

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
	gua_event_v2_t failed_click_event { sizeof(gua_event_v2_t) };
	assert(gua_poll_event_v2_for_request(context, failed_click_id, &failed_click_event) == 1);
	assert(failed_click_event.status == GUA_ACTION_STATUS_FAILED);
	assert(failed_click_event.error_code == GUA_ACTION_ERROR_DISABLED);

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
    const gua_reset_options_t strict_reset { sizeof(gua_reset_options_t), GUA_RESET_DEFAULT, 1, 1 };
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
    const gua_reset_options_t stale_reset { sizeof(gua_reset_options_t), GUA_RESET_DEFAULT, 0, 2 };
    assert(gua_reset_context(context, &stale_reset, &report) == GUA_RESET_ERROR_STALE_EPOCH);
    assert(report.session_epoch == 1);

    report = gua_reset_report_t { sizeof(gua_reset_report_t) };
    const gua_reset_options_t reset { sizeof(gua_reset_options_t), GUA_RESET_DEFAULT, 0, 1 };
    assert(gua_reset_context(context, &reset, &report) == GUA_RESET_SUCCEEDED);
    assert(report.previous_session_epoch == 1 && report.session_epoch == 2);
    assert(report.discarded_pending_request_count == 5);
    assert(report.discarded_in_flight_request_count == 1);
    assert(gua_get_context_status(context, &status) == 1);
    assert(status.session_epoch == 2 && status.frame_sequence == 0 && status.revision == 0);
    assert(status.node_count == 0 && status.pending_request_count == 0 && status.unconsumed_event_count == 0);
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
    const gua_reset_options_t strict_events { sizeof(gua_reset_options_t), GUA_RESET_EVENTS, 1, 2 };
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

    gua_destroy_context(context);
    return 0;
}
