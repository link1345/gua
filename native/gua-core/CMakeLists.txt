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
