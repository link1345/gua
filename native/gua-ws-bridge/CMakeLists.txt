add_library(gua-ws-bridge
    src/ws_bridge.cpp
)

if(WIN32)
    target_sources(gua-ws-bridge PRIVATE src/socket_win32.cpp)
else()
    target_sources(gua-ws-bridge PRIVATE src/socket_posix.cpp)
endif()

add_library(gua::ws-bridge ALIAS gua-ws-bridge)

find_package(Threads REQUIRED)

target_include_directories(gua-ws-bridge
    PUBLIC
        include
)

target_link_libraries(gua-ws-bridge PUBLIC gua::core PRIVATE Threads::Threads)

if(WIN32)
    target_link_libraries(gua-ws-bridge PRIVATE ws2_32)
endif()

target_compile_features(gua-ws-bridge PUBLIC cxx_std_20)
set_target_properties(gua-ws-bridge PROPERTIES POSITION_INDEPENDENT_CODE ON)

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
