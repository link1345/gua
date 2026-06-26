#pragma once

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

typedef struct gua_node_state_t {
    int visible;
    int enabled;
} gua_node_state_t;

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

const char* gua_get_ui_tree_json(gua_context_t* ctx);
int gua_get_node_state(gua_context_t* ctx, const char* node_id, gua_node_state_t* out_state);
int gua_find_node_by_id(gua_context_t* ctx, const char* node_id, char* out_node_id, int out_node_id_size);
int gua_find_node_by_role(gua_context_t* ctx, const char* role, const char* name, char* out_node_id, int out_node_id_size);
int gua_find_node_by_text(gua_context_t* ctx, const char* text, char* out_node_id, int out_node_id_size);
int gua_enqueue_click(gua_context_t* ctx, const char* node_id);
int gua_poll_event(gua_context_t* ctx, gua_event_t* out_event);

#ifdef __cplusplus
}
#endif
