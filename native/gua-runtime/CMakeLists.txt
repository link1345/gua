add_library(gua-runtime SHARED
    src/runtime.cpp
)

add_library(gua::runtime ALIAS gua-runtime)

target_include_directories(gua-runtime
    PUBLIC
        include
)

target_link_libraries(gua-runtime
    PUBLIC
        gua::core
        gua::ws-bridge
)

target_compile_features(gua-runtime PUBLIC cxx_std_20)

set_target_properties(gua-runtime
    PROPERTIES
        OUTPUT_NAME "gua_runtime"
        WINDOWS_EXPORT_ALL_SYMBOLS ON
)

if(MSVC)
    target_compile_options(gua-runtime
        PRIVATE
            /W4
            /permissive-
            /Zc:__cplusplus
            /utf-8
    )
else()
    target_compile_options(gua-runtime
        PRIVATE
            -Wall
            -Wextra
            -Wpedantic
    )
endif()

if(WIN32)
    add_custom_command(TARGET gua-runtime POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E make_directory
            "${CMAKE_SOURCE_DIR}/examples/dotnet-godot/addons/gua/bin"
            "${CMAKE_SOURCE_DIR}/examples/godot-gdscript/addons/gua/bin"
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "$<TARGET_FILE:gua-runtime>"
            "${CMAKE_SOURCE_DIR}/examples/dotnet-godot/addons/gua/bin/$<TARGET_FILE_NAME:gua-runtime>"
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "$<TARGET_FILE:gua-runtime>"
            "${CMAKE_SOURCE_DIR}/examples/godot-gdscript/addons/gua/bin/$<TARGET_FILE_NAME:gua-runtime>"
    )
endif()
