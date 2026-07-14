add_library(gua-core
    src/gua.cpp
)

add_library(gua::core ALIAS gua-core)

add_library(gua SHARED
    src/gua.cpp
)

add_library(gua::shared ALIAS gua)

target_include_directories(gua-core
    PUBLIC
        include
)

target_include_directories(gua
    PUBLIC
        include
)

target_compile_features(gua-core PUBLIC cxx_std_20)
target_compile_features(gua PUBLIC cxx_std_20)

set_target_properties(gua
    PROPERTIES
        OUTPUT_NAME "gua"
        WINDOWS_EXPORT_ALL_SYMBOLS ON
)

if(MSVC)
    target_compile_options(gua-core
        PRIVATE
            /W4
            /permissive-
            /Zc:__cplusplus
            /utf-8
    )
    target_compile_options(gua
        PRIVATE
            /W4
            /permissive-
            /Zc:__cplusplus
            /utf-8
    )
else()
    target_compile_options(gua-core
        PRIVATE
            -Wall
            -Wextra
            -Wpedantic
    )
    target_compile_options(gua
        PRIVATE
            -Wall
            -Wextra
            -Wpedantic
    )
endif()

if(BUILD_TESTING)
    add_executable(gua-core-state-tests tests/state_model_tests.cpp)
    target_link_libraries(gua-core-state-tests PRIVATE gua-core)
    target_compile_features(gua-core-state-tests PRIVATE cxx_std_20)
    target_compile_options(gua-core-state-tests PRIVATE $<$<CXX_COMPILER_ID:MSVC>:/UNDEBUG> $<$<NOT:$<CXX_COMPILER_ID:MSVC>>:-UNDEBUG>)
    add_test(NAME gua-core-state-tests COMMAND gua-core-state-tests)

endif()
