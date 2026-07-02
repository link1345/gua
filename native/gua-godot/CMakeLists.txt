include(FetchContent)

set(GODOTCPP_API_VERSION "4.7" CACHE STRING "Godot API version for the Gua GDExtension." FORCE)
set(GODOTCPP_TARGET "template_debug" CACHE STRING "Godot build target for the Gua GDExtension." FORCE)
set(GODOTCPP_USE_STATIC_CPP OFF CACHE BOOL "Use the same dynamic CRT policy as the rest of Gua." FORCE)
set(GODOTCPP_ENABLE_TESTING OFF CACHE BOOL "Disable godot-cpp integration tests." FORCE)
set(GODOTCPP_SYSTEM_HEADERS ON CACHE BOOL "Treat godot-cpp headers as external system headers." FORCE)

FetchContent_Declare(
    godot-cpp
    GIT_REPOSITORY https://github.com/godotengine/godot-cpp.git
    GIT_TAG ba0edfed90512ec64aba51d4295a3e7e30112f86
)

FetchContent_MakeAvailable(godot-cpp)

add_library(gua-godot SHARED
    src/gua_context.cpp
    src/register_types.cpp
)

add_library(gua::godot ALIAS gua-godot)

target_include_directories(gua-godot
    PRIVATE
        include
)

target_link_libraries(gua-godot
    PRIVATE
        gua::runtime
        godot::cpp
)

target_compile_features(gua-godot PRIVATE cxx_std_20)

set_target_properties(gua-godot
    PROPERTIES
        OUTPUT_NAME "gua_godot.windows.debug.x86_64"
        RUNTIME_OUTPUT_DIRECTORY "${CMAKE_SOURCE_DIR}/examples/godot-gdscript/addons/gua/bin"
        RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_SOURCE_DIR}/examples/godot-gdscript/addons/gua/bin"
        LIBRARY_OUTPUT_DIRECTORY "${CMAKE_SOURCE_DIR}/examples/godot-gdscript/addons/gua/bin"
        LIBRARY_OUTPUT_DIRECTORY_DEBUG "${CMAKE_SOURCE_DIR}/examples/godot-gdscript/addons/gua/bin"
)

if(MSVC)
    target_compile_options(gua-godot
        PRIVATE
            /W4
            /permissive-
            /Zc:__cplusplus
            /utf-8
    )
else()
    target_compile_options(gua-godot
        PRIVATE
            -Wall
            -Wextra
            -Wpedantic
    )
endif()
