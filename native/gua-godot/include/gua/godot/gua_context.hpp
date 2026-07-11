#pragma once

#include "gua/runtime.h"

#include <godot_cpp/classes/ref_counted.hpp>
#include <godot_cpp/variant/dictionary.hpp>
#include <godot_cpp/variant/rect2.hpp>
#include <godot_cpp/variant/string.hpp>

namespace godot {

class GuaContext : public RefCounted {
    GDCLASS(GuaContext, RefCounted)

public:
    GuaContext();
    ~GuaContext();

    void begin_frame(const String& screen);
    void end_frame();
    void register_node(
        const String& id,
        const String& role,
        const String& label,
        const Rect2& bounds,
        bool visible = true,
        bool enabled = true);
    bool register_node_v2(const Dictionary& descriptor);

    String get_ui_tree_json() const;
    String get_version_json() const;
    void set_screenshot(const String& data_uri, int width, int height);
    String get_screenshot_json() const;
    bool enqueue_click(const String& node_id);
    bool consume_click_request(const String& node_id);
    bool emit_click(const String& node_id);
    Dictionary poll_event();
    Dictionary enqueue_action(const Dictionary& request);
    Dictionary consume_action_request(const String& action, const String& node_id);
    bool emit_action_result(const Dictionary& result);
    Dictionary poll_event_v2();
    Dictionary get_context_status() const;
    Dictionary reset_context(const Dictionary& options = Dictionary());
    bool start_inspector_bridge(int port = 8765);
    void stop_inspector_bridge();
    bool inspector_bridge_running() const;
    String inspector_bridge_url() const;
    void publish_inspector_snapshot();

protected:
    static void _bind_methods();

private:
    gua_runtime_t* runtime_ = nullptr;
};

} // namespace godot
