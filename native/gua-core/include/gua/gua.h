#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct gua_context_t gua_context_t;

typedef struct gua_bounds_t {
    float x;
    float y;
    float w;
    float h;
} gua_bounds_t;

typedef struct gua_event_t {
    int type;
    char node_id[128];
} gua_event_t;

enum {
    GUA_ACTION_CLICK = 1,
    GUA_ACTION_FOCUS = 2,
    GUA_ACTION_SET_VALUE = 3,
    GUA_ACTION_SET_CHECKED = 4,
    GUA_ACTION_SELECT = 5,
    GUA_ACTION_SCROLL = 6,
    GUA_ACTION_PRESS_KEY = 7
};

enum {
    GUA_ACTION_ACCEPTED = 1,
    GUA_ACTION_ERROR_INVALID_ARGUMENT = -1,
    GUA_ACTION_ERROR_NODE_NOT_FOUND = -2,
    GUA_ACTION_ERROR_HIDDEN = -3,
    GUA_ACTION_ERROR_DISABLED = -4,
    GUA_ACTION_ERROR_UNSUPPORTED = -5,
    GUA_ACTION_ERROR_INVALID_VALUE = -6
};

enum {
    GUA_ACTION_STATUS_SUCCEEDED = 1,
    GUA_ACTION_STATUS_FAILED = 2
};

typedef struct gua_action_request_descriptor_t {
    uint32_t struct_size;
    int action;
    const char* node_id;
    const char* value;
    float delta_x;
    float delta_y;
    int bool_value;
    const char* key;
    uint32_t modifiers;
    int sensitive;
    int scroll_unit;
} gua_action_request_descriptor_t;

typedef struct gua_action_request_t {
    uint32_t struct_size;
    uint64_t request_id;
    int action;
    char node_id[128];
    char value[256];
    float delta_x;
    float delta_y;
    int bool_value;
    char key[64];
    uint32_t modifiers;
    int sensitive;
    int scroll_unit;
} gua_action_request_t;

typedef struct gua_action_result_t {
    uint32_t struct_size;
    uint64_t request_id;
    int action;
    int status;
    int error_code;
    const char* node_id;
    const char* value;
    int sensitive;
} gua_action_result_t;

typedef struct gua_event_v2_t {
    uint32_t struct_size;
    uint64_t request_id;
    int action;
    int status;
    int error_code;
    char node_id[128];
    char value[256];
    int sensitive;
} gua_event_v2_t;

typedef struct gua_node_state_t {
    int visible;
    int enabled;
} gua_node_state_t;

enum {
    GUA_NODE_KNOWN_PARENT_ID = 1ULL << 0,
    GUA_NODE_KNOWN_TEXT = 1ULL << 1,
    GUA_NODE_KNOWN_VALUE = 1ULL << 2,
    GUA_NODE_KNOWN_FOCUSED = 1ULL << 3,
    GUA_NODE_KNOWN_HOVERED = 1ULL << 4,
    GUA_NODE_KNOWN_PRESSED = 1ULL << 5,
    GUA_NODE_KNOWN_CHECKED = 1ULL << 6,
    GUA_NODE_KNOWN_SELECTED = 1ULL << 7
};

typedef struct gua_node_descriptor_v2_t {
    uint32_t struct_size;
    uint64_t known_mask;
    const char* id;
    const char* parent_id;
    const char* role;
    const char* label;
    const char* text;
    const char* value;
    gua_bounds_t bounds;
    int visible;
    int enabled;
    int focused;
    int hovered;
    int pressed;
    int checked;
    int selected;
} gua_node_descriptor_v2_t;

typedef struct gua_node_state_v2_t {
    uint32_t struct_size;
    uint64_t known_mask;
    int visible;
    int enabled;
    int focused;
    int hovered;
    int pressed;
    int checked;
    int selected;
    char parent_id[128];
    char text[256];
    char value[256];
} gua_node_state_v2_t;

enum {
    GUA_LOG_TRACE = 0,
    GUA_LOG_DEBUG = 1,
    GUA_LOG_INFO = 2,
    GUA_LOG_WARN = 3,
    GUA_LOG_ERROR = 4
};

enum {
    GUA_EVENT_NONE = 0,
    GUA_EVENT_CLICK = 1,
    GUA_EVENT_FOCUS = 2
};

gua_context_t* gua_create_context(void);
void gua_destroy_context(gua_context_t* ctx);

void gua_begin_frame(gua_context_t* ctx, const char* screen);
void gua_end_frame(gua_context_t* ctx);

void gua_register_node(
    gua_context_t* ctx,
    const char* id,
    const char* role,
    const char* label,
    gua_bounds_t bounds,
    int visible,
    int enabled
);
int gua_register_node_v2(gua_context_t* ctx, const gua_node_descriptor_v2_t* descriptor);

const char* gua_get_ui_tree_json(gua_context_t* ctx);
/* Returns the required byte size including the trailing NUL. Output is NUL-terminated when out_json_size > 0. */
int gua_copy_ui_tree_json(gua_context_t* ctx, char* out_json, int out_json_size);
void gua_add_log(gua_context_t* ctx, int level, const char* message);
const char* gua_get_logs_json(gua_context_t* ctx);
/* Returns the required byte size including the trailing NUL. Output is NUL-terminated when out_json_size > 0. */
int gua_copy_logs_json(gua_context_t* ctx, char* out_json, int out_json_size);
void gua_set_screenshot(gua_context_t* ctx, const char* data_uri, int width, int height);
const char* gua_get_screenshot_json(gua_context_t* ctx);
/* Returns the required byte size including the trailing NUL. Output is NUL-terminated when out_json_size > 0. */
int gua_copy_screenshot_json(gua_context_t* ctx, char* out_json, int out_json_size);
int gua_get_node_state(gua_context_t* ctx, const char* node_id, gua_node_state_t* out_state);
int gua_get_node_state_v2(gua_context_t* ctx, const char* node_id, gua_node_state_v2_t* out_state);
int gua_find_node_by_id(gua_context_t* ctx, const char* node_id, char* out_node_id, int out_node_id_size);
int gua_find_node_by_role(gua_context_t* ctx, const char* role, const char* name, char* out_node_id, int out_node_id_size);
int gua_find_node_by_text(gua_context_t* ctx, const char* text, char* out_node_id, int out_node_id_size);
int gua_enqueue_click(gua_context_t* ctx, const char* node_id);
int gua_consume_click_request(gua_context_t* ctx, const char* node_id);
int gua_emit_click(gua_context_t* ctx, const char* node_id);
int gua_poll_event(gua_context_t* ctx, gua_event_t* out_event);
int gua_enqueue_action(gua_context_t* ctx, const gua_action_request_descriptor_t* descriptor, uint64_t* out_request_id);
int gua_consume_action_request(gua_context_t* ctx, int action, const char* node_id, gua_action_request_t* out_request);
int gua_emit_action_result(gua_context_t* ctx, const gua_action_result_t* result);
int gua_poll_event_v2(gua_context_t* ctx, gua_event_v2_t* out_event);
int gua_poll_event_v2_for_request(gua_context_t* ctx, uint64_t request_id, gua_event_v2_t* out_event);

#ifdef __cplusplus
}
#endif
