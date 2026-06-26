cmake_minimum_required(VERSION 3.20)

project(gua LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_subdirectory(native/gua-core)
add_subdirectory(native/gua-imgui)
add_subdirectory(native/gua-testing)
