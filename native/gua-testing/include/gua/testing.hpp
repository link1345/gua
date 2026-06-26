#pragma once

#include "gua/gua.h"

#include <stdexcept>
#include <string>
#include <utility>

namespace gua::testing {

class ExpectNode {
public:
    ExpectNode(gua_context_t* context, std::string id)
        : context_(context)
        , id_(std::move(id))
    {
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

private:
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

inline ExpectNode expect_node(gua_context_t* context, std::string id)
{
    return ExpectNode(context, std::move(id));
}

} // namespace gua::testing
