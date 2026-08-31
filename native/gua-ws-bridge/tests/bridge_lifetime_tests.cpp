#include <gua/ws_bridge.hpp>

#include <cassert>
#include <chrono>

int main()
{
    gua::ws::BridgeServer bridge({}, { .port = 0 });
    bridge.start();
    assert(bridge.running());
    assert(bridge.port() != 0);

    const auto started = std::chrono::steady_clock::now();
    bridge.stop();
    assert(!bridge.running());
    assert(std::chrono::steady_clock::now() - started < std::chrono::seconds(3));
}
