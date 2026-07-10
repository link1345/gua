#pragma once

#include "gua/gua.h"
#include "gua/gua.hpp"

#include "imgui.h"

#include <string>
#include <string_view>
#include <array>
#include <vector>
#include <cstring>
#include <algorithm>
#include <cstdio>

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
    ActionRequest focus_request;
    const bool requested_focus = context.consume_action(ActionType::focus, id, focus_request);
    if (requested_focus) ImGui::SetKeyboardFocusHere();
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
    if (requested_focus) (void)context.emit_action_result(ActionEvent { focus_request.request_id, ActionType::focus, GUA_ACTION_STATUS_SUCCEEDED, 0, id });

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
    ActionRequest focus_request;
    const bool requested_focus = context.consume_action(ActionType::focus, id, focus_request);
    ImGui::PushID(id_buffer.c_str());
    if (requested_focus) ImGui::SetKeyboardFocusHere();
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
    if (requested_focus) (void)context.emit_action_result(ActionEvent { focus_request.request_id, ActionType::focus, GUA_ACTION_STATUS_SUCCEEDED, 0, std::string(id) });

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

inline bool input_text(Context& context, std::string_view label, std::string& value, bool sensitive = false)
{
    const std::string id = semantic_id_from_label(label);
    ActionRequest request;
    ActionRequest focus_request;
    const bool requested_focus = context.consume_action(ActionType::focus, id, focus_request);
    if (requested_focus) ImGui::SetKeyboardFocusHere();
    while (context.consume_action(ActionType::set_value, id, request)) {
        value = request.value;
        (void)context.emit_action_result(ActionEvent { request.request_id, ActionType::set_value, GUA_ACTION_STATUS_SUCCEEDED, 0, id, value, sensitive || request.sensitive });
    }
    while (context.consume_action(ActionType::press_key, id, request)) {
        bool success = true;
        if (request.key == "Backspace") {
            if (!value.empty()) value.pop_back();
        } else if (request.key.size() == 1) {
            value += request.key;
        } else {
            success = false;
        }
        (void)context.emit_action_result(ActionEvent { request.request_id, ActionType::press_key,
            success ? GUA_ACTION_STATUS_SUCCEEDED : GUA_ACTION_STATUS_FAILED,
            success ? 0 : GUA_ACTION_ERROR_INVALID_VALUE, id, "", sensitive });
    }
    std::array<char, 512> buffer {};
    std::snprintf(buffer.data(), buffer.size(), "%s", value.c_str());
    const std::string label_buffer(label);
    const bool changed = ImGui::InputText(label_buffer.c_str(), buffer.data(), buffer.size());
    if (changed) {
        value = buffer.data();
        (void)context.emit_action_result(ActionEvent { 0, ActionType::set_value, GUA_ACTION_STATUS_SUCCEEDED, 0, id, value, sensitive });
    }
    const ImVec2 min = ImGui::GetItemRectMin();
    const ImVec2 max = ImGui::GetItemRectMax();
    context.node_v2(id, "textbox", visible_label(label), Rect { min.x, min.y, max.x - min.x, max.y - min.y },
        NodeProperties { .text = sensitive ? std::optional<std::string_view>() : std::optional<std::string_view>(value),
            .value = sensitive ? std::optional<std::string_view>() : std::optional<std::string_view>(value), .focused = ImGui::IsItemFocused() },
        ImGui::IsItemVisible(), true);
    if (requested_focus) (void)context.emit_action_result(ActionEvent { focus_request.request_id, ActionType::focus, GUA_ACTION_STATUS_SUCCEEDED, 0, id });
    return changed;
}

