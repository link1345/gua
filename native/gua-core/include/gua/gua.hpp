#pragma once

#include "gua/gua.h"

#include <stdexcept>
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
        return gua_get_ui_tree_json(context_);
    }

    void log(LogLevel level, std::string_view message)
    {
        message_buffer_.assign(message);
        gua_add_log(context_, static_cast<int>(level), message_buffer_.c_str());
    }

    [[nodiscard]] std::string logs_json() const
    {
        return gua_get_logs_json(context_);
    }

    void set_screenshot(std::string_view data_uri, int width, int height)
    {
        screenshot_buffer_.assign(data_uri);
        gua_set_screenshot(context_, screenshot_buffer_.c_str(), width, height);
    }

    [[nodiscard]] std::string screenshot_json() const
    {
        return gua_get_screenshot_json(context_);
    }

    [[nodiscard]] bool enqueue_click(std::string_view node_id)
    {
        id_buffer_.assign(node_id);
        return gua_enqueue_click(context_, id_buffer_.c_str()) != 0;
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
    void reset() noexcept
    {
        gua_destroy_context(context_);
        context_ = nullptr;
    }

    gua_context_t* context_;
    mutable std::string id_buffer_;
    mutable std::string role_buffer_;
    mutable std::string label_buffer_;
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
