cmake_minimum_required(VERSION 3.20)

project(gua LANGUAGES C CXX)

set(GUA_VERSION "0.5.0-preview.3" CACHE STRING "Gua component version")
set(GUA_BUILD_ID "development" CACHE STRING "Reproducible Gua build identifier")
set(GUA_GODOT_PLUGIN_VERSION "0.4.0" CACHE STRING "Gua Godot plugin version")
add_compile_definitions(
    GUA_VERSION="${GUA_VERSION}"
    GUA_BUILD_ID="${GUA_BUILD_ID}"
    GUA_GODOT_PLUGIN_VERSION="${GUA_GODOT_PLUGIN_VERSION}"
)

include(CTest)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL")
endif()

option(GUA_BUILD_EXAMPLES "Build Gua examples." ON)
option(GUA_BUILD_GODOT "Build the Godot GDExtension adapter." ON)

add_subdirectory(native/gua-core)
add_subdirectory(native/gua-imgui)
add_subdirectory(native/gua-testing)
if(WIN32)
    add_subdirectory(native/gua-ws-bridge)
    add_subdirectory(native/gua-runtime)
endif()

if(GUA_BUILD_GODOT AND WIN32)
    add_subdirectory(native/gua-godot)
elseif(GUA_BUILD_GODOT)
    message(STATUS "Skipping native/gua-godot because the shared runtime bridge currently requires Win32.")
endif()

if(GUA_BUILD_EXAMPLES)
    if(WIN32)
        add_subdirectory(examples/cpp-imgui)
        add_subdirectory(examples/native-bridge)
    else()
        message(STATUS "Skipping examples/cpp-imgui because it requires Win32 and DirectX 11.")
        message(STATUS "Skipping examples/native-bridge because the sample WebSocket server currently uses WinSock.")
    endif()
endif()
