#include "gua/runtime.h"

#include <cassert>
#include <string>
#include <vector>

namespace {

std::string poll(gua_runtime_t* runtime, uint64_t request_id)
{
    const int size = gua_runtime_poll_screenshot_result_json(runtime, request_id, nullptr, 0);
    if (size == 0) return {};
    std::vector<char> buffer(static_cast<std::size_t>(size));
    assert(gua_runtime_poll_screenshot_result_json(runtime, request_id, buffer.data(), size) == size);
    return buffer.data();
}

std::string version(gua_runtime_t* runtime)
{
    const int size = gua_runtime_copy_version_json(runtime, nullptr, 0);
    std::vector<char> buffer(static_cast<std::size_t>(size));
    assert(gua_runtime_copy_version_json(runtime, buffer.data(), size) == size);
    return buffer.data();
}

} // namespace

int main()
{
    gua_runtime_t* runtime = gua_runtime_create();
    assert(runtime != nullptr);
    gua_runtime_set_adapter_version(runtime, "unity", "0.5.0-preview.3");
    gua_runtime_set_godot_plugin_version(runtime, "0.4.0");
    const std::string version_json = version(runtime);
    assert(version_json.find("\"adapterVersions\":{\"godot\":\"0.4.0\",\"unity\":\"0.5.0-preview.3\"}") != std::string::npos);
    assert(version_json.find("\"godotPluginVersion\":\"0.4.0\"") != std::string::npos);
    gua_runtime_begin_frame(runtime, "title");
    gua_runtime_end_frame(runtime);

    uint64_t first = 0;
    uint64_t second = 0;
    assert(gua_runtime_enqueue_screenshot_request(runtime, 1, &first) == 1);
    assert(gua_runtime_enqueue_screenshot_request(runtime, 3, &second) == 1);
    assert(first != second);
    assert(poll(runtime, first).empty());

    gua_screenshot_request_t request { sizeof(gua_screenshot_request_t) };
    assert(gua_runtime_consume_screenshot_request(runtime, &request) == 1);
    assert(request.request_id == first);
    assert(request.session_epoch == 1);
    assert(request.after_frame_sequence == 3);
    assert(gua_runtime_consume_screenshot_request(runtime, &request) == 0);

    gua_runtime_begin_frame(runtime, "title");
    gua_runtime_end_frame(runtime);
    assert(gua_runtime_complete_screenshot_request(
        runtime, first, GUA_SCREENSHOT_AVAILABLE, "data:image/png;base64,iVBORw0KGgo=", 2, 3) == 1);
    const std::string first_json = poll(runtime, first);
    const std::string second_json = poll(runtime, second);
    assert(first_json.find("\"requestId\":" + std::to_string(first)) != std::string::npos);
    assert(second_json.find("\"requestId\":" + std::to_string(second)) != std::string::npos);
    assert(first_json.find("\"frameSequence\":2") != std::string::npos);
    assert(first_json.find("\"width\":2,\"height\":3") != std::string::npos);

    uint64_t unavailable = 0;
    assert(gua_runtime_enqueue_screenshot_request(runtime, 2, &unavailable) == 1);
    request = { sizeof(gua_screenshot_request_t) };
    assert(gua_runtime_consume_screenshot_request(runtime, &request) == 1);
    assert(gua_runtime_complete_screenshot_request(runtime, request.request_id, GUA_SCREENSHOT_UNAVAILABLE_HEADLESS, nullptr, 0, 0) == 1);
    assert(poll(runtime, unavailable).find("\"unavailable\":\"headless\"") != std::string::npos);

    uint64_t stale_after_consume = 0;
    assert(gua_runtime_enqueue_screenshot_request(runtime, 2, &stale_after_consume) == 1);
    request = { sizeof(gua_screenshot_request_t) };
    assert(gua_runtime_consume_screenshot_request(runtime, &request) == 1);
    assert(request.session_epoch == 1);
    gua_reset_options_t reset { sizeof(gua_reset_options_t), GUA_RESET_DEFAULT, 0, 1 };
    gua_reset_report_t reset_report { sizeof(gua_reset_report_t) };
    assert(gua_runtime_reset_context(runtime, &reset, &reset_report) == 1);
    assert(reset_report.result == GUA_RESET_SUCCEEDED);
    assert(reset_report.session_epoch == 2);
    assert(gua_runtime_complete_screenshot_request(
        runtime, request.request_id, GUA_SCREENSHOT_AVAILABLE, "data:image/png;base64,c3RhbGU=", 4, 5) == 1);
    const std::string stale_json = poll(runtime, stale_after_consume);
    assert(stale_json.find("\"sessionEpoch\":2") != std::string::npos);
    assert(stale_json.find("\"unavailable\":\"stale_session\"") != std::string::npos);
    assert(stale_json.find("dataUri") == std::string::npos);

    uint64_t cancelled = 0;
    assert(gua_runtime_enqueue_screenshot_request(runtime, 2, &cancelled) == 1);
    assert(gua_runtime_cancel_screenshot_request(runtime, cancelled) == 1);
    request = { sizeof(gua_screenshot_request_t) };
    assert(gua_runtime_consume_screenshot_request(runtime, &request) == 0);

    gua_runtime_destroy(runtime);
    return 0;
}
