add_library(gua-testing INTERFACE)
add_library(gua::testing ALIAS gua-testing)

target_include_directories(gua-testing
    INTERFACE
        include
)

target_link_libraries(gua-testing
    INTERFACE
        gua::core
)

if(BUILD_TESTING)
    add_executable(gua-core-selector-tests ../gua-core/tests/selector_tests.cpp)
    target_link_libraries(gua-core-selector-tests PRIVATE gua-core gua-testing)
    target_compile_features(gua-core-selector-tests PRIVATE cxx_std_20)
    target_compile_options(gua-core-selector-tests PRIVATE $<$<CXX_COMPILER_ID:MSVC>:/UNDEBUG> $<$<NOT:$<CXX_COMPILER_ID:MSVC>>:-UNDEBUG>)
    add_test(NAME gua-core-selector-tests COMMAND gua-core-selector-tests)
endif()