inline bool checkbox(Context& context, std::string_view label, bool& checked)
{
    const std::string id = semantic_id_from_label(label);
    ActionRequest request;
    ActionRequest focus_request;
    const bool requested_focus = context.consume_action(ActionType::focus, id, focus_request);
    if (requested_focus) ImGui::SetKeyboardFocusHere();
    while (context.consume_action(ActionType::set_checked, id, request)) {
        checked = request.bool_value;
        (void)context.emit_action_result(ActionEvent { request.request_id, ActionType::set_checked, GUA_ACTION_STATUS_SUCCEEDED, 0, id, checked ? "true" : "false" });
    }
    const std::string label_buffer(label);
    const bool changed = ImGui::Checkbox(label_buffer.c_str(), &checked);
    if (changed) (void)context.emit_action_result(ActionEvent { 0, ActionType::set_checked, GUA_ACTION_STATUS_SUCCEEDED, 0, id, checked ? "true" : "false" });
    const ImVec2 min = ImGui::GetItemRectMin();
    const ImVec2 max = ImGui::GetItemRectMax();
    context.node_v2(id, "checkbox", visible_label(label), Rect { min.x, min.y, max.x - min.x, max.y - min.y },
        NodeProperties { .focused = ImGui::IsItemFocused(), .checked = checked }, ImGui::IsItemVisible(), true);
    if (requested_focus) (void)context.emit_action_result(ActionEvent { focus_request.request_id, ActionType::focus, GUA_ACTION_STATUS_SUCCEEDED, 0, id });
    return changed;
}

inline bool combo(Context& context, std::string_view label, int& selected, const std::vector<std::string>& items)
{
    const std::string id = semantic_id_from_label(label);
    ActionRequest request;
    ActionRequest focus_request;
    const bool requested_focus = context.consume_action(ActionType::focus, id, focus_request);
    if (requested_focus) ImGui::SetKeyboardFocusHere();
    while (context.consume_action(ActionType::select, id, request)) {
        const auto found = std::find(items.begin(), items.end(), request.value);
        const bool success = found != items.end();
        if (success) selected = static_cast<int>(std::distance(items.begin(), found));
        (void)context.emit_action_result(ActionEvent { request.request_id, ActionType::select,
            success ? GUA_ACTION_STATUS_SUCCEEDED : GUA_ACTION_STATUS_FAILED,
            success ? 0 : GUA_ACTION_ERROR_INVALID_VALUE, id, success ? request.value : "" });
    }
    std::vector<const char*> pointers;
    for (const auto& item : items) pointers.push_back(item.c_str());
    const std::string label_buffer(label);
    const bool changed = ImGui::Combo(label_buffer.c_str(), &selected, pointers.data(), static_cast<int>(pointers.size()));
    const std::string value = selected >= 0 && selected < static_cast<int>(items.size()) ? items[static_cast<std::size_t>(selected)] : "";
    if (changed) (void)context.emit_action_result(ActionEvent { 0, ActionType::select, GUA_ACTION_STATUS_SUCCEEDED, 0, id, value });
    const ImVec2 min = ImGui::GetItemRectMin();
    const ImVec2 max = ImGui::GetItemRectMax();
    context.node_v2(id, "combobox", visible_label(label), Rect { min.x, min.y, max.x - min.x, max.y - min.y },
        NodeProperties { .value = value, .focused = ImGui::IsItemFocused() }, ImGui::IsItemVisible(), true);
    if (requested_focus) (void)context.emit_action_result(ActionEvent { focus_request.request_id, ActionType::focus, GUA_ACTION_STATUS_SUCCEEDED, 0, id });
    return changed;
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

inline bool InputText(gua::Context& context, std::string_view label, std::string& value, bool sensitive = false)
{
    return gua::imgui::input_text(context, label, value, sensitive);
}

inline bool Checkbox(gua::Context& context, std::string_view label, bool& checked)
{
    return gua::imgui::checkbox(context, label, checked);
}

inline bool Combo(gua::Context& context, std::string_view label, int& selected, const std::vector<std::string>& items)
{
    return gua::imgui::combo(context, label, selected, items);
}

} // namespace GuaImGui
