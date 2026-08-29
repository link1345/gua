include(FetchContent)

set(GODOTCPP_API_VERSION "4.7" CACHE STRING "Godot API version for the Gua GDExtension." FORCE)
set(GUA_GODOT_CONFIGURATION "Debug" CACHE STRING "Build configuration for the Gua GDExtension (Debug or Release).")
set_property(CACHE GUA_GODOT_CONFIGURATION PROPERTY STRINGS Debug Release)
string(TOLOWER "${GUA_GODOT_CONFIGURATION}" GUA_GODOT_CONFIGURATION_LOWER)
if(NOT EMSCRIPTEN)
    # The existing Windows addon remains a Debug GDExtension. Web is the first
    # platform distributed in both configurations.
    set(GODOTCPP_TARGET "template_debug" CACHE STRING "Godot build target for the Gua GDExtension." FORCE)
elseif(GUA_GODOT_CONFIGURATION_LOWER STREQUAL "debug")
    set(GODOTCPP_TARGET "template_debug" CACHE STRING "Godot build target for the Gua GDExtension." FORCE)
elseif(GUA_GODOT_CONFIGURATION_LOWER STREQUAL "release")
    set(GODOTCPP_TARGET "template_release" CACHE STRING "Godot build target for the Gua GDExtension." FORCE)
else()
    message(FATAL_ERROR "GUA_GODOT_CONFIGURATION must be Debug or Release, got '${GUA_GODOT_CONFIGURATION}'.")
endif()
if(EMSCRIPTEN)
    # The distributed descriptor targets Godot's single-threaded Web template.
    # Keep godot-cpp and the side module on the same thread ABI.
    set(GODOTCPP_THREADS OFF CACHE BOOL "Enable threading support" FORCE)
endif()
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

if(EMSCRIPTEN)
    set(GUA_GODOT_OUTPUT_NAME "gua_godot.web.${GUA_GODOT_CONFIGURATION_LOWER}.wasm32")
else()
    set(GUA_GODOT_OUTPUT_NAME "gua_godot.windows.debug.x86_64")
endif()

set_target_properties(gua-godot
    PROPERTIES
        OUTPUT_NAME "${GUA_GODOT_OUTPUT_NAME}"
        RUNTIME_OUTPUT_DIRECTORY "${CMAKE_SOURCE_DIR}/examples/godot-gdscript/addons/gua/bin"
        RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_SOURCE_DIR}/examples/godot-gdscript/addons/gua/bin"
        RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_SOURCE_DIR}/examples/godot-gdscript/addons/gua/bin"
        RUNTIME_OUTPUT_DIRECTORY_RELWITHDEBINFO "${CMAKE_SOURCE_DIR}/examples/godot-gdscript/addons/gua/bin"
        RUNTIME_OUTPUT_DIRECTORY_MINSIZEREL "${CMAKE_SOURCE_DIR}/examples/godot-gdscript/addons/gua/bin"
        LIBRARY_OUTPUT_DIRECTORY "${CMAKE_SOURCE_DIR}/examples/godot-gdscript/addons/gua/bin"
        LIBRARY_OUTPUT_DIRECTORY_DEBUG "${CMAKE_SOURCE_DIR}/examples/godot-gdscript/addons/gua/bin"
        LIBRARY_OUTPUT_DIRECTORY_RELEASE "${CMAKE_SOURCE_DIR}/examples/godot-gdscript/addons/gua/bin"
        LIBRARY_OUTPUT_DIRECTORY_RELWITHDEBINFO "${CMAKE_SOURCE_DIR}/examples/godot-gdscript/addons/gua/bin"
        LIBRARY_OUTPUT_DIRECTORY_MINSIZEREL "${CMAKE_SOURCE_DIR}/examples/godot-gdscript/addons/gua/bin"
)

if(EMSCRIPTEN)
    set_target_properties(gua-godot PROPERTIES PREFIX "" SUFFIX ".wasm")
    target_link_options(gua-godot PRIVATE "-sSIDE_MODULE=1")
endif()

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
