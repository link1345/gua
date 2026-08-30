if(EMSCRIPTEN)
    set(GUA_RUNTIME_LIBRARY_TYPE STATIC)
else()
    set(GUA_RUNTIME_LIBRARY_TYPE SHARED)
endif()

add_library(gua-runtime ${GUA_RUNTIME_LIBRARY_TYPE}
    src/runtime.cpp
)

add_library(gua::runtime ALIAS gua-runtime)

if(WIN32 AND GUA_BUILD_GODOT)
    # A distributed Godot addon contains Debug and Release GDExtensions side by
    # side. Embed the runtime so they do not compete for one configuration-
    # ambiguous gua_runtime.dll next to the addon binaries.
    add_library(gua-runtime-godot STATIC
        src/runtime.cpp
    )
    add_library(gua::runtime-godot ALIAS gua-runtime-godot)
endif()

target_include_directories(gua-runtime
    PUBLIC
        include
)

target_link_libraries(gua-runtime
    PUBLIC
        gua::core
)

if(TARGET gua-ws-bridge)
    target_link_libraries(gua-runtime PUBLIC gua::ws-bridge)
    target_compile_definitions(gua-runtime PRIVATE GUA_RUNTIME_WITH_WS=1)
else()
    target_compile_definitions(gua-runtime PRIVATE GUA_RUNTIME_WITH_WS=0)
endif()

target_compile_features(gua-runtime PUBLIC cxx_std_20)

if(TARGET gua-runtime-godot)
    target_include_directories(gua-runtime-godot PUBLIC include)
    target_link_libraries(gua-runtime-godot PUBLIC gua::core)
    if(TARGET gua-ws-bridge)
        target_link_libraries(gua-runtime-godot PUBLIC gua::ws-bridge)
        target_compile_definitions(gua-runtime-godot PRIVATE GUA_RUNTIME_WITH_WS=1)
    else()
        target_compile_definitions(gua-runtime-godot PRIVATE GUA_RUNTIME_WITH_WS=0)
    endif()
    target_compile_features(gua-runtime-godot PUBLIC cxx_std_20)
endif()

set_target_properties(gua-runtime
    PROPERTIES
        OUTPUT_NAME "gua_runtime"
        WINDOWS_EXPORT_ALL_SYMBOLS ON
)

if(EMSCRIPTEN)
    # Godot loads the Web GDExtension as a side module. Every static archive
    # linked into that module must use relocatable WebAssembly code.
    set_target_properties(gua-runtime PROPERTIES POSITION_INDEPENDENT_CODE ON)
endif()

if(MSVC)
    target_compile_options(gua-runtime
        PRIVATE
            /W4
            /permissive-
            /Zc:__cplusplus
            /utf-8
    )
    if(TARGET gua-runtime-godot)
        target_compile_options(gua-runtime-godot
            PRIVATE
                /W4
                /permissive-
                /Zc:__cplusplus
                /utf-8
        )
    endif()
else()
    target_compile_options(gua-runtime
        PRIVATE
            -Wall
            -Wextra
            -Wpedantic
    )
endif()

if(BUILD_TESTING)
    add_executable(gua-runtime-screenshot-tests tests/screenshot_request_tests.cpp)
    target_link_libraries(gua-runtime-screenshot-tests PRIVATE gua-runtime)
    target_compile_features(gua-runtime-screenshot-tests PRIVATE cxx_std_20)
    target_compile_options(gua-runtime-screenshot-tests PRIVATE $<$<CXX_COMPILER_ID:MSVC>:/UNDEBUG> $<$<NOT:$<CXX_COMPILER_ID:MSVC>>:-UNDEBUG>)
    add_test(NAME gua-runtime-screenshot-tests COMMAND gua-runtime-screenshot-tests)
endif()

if(WIN32)
    add_custom_command(TARGET gua-runtime POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E make_directory
            "${CMAKE_SOURCE_DIR}/examples/dotnet-godot/addons/gua/bin"
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "$<TARGET_FILE:gua-runtime>"
            "${CMAKE_SOURCE_DIR}/examples/dotnet-godot/addons/gua/bin/$<TARGET_FILE_NAME:gua-runtime>"
    )
endif()
