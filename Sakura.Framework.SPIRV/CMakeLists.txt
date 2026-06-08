cmake_minimum_required (VERSION 3.22.1)
cmake_policy(SET CMP0057 NEW)

project(sakura-spirv)

set(CMAKE_OSX_DEPLOYMENT_TARGET "10.15" CACHE STRING "Minimum OS X deployment version")

if(IOS)
    include(ios/ios.toolchain.cmake)
endif()

set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Force dynamic CRT (/MD) on MSVC for all targets.
# Shaderc defaults SHADERC_ENABLE_SHARED_CRT to OFF (= /MT static CRT), which
# causes a RuntimeLibrary mismatch when linking the sakura-spirv DLL (/MD).
# Setting this option ON makes shaderc pick MultiThreadedDLL for all its targets.
# We also set CMAKE_MSVC_RUNTIME_LIBRARY as a belt-and-suspenders for SPIRV-Cross
# and any other subdirectory that doesn't use shaderc's option.
if(MSVC)
    option(SHADERC_ENABLE_SHARED_CRT "Use the shared CRT instead of the static CRT" ON)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL")
endif()

add_subdirectory(ext/SPIRV-Cross)

set(SHADERC_SKIP_TESTS ON)
set(SHADERC_SKIP_INSTALL ON)
add_subdirectory(ext/shaderc)

set(CMAKE_CXX_STANDARD 17)
include_directories(ext/SPIRV-Cross)
include_directories(ext/SPIRV-Cross/include)
include_directories(ext/shaderc/libshaderc/include/shaderc)

file(GLOB_RECURSE LIBSAKURA_SPIRV_SOURCES src/libsakura-spirv/*.cpp src/libsakura-spirv/*.hpp)

set(LIBRARY_TYPE SHARED)
message( "Configured to build a shared library." )

add_library(sakura-spirv ${LIBRARY_TYPE} ${LIBSAKURA_SPIRV_SOURCES})
target_link_libraries(sakura-spirv
    spirv-cross-core
    spirv-cross-glsl
    spirv-cross-reflect
    spirv-cross-msl
    spirv-cross-hlsl
    shaderc
)

# macOS builds are expected to be dynamic libraries rather than frameworks,
# only output frameworks for iOS builds.
if(IOS)
    set_target_properties(sakura-spirv PROPERTIES FRAMEWORK true)
endif()

set_target_properties(sakura-spirv PROPERTIES MACOSX_FRAMEWORK_IDENTIFIER "dev.helloyeew.sakura-spirv")
set_target_properties(sakura-spirv PROPERTIES MACOSX_FRAMEWORK_BUNDLE_VERSION "1.0.0")
set_target_properties(sakura-spirv PROPERTIES MACOSX_FRAMEWORK_SHORT_VERSION_STRING "1.0.0")

set_target_properties(sakura-spirv PROPERTIES PREFIX "lib")
set_target_properties(sakura-spirv PROPERTIES DEBUG_POSTFIX "")

set_target_properties(sakura-spirv PROPERTIES XCODE_ATTRIBUTE_ENABLE_BITCODE "NO")
