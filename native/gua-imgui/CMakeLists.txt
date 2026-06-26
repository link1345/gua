add_library(gua-imgui INTERFACE)
add_library(gua::imgui ALIAS gua-imgui)

target_include_directories(gua-imgui
    INTERFACE
        include
)

target_link_libraries(gua-imgui
    INTERFACE
        gua::core
)
