#include "gua/gua.h"

#include <cassert>
#include <cstring>
#include <string>

namespace {

void register_checkbox(gua_context_t* context, bool checked)
{
    const gua_node_descriptor_v2_t descriptor {
        sizeof(gua_node_descriptor_v2_t),
        GUA_NODE_KNOWN_PARENT_ID | GUA_NODE_KNOWN_TEXT | GUA_NODE_KNOWN_FOCUSED | GUA_NODE_KNOWN_CHECKED,
        "remember",
        "form",
        "checkbox",
        "Remember me",
        "Remember me",
        nullptr,
        { 10.0F, 20.0F, 100.0F, 24.0F },
        1,
        1,
        0,
        0,
        0,
        checked ? 1 : 0,
        0,
    };
    assert(gua_register_node_v2(context, &descriptor) == 1);
}

} // namespace

int main()
{
    gua_context_t* context = gua_create_context();
    assert(context != nullptr);

    gua_begin_frame(context, "settings");
    register_checkbox(context, false);
    gua_end_frame(context);
    const std::string first = gua_get_ui_tree_json(context);
    assert(first.find("\"schemaVersion\":2") != std::string::npos);
    assert(first.find("\"frameSequence\":1") != std::string::npos);
    assert(first.find("\"revision\":1") != std::string::npos);
    assert(first.find("\"parentId\":\"form\"") != std::string::npos);
    assert(first.find("\"checked\":false") != std::string::npos);
    assert(first.find("\"selected\"") == std::string::npos);

    gua_node_state_v2_t state {};
    state.struct_size = sizeof(state);
    assert(gua_get_node_state_v2(context, "remember", &state) == 1);
    assert((state.known_mask & GUA_NODE_KNOWN_CHECKED) != 0U);
    assert(state.checked == 0);
    assert(std::strcmp(state.parent_id, "form") == 0);

    gua_begin_frame(context, "settings");
    register_checkbox(context, false);
    gua_end_frame(context);
    const std::string stable = gua_get_ui_tree_json(context);
    assert(stable.find("\"frameSequence\":2") != std::string::npos);
    assert(stable.find("\"revision\":1") != std::string::npos);

    gua_begin_frame(context, "settings");
    register_checkbox(context, true);
    gua_end_frame(context);
    const std::string changed = gua_get_ui_tree_json(context);
    assert(changed.find("\"frameSequence\":3") != std::string::npos);
    assert(changed.find("\"revision\":2") != std::string::npos);
    assert(changed.find("\"checked\":true") != std::string::npos);

    gua_begin_frame(context, "settings");
    gua_register_node(context, "legacy", "button", "Legacy", { 0, 0, 1, 1 }, 1, 1);
    gua_end_frame(context);
    gua_node_state_t legacy {};
    assert(gua_get_node_state(context, "legacy", &legacy) == 1);
    assert(legacy.visible == 1 && legacy.enabled == 1);

    gua_destroy_context(context);
    return 0;
}
