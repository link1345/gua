#pragma once

#include "gua/gua.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct gua_runtime_t gua_runtime_t;

enum {
    GUA_SCREENSHOT_AVAILABLE = 1,
    GUA_SCREENSHOT_UNAVAILABLE_HEADLESS = -1,
    GUA_SCREENSHOT_UNAVAILABLE_RENDERING_DISABLED = -2,
    GUA_SCREENSHOT_UNAVAILABLE_UNSUPPORTED = -3,
    GUA_SCREENSHOT_UNAVAILABLE_STALE_SESSION = -4
};

typedef struct gua_screenshot_request_t {
    uint32_t struct_size;
    uint64_t request_id;
    uint64_t session_epoch;
    uint64_t after_frame_sequence;
} gua_screenshot_request_t;

gua_runtime_t* gua_runtime_create(void);
void gua_runtime_destroy(gua_runtime_t* runtime);

void gua_runtime_begin_frame(gua_runtime_t* runtime, const char* screen);
void gua_runtime_end_frame(gua_runtime_t* runtime);
void gua_runtime_register_node(
    gua_runtime_t* runtime,
    const char* id,
    const char* role,
    const char* label,
    gua_bounds_t bounds,
    int visible,
    int enabled
);
int gua_runtime_register_node_v2(gua_runtime_t* runtime, const gua_node_descriptor_v2_t* descriptor);
int gua_runtime_register_node_v3(gua_runtime_t* runtime, const gua_node_descriptor_v3_t* descriptor);

const char* gua_runtime_get_ui_tree_json(gua_runtime_t* runtime);
/* Returns the required byte size including the trailing NUL. Output is NUL-terminated when out_json_size > 0. */
int gua_runtime_copy_ui_tree_json(gua_runtime_t* runtime, char* out_json, int out_json_size);
void gua_runtime_add_log(gua_runtime_t* runtime, int level, const char* message);
const char* gua_runtime_get_logs_json(gua_runtime_t* runtime);
/* Returns the required byte size including the trailing NUL. Output is NUL-terminated when out_json_size > 0. */
int gua_runtime_copy_logs_json(gua_runtime_t* runtime, char* out_json, int out_json_size);
void gua_runtime_set_screenshot(gua_runtime_t* runtime, const char* data_uri, int width, int height);
const char* gua_runtime_get_screenshot_json(gua_runtime_t* runtime);
/* Returns the required byte size including the trailing NUL. Output is NUL-terminated when out_json_size > 0. */
int gua_runtime_copy_screenshot_json(gua_runtime_t* runtime, char* out_json, int out_json_size);
int gua_runtime_enqueue_screenshot_request(gua_runtime_t* runtime, uint64_t after_frame_sequence, uint64_t* out_request_id);
int gua_runtime_consume_screenshot_request(gua_runtime_t* runtime, gua_screenshot_request_t* out_request);
int gua_runtime_complete_screenshot_request(gua_runtime_t* runtime, uint64_t request_id, int result, const char* data_uri, int width, int height);
int gua_runtime_poll_screenshot_result_json(gua_runtime_t* runtime, uint64_t request_id, char* out_json, int out_json_size);
int gua_runtime_cancel_screenshot_request(gua_runtime_t* runtime, uint64_t request_id);
int gua_runtime_set_diagnostics_history_limit(gua_runtime_t* runtime, uint32_t history_limit);
int gua_runtime_set_diagnostics_environment_json(gua_runtime_t* runtime, const char* environment_json);
const char* gua_runtime_get_diagnostics_json(gua_runtime_t* runtime);
int gua_runtime_copy_diagnostics_json(gua_runtime_t* runtime, char* out_json, int out_json_size);
int gua_runtime_copy_version_json(gua_runtime_t* runtime, char* out_json, int out_json_size);
void gua_runtime_set_godot_plugin_version(gua_runtime_t* runtime, const char* version);
void gua_runtime_set_adapter_version(gua_runtime_t* runtime, const char* adapter, const char* version);
int gua_runtime_get_node_state(gua_runtime_t* runtime, const char* node_id, gua_node_state_t* out_state);
int gua_runtime_get_node_state_v2(gua_runtime_t* runtime, const char* node_id, gua_node_state_v2_t* out_state);
int gua_runtime_find_node_by_id(gua_runtime_t* runtime, const char* node_id, char* out_node_id, int out_node_id_size);
int gua_runtime_find_node_by_role(gua_runtime_t* runtime, const char* role, const char* name, char* out_node_id, int out_node_id_size);
int gua_runtime_find_node_by_text(gua_runtime_t* runtime, const char* text, char* out_node_id, int out_node_id_size);
int gua_runtime_query_nodes_json(gua_runtime_t* runtime, const gua_selector_v1_t* selector, char* out_json, int out_json_size);
int gua_runtime_enqueue_click(gua_runtime_t* runtime, const char* node_id);
int gua_runtime_consume_click_request(gua_runtime_t* runtime, const char* node_id);
int gua_runtime_emit_click(gua_runtime_t* runtime, const char* node_id);
int gua_runtime_poll_event(gua_runtime_t* runtime, gua_event_t* out_event);
int gua_runtime_enqueue_action(gua_runtime_t* runtime, const gua_action_request_descriptor_t* descriptor, uint64_t* out_request_id);
int gua_runtime_consume_action_request(gua_runtime_t* runtime, int action, const char* node_id, gua_action_request_t* out_request);
int gua_runtime_emit_action_result(gua_runtime_t* runtime, const gua_action_result_t* result);
int gua_runtime_poll_event_v2(gua_runtime_t* runtime, gua_event_v2_t* out_event);
int gua_runtime_poll_event_v2_for_request(gua_runtime_t* runtime, uint64_t request_id, gua_event_v2_t* out_event);
int gua_runtime_poll_event_v3(gua_runtime_t* runtime, gua_event_v3_t* out_event);
int gua_runtime_poll_event_v3_for_request(gua_runtime_t* runtime, uint64_t request_id, gua_event_v3_t* out_event);
int gua_runtime_get_context_status(gua_runtime_t* runtime, gua_context_status_t* out_status);
int gua_runtime_reset_context(gua_runtime_t* runtime, const gua_reset_options_t* options, gua_reset_report_t* out_report);

int gua_runtime_start_inspector_bridge(gua_runtime_t* runtime, int port);
void gua_runtime_stop_inspector_bridge(gua_runtime_t* runtime);
int gua_runtime_inspector_bridge_running(gua_runtime_t* runtime);
const char* gua_runtime_inspector_bridge_url(gua_runtime_t* runtime);
void gua_runtime_publish_inspector_snapshot(gua_runtime_t* runtime);

#ifdef __cplusplus
}
#endif
