#pragma once

#include "gua/gua.h"

#include <stdexcept>
#include <optional>
#include <string>
#include <string_view>

namespace gua {

struct Rect {
    float x;
    float y;
    float w;
    float h;
};

enum class EventType {
    none = GUA_EVENT_NONE,
    click = GUA_EVENT_CLICK,
    focus = GUA_EVENT_FOCUS,
};

enum class LogLevel {
    trace = GUA_LOG_TRACE,
    debug = GUA_LOG_DEBUG,
    info = GUA_LOG_INFO,
    warn = GUA_LOG_WARN,
    error = GUA_LOG_ERROR,
};

struct Event {
    EventType type = EventType::none;
    std::string node_id;
};

struct NodeProperties {
    std::optional<std::string_view> parent_id;
    std::optional<std::string_view> text;
    std::optional<std::string_view> value;
    std::optional<bool> focused;
    std::optional<bool> hovered;
    std::optional<bool> pressed;
    std::optional<bool> checked;
    std::optional<bool> selected;
};

class Context {
public:
    Context()
        : context_(gua_create_context())
    {
        if (context_ == nullptr) {
            throw std::runtime_error("Failed to create Gua context");
        }
    }

    Context(const Context&) = delete;
    Context& operator=(const Context&) = delete;

    Context(Context&& other) noexcept
        : context_(other.context_)
    {
        other.context_ = nullptr;
    }

    Context& operator=(Context&& other) noexcept
    {
        if (this != &other) {
            reset();
            context_ = other.context_;
            other.context_ = nullptr;
        }
        return *this;
    }

    ~Context()
    {
        reset();
    }

    [[nodiscard]] gua_context_t* native_handle() const noexcept
    {
        return context_;
    }

    void begin_frame(std::string_view screen)
    {
        screen_buffer_.assign(screen);
        gua_begin_frame(context_, screen_buffer_.c_str());
    }

    void end_frame()
    {
        gua_end_frame(context_);
    }

    void node(
        std::string_view id,
        std::string_view role,
        std::string_view label,
        Rect bounds,
        bool visible = true,
        bool enabled = true)
    {
        id_buffer_.assign(id);
        role_buffer_.assign(role);
        label_buffer_.assign(label);
        gua_register_node(
            context_,
            id_buffer_.c_str(),
            role_buffer_.c_str(),
            label_buffer_.c_str(),
            gua_bounds_t { bounds.x, bounds.y, bounds.w, bounds.h },
            visible ? 1 : 0,
            enabled ? 1 : 0);
    }

    void node_v2(
        std::string_view id,
        std::string_view role,
        std::string_view label,
        Rect bounds,
        const NodeProperties& properties = {},
        bool visible = true,
        bool enabled = true)
    {
        id_buffer_.assign(id);
        role_buffer_.assign(role);
        label_buffer_.assign(label);
        parent_id_buffer_ = properties.parent_id ? std::string(*properties.parent_id) : std::string();
        text_buffer_ = properties.text ? std::string(*properties.text) : std::string();
        value_buffer_ = properties.value ? std::string(*properties.value) : std::string();

        unsigned long long known_mask = 0;
        if (properties.parent_id) known_mask |= GUA_NODE_KNOWN_PARENT_ID;
        if (properties.text) known_mask |= GUA_NODE_KNOWN_TEXT;
        if (properties.value) known_mask |= GUA_NODE_KNOWN_VALUE;
        if (properties.focused) known_mask |= GUA_NODE_KNOWN_FOCUSED;
        if (properties.hovered) known_mask |= GUA_NODE_KNOWN_HOVERED;
        if (properties.pressed) known_mask |= GUA_NODE_KNOWN_PRESSED;
        if (properties.checked) known_mask |= GUA_NODE_KNOWN_CHECKED;
        if (properties.selected) known_mask |= GUA_NODE_KNOWN_SELECTED;

        const gua_node_descriptor_v2_t descriptor {
            sizeof(gua_node_descriptor_v2_t),
            known_mask,
            id_buffer_.c_str(),
            properties.parent_id ? parent_id_buffer_.c_str() : nullptr,
            role_buffer_.c_str(),
            label_buffer_.c_str(),
            properties.text ? text_buffer_.c_str() : nullptr,
            properties.value ? value_buffer_.c_str() : nullptr,
            gua_bounds_t { bounds.x, bounds.y, bounds.w, bounds.h },
            visible ? 1 : 0,
            enabled ? 1 : 0,
            properties.focused.value_or(false) ? 1 : 0,
            properties.hovered.value_or(false) ? 1 : 0,
            properties.pressed.value_or(false) ? 1 : 0,
            properties.checked.value_or(false) ? 1 : 0,
            properties.selected.value_or(false) ? 1 : 0,
        };
        if (gua_register_node_v2(context_, &descriptor) == 0) {
            throw std::runtime_error("Failed to register Gua v2 node");
        }
    }

