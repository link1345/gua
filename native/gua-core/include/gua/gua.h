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

enum {
    GUA_RESET_NODES = 1U << 0,
    GUA_RESET_REQUESTS = 1U << 1,
    GUA_RESET_EVENTS = 1U << 2,
    GUA_RESET_HISTORY = 1U << 3,
    GUA_RESET_LOGS = 1U << 4,
    GUA_RESET_SCREENSHOT = 1U << 5,
    GUA_RESET_DEFAULT = GUA_RESET_NODES | GUA_RESET_REQUESTS | GUA_RESET_EVENTS | GUA_RESET_HISTORY
};

enum {
    GUA_RESET_SUCCEEDED = 1,
    GUA_RESET_ERROR_INVALID_ARGUMENT = -1,
    GUA_RESET_ERROR_DIRTY = -2,
    GUA_RESET_ERROR_STALE_EPOCH = -3
};

typedef struct gua_context_status_t {
    uint32_t struct_size;
    uint64_t session_epoch;
    uint64_t frame_sequence;
    uint64_t revision;
    uint32_t node_count;
    uint32_t pending_request_count;
    uint32_t in_flight_request_count;
    uint32_t unconsumed_event_count;
    uint32_t log_count;
    int has_screenshot;
    int first_pending_action;
    char first_pending_node_id[128];
    int first_event_action;
    char first_event_node_id[128];
} gua_context_status_t;

typedef struct gua_reset_options_t {
    uint32_t struct_size;
    uint32_t flags;
    int strict;
    uint64_t expected_session_epoch;
} gua_reset_options_t;

typedef struct gua_reset_report_t {
    uint32_t struct_size;
    int result;
    uint64_t previous_session_epoch;
    uint64_t session_epoch;
    uint32_t pending_request_count;
    uint32_t in_flight_request_count;
    uint32_t unconsumed_event_count;
    uint32_t discarded_node_count;
    uint32_t discarded_pending_request_count;
    uint32_t discarded_in_flight_request_count;
    uint32_t discarded_event_count;
    uint32_t discarded_log_count;
    int discarded_screenshot;
    int first_pending_action;
    char first_pending_node_id[128];
    int first_event_action;
    char first_event_node_id[128];
} gua_reset_report_t;

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
    GUA_MATCH_EXACT = 0,
    GUA_MATCH_CONTAINS = 1,
    GUA_MATCH_REGEX = 2
};

enum {
    GUA_FILTER_ANY = 0,
    GUA_FILTER_FALSE = 1,
    GUA_FILTER_TRUE = 2
};

typedef struct gua_selector_v1_t {
    uint32_t struct_size;
    const char* id;
    int id_match;
    const char* role;
    int role_match;
    const char* name;
    int name_match;
    const char* text;
    int text_match;
    const char* parent_id;
    int direct_child;
    int visible;
    int enabled;
} gua_selector_v1_t;

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
/* Configures the bounded diagnostics history. A limit of 0 disables retained history. */
int gua_set_diagnostics_history_limit(gua_context_t* ctx, uint32_t history_limit);
/* Stores caller-provided environment metadata as a JSON object for later diagnostics capture. */
int gua_set_diagnostics_environment_json(gua_context_t* ctx, const char* environment_json);
const char* gua_get_diagnostics_json(gua_context_t* ctx);
/* Returns the required byte size including the trailing NUL. Output is NUL-terminated when out_json_size > 0. */
int gua_copy_diagnostics_json(gua_context_t* ctx, char* out_json, int out_json_size);
int gua_get_node_state(gua_context_t* ctx, const char* node_id, gua_node_state_t* out_state);
/* Returns 0 rather than a partial state when a v2 string does not fit its fixed output buffer. */
int gua_get_node_state_v2(gua_context_t* ctx, const char* node_id, gua_node_state_v2_t* out_state);
int gua_find_node_by_id(gua_context_t* ctx, const char* node_id, char* out_node_id, int out_node_id_size);
int gua_find_node_by_role(gua_context_t* ctx, const char* role, const char* name, char* out_node_id, int out_node_id_size);
int gua_find_node_by_text(gua_context_t* ctx, const char* text, char* out_node_id, int out_node_id_size);
/* Returns the required JSON byte size including the trailing NUL. The result contains valid, matches, and optional error fields. */
int gua_query_nodes_json(gua_context_t* ctx, const gua_selector_v1_t* selector, char* out_json, int out_json_size);
int gua_enqueue_click(gua_context_t* ctx, const char* node_id);
int gua_consume_click_request(gua_context_t* ctx, const char* node_id);
int gua_emit_click(gua_context_t* ctx, const char* node_id);
int gua_poll_event(gua_context_t* ctx, gua_event_t* out_event);
int gua_enqueue_action(gua_context_t* ctx, const gua_action_request_descriptor_t* descriptor, uint64_t* out_request_id);
int gua_consume_action_request(gua_context_t* ctx, int action, const char* node_id, gua_action_request_t* out_request);
int gua_emit_action_result(gua_context_t* ctx, const gua_action_result_t* result);
int gua_poll_event_v2(gua_context_t* ctx, gua_event_v2_t* out_event);
int gua_poll_event_v2_for_request(gua_context_t* ctx, uint64_t request_id, gua_event_v2_t* out_event);
int gua_get_context_status(gua_context_t* ctx, gua_context_status_t* out_status);
int gua_reset_context(gua_context_t* ctx, const gua_reset_options_t* options, gua_reset_report_t* out_report);

#ifdef __cplusplus
}
#endif
