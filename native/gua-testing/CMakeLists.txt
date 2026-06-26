add_library(gua-testing INTERFACE)
add_library(gua::testing ALIAS gua-testing)

target_include_directories(gua-testing
    INTERFACE
        include
)

target_link_libraries(gua-testing
    INTERFACE
        gua::core
)
