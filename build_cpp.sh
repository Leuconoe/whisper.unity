#!/bin/bash

whisper_path="$(cd "$1" && pwd)"
targets=${2:-all}
android_sdk_path="$3"
unity_project="$PWD"
build_path="$whisper_path/build"

clean_build(){
  rm -rf "$build_path"
  mkdir "$build_path"
  cd "$build_path"
}

build_mac() {
  clean_build
  echo "Starting building for Mac (Metal)..."

  cmake -DCMAKE_OSX_ARCHITECTURES="x86_64;arm64" -DGGML_METAL=ON -DCMAKE_BUILD_TYPE=Release  \
   -DWHISPER_BUILD_TESTS=OFF -DWHISPER_BUILD_EXAMPLES=OFF -DGGML_METAL_EMBED_LIBRARY=ON ../
  make

  echo "Build for Mac (Metal) complete!"

  rm $unity_project/Packages/com.whisper.unity/Plugins/MacOS/*.dylib

  artifact_path="$build_path/src/libwhisper.dylib"
  target_path="$unity_project/Packages/com.whisper.unity/Plugins/MacOS/libwhisper.dylib"
  cp "$artifact_path" "$target_path"

  artifact_path=$build_path/ggml/src
  target_path=$unity_project/Packages/com.whisper.unity/Plugins/MacOS/
  cp "$artifact_path"/*.dylib "$target_path"
  cp "$artifact_path"/*/*.dylib "$target_path"

  # Required by Unity to properly find the dependencies
  for file in "$target_path"*.dylib; do
    install_name_tool -add_rpath @loader_path $file
  done

  echo "Build files copied to $target_path"
}

build_ios() {
  clean_build
  echo "Starting building for ios..."

  cmake -DBUILD_SHARED_LIBS=OFF -DCMAKE_SYSTEM_NAME=iOS -DCMAKE_BUILD_TYPE=Release  \
  -DCMAKE_OSX_ARCHITECTURES="x86_64;arm64" -DGGML_METAL=ON \
  -DCMAKE_SYSTEM_PROCESSOR=arm64 -DCMAKE_IOS_INSTALL_COMBINED=YES \
  -DWHISPER_BUILD_TESTS=OFF -DWHISPER_BUILD_EXAMPLES=OFF ../
  make

  echo "Build for ios complete!"

  rm $unity_project/Packages/com.whisper.unity/Plugins/iOS/*.a

  artifact_path="$build_path/src/libwhisper.a"
  target_path="$unity_project/Packages/com.whisper.unity/Plugins/iOS/libwhisper.a"
  cp "$artifact_path" "$target_path"

  artifact_path=$build_path/ggml/src
  target_path=$unity_project/Packages/com.whisper.unity/Plugins/iOS/
  cp "$artifact_path"/*.a "$target_path"
  cp "$artifact_path"/*/*.a "$target_path"

  echo "Build files copied to $target_path"
}

