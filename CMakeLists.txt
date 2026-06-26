cmake_minimum_required(VERSION 3.20)

project(gua LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL")
endif()

option(GUA_BUILD_EXAMPLES "Build Gua examples." ON)

add_subdirectory(native/gua-core)
add_subdirectory(native/gua-imgui)
add_subdirectory(native/gua-testing)

if(GUA_BUILD_EXAMPLES)
    if(WIN32)
        add_subdirectory(examples/cpp-imgui)
    else()
        message(STATUS "Skipping examples/cpp-imgui because it requires Win32 and DirectX 11.")
    endif()
endif()
