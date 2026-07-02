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

const char* gua_runtime_get_ui_tree_json(gua_runtime_t* runtime);
void gua_runtime_add_log(gua_runtime_t* runtime, int level, const char* message);
const char* gua_runtime_get_logs_json(gua_runtime_t* runtime);
void gua_runtime_set_screenshot(gua_runtime_t* runtime, const char* data_uri, int width, int height);
const char* gua_runtime_get_screenshot_json(gua_runtime_t* runtime);
int gua_runtime_get_node_state(gua_runtime_t* runtime, const char* node_id, gua_node_state_t* out_state);
int gua_runtime_find_node_by_id(gua_runtime_t* runtime, const char* node_id, char* out_node_id, int out_node_id_size);
int gua_runtime_find_node_by_role(gua_runtime_t* runtime, const char* role, const char* name, char* out_node_id, int out_node_id_size);
int gua_runtime_find_node_by_text(gua_runtime_t* runtime, const char* text, char* out_node_id, int out_node_id_size);
int gua_runtime_enqueue_click(gua_runtime_t* runtime, const char* node_id);
int gua_runtime_poll_event(gua_runtime_t* runtime, gua_event_t* out_event);

int gua_runtime_start_inspector_bridge(gua_runtime_t* runtime, int port);
void gua_runtime_stop_inspector_bridge(gua_runtime_t* runtime);
int gua_runtime_inspector_bridge_running(gua_runtime_t* runtime);
const char* gua_runtime_inspector_bridge_url(gua_runtime_t* runtime);
void gua_runtime_publish_inspector_snapshot(gua_runtime_t* runtime);

#ifdef __cplusplus
}
#endif
