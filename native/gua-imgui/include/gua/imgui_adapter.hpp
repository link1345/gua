#pragma once

#include "gua/gua.h"

namespace gua::imgui {

struct Rect {
    float x;
    float y;
    float w;
    float h;
};

inline void register_button(
    gua_context_t* context,
    const char* id,
    const char* label,
    Rect bounds,
    bool visible = true,
    bool enabled = true)
{
    gua_register_node(
        context,
        id,
        "button",
        label,
        gua_bounds_t { bounds.x, bounds.y, bounds.w, bounds.h },
        visible ? 1 : 0,
        enabled ? 1 : 0);
}

} // namespace gua::imgui
