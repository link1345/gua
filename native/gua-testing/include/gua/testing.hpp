#pragma once

#include "gua/gua.h"

#include <chrono>
#include <cstdint>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

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

enum class match_mode { exact = GUA_MATCH_EXACT, contains = GUA_MATCH_CONTAINS, regex = GUA_MATCH_REGEX };

class Query {
public:
    explicit Query(gua_context_t* context) : context_(context) {}

    Query by_id(std::string value, match_mode mode = match_mode::exact) const { Query copy(*this); copy.id_ = std::move(value); copy.id_match_ = mode; return copy; }
    Query by_role(std::string value, match_mode mode = match_mode::exact) const { Query copy(*this); copy.role_ = std::move(value); copy.role_match_ = mode; return copy; }
    Query by_name(std::string value, match_mode mode = match_mode::exact) const { Query copy(*this); copy.name_ = std::move(value); copy.name_match_ = mode; return copy; }
    Query by_text(std::string value, match_mode mode = match_mode::exact) const { Query copy(*this); copy.text_ = std::move(value); copy.text_match_ = mode; return copy; }
    Query within(std::string parent_id, bool direct_child = false) const { Query copy(*this); copy.parent_id_ = std::move(parent_id); copy.direct_child_ = direct_child; return copy; }
    Query where_visible(bool value = true) const { Query copy(*this); copy.visible_ = value ? GUA_FILTER_TRUE : GUA_FILTER_FALSE; return copy; }
    Query where_enabled(bool value = true) const { Query copy(*this); copy.enabled_ = value ? GUA_FILTER_TRUE : GUA_FILTER_FALSE; return copy; }

    [[nodiscard]] std::vector<Locator> query_all() const
    {
        const std::string json = execute();
        std::vector<Locator> matches;
        std::size_t cursor = 0;
        while ((cursor = json.find("\"id\":\"", cursor)) != std::string::npos) {
            cursor += 6;
            const std::size_t end = json.find('"', cursor);
            if (end == std::string::npos) break;
            matches.emplace_back(context_, json.substr(cursor, end - cursor));
            cursor = end + 1;
        }
        return matches;
    }

    [[nodiscard]] Locator get() const
    {
        auto matches = query_all();
        if (matches.empty()) throw std::runtime_error("Strict Gua selector matched no nodes: " + describe());
        if (matches.size() > 1) throw std::runtime_error("Strict Gua selector matched multiple nodes: " + describe() + ". Narrow the scope with within() or add a stable id/state filter. " + execute());
        return std::move(matches.front());
    }

    const Query& assert_count(std::size_t expected) const
    {
        const std::size_t actual = query_all().size();
        if (actual != expected) throw std::runtime_error("Expected selector " + describe() + " to match " + std::to_string(expected) + " nodes, but matched " + std::to_string(actual));
        return *this;
    }

private:
    [[nodiscard]] std::string execute() const
    {
        gua_selector_v1_t selector {
            sizeof(gua_selector_v1_t),
            pointer(id_), static_cast<int>(id_match_), pointer(role_), static_cast<int>(role_match_),
            pointer(name_), static_cast<int>(name_match_), pointer(text_), static_cast<int>(text_match_),
            pointer(parent_id_), direct_child_ ? 1 : 0, visible_, enabled_,
        };
        const int required = gua_query_nodes_json(context_, &selector, nullptr, 0);
        std::string json(static_cast<std::size_t>(required), '\0');
        gua_query_nodes_json(context_, &selector, json.data(), required);
        json.resize(static_cast<std::size_t>(required - 1));
        if (json.find("\"valid\":false") != std::string::npos) throw std::runtime_error("Invalid Gua selector " + describe() + ": " + json);
        return json;
    }

    static const char* pointer(const std::string& value) { return value.empty() ? nullptr : value.c_str(); }
    [[nodiscard]] std::string describe() const { return "{id='" + id_ + "', role='" + role_ + "', name='" + name_ + "', text='" + text_ + "', scope='" + parent_id_ + "'}"; }

    gua_context_t* context_;
    std::string id_, role_, name_, text_, parent_id_;
    match_mode id_match_ = match_mode::exact, role_match_ = match_mode::exact, name_match_ = match_mode::exact, text_match_ = match_mode::exact;
    bool direct_child_ = false;
    int visible_ = GUA_FILTER_ANY, enabled_ = GUA_FILTER_ANY;
};

