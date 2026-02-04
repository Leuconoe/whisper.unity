#!/bin/bash

# ============================================================
# FUTO ggml Build Script for whisper.unity
# ============================================================
#
# This script builds whisper using FUTO Voice Input's optimized ggml
# which provides lower overhead and faster inference on Android.
#
# Key differences from standard whisper.cpp build:
# - Monolithic ggml.c (all CPU ops inline, no backend abstraction)
# - No extra buffer type checks (eliminates per-operation overhead)
# - Simpler threadpool (alloca-based, no dynamic allocation)
#
# Usage:
#   ./build_cpp_futo.sh <android_ndk_toolchain> [architecture]
#
# Arguments:
#   android_ndk_toolchain: Path to NDK's CMake toolchain file
#                          e.g., $ANDROID_NDK/build/cmake/android.toolchain.cmake
#   architecture: Optional. "arm64" (default) or "arm32"
#
# Examples:
#   ./build_cpp_futo.sh $ANDROID_NDK/build/cmake/android.toolchain.cmake
#   ./build_cpp_futo.sh $ANDROID_NDK/build/cmake/android.toolchain.cmake arm32
#
# Output:
#   Plugins/Android-FUTO/libwhisper-futo.a
#   Plugins/Android-FUTO-v7a/libwhisper-futo.a  (if arm32)
#
# ============================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FUTO_SRC="$SCRIPT_DIR/ggml-futo"
BUILD_DIR="$SCRIPT_DIR/build-futo"
UNITY_PROJECT="$SCRIPT_DIR"

android_ndk_toolchain="$1"
architecture="${2:-arm64}"

if [ -z "$android_ndk_toolchain" ]; then
    echo "Error: Android NDK toolchain path required"
    echo "Usage: $0 <android_ndk_toolchain> [arm64|arm32]"
    exit 1
fi

if [ ! -f "$android_ndk_toolchain" ]; then
    echo "Error: NDK toolchain not found at: $android_ndk_toolchain"
    exit 1
fi

if [ ! -d "$FUTO_SRC" ]; then
    echo "Error: FUTO ggml source not found at: $FUTO_SRC"
    echo "Please ensure ggml-futo folder exists with FUTO source files"
    exit 1
fi

clean_build() {
    rm -rf "$BUILD_DIR"
    mkdir -p "$BUILD_DIR"
    cd "$BUILD_DIR"
}

print_build_info() {
    echo ""
    echo "============================================================"
    echo "FUTO ggml Build for whisper.unity"
    echo "============================================================"
    echo ""
    echo "Building FUTO-style optimized whisper for Android"
    echo ""
    echo "Key optimizations:"
    echo "  - Monolithic ggml.c (19K lines, all ops inline)"
    echo "  - No backend abstraction overhead"
    echo "  - No extra buffer type checks per operation"
    echo "  - Simpler thread pool (alloca-based)"
    echo "  - ARM NEON + FP16 optimizations"
    echo ""
    echo "Source: $FUTO_SRC"
    echo "Build: $BUILD_DIR"
    echo "NDK: $android_ndk_toolchain"
    echo "Architecture: $architecture"
    echo ""
    echo "============================================================"
    echo ""
}

build_android_arm64() {
    clean_build
    echo "Building FUTO ggml for Android arm64-v8a..."
    
    # FUTO-style optimization flags
    OPTIMIZATION_C_FLAGS="-O3 -fvisibility=hidden -ffunction-sections -fdata-sections -march=armv8.2-a+fp16+dotprod -fno-exceptions"
    OPTIMIZATION_CXX_FLAGS="-O3 -fvisibility=hidden -fvisibility-inlines-hidden -ffunction-sections -fdata-sections -march=armv8.2-a+fp16+dotprod -fno-exceptions -fno-rtti"
    OPTIMIZATION_LINKER_FLAGS="-Wl,--gc-sections -Wl,--exclude-libs,ALL -flto"
    
    cmake -DCMAKE_TOOLCHAIN_FILE="$android_ndk_toolchain" \
          -DANDROID_ABI=arm64-v8a \
          -DANDROID_PLATFORM=android-24 \
          -DGGML_FUTO_STATIC=ON \
          -DGGML_FUTO_LTO=ON \
          -DCMAKE_BUILD_TYPE=Release \
          -DCMAKE_C_FLAGS_RELEASE="$OPTIMIZATION_C_FLAGS" \
          -DCMAKE_CXX_FLAGS_RELEASE="$OPTIMIZATION_CXX_FLAGS" \
          -DCMAKE_EXE_LINKER_FLAGS_RELEASE="$OPTIMIZATION_LINKER_FLAGS" \
          -DCMAKE_SHARED_LINKER_FLAGS_RELEASE="$OPTIMIZATION_LINKER_FLAGS" \
          "$FUTO_SRC"
    
    make -j$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo 4)
    
    echo "Build complete!"
    
    # Copy to Unity plugins
    TARGET_DIR="$UNITY_PROJECT/Packages/com.whisper.unity/Plugins/Android-FUTO"
    mkdir -p "$TARGET_DIR"
    
    cp "$BUILD_DIR/lib/libwhisper-futo.a" "$TARGET_DIR/"
    
    echo "Artifacts copied to: $TARGET_DIR"
    echo ""
    ls -la "$TARGET_DIR"
}