    void button(std::string_view id, std::string_view label, Rect bounds, bool visible = true, bool enabled = true)
    {
        node(id, "button", label, bounds, visible, enabled);
    }

    void text(std::string_view id, std::string_view label, Rect bounds, bool visible = true)
    {
        node(id, "text", label, bounds, visible, false);
    }

    void panel(std::string_view id, std::string_view label, Rect bounds, bool visible = true)
    {
        node(id, "panel", label, bounds, visible, false);
    }

    [[nodiscard]] std::string ui_tree_json() const
    {
        return copy_json(gua_copy_ui_tree_json);
    }

    void log(LogLevel level, std::string_view message)
    {
        message_buffer_.assign(message);
        gua_add_log(context_, static_cast<int>(level), message_buffer_.c_str());
    }

    [[nodiscard]] std::string logs_json() const
    {
        return copy_json(gua_copy_logs_json);
    }

    void set_screenshot(std::string_view data_uri, int width, int height)
    {
        screenshot_buffer_.assign(data_uri);
        gua_set_screenshot(context_, screenshot_buffer_.c_str(), width, height);
    }

    [[nodiscard]] std::string screenshot_json() const
    {
        return copy_json(gua_copy_screenshot_json);
    }

    [[nodiscard]] bool enqueue_click(std::string_view node_id)
    {
        id_buffer_.assign(node_id);
        return gua_enqueue_click(context_, id_buffer_.c_str()) != 0;
    }

    [[nodiscard]] bool consume_click_request(std::string_view node_id)
    {
        id_buffer_.assign(node_id);
        return gua_consume_click_request(context_, id_buffer_.c_str()) != 0;
    }

    [[nodiscard]] bool emit_click(std::string_view node_id)
    {
        id_buffer_.assign(node_id);
        return gua_emit_click(context_, id_buffer_.c_str()) != 0;
    }

    [[nodiscard]] bool poll_event(Event& out_event)
    {
        gua_event_t native_event {};
        if (gua_poll_event(context_, &native_event) == 0) {
            return false;
        }

        out_event.type = static_cast<EventType>(native_event.type);
        out_event.node_id = native_event.node_id;
        return true;
    }

private:
    template <typename CopyJson>
    [[nodiscard]] std::string copy_json(CopyJson copy) const
    {
        int required_size = copy(context_, nullptr, 0);
        if (required_size <= 0) {
            throw std::runtime_error("Failed to copy Gua JSON");
        }

        for (;;) {
            std::string json(static_cast<std::size_t>(required_size), '\0');
            const int actual_size = copy(context_, json.data(), static_cast<int>(json.size()));
            if (actual_size <= 0) {
                throw std::runtime_error("Failed to copy Gua JSON");
            }
            if (actual_size <= static_cast<int>(json.size())) {
                json.resize(static_cast<std::size_t>(actual_size - 1));
                return json;
            }
            required_size = actual_size;
        }
    }

    void reset() noexcept
    {
        gua_destroy_context(context_);
        context_ = nullptr;
    }

    gua_context_t* context_;
    mutable std::string id_buffer_;
    mutable std::string role_buffer_;
    mutable std::string label_buffer_;
    mutable std::string parent_id_buffer_;
    mutable std::string text_buffer_;
    mutable std::string value_buffer_;
    mutable std::string message_buffer_;
    std::string screenshot_buffer_;
    std::string screen_buffer_;
};

inline void begin_frame(Context& context, std::string_view screen)
{
    context.begin_frame(screen);
}

inline void end_frame(Context& context)
{
    context.end_frame();
}

inline void button(Context& context, std::string_view id, std::string_view label, Rect bounds, bool visible = true, bool enabled = true)
{
    context.button(id, label, bounds, visible, enabled);
}

inline void text(Context& context, std::string_view id, std::string_view label, Rect bounds, bool visible = true)
{
    context.text(id, label, bounds, visible);
}

inline void panel(Context& context, std::string_view id, std::string_view label, Rect bounds, bool visible = true)
{
    context.panel(id, label, bounds, visible);
}

inline void log(Context& context, LogLevel level, std::string_view message)
{
    context.log(level, message);
}

inline void set_screenshot(Context& context, std::string_view data_uri, int width, int height)
{
    context.set_screenshot(data_uri, width, height);
}

} // namespace gua
