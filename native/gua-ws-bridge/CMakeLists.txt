add_library(gua-ws-bridge
    src/ws_bridge_win32.cpp
)

add_library(gua::ws-bridge ALIAS gua-ws-bridge)

target_include_directories(gua-ws-bridge
    PUBLIC
        include
)

target_link_libraries(gua-ws-bridge
    PUBLIC
        gua::core
    PRIVATE
        ws2_32
)

target_compile_features(gua-ws-bridge PUBLIC cxx_std_20)

if(MSVC)
    target_compile_options(gua-ws-bridge
        PRIVATE
            /W4
            /permissive-
            /Zc:__cplusplus
            /utf-8
    )
else()
    target_compile_options(gua-ws-bridge
        PRIVATE
            -Wall
            -Wextra
            -Wpedantic
    )
endif()
