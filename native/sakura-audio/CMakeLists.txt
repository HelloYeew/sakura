# This code is part of the Sakura framework project. Licensed under the MIT License.
# See the LICENSE file for full license text.

cmake_minimum_required(VERSION 3.22.1)

project(sakura-audio C)

set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Unlike sakura-metal, which opens with a FATAL_ERROR on non-Apple platforms, this library touches no
# platform API at all: SDL arrives as an injected function pointer, the only link dependency is libm,
# and everything else is arithmetic on buffers. It has to build for every RID the framework ships to,
# so an equivalent platform guard here would be a bug rather than a convention.

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

# C11 for <stdatomic.h>. MSVC only exposes it with this flag, and the fallback in sakura_atomic.h
# covers the toolchains that still do not -- but ask for the standard path first.
if(MSVC)
    add_compile_options(/W4)
    # /experimental:c11atomics is what makes <stdatomic.h> available in MSVC's C mode. Harmless where
    # unsupported: the atomics shim falls back to _Interlocked intrinsics.
    if(MSVC_VERSION GREATER_EQUAL 1935)
        add_compile_options(/experimental:c11atomics)
    endif()
else()
    add_compile_options(-Wall -Wextra -Wpedantic)
endif()

# The Darwin cache variables below are the Apple leg only. They are still required there -- a
# silently single-arch or minos-too-high dylib fails at load rather than at build -- and they say
# nothing about the Windows, Linux or Android legs, which verify their arch in CI instead.
if(APPLE)
    # Both are already in the cache as empty strings by the time project() returns, so a plain
    # set(... CACHE ...) is silently a no-op. FORCE is what makes the defaults stick; the empty check
    # is what keeps an explicit -DCMAKE_OSX_* on the command line winning.
    if(NOT IOS AND NOT CMAKE_OSX_ARCHITECTURES)
        set(CMAKE_OSX_ARCHITECTURES "x86_64;arm64" CACHE STRING "macOS architectures to build" FORCE)
    endif()

    if(NOT CMAKE_OSX_DEPLOYMENT_TARGET)
        if(IOS)
            set(CMAKE_OSX_DEPLOYMENT_TARGET "12.0" CACHE STRING "Minimum iOS deployment version" FORCE)
        else()
            set(CMAKE_OSX_DEPLOYMENT_TARGET "10.15" CACHE STRING "Minimum macOS deployment version" FORCE)
        endif()
    endif()
endif()

add_library(sakura-audio SHARED sakura_audio.c)

target_include_directories(sakura-audio PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

# libm, and nothing else, ever. A new link dependency here is a review failure, not a build detail:
# it is what would put an SDK per target back into CI.
if(NOT MSVC)
    target_link_libraries(sakura-audio m)
endif()

if(IOS)
    set_target_properties(sakura-audio PROPERTIES
        FRAMEWORK TRUE
        MACOSX_FRAMEWORK_IDENTIFIER com.sakura.framework.audio
        MACOSX_FRAMEWORK_BUNDLE_VERSION 1.0
        MACOSX_FRAMEWORK_SHORT_VERSION_STRING 1.0
        PUBLIC_HEADER sakura_audio.h)
endif()

# The test binary is a plain C program against the same source, so it runs on every platform the
# library builds for and needs no test framework.
option(SAKURA_AUDIO_BUILD_TESTS "Build the sakura-audio test binary" ON)

if(SAKURA_AUDIO_BUILD_TESTS)
    enable_testing()
    add_executable(sakura-audio-test tests/sakura_audio_test.c sakura_audio.c)
    target_include_directories(sakura-audio-test PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

    if(NOT MSVC)
        target_link_libraries(sakura-audio-test m)
    endif()

    add_test(NAME sakura-audio-test COMMAND sakura-audio-test)
endif()