build_android_arm32() {
    clean_build
    echo "Building FUTO ggml for Android armeabi-v7a..."
    
    # ARM32 optimization flags
    OPTIMIZATION_C_FLAGS="-O3 -fvisibility=hidden -ffunction-sections -fdata-sections -mfpu=neon-vfpv4 -mfloat-abi=softfp -fno-exceptions"
    OPTIMIZATION_CXX_FLAGS="-O3 -fvisibility=hidden -fvisibility-inlines-hidden -ffunction-sections -fdata-sections -mfpu=neon-vfpv4 -mfloat-abi=softfp -fno-exceptions -fno-rtti"
    OPTIMIZATION_LINKER_FLAGS="-Wl,--gc-sections -Wl,--exclude-libs,ALL -flto"
    
    cmake -DCMAKE_TOOLCHAIN_FILE="$android_ndk_toolchain" \
          -DANDROID_ABI=armeabi-v7a \
          -DANDROID_PLATFORM=android-24 \
          -DGGML_FUTO_STATIC=ON \
          -DGGML_FUTO_LTO=ON \
          -DCMAKE_BUILD_TYPE=Release \
          -DCMAKE_C_FLAGS_RELEASE="$OPTIMIZATION_C_FLAGS" \
          -DCMAKE_CXX_FLAGS_RELEASE="$OPTIMIZATION_CXX_FLAGS" \
          -DCMAKE_EXE_LINKER_FLAGS_RELEASE="$OPTIMIZATION_LINKER_FLAGS" \
          -DCMAKE_SHARED_LINKER_FLAGS_RELEASE="$OPTIMIZATION_LINKER_FLAGS" \
          "$FUTO_SRC"
    
    make -j$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo 4)
    
    echo "Build complete!"
    
    # Copy to Unity plugins
    TARGET_DIR="$UNITY_PROJECT/Packages/com.whisper.unity/Plugins/Android-FUTO-v7a"
    mkdir -p "$TARGET_DIR"
    
    cp "$BUILD_DIR/lib/libwhisper-futo.a" "$TARGET_DIR/"
    
    echo "Artifacts copied to: $TARGET_DIR"
    echo ""
    ls -la "$TARGET_DIR"
}

print_usage_instructions() {
    echo ""
    echo "============================================================"
    echo "USAGE INSTRUCTIONS"
    echo "============================================================"
    echo ""
    echo "To use FUTO ggml in Unity:"
    echo ""
    echo "1. In Unity, go to Player Settings > Android"
    echo "2. Change the library from libwhisper.a to libwhisper-futo.a"
    echo "3. Ensure the Android-FUTO plugin folder is included"
    echo ""
    echo "Expected performance improvement: 15-40% faster inference"
    echo "compared to standard whisper.cpp build."
    echo ""
    echo "============================================================"
}

# Main
print_build_info

case "$architecture" in
    arm64)
        build_android_arm64
        ;;
    arm32)
        build_android_arm32
        ;;
    all)
        build_android_arm64
        BUILD_DIR="$SCRIPT_DIR/build-futo-v7a"
        build_android_arm32
        ;;
    *)
        echo "Unknown architecture: $architecture"
        echo "Available: arm64, arm32, all"
        exit 1
        ;;
esac

print_usage_instructions

echo "Build completed successfully!"
