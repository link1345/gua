#pragma once

#include "gua/gua.h"

#include <chrono>
#include <cstdint>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <utility>

namespace gua::testing {

class Locator {
public:
    Locator(gua_context_t* context, std::string id)
        : context_(context)
        , id_(std::move(id))
    {
    }

    [[nodiscard]] const std::string& id() const noexcept
    {
        return id_;
    }

    void to_exist() const
    {
        (void)read_state();
    }

    void to_be_visible() const
    {
        const gua_node_state_t state = read_state();
        if (state.visible == 0) {
            throw std::runtime_error("Expected Gua node to be visible: " + id_);
        }
    }

    void to_be_enabled() const
    {
        const gua_node_state_t state = read_state();
        if (state.enabled == 0) {
            throw std::runtime_error("Expected Gua node to be enabled: " + id_);
        }
    }

    void click() const
    {
        if (gua_enqueue_click(context_, id_.c_str()) == 0) {
            throw std::runtime_error("Failed to click Gua node: " + id_);
        }
    }

    [[nodiscard]] std::uint64_t focus() const { return enqueue(GUA_ACTION_FOCUS); }
    [[nodiscard]] std::uint64_t set_value(std::string_view value, bool sensitive = false) const { return enqueue(GUA_ACTION_SET_VALUE, value, false, sensitive); }
    [[nodiscard]] std::uint64_t set_checked(bool value) const { return enqueue(GUA_ACTION_SET_CHECKED, {}, value); }
    [[nodiscard]] std::uint64_t select(std::string_view value) const { return enqueue(GUA_ACTION_SELECT, value); }
    [[nodiscard]] std::uint64_t scroll(float dx, float dy, int unit = 0) const
    {
        gua_action_request_descriptor_t descriptor { sizeof(gua_action_request_descriptor_t), GUA_ACTION_SCROLL, id_.c_str(), nullptr, dx, dy, 0, nullptr, 0, 0, unit };
        return enqueue_descriptor(descriptor);
    }

    [[nodiscard]] std::uint64_t press_key(std::string_view key, std::uint32_t modifiers = 0) const
    {
        std::string key_buffer(key);
        gua_action_request_descriptor_t descriptor { sizeof(gua_action_request_descriptor_t), GUA_ACTION_PRESS_KEY, id_.c_str(), nullptr, 0, 0, 0, key_buffer.c_str(), modifiers, 0, 0 };
        return enqueue_descriptor(descriptor);
    }

