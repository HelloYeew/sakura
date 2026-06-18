# This code is part of the Sakura framework project. Licensed under the MIT License.
# See the LICENSE file for full license text.

cmake_minimum_required(VERSION 3.22.1)

project(sakura-metal C)

set(CMAKE_POSITION_INDEPENDENT_CODE ON)

if(NOT APPLE)
    message(FATAL_ERROR "sakura-metal is Apple-only (Metal framework). Configure on macOS/iOS.")
endif()

if(NOT CMAKE_OSX_DEPLOYMENT_TARGET)
    if(IOS)
        set(CMAKE_OSX_DEPLOYMENT_TARGET "12.0" CACHE STRING "Minimum iOS deployment version")
    else()
        set(CMAKE_OSX_DEPLOYMENT_TARGET "10.15" CACHE STRING "Minimum macOS deployment version")
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
