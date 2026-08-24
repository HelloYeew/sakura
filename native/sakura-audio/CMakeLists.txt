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

# No /experimental:c11atomics: sakura_atomic.h uses the _Interlocked* intrinsics on MSVC
# unconditionally, so the Windows build does not depend on which MSVC the runner installed.
if(MSVC)
    add_compile_options(/W4)
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

# The one thing about the host that has to be discovered rather than assumed. timespec_get is C11,
# but bionic only declares it from API 29 and the Android legs target 21 to match the shipped FFmpeg,
# so a compile-time check is the difference between "no callback timing on old Android" and "does not
# build for Android". A feature check, not a platform branch.
include(CheckSymbolExists)
check_symbol_exists(timespec_get "time.h" SAKURA_AUDIO_HAVE_TIMESPEC_GET)

add_library(sakura-audio SHARED sakura_audio.c)

# PE exports nothing unless asked, where ELF and Mach-O export everything unless asked not to. Without
# this the Windows legs build, link and package a DLL with no export directory at all, so the managed
# side loads it and then throws EntryPointNotFoundException on the very first call -- which
# SakuraAudioNative.IsAvailable catches and reports as "not available", falling back to the managed
# mixer. It looks exactly like a platform where the library was never shipped, on all three Windows
# RIDs, silently. See AUDIO_SDL.md.
#
# Exporting everything is safe here rather than lazy: every function in sakura_audio.c that is not part
# of the header's ABI is static, so "all symbols" and "the public API" are already the same set. A
# __declspec(dllexport) macro on each of the header's declarations would be the more precise spelling
# and is a fine later change; it is not a different outcome.
set_target_properties(sakura-audio PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS ON)

target_include_directories(sakura-audio PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

if(SAKURA_AUDIO_HAVE_TIMESPEC_GET)
    target_compile_definitions(sakura-audio PRIVATE SAKURA_AUDIO_HAVE_TIMESPEC_GET=1)
endif()

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

    if(SAKURA_AUDIO_HAVE_TIMESPEC_GET)
        target_compile_definitions(sakura-audio-test PRIVATE SAKURA_AUDIO_HAVE_TIMESPEC_GET=1)
    endif()

    if(NOT MSVC)
        target_link_libraries(sakura-audio-test m)
    endif()

    add_test(NAME sakura-audio-test COMMAND sakura-audio-test)
endif()
