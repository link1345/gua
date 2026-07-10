#pragma once

#include "gua/gua.h"

#include <chrono>
#include <atomic>
#include <cctype>
#include <cstdint>
#include <functional>
#include <filesystem>
#include <fstream>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

namespace gua::testing {

struct DiagnosticOptions {
    std::filesystem::path output_directory = std::filesystem::path("artifacts") / "gua";
    std::string test_name = "native-test";
};

inline thread_local std::optional<DiagnosticOptions> current_diagnostics;

class DiagnosticScope {
public:
    explicit DiagnosticScope(DiagnosticOptions options) : previous_(current_diagnostics)
    {
        current_diagnostics = std::move(options);
    }
    ~DiagnosticScope() { current_diagnostics = previous_; }
private:
    std::optional<DiagnosticOptions> previous_;
};

[[noreturn]] inline void fail(gua_context_t* context, std::string message)
{
    if (!current_diagnostics.has_value() || context == nullptr) throw std::runtime_error(message);
    std::filesystem::path directory;
    try {
        std::string name = current_diagnostics->test_name;
        for (char& ch : name) if (!std::isalnum(static_cast<unsigned char>(ch)) && ch != '-' && ch != '_') ch = '_';
        static std::atomic<unsigned long long> failure_id { 1 };
        directory = current_diagnostics->output_directory / name /
            (std::to_string(std::chrono::duration_cast<std::chrono::milliseconds>(
                std::chrono::system_clock::now().time_since_epoch()).count()) + "-" + std::to_string(failure_id++));
        std::filesystem::create_directories(directory);
        const int required = gua_copy_diagnostics_json(context, nullptr, 0);
        std::string json(static_cast<std::size_t>(required), '\0');
        gua_copy_diagnostics_json(context, json.data(), required);
        json.resize(static_cast<std::size_t>(required - 1));
        std::ofstream diagnostics_file(directory / "diagnostics.json", std::ios::binary);
        diagnostics_file.exceptions(std::ios::badbit | std::ios::failbit);
        diagnostics_file << json;
        std::ofstream summary_file(directory / "failure-summary.txt", std::ios::binary);
        summary_file.exceptions(std::ios::badbit | std::ios::failbit);
        summary_file << message << '\n';
    } catch (const std::exception& error) {
        throw std::runtime_error(message + " Gua diagnostics capture error: " + error.what());
    }
    throw std::runtime_error(message + " Gua diagnostics: " + directory.string());
}

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
            fail(context_, "Expected Gua node to be visible: " + id_);
        }
    }

    void to_be_enabled() const
    {
        const gua_node_state_t state = read_state();
        if (state.enabled == 0) {
            fail(context_, "Expected Gua node to be enabled: " + id_);
        }
    }

    void click() const
    {
        if (gua_enqueue_click(context_, id_.c_str()) == 0) {
            fail(context_, "Failed to click Gua node: " + id_);
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
        fail(context_, "Timed out waiting for Gua action completion: " + id_);
    }

    void wait_for(
        std::chrono::milliseconds timeout = std::chrono::milliseconds(1000),
        std::chrono::milliseconds poll_interval = std::chrono::milliseconds(10)) const
    {
        if (timeout.count() < 0 || poll_interval.count() <= 0) throw std::invalid_argument("Gua wait timeout must be non-negative and poll interval must be positive");
        const auto start = std::chrono::steady_clock::now();
        const auto deadline = start + timeout;
        do {
            gua_node_state_t state {};
            if (gua_get_node_state(context_, id_.c_str(), &state) != 0) {
                return;
            }

            if (std::chrono::steady_clock::now() >= deadline) break;
            std::this_thread::sleep_for(poll_interval);
        } while (true);

        throw_wait_timeout("exist", timeout, std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - start));
    }

    void wait_for_visible(std::chrono::milliseconds timeout = std::chrono::milliseconds(1000), std::chrono::milliseconds poll_interval = std::chrono::milliseconds(10)) const
    {
        wait_for_state([](const gua_node_state_v2_t& state, bool found) { return found && state.visible != 0; }, "be visible", timeout, poll_interval);
    }

    void wait_for_hidden(std::chrono::milliseconds timeout = std::chrono::milliseconds(1000), std::chrono::milliseconds poll_interval = std::chrono::milliseconds(10)) const
    {
        wait_for_state([](const gua_node_state_v2_t& state, bool found) { return !found || state.visible == 0; }, "be hidden or removed", timeout, poll_interval);
    }

    void wait_for_enabled(std::chrono::milliseconds timeout = std::chrono::milliseconds(1000), std::chrono::milliseconds poll_interval = std::chrono::milliseconds(10)) const
    {
        wait_for_state([](const gua_node_state_v2_t& state, bool found) { return found && state.enabled != 0; }, "be enabled", timeout, poll_interval);
    }

    void wait_for_disabled(std::chrono::milliseconds timeout = std::chrono::milliseconds(1000), std::chrono::milliseconds poll_interval = std::chrono::milliseconds(10)) const
    {
        wait_for_state([](const gua_node_state_v2_t& state, bool found) { return found && state.enabled == 0; }, "be disabled", timeout, poll_interval);
    }

    void wait_for_text(std::string_view expected, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000), std::chrono::milliseconds poll_interval = std::chrono::milliseconds(10)) const
    {
        const std::string value(expected);
        wait_for_state([&](const gua_node_state_v2_t& state, bool found) {
            return found && (state.known_mask & GUA_NODE_KNOWN_TEXT) != 0U && value == state.text;
        }, "have text '" + value + "'", timeout, poll_interval);
    }

    void wait_for_value(std::string_view expected, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000), std::chrono::milliseconds poll_interval = std::chrono::milliseconds(10)) const
    {
        const std::string value(expected);
        wait_for_state([&](const gua_node_state_v2_t& state, bool found) {
            return found && (state.known_mask & GUA_NODE_KNOWN_VALUE) != 0U && value == state.value;
        }, "have value '" + value + "'", timeout, poll_interval);
    }

