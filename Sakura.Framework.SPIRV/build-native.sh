#!/usr/bin/env bash

scriptPath="$( cd "$(dirname "$0")" ; pwd -P )"
_CMakeBuildType=Debug
_CMakeEnableBitcode=
_OutputPathPrefix=
_CMakeBuildTarget=sakura-spirv
_CMakeOsxArchitectures=
_CMakeGenerator=
_CMakeExtraBuildArgs=
_OSDir=

while :; do
    if [ $# -le 0 ]; then
        break
    fi

    lowerI="$(echo $1 | awk '{print tolower($0)}')"
    case $lowerI in
        debug|-debug)
            _CMakeBuildType=Debug
            ;;
        release|-release)
            _CMakeBuildType=Release
            ;;
        osx)
            _CMakeOsxArchitectures=$2
            _OSDir=osx
            shift
            ;;
        linux-x64)
            _OSDir=linux-x64
            ;;
        linux-arm64)
            _OSDir=linux-arm64
            ;;
        android)
            _AndroidABI=$2
            _OSDir=android-$2
            shift
            ;;
        ios)
            _CMakeEnableBitcode=-DENABLE_BITCODE=0
            _CMakeBuildTarget=sakura-spirv
            _CMakeGenerator="-G Xcode"
            _CMakeExtraBuildArgs="--config Release"
            _OSDir=ios
            ;;
        *)
            __UnprocessedBuildArgs="$__UnprocessedBuildArgs $1"
    esac

    shift
done

_OutputPath=$scriptPath/build/$_CMakeBuildType/$_OSDir
_PythonExePath=$(which python3)
if [[ $_PythonExePath == "" ]]; then
    echo Build failed: could not locate python executable.
    exit 1
fi

mkdir -p $_OutputPath
pushd $_OutputPath

if [[ $_OSDir == android-* ]]; then
    if [[ -z "$ANDROID_NDK_ROOT" ]]; then
        echo "Build failed: ANDROID_NDK_ROOT must be set."
        exit 1
    fi

    cmake ../../.. \
        -DCMAKE_BUILD_TYPE=MinSizeRel \
        -DCMAKE_TOOLCHAIN_FILE="$ANDROID_NDK_ROOT/build/cmake/android.toolchain.cmake" \
        -DANDROID_ABI="$_AndroidABI" \
        -DANDROID_PLATFORM=android-21 \
        -DANDROID_STL=c++_shared \
        -DCMAKE_C_FLAGS_MINSIZEREL="-Os -DNDEBUG" \
        -DCMAKE_CXX_FLAGS_MINSIZEREL="-Os -DNDEBUG" \
        -DCMAKE_SHARED_LINKER_FLAGS="-Wl,--strip-all" \
        -DSHADERC_SKIP_TESTS=ON \
        -DSHADERC_SKIP_INSTALL=ON \
        -DENABLE_GLSLANG_BINARIES=OFF \
        -DPYTHON_EXECUTABLE=$_PythonExePath -DCMAKE_POLICY_VERSION_MINIMUM=3.5

    cmake --build . --target $_CMakeBuildTarget

    # Strip debug symbols from the output binary
    STRIP="$ANDROID_NDK_ROOT/toolchains/llvm/prebuilt/linux-x86_64/bin/llvm-strip"
    if [ -f "$STRIP" ]; then
        find . -name "libsakura-spirv.so" -exec "$STRIP" --strip-unneeded {} \;
    fi

elif [[ $_OSDir == "ios" ]]; then
    mkdir -p device-build
    pushd device-build

    cmake ../../../.. -DIOS=ON -DCMAKE_BUILD_TYPE=$_CMakeBuildType $_CMakeGenerator -DPLATFORM=OS64 -DDEPLOYMENT_TARGET=13.4 $_CMakeEnableBitcode -DPYTHON_EXECUTABLE=$_PythonExePath -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DCMAKE_OSX_ARCHITECTURES="$_CMakeOsxArchitectures" -DGLSLANG_ENABLE_INSTALL=OFF -DSPIRV_SKIP_INSTALL=ON -DSPIRV_SKIP_EXECUTABLES=ON
    cmake --build . --target $_CMakeBuildTarget $_CMakeExtraBuildArgs

    popd

    mkdir -p simulator-build-arm64
    pushd simulator-build-arm64

    cmake ../../../.. -DIOS=ON -DCMAKE_BUILD_TYPE=$_CMakeBuildType $_CMakeGenerator -DPLATFORM=SIMULATORARM64 -DDEPLOYMENT_TARGET=13.4 $_CMakeEnableBitcode -DPYTHON_EXECUTABLE=$_PythonExePath -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DCMAKE_OSX_ARCHITECTURES="$_CMakeOsxArchitectures" -DGLSLANG_ENABLE_INSTALL=OFF -DSPIRV_SKIP_INSTALL=ON -DSPIRV_SKIP_EXECUTABLES=ON
    cmake --build . --target $_CMakeBuildTarget $_CMakeExtraBuildArgs

    popd

    mkdir -p simulator-build-x64
    pushd simulator-build-x64

    cmake ../../../.. -DIOS=ON -DCMAKE_BUILD_TYPE=$_CMakeBuildType $_CMakeGenerator -DPLATFORM=SIMULATOR64 -DDEPLOYMENT_TARGET=13.4 $_CMakeEnableBitcode -DPYTHON_EXECUTABLE=$_PythonExePath -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DCMAKE_OSX_ARCHITECTURES="$_CMakeOsxArchitectures" -DGLSLANG_ENABLE_INSTALL=OFF -DSPIRV_SKIP_INSTALL=ON -DSPIRV_SKIP_EXECUTABLES=ON
    cmake --build . --target $_CMakeBuildTarget $_CMakeExtraBuildArgs

    popd

    mkdir -p simulator-build-combined/sakura-spirv.framework

    cp ./simulator-build-arm64/Release-iphonesimulator/sakura-spirv.framework/Info.plist ./simulator-build-combined/sakura-spirv.framework/Info.plist

    lipo -create \
	    ./simulator-build-arm64/Release-iphonesimulator/sakura-spirv.framework/sakura-spirv \
	    ./simulator-build-x64/Release-iphonesimulator/sakura-spirv.framework/sakura-spirv \
	 -output ./simulator-build-combined/sakura-spirv.framework/sakura-spirv

    xcodebuild -create-xcframework \
	    -framework ./device-build/Release-iphoneos/sakura-spirv.framework \
	    -framework ./simulator-build-combined/sakura-spirv.framework \
	    -output ./sakura-spirv.xcframework
else
    # macOS / Linux
    cmake ../../.. -DCMAKE_BUILD_TYPE=$_CMakeBuildType $_CMakeGenerator $_CMakeEnableBitcode -DPYTHON_EXECUTABLE=$_PythonExePath -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DCMAKE_OSX_ARCHITECTURES="$_CMakeOsxArchitectures"
    cmake --build . --target $_CMakeBuildTarget $_CMakeExtraBuildArgs
fi

popd