build_android() {
  clean_build
  echo "Starting building for Android (optimized)..."

  cmake -DCMAKE_TOOLCHAIN_FILE="$android_sdk_path" \
    -DANDROID_ABI=arm64-v8a \
    -DANDROID_PLATFORM=android-21 \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_C_FLAGS_RELEASE="-Ofast -ffast-math -fno-finite-math-only -ffp-contract=fast -fvisibility=hidden -ffunction-sections -fdata-sections -march=armv8.2-a+fp16+dotprod -mtune=cortex-a76 -funroll-loops -fomit-frame-pointer -finline-functions -fno-stack-protector -fno-exceptions -DNDEBUG -D__ARM_NEON -D__ARM_FEATURE_FMA -D__ARM_FEATURE_DOTPROD -D__ARM_FEATURE_FP16_VECTOR_ARITHMETIC -ftree-vectorize -fvectorize -fslp-vectorize" \
    -DCMAKE_CXX_FLAGS_RELEASE="-Ofast -ffast-math -fno-finite-math-only -ffp-contract=fast -fvisibility=hidden -ffunction-sections -fdata-sections -march=armv8.2-a+fp16+dotprod -mtune=cortex-a76 -funroll-loops -fomit-frame-pointer -finline-functions -fno-stack-protector -fvisibility-inlines-hidden -fno-rtti -DNDEBUG -D__ARM_NEON -D__ARM_FEATURE_FMA -D__ARM_FEATURE_DOTPROD -D__ARM_FEATURE_FP16_VECTOR_ARITHMETIC -ftree-vectorize -fvectorize -fslp-vectorize" \
    -DCMAKE_EXE_LINKER_FLAGS_RELEASE="-Wl,--gc-sections -Wl,--exclude-libs,ALL -Wl,--strip-debug" \
    -DBUILD_SHARED_LIBS=OFF \
    -DGGML_STATIC=ON \
    -DGGML_NATIVE=OFF \
    -DGGML_OPENMP=OFF \
    -DGGML_LTO=OFF \
    -DGGML_CPU=ON \
    -DGGML_CPU_AARCH64=ON \
    -DGGML_CPU_ARM_ARCH="armv8.2-a+fp16+dotprod" \
    -DGGML_CUDA=OFF -DGGML_METAL=OFF -DGGML_VULKAN=OFF -DGGML_OPENCL=OFF \
    -DWHISPER_BUILD_TESTS=OFF -DWHISPER_BUILD_EXAMPLES=OFF \
    ../
  make -j$(nproc)

  echo "Build for Android complete!"

  mkdir -p "$unity_project/Packages/com.whisper.unity/Plugins/Android"
  rm -f "$unity_project/Packages/com.whisper.unity/Plugins/Android"/*.a

  # Merge all static libraries into a single archive to avoid link order issues
  # Unity's IL2CPP linker processes archives left-to-right, so separate .a files
  # can cause undefined reference errors due to circular dependencies
  target_path="$unity_project/Packages/com.whisper.unity/Plugins/Android"
  combined_lib="$target_path/libwhisper.a"

  # Collect all .a files
  all_libs="$build_path/src/libwhisper.a"
  for lib in "$build_path/ggml/src"/*.a; do
    [ -f "$lib" ] && all_libs="$all_libs $lib"
  done
  if compgen -G "$build_path/ggml/src"/*/*.a > /dev/null; then
    for lib in "$build_path/ggml/src"/*/*.a; do
      [ -f "$lib" ] && all_libs="$all_libs $lib"
    done
  fi

  echo "Merging libraries into single archive: $all_libs"

  # Use ar MRI script to merge all archives
  mri_script="CREATE $combined_lib\n"
  for lib in $all_libs; do
    mri_script="${mri_script}ADDLIB $lib\n"
  done
  mri_script="${mri_script}SAVE\nEND\n"

  # Use the NDK's ar tool
  ndk_dir=$(dirname $(dirname "$android_sdk_path"))
  ar_tool=$(find "$ndk_dir/toolchains/llvm/prebuilt" -name "llvm-ar" 2>/dev/null | head -1)
  if [ -z "$ar_tool" ]; then
    # Try to find ar from the NDK root
    ndk_root=$(echo "$android_sdk_path" | sed 's|/build/cmake/android.toolchain.cmake||')
    ar_tool=$(find "$ndk_root/toolchains/llvm/prebuilt" -name "llvm-ar" 2>/dev/null | head -1)
  fi
  if [ -z "$ar_tool" ]; then
    ar_tool="ar"
  fi

  echo -e "$mri_script" | "$ar_tool" -M
  echo "Combined library created at $combined_lib"
  echo "Library size: $(du -h "$combined_lib" | cut -f1)"
}

if [ "$targets" = "all" ]; then
  build_mac
  build_ios
  build_android
elif [ "$targets" = "mac" ]; then
  build_mac
elif [ "$targets" = "ios" ]; then
  build_ios
elif [ "$targets" = "android" ]; then
  build_android
else
  echo "Unknown targets: $targets"
fi