private:
    template <typename Predicate>
    void wait_for_state(Predicate&& predicate, const std::string& description, std::chrono::milliseconds timeout, std::chrono::milliseconds poll_interval) const
    {
        if (timeout.count() < 0 || poll_interval.count() <= 0) throw std::invalid_argument("Gua wait timeout must be non-negative and poll interval must be positive");
        const auto start = std::chrono::steady_clock::now();
        const auto deadline = start + timeout;
        do {
            gua_node_state_v2_t state { sizeof(gua_node_state_v2_t) };
            const bool found = gua_get_node_state_v2(context_, id_.c_str(), &state) != 0;
            if (std::invoke(predicate, state, found)) return;
            if (std::chrono::steady_clock::now() >= deadline) break;
            std::this_thread::sleep_for(poll_interval);
        } while (true);
        throw_wait_timeout(description, timeout, std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - start));
    }

    [[noreturn]] void throw_wait_timeout(const std::string& description, std::chrono::milliseconds timeout, std::chrono::milliseconds elapsed) const
    {
        gua_context_status_t status { sizeof(gua_context_status_t) };
        (void)gua_get_context_status(context_, &status);
        gua_node_state_v2_t state { sizeof(gua_node_state_v2_t) };
        const bool found = gua_get_node_state_v2(context_, id_.c_str(), &state) != 0;
        const std::string last_state = found
            ? "visible=" + std::to_string(state.visible) + ", enabled=" + std::to_string(state.enabled) +
                ", text='" + state.text + "', value='" + state.value + "'"
            : "removed or not found";
        throw std::runtime_error("Timed out after " + std::to_string(timeout.count()) + "ms (elapsed " + std::to_string(elapsed.count()) + "ms) waiting for Gua node id '" + id_ + "' to " + description +
            ". Last state: " + last_state + "; frameSequence=" + std::to_string(status.frame_sequence) + ", revision=" + std::to_string(status.revision));
    }

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

