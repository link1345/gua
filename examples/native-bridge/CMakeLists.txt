add_executable(gua-native-bridge-example
    main.cpp
)

target_link_libraries(gua-native-bridge-example
    PRIVATE
        gua::core
        ws2_32
)

target_compile_features(gua-native-bridge-example PRIVATE cxx_std_20)

if(MSVC)
    target_compile_options(gua-native-bridge-example
        PRIVATE
            /W4
            /permissive-
            /Zc:__cplusplus
            /utf-8
    )
else()
    target_compile_options(gua-native-bridge-example
        PRIVATE
            -Wall
            -Wextra
            -Wpedantic
    )
endif()
