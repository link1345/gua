#pragma once

#include "gua/gua.h"
#include "gua/gua.hpp"

#include "imgui.h"

#include <string>
#include <string_view>

namespace gua::imgui {

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

inline bool button(
    Context& context,
    std::string_view id,
    std::string_view label,
    Rect bounds,
    bool clicked,
    bool visible = true,
    bool enabled = true)
{
    context.button(id, label, bounds, visible, enabled);
    if (clicked && visible && enabled) {
        return context.enqueue_click(id);
    }

    return false;
}

inline bool button(Context& context, std::string_view id, std::string_view label)
{
    const std::string label_buffer(label);
    const bool clicked = ImGui::Button(label_buffer.c_str());
    const ImVec2 min = ImGui::GetItemRectMin();
    const ImVec2 max = ImGui::GetItemRectMax();
    const bool visible = ImGui::IsItemVisible();

    context.button(
        id,
        label,
        Rect { min.x, min.y, max.x - min.x, max.y - min.y },
        visible,
        true);

    if (clicked && visible) {
        return context.enqueue_click(id);
    }

    return false;
}

} // namespace gua::imgui

namespace GuaImGui {

using Rect = gua::Rect;

inline bool Button(
    gua::Context& context,
    std::string_view id,
    std::string_view label,
    Rect bounds,
    bool clicked = false,
    bool visible = true,
    bool enabled = true)
{
    return gua::imgui::button(context, id, label, bounds, clicked, visible, enabled);
}

inline bool Button(gua::Context& context, std::string_view id, std::string_view label)
{
    return gua::imgui::button(context, id, label);
}

} // namespace GuaImGui
