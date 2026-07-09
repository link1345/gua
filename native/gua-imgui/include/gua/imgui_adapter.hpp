#pragma once

#include "gua/gua.h"
#include "gua/gua.hpp"

#include "imgui.h"

#include <string>
#include <string_view>

namespace gua::imgui {

inline std::string visible_label(std::string_view label)
{
    const std::size_t marker = label.find("##");
    return std::string(marker == std::string_view::npos ? label : label.substr(0, marker));
}

inline std::string semantic_id_from_label(std::string_view label)
{
    const std::size_t marker = label.find("##");
    if (marker != std::string_view::npos && marker + 2U < label.size()) {
        return std::string(label.substr(marker + 2U));
    }

    const std::string label_buffer(label);
    return "imgui:" + std::to_string(static_cast<unsigned int>(ImGui::GetID(label_buffer.c_str())));
}

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
    context.node_v2(
        id,
        "button",
        label,
        bounds,
        NodeProperties { .text = label, .pressed = clicked },
        visible,
        enabled);
    if (clicked && visible && enabled) {
        return context.emit_click(id);
    }

    return false;
}

inline bool button(Context& context, std::string_view label)
{
    const std::string label_buffer(label);
    const std::string id = semantic_id_from_label(label);
    const std::string visible_label_buffer = visible_label(label);
    const bool clicked = ImGui::Button(label_buffer.c_str());
    const ImVec2 min = ImGui::GetItemRectMin();
    const ImVec2 max = ImGui::GetItemRectMax();
    const bool visible = ImGui::IsItemVisible();
    const bool enabled = true;
    const bool focused = ImGui::IsItemFocused();
    const bool hovered = ImGui::IsItemHovered();
    const bool pressed = ImGui::IsItemActive();

    context.node_v2(
        id,
        "button",
        visible_label_buffer,
        Rect { min.x, min.y, max.x - min.x, max.y - min.y },
        NodeProperties { .text = visible_label_buffer, .focused = focused, .hovered = hovered, .pressed = pressed },
        visible,
        enabled);

    const bool requested = context.consume_click_request(id);
    if ((clicked || requested) && visible && enabled) {
        (void)context.emit_click(id);
        return true;
    }

    return false;
}

inline bool button(Context& context, std::string_view id, std::string_view label)
{
    const std::string id_buffer(id);
    const std::string label_buffer(label);
    const std::string visible_label_buffer = visible_label(label);
    ImGui::PushID(id_buffer.c_str());
    const bool clicked = ImGui::Button(label_buffer.c_str());
    const ImVec2 min = ImGui::GetItemRectMin();
    const ImVec2 max = ImGui::GetItemRectMax();
    const bool visible = ImGui::IsItemVisible();
    const bool enabled = true;
    const bool focused = ImGui::IsItemFocused();
    const bool hovered = ImGui::IsItemHovered();
    const bool pressed = ImGui::IsItemActive();
    ImGui::PopID();

    context.node_v2(
        id,
        "button",
        visible_label_buffer,
        Rect { min.x, min.y, max.x - min.x, max.y - min.y },
        NodeProperties { .text = visible_label_buffer, .focused = focused, .hovered = hovered, .pressed = pressed },
        visible,
        enabled);

    const bool requested = context.consume_click_request(id);
    if ((clicked || requested) && visible && enabled) {
        (void)context.emit_click(id);
        return true;
    }

    return false;
}

inline void text(Context& context, std::string_view label)
{
    const std::string label_buffer(label);
    const std::string id = semantic_id_from_label(label);
    const std::string visible_label_buffer = visible_label(label);
    ImGui::TextUnformatted(visible_label_buffer.c_str());
    const ImVec2 min = ImGui::GetItemRectMin();
    const ImVec2 max = ImGui::GetItemRectMax();
    context.node_v2(
        id,
        "text",
        visible_label_buffer,
        Rect { min.x, min.y, max.x - min.x, max.y - min.y },
        NodeProperties { .text = visible_label_buffer },
        ImGui::IsItemVisible(),
        false);
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

inline bool Button(gua::Context& context, std::string_view label)
{
    return gua::imgui::button(context, label);
}

inline void Text(gua::Context& context, std::string_view label)
{
    gua::imgui::text(context, label);
}

} // namespace GuaImGui