    void wait_for_completion(std::uint64_t request_id, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000)) const
    {
        const auto deadline = std::chrono::steady_clock::now() + timeout;
        do {
            gua_event_v2_t event { sizeof(gua_event_v2_t) };
            if (gua_poll_event_v2_for_request(context_, request_id, &event) != 0) {
                if (event.status == GUA_ACTION_STATUS_FAILED) throw std::runtime_error("Gua action failed for node: " + id_);
                return;
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        } while (std::chrono::steady_clock::now() < deadline);
        throw std::runtime_error("Timed out waiting for Gua action completion: " + id_);
    }

    void wait_for(std::chrono::milliseconds timeout = std::chrono::milliseconds(1000)) const
    {
        const auto deadline = std::chrono::steady_clock::now() + timeout;
        do {
            gua_node_state_t state {};
            if (gua_get_node_state(context_, id_.c_str(), &state) != 0) {
                return;
            }

            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        } while (std::chrono::steady_clock::now() < deadline);

        throw std::runtime_error("Timed out waiting for Gua node: " + id_);
    }

private:
    [[nodiscard]] std::uint64_t enqueue(int action, std::string_view value = {}, bool bool_value = false, bool sensitive = false) const
    {
        std::string value_buffer(value);
        gua_action_request_descriptor_t descriptor { sizeof(gua_action_request_descriptor_t), action, id_.c_str(),
            value_buffer.empty() ? nullptr : value_buffer.c_str(), 0, 0, bool_value ? 1 : 0, nullptr, 0, sensitive ? 1 : 0, 0 };
        return enqueue_descriptor(descriptor);
    }

    [[nodiscard]] std::uint64_t enqueue_descriptor(const gua_action_request_descriptor_t& descriptor) const
    {
        std::uint64_t request_id = 0;
        const int result = gua_enqueue_action(context_, &descriptor, &request_id);
        if (result != GUA_ACTION_ACCEPTED) throw std::runtime_error("Failed to enqueue Gua action (" + std::to_string(result) + ") for node: " + id_);
        return request_id;
    }

    gua_node_state_t read_state() const
    {
        gua_node_state_t state {};
        if (gua_get_node_state(context_, id_.c_str(), &state) == 0) {
            throw std::runtime_error("Gua node not found: " + id_);
        }
        return state;
    }

    gua_context_t* context_;
    std::string id_;
};

inline std::string read_node_id(char const* description, int found, const char* buffer)
{
    if (found == 0) {
        throw std::runtime_error(std::string("Gua node not found by ") + description);
    }
    return buffer;
}

inline Locator get_by_id(gua_context_t* context, std::string id)
{
    char node_id[128] {};
    return Locator(context, read_node_id("id", gua_find_node_by_id(context, id.c_str(), node_id, static_cast<int>(sizeof(node_id))), node_id));
}

inline Locator get_by_role(gua_context_t* context, std::string_view role)
{
    char node_id[128] {};
    std::string role_buffer(role);
    return Locator(context, read_node_id("role", gua_find_node_by_role(context, role_buffer.c_str(), nullptr, node_id, static_cast<int>(sizeof(node_id))), node_id));
}

inline Locator get_by_role(gua_context_t* context, std::string_view role, std::string_view name)
{
    char node_id[128] {};
    std::string role_buffer(role);
    std::string name_buffer(name);
    return Locator(context, read_node_id("role and name", gua_find_node_by_role(context, role_buffer.c_str(), name_buffer.c_str(), node_id, static_cast<int>(sizeof(node_id))), node_id));
}

inline Locator get_by_text(gua_context_t* context, std::string_view text)
{
    char node_id[128] {};
    std::string text_buffer(text);
    return Locator(context, read_node_id("text", gua_find_node_by_text(context, text_buffer.c_str(), node_id, static_cast<int>(sizeof(node_id))), node_id));
}

inline Locator expect_node(gua_context_t* context, std::string id)
{
    return get_by_id(context, std::move(id));
}

inline void wait_for(const Locator& locator, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000))
{
    locator.wait_for(timeout);
}

inline Locator wait_for_id(gua_context_t* context, std::string id, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000))
{
    const auto deadline = std::chrono::steady_clock::now() + timeout;
    do {
        char node_id[128] {};
        if (gua_find_node_by_id(context, id.c_str(), node_id, static_cast<int>(sizeof(node_id))) != 0) {
            return Locator(context, node_id);
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    } while (std::chrono::steady_clock::now() < deadline);

    throw std::runtime_error("Timed out waiting for Gua node by id: " + id);
}

inline Locator wait_for_role(gua_context_t* context, std::string_view role, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000))
{
    const auto deadline = std::chrono::steady_clock::now() + timeout;
    std::string role_buffer(role);
    do {
        char node_id[128] {};
        if (gua_find_node_by_role(context, role_buffer.c_str(), nullptr, node_id, static_cast<int>(sizeof(node_id))) != 0) {
            return Locator(context, node_id);
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    } while (std::chrono::steady_clock::now() < deadline);

    throw std::runtime_error("Timed out waiting for Gua node by role: " + role_buffer);
}

inline Locator wait_for_role(gua_context_t* context, std::string_view role, std::string_view name, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000))
{
    const auto deadline = std::chrono::steady_clock::now() + timeout;
    std::string role_buffer(role);
    std::string name_buffer(name);
    do {
        char node_id[128] {};
        if (gua_find_node_by_role(context, role_buffer.c_str(), name_buffer.c_str(), node_id, static_cast<int>(sizeof(node_id))) != 0) {
            return Locator(context, node_id);
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    } while (std::chrono::steady_clock::now() < deadline);

    throw std::runtime_error("Timed out waiting for Gua node by role and name: " + role_buffer + ", " + name_buffer);
}

inline Locator wait_for_text(gua_context_t* context, std::string_view text, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000))
{
    const auto deadline = std::chrono::steady_clock::now() + timeout;
    std::string text_buffer(text);
    do {
        char node_id[128] {};
        if (gua_find_node_by_text(context, text_buffer.c_str(), node_id, static_cast<int>(sizeof(node_id))) != 0) {
            return Locator(context, node_id);
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    } while (std::chrono::steady_clock::now() < deadline);

    throw std::runtime_error("Timed out waiting for Gua node by text: " + text_buffer);
}

} // namespace gua::testing
