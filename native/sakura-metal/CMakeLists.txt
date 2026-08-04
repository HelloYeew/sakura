# This code is part of the Sakura framework project. Licensed under the MIT License.
# See the LICENSE file for full license text.

cmake_minimum_required(VERSION 3.22.1)

project(sakura-metal C)

set(CMAKE_POSITION_INDEPENDENT_CODE ON)

if(NOT APPLE)
    message(FATAL_ERROR "sakura-metal is Apple-only (Metal framework). Configure on macOS/iOS.")
endif()

# Both of these are already in the cache as empty strings by the time project() returns -- CMake's Darwin
# platform module puts them there -- so a plain `set(... CACHE ...)` is silently a no-op and the defaults
# below never apply. FORCE is what makes them stick; the empty check is what keeps an explicit
# -DCMAKE_OSX_* on the command line winning.

# Default to a universal binary on macOS. Without this the dylib is built for the host arch alone, which
# no build step fails on -- it only shows up as a missing slice in `lipo -info`, by which point the
# single-arch binary is already shipped. iOS is left to the toolchain/SDK to decide.
if(NOT IOS AND NOT CMAKE_OSX_ARCHITECTURES)
    set(CMAKE_OSX_ARCHITECTURES "x86_64;arm64" CACHE STRING "macOS architectures to build" FORCE)
endif()

# Likewise silent: with no deployment target the dylib takes the build machine's SDK version as its
# minimum, so it refuses to load on anything older than whatever Xcode happened to be installed.
if(NOT CMAKE_OSX_DEPLOYMENT_TARGET)
    if(IOS)
        set(CMAKE_OSX_DEPLOYMENT_TARGET "12.0" CACHE STRING "Minimum iOS deployment version" FORCE)
    else()
        set(CMAKE_OSX_DEPLOYMENT_TARGET "10.15" CACHE STRING "Minimum macOS deployment version" FORCE)
    endif()
endif()

set(SAKURA_METAL_SOURCES SakuraMetal.m)
set_source_files_properties(${SAKURA_METAL_SOURCES} PROPERTIES COMPILE_FLAGS "-fobjc-arc")

add_library(sakura-metal SHARED ${SAKURA_METAL_SOURCES})

target_include_directories(sakura-metal PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

target_link_libraries(sakura-metal
    "-framework Metal"
    "-framework QuartzCore"
    "-framework Foundation")

if(IOS)
    if(NOT CMAKE_OSX_DEPLOYMENT_TARGET OR CMAKE_OSX_DEPLOYMENT_TARGET VERSION_LESS 12.0)
        set(CMAKE_OSX_DEPLOYMENT_TARGET "12.0" CACHE STRING "Minimum iOS deployment version" FORCE)
    endif()

    set_target_properties(sakura-metal PROPERTIES
        FRAMEWORK TRUE
        MACOSX_FRAMEWORK_IDENTIFIER com.sakura.framework.metal
        MACOSX_FRAMEWORK_BUNDLE_VERSION 1.0
        MACOSX_FRAMEWORK_SHORT_VERSION_STRING 1.0
        PUBLIC_HEADER SakuraMetal.h)
    target_link_libraries(sakura-metal "-framework UIKit")
else()
    # macOS: AppKit is available for any window/layer interop that may be needed later.
    target_link_libraries(sakura-metal "-framework AppKit")
endif()
