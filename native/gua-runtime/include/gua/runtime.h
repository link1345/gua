#pragma once

#include "gua/gua.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct gua_runtime_t gua_runtime_t;

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
int gua_runtime_get_node_state(gua_runtime_t* runtime, const char* node_id, gua_node_state_t* out_state);
int gua_runtime_get_node_state_v2(gua_runtime_t* runtime, const char* node_id, gua_node_state_v2_t* out_state);
int gua_runtime_find_node_by_id(gua_runtime_t* runtime, const char* node_id, char* out_node_id, int out_node_id_size);
int gua_runtime_find_node_by_role(gua_runtime_t* runtime, const char* role, const char* name, char* out_node_id, int out_node_id_size);
int gua_runtime_find_node_by_text(gua_runtime_t* runtime, const char* text, char* out_node_id, int out_node_id_size);
int gua_runtime_enqueue_click(gua_runtime_t* runtime, const char* node_id);
int gua_runtime_consume_click_request(gua_runtime_t* runtime, const char* node_id);
int gua_runtime_emit_click(gua_runtime_t* runtime, const char* node_id);
int gua_runtime_poll_event(gua_runtime_t* runtime, gua_event_t* out_event);
int gua_runtime_enqueue_action(gua_runtime_t* runtime, const gua_action_request_descriptor_t* descriptor, uint64_t* out_request_id);
int gua_runtime_consume_action_request(gua_runtime_t* runtime, int action, const char* node_id, gua_action_request_t* out_request);
int gua_runtime_emit_action_result(gua_runtime_t* runtime, const gua_action_result_t* result);
int gua_runtime_poll_event_v2(gua_runtime_t* runtime, gua_event_v2_t* out_event);
int gua_runtime_poll_event_v2_for_request(gua_runtime_t* runtime, uint64_t request_id, gua_event_v2_t* out_event);

int gua_runtime_start_inspector_bridge(gua_runtime_t* runtime, int port);
void gua_runtime_stop_inspector_bridge(gua_runtime_t* runtime);
int gua_runtime_inspector_bridge_running(gua_runtime_t* runtime);
const char* gua_runtime_inspector_bridge_url(gua_runtime_t* runtime);
void gua_runtime_publish_inspector_snapshot(gua_runtime_t* runtime);

#ifdef __cplusplus
}
#endif