inline void wait_for(const Locator& locator, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000), std::chrono::milliseconds poll_interval = std::chrono::milliseconds(10))
{
    locator.wait_for(timeout, poll_interval);
}

[[noreturn]] inline void throw_selector_wait_timeout(gua_context_t* context, const std::string& description,
    std::chrono::milliseconds timeout, std::chrono::steady_clock::time_point start)
{
    gua_context_status_t status { sizeof(gua_context_status_t) };
    (void)gua_get_context_status(context, &status);
    const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - start);
    throw std::runtime_error("Timed out after " + std::to_string(timeout.count()) + "ms (elapsed " + std::to_string(elapsed.count()) +
        "ms) waiting for Gua node by " + description + "; last frameSequence=" + std::to_string(status.frame_sequence) +
        ", revision=" + std::to_string(status.revision));
}

template <typename Predicate>
inline void wait_until(Predicate&& condition, std::string_view description,
    std::chrono::milliseconds timeout = std::chrono::milliseconds(1000),
    std::chrono::milliseconds poll_interval = std::chrono::milliseconds(10))
{
    if (timeout.count() < 0 || poll_interval.count() <= 0) throw std::invalid_argument("Gua wait timeout must be non-negative and poll interval must be positive");
    const auto start = std::chrono::steady_clock::now();
    const auto deadline = start + timeout;
    do {
        if (std::invoke(condition)) return;
        if (std::chrono::steady_clock::now() >= deadline) break;
        std::this_thread::sleep_for(poll_interval);
    } while (true);
    const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - start);
    throw std::runtime_error("Timed out after " + std::to_string(timeout.count()) + "ms (elapsed " + std::to_string(elapsed.count()) + "ms) waiting for " + std::string(description));
}

inline void wait_for_stable_snapshot(gua_context_t* context, std::size_t stable_frames = 3,
    std::chrono::milliseconds timeout = std::chrono::milliseconds(1000),
    std::chrono::milliseconds poll_interval = std::chrono::milliseconds(10))
{
    if (context == nullptr) throw std::invalid_argument("Gua stable snapshot wait requires a context");
    if (stable_frames == 0) throw std::invalid_argument("Gua stable snapshot wait requires at least one frame");
    if (timeout.count() < 0 || poll_interval.count() <= 0) throw std::invalid_argument("Gua wait timeout must be non-negative and poll interval must be positive");
    const auto start = std::chrono::steady_clock::now();
    const auto deadline = start + timeout;
    std::optional<std::uint64_t> session_epoch;
    std::optional<std::uint64_t> last_frame;
    std::optional<std::uint64_t> stable_revision;
    std::size_t observed = 0;
    gua_context_status_t status { sizeof(gua_context_status_t) };
    do {
        if (gua_get_context_status(context, &status) == 0) throw std::runtime_error("Failed to inspect Gua context while waiting for stable snapshot");
        if (!last_frame.has_value() || status.frame_sequence != *last_frame) {
            if (!session_epoch.has_value() || status.session_epoch != *session_epoch || !stable_revision.has_value() || status.revision != *stable_revision) {
                session_epoch = status.session_epoch;
                stable_revision = status.revision;
                observed = 1;
            } else {
                ++observed;
            }
            last_frame = status.frame_sequence;
            if (observed >= stable_frames) return;
        }
        if (std::chrono::steady_clock::now() >= deadline) break;
        std::this_thread::sleep_for(poll_interval);
    } while (true);
    const auto elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - start);
    throw std::runtime_error("Timed out after " + std::to_string(timeout.count()) + "ms (elapsed " + std::to_string(elapsed.count()) + "ms) waiting for " + std::to_string(stable_frames) + " distinct stable semantic frames; observed " +
        std::to_string(observed) + ", frameSequence=" + std::to_string(status.frame_sequence) + ", revision=" + std::to_string(status.revision));
}

