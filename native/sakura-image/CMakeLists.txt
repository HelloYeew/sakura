# This code is part of the Sakura framework project. Licensed under the MIT License.
# See the LICENSE file for full license text.

cmake_minimum_required(VERSION 3.22.1)

project(sakura-image C)

set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# It has to build for every RID the framework ships to, so a platform guard here
# would be a bug rather than a convention.
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

if(MSVC)
    add_compile_options(/W4)
else()
    add_compile_options(-Wall -Wextra -Wpedantic)
endif()

# The Darwin cache variables below are the Apple leg only. They are still required there, a silently
# single-arch or minos-too-high dylib fails at load rather than at build.
if(APPLE)
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

add_library(sakura-image SHARED sakura_image.c stb_impl.c)

# The vendored stb headers are third-party and not warning-clean under the flags above. Relaxing them
# for the one translation unit that instantiates them is what lets sakura_image.c stay strict; a
# blanket relaxation in this file would silently cover our own code too.
if(MSVC)
    set_source_files_properties(stb_impl.c PROPERTIES COMPILE_OPTIONS "/W0")
else()
    set_source_files_properties(stb_impl.c PROPERTIES COMPILE_OPTIONS "-w")
endif()

# PE exports nothing unless asked, where ELF and Mach-O export everything unless asked not to. Without
# this the Windows legs build, link and package a DLL with no export directory at all, so the managed
# side loads it and then throws EntryPointNotFoundException on the very first call -- which
# StbImageNative.IsAvailable catches and reports as "not available", falling back to ImageSharp. It
# looks exactly like a platform where the library was never shipped, silently. See AUDIO_SDL.md, where
# this happened for real.
set_target_properties(sakura-image PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS ON)

# ELF and Mach-O export everything unless told not to, and the vendored stb functions are extern rather
# than static -- so without this the dylib publishes ~50 stbi_*/stbir_* symbols alongside its own three.
# SAKURA_IMAGE_API in sakura_image.h re-exposes the API.
set_target_properties(sakura-image PROPERTIES C_VISIBILITY_PRESET hidden VISIBILITY_INLINES_HIDDEN ON)

target_include_directories(sakura-image PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

# libm, and nothing else, ever -- the same rule sakura-audio states. stb needs it: the resampler's
# sRGB tables call pow(), and its filters call ceilf()/floorf(). macOS folds libm into libSystem so
# this is a no-op there, which is exactly why it has to be stated rather than discovered on the Linux
# leg. A link dependency beyond libm is a design regression, not a build detail; CI fails on one.
if(NOT MSVC)
    target_link_libraries(sakura-image m)
endif()

if(IOS)
    set_target_properties(sakura-image PROPERTIES
        FRAMEWORK TRUE
        MACOSX_FRAMEWORK_IDENTIFIER com.sakura.framework.image
        MACOSX_FRAMEWORK_BUNDLE_VERSION 1.0
        MACOSX_FRAMEWORK_SHORT_VERSION_STRING 1.0
        PUBLIC_HEADER sakura_image.h)
endif()

# A plain C program against the same sources, so it runs on every platform the library builds for and
# needs no test framework.
option(SAKURA_IMAGE_BUILD_TESTS "Build the sakura-image test binary" ON)

if(SAKURA_IMAGE_BUILD_TESTS)
    enable_testing()
    add_executable(sakura-image-test tests/sakura_image_test.c sakura_image.c stb_impl.c)
    target_include_directories(sakura-image-test PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

    if(NOT MSVC)
        target_link_libraries(sakura-image-test m)
    endif()

    # No second warning override here: set_source_files_properties is directory-scoped, so the one
    # above already covers stb_impl.c in this target too.

    add_test(NAME sakura-image-test COMMAND sakura-image-test)
endif()