inline Query query(gua_context_t* context) { return Query(context); }

inline Locator get_by_id(gua_context_t* context, std::string id)
{
    return query(context).by_id(std::move(id)).get();
}

inline Locator get_by_role(gua_context_t* context, std::string_view role)
{
    return query(context).by_role(std::string(role)).get();
}

inline Locator get_by_role(gua_context_t* context, std::string_view role, std::string_view name)
{
    return query(context).by_role(std::string(role)).by_name(std::string(name)).get();
}

inline Locator get_by_text(gua_context_t* context, std::string_view text)
{
    return query(context).by_text(std::string(text)).get();
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
        auto matches = query(context).by_id(id).query_all();
        if (matches.size() == 1) return std::move(matches.front());
        if (matches.size() > 1) throw std::runtime_error("Strict Gua selector matched multiple nodes by id: " + id);

        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    } while (std::chrono::steady_clock::now() < deadline);

    throw std::runtime_error("Timed out waiting for Gua node by id: " + id);
}

inline Locator wait_for_role(gua_context_t* context, std::string_view role, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000))
{
    const auto deadline = std::chrono::steady_clock::now() + timeout;
    std::string role_buffer(role);
    do {
        auto matches = query(context).by_role(role_buffer).query_all();
        if (matches.size() == 1) return std::move(matches.front());
        if (matches.size() > 1) throw std::runtime_error("Strict Gua selector matched multiple nodes by role: " + role_buffer);

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
        auto matches = query(context).by_role(role_buffer).by_name(name_buffer).query_all();
        if (matches.size() == 1) return std::move(matches.front());
        if (matches.size() > 1) throw std::runtime_error("Strict Gua selector matched multiple nodes by role and name: " + role_buffer + ", " + name_buffer);

        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    } while (std::chrono::steady_clock::now() < deadline);

    throw std::runtime_error("Timed out waiting for Gua node by role and name: " + role_buffer + ", " + name_buffer);
}

inline Locator wait_for_text(gua_context_t* context, std::string_view text, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000))
{
    const auto deadline = std::chrono::steady_clock::now() + timeout;
    std::string text_buffer(text);
    do {
        auto matches = query(context).by_text(text_buffer).query_all();
        if (matches.size() == 1) return std::move(matches.front());
        if (matches.size() > 1) throw std::runtime_error("Strict Gua selector matched multiple nodes by text: " + text_buffer);

        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    } while (std::chrono::steady_clock::now() < deadline);

    throw std::runtime_error("Timed out waiting for Gua node by text: " + text_buffer);
}

class TestSession {
public:
    explicit TestSession(gua_context_t* context) : context_(context)
    {
        if (context_ == nullptr) throw std::invalid_argument("Gua TestSession requires a context");
    }

    [[nodiscard]] gua_context_status_t inspect() const
    {
        gua_context_status_t status { sizeof(gua_context_status_t) };
        if (gua_get_context_status(context_, &status) == 0) throw std::runtime_error("Failed to inspect Gua context");
        return status;
    }

    void assert_clean() const
    {
        const auto status = inspect();
        if (status.pending_request_count == 0 && status.in_flight_request_count == 0 && status.unconsumed_event_count == 0) return;
        throw std::runtime_error("Dirty Gua test session: pending=" + std::to_string(status.pending_request_count) +
            ", in-flight=" + std::to_string(status.in_flight_request_count) + ", events=" +
            std::to_string(status.unconsumed_event_count) + ", first request=" + status.first_pending_node_id +
            ". Payload values are redacted.");
    }

    [[nodiscard]] gua_reset_report_t reset(bool strict = false, std::uint32_t flags = GUA_RESET_DEFAULT) const
    {
        const auto status = inspect();
        const gua_reset_options_t options { sizeof(gua_reset_options_t), flags, strict ? 1 : 0, status.session_epoch };
        gua_reset_report_t report { sizeof(gua_reset_report_t) };
        const int result = gua_reset_context(context_, &options, &report);
        if (result == GUA_RESET_ERROR_DIRTY) throw std::runtime_error("Strict Gua reset rejected dirty state without discarding it");
        if (result != GUA_RESET_SUCCEEDED) throw std::runtime_error("Gua reset failed: " + std::to_string(result));
        return report;
    }

private:
    gua_context_t* context_;
};

} // namespace gua::testing
