add_library(gua-core
    src/gua.cpp
)

add_library(gua::core ALIAS gua-core)

target_include_directories(gua-core
    PUBLIC
        include
)

target_compile_features(gua-core PUBLIC cxx_std_20)

if(MSVC)
    target_compile_options(gua-core
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
endif()