inline Locator wait_for_id(gua_context_t* context, std::string id, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000), std::chrono::milliseconds poll_interval = std::chrono::milliseconds(10))
{
    if (timeout.count() < 0 || poll_interval.count() <= 0) throw std::invalid_argument("Gua wait timeout must be non-negative and poll interval must be positive");
    const auto start = std::chrono::steady_clock::now();
    const auto deadline = start + timeout;
    do {
        auto matches = query(context).by_id(id).query_all();
        if (matches.size() == 1) return std::move(matches.front());
        if (matches.size() > 1) throw std::runtime_error("Strict Gua selector matched multiple nodes by id: " + id);

        std::this_thread::sleep_for(poll_interval);
    } while (std::chrono::steady_clock::now() < deadline);

    throw_selector_wait_timeout(context, "id '" + id + "'", timeout, start);
}

inline Locator wait_for_role(gua_context_t* context, std::string_view role, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000), std::chrono::milliseconds poll_interval = std::chrono::milliseconds(10))
{
    if (timeout.count() < 0 || poll_interval.count() <= 0) throw std::invalid_argument("Gua wait timeout must be non-negative and poll interval must be positive");
    const auto start = std::chrono::steady_clock::now();
    const auto deadline = start + timeout;
    std::string role_buffer(role);
    do {
        auto matches = query(context).by_role(role_buffer).query_all();
        if (matches.size() == 1) return std::move(matches.front());
        if (matches.size() > 1) throw std::runtime_error("Strict Gua selector matched multiple nodes by role: " + role_buffer);

        std::this_thread::sleep_for(poll_interval);
    } while (std::chrono::steady_clock::now() < deadline);

    throw_selector_wait_timeout(context, "role '" + role_buffer + "'", timeout, start);
}

inline Locator wait_for_role(gua_context_t* context, std::string_view role, std::string_view name, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000), std::chrono::milliseconds poll_interval = std::chrono::milliseconds(10))
{
    if (timeout.count() < 0 || poll_interval.count() <= 0) throw std::invalid_argument("Gua wait timeout must be non-negative and poll interval must be positive");
    const auto start = std::chrono::steady_clock::now();
    const auto deadline = start + timeout;
    std::string role_buffer(role);
    std::string name_buffer(name);
    do {
        auto matches = query(context).by_role(role_buffer).by_name(name_buffer).query_all();
        if (matches.size() == 1) return std::move(matches.front());
        if (matches.size() > 1) throw std::runtime_error("Strict Gua selector matched multiple nodes by role and name: " + role_buffer + ", " + name_buffer);

        std::this_thread::sleep_for(poll_interval);
    } while (std::chrono::steady_clock::now() < deadline);

    throw_selector_wait_timeout(context, "role '" + role_buffer + "' and name '" + name_buffer + "'", timeout, start);
}

inline Locator wait_for_text(gua_context_t* context, std::string_view text, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000), std::chrono::milliseconds poll_interval = std::chrono::milliseconds(10))
{
    if (timeout.count() < 0 || poll_interval.count() <= 0) throw std::invalid_argument("Gua wait timeout must be non-negative and poll interval must be positive");
    const auto start = std::chrono::steady_clock::now();
    const auto deadline = start + timeout;
    std::string text_buffer(text);
    do {
        auto matches = query(context).by_text(text_buffer).query_all();
        if (matches.size() == 1) return std::move(matches.front());
        if (matches.size() > 1) throw std::runtime_error("Strict Gua selector matched multiple nodes by text: " + text_buffer);

        std::this_thread::sleep_for(poll_interval);
    } while (std::chrono::steady_clock::now() < deadline);

    throw_selector_wait_timeout(context, "text '" + text_buffer + "'", timeout, start);
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
