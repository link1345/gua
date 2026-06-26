add_library(gua-core
    src/gua.cpp
)

add_library(gua::core ALIAS gua-core)

target_include_directories(gua-core
    PUBLIC
        include
)

target_compile_features(gua-core PUBLIC cxx_std_20)
