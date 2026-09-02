#include <gua/ws_bridge.hpp>

#include <cassert>
#include <chrono>
#include <string>
#include <vector>

int main()
{
    const gua::ws::GameInputQuerySelector valid { .id = "jump", .query = "Jump", .value_type = 1,
        .active = 2, .context = "gameplay", .category = "movement", .tags = { "core", "player" }, .limit = 20 };
    assert(gua::ws::detail::valid_game_input_query_selector(valid));
    auto invalid = valid;
    invalid.id = "Invalid";
    assert(!gua::ws::detail::valid_game_input_query_selector(invalid));
    invalid = valid; invalid.category = std::string(128, 'a');
    assert(!gua::ws::detail::valid_game_input_query_selector(invalid));
    invalid = valid; invalid.query = std::string(129, 'q');
    assert(!gua::ws::detail::valid_game_input_query_selector(invalid));
    invalid = valid; invalid.tags = { std::string(65, 't') };
    assert(!gua::ws::detail::valid_game_input_query_selector(invalid));
    invalid = valid; invalid.tags = { "same", "same" };
    assert(!gua::ws::detail::valid_game_input_query_selector(invalid));
    invalid = valid; invalid.tags = std::vector<std::string>(17, "tag");
    assert(!gua::ws::detail::valid_game_input_query_selector(invalid));
    invalid = valid; invalid.limit = 101;
    assert(!gua::ws::detail::valid_game_input_query_selector(invalid));
    invalid = valid; invalid.query = std::string("before\0after", 12);
    assert(!gua::ws::detail::valid_game_input_query_selector(invalid));
    assert(gua::ws::detail::valid_game_input_query_request_json(
        R"({"id":1,"type":"find_game_input_actions","tags":["x"]})"));
    assert(!gua::ws::detail::valid_game_input_query_request_json(
        R"({"id":1,"type":"find_game_input_actions","tags":["x",]})"));

    gua::ws::BridgeServer bridge({}, { .port = 0 });
    bridge.start();
    assert(bridge.running());
    assert(bridge.port() != 0);

    const auto started = std::chrono::steady_clock::now();
    bridge.stop();
    assert(!bridge.running());
    assert(std::chrono::steady_clock::now() - started < std::chrono::seconds(3));
}
