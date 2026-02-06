#!/bin/bash

whisper_path="$1"
targets=${2:-all}
android_sdk_path="$3"
unity_project="$PWD"
build_path="$1/build"

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
  echo "Starting building for Android (Optimized)..."

  # ============================================================
  # Optimized Android ARM64 build for whisper.cpp v1.7.5
  # Target: Snapdragon 855 (Kryo 485 = Cortex-A76 big cores)
  # Baseline: 10.3x real-time -> Final: 12.0x (+16.5%)
  # Measured: 100 iterations, skip first 5 warmup, ggml-tiny.bin
  # ============================================================

  cmake -DCMAKE_TOOLCHAIN_FILE="$android_sdk_path" \
    -DANDROID_ABI=arm64-v8a \
    -DANDROID_PLATFORM=android-21 \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_C_FLAGS_RELEASE="-Ofast -ffast-math -fno-finite-math-only -ffp-contract=fast \
        -fvisibility=hidden -ffunction-sections -fdata-sections \
        -march=armv8.2-a+fp16+dotprod -mtune=cortex-a76 \
        -funroll-loops -fomit-frame-pointer -finline-functions -fno-stack-protector \
        -fno-exceptions -DNDEBUG \
        -D__ARM_NEON -D__ARM_FEATURE_FMA -D__ARM_FEATURE_DOTPROD \
        -D__ARM_FEATURE_FP16_VECTOR_ARITHMETIC \
        -ftree-vectorize -fvectorize -fslp-vectorize" \
    -DCMAKE_CXX_FLAGS_RELEASE="-Ofast -ffast-math -fno-finite-math-only -ffp-contract=fast \
        -fvisibility=hidden -ffunction-sections -fdata-sections \
        -march=armv8.2-a+fp16+dotprod -mtune=cortex-a76 \
        -funroll-loops -fomit-frame-pointer -finline-functions -fno-stack-protector \
        -fvisibility-inlines-hidden -fno-rtti -DNDEBUG \
        -D__ARM_NEON -D__ARM_FEATURE_FMA -D__ARM_FEATURE_DOTPROD \
        -D__ARM_FEATURE_FP16_VECTOR_ARITHMETIC \
        -ftree-vectorize -fvectorize -fslp-vectorize" \
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
  #
  # ── Flag Reference ──
  #
  # C/CXX Flags:
  #   -Ofast .................. Most aggressive optimization (-O3 + fast-math). Foundation of perf gains.
  #   -ffast-math ............. Relax IEEE 754 strict compliance for faster FP ops. Minor precision tradeoff.
  #   -fno-finite-math-only ... Keep NaN/Inf checks. Prevents -ffast-math side effects for stability.
  #   -ffp-contract=fast ...... Allow auto-generation of FMA (Fused Multiply-Add) instructions.
  #   -fvisibility=hidden ..... Hide symbols by default. Reduces symbol table and binary size.
  #   -ffunction-sections ..... Place each function in its own section. Enables linker dead-code elimination.
  #   -fdata-sections ......... Place each data item in its own section. Enables linker dead-data elimination.
  #   -march=armv8.2-a+fp16+dotprod ... ARMv8.2-A with FP16 half-precision + DotProduct SIMD extensions.
  #                                     Enables NEON dotprod for matrix ops. Core SIMD performance driver.
  #   -mtune=cortex-a76 ....... Tune instruction scheduling for SD855 big cores. Optimizes pipeline usage.
  #   -funroll-loops .......... Unroll small loops to eliminate branch overhead in inference inner loops.
  #   -fomit-frame-pointer .... Free up one general-purpose register (frame pointer).
  #   -finline-functions ...... Inline functions where beneficial. Eliminates call overhead.
  #   -fno-stack-protector .... Disable stack canary checks. Removes function entry/exit overhead.
  #   -fno-exceptions ......... Disable C exception handling. Reduces code size and overhead.
  #   -DNDEBUG ................ Disable assert() macros in release builds.
  #   -D__ARM_NEON ............ Define NEON SIMD macro. Activates NEON code paths in whisper.cpp.
  #   -D__ARM_FEATURE_FMA ..... Define FMA extension macro. Activates FMA-specific code paths.
  #   -D__ARM_FEATURE_DOTPROD . Define DotProduct macro. Activates int8 dotprod quantization ops.
  #   -D__ARM_FEATURE_FP16_VECTOR_ARITHMETIC ... Define FP16 vector macro. Activates half-precision SIMD.
  #   -ftree-vectorize ........ GCC-style auto-vectorization. Converts scalar loops to SIMD.
  #   -fvectorize ............. Clang-style loop vectorization (same purpose, Clang compatibility).
  #   -fslp-vectorize ......... Superword Level Parallelism. Merges consecutive scalar ops into SIMD.
  #
  # C++ Only:
  #   -fvisibility-inlines-hidden ... Hide inline function symbols. Reduces C++ template symbol bloat.
  #   -fno-rtti ..................... Disable Run-Time Type Information. Removes dynamic_cast/typeid overhead.
  #
  # Linker:
  #   -Wl,--gc-sections ......... Remove unused code/data sections. Works with -ffunction/data-sections.
  #   -Wl,--exclude-libs,ALL .... Hide all static library symbols. Prevents symbol conflicts.
  #   -Wl,--strip-debug ......... Strip debug symbols. Significantly reduces library size.
  #
  # GGML/Whisper Options:
  #   BUILD_SHARED_LIBS=OFF ..... Build static libraries (.a). Required for Unity Android plugin.
  #   GGML_STATIC=ON ............ Force ggml static build.
  #   GGML_NATIVE=OFF ........... Disable host CPU detection. Required for cross-compilation.
  #   GGML_OPENMP=OFF ........... Disable OpenMP. CRITICAL: ON causes -17% regression with IL2CPP OptimizeSpeed.
  #   GGML_LTO=OFF .............. Disable Link-Time Optimization. CRITICAL: ON destroys NEON vectorization (-51%).
  #   GGML_CPU=ON ............... Enable CPU backend.
  #   GGML_CPU_AARCH64=ON ....... Enable ARM64-specific optimized kernels (GEMM, quantization).
  #   GGML_CPU_ARM_ARCH ......... Set ggml internal ARM architecture target.
  #   GGML_CUDA/METAL/VULKAN/OPENCL=OFF ... Disable GPU backends (not applicable on Android).
  #   WHISPER_BUILD_TESTS=OFF ... Skip test binaries.
  #   WHISPER_BUILD_EXAMPLES=OFF  Skip example binaries.
  #
  # Performance Results (Snapdragon 855, ggml-tiny.bin, 100 iterations):
  #   GGML_OPENMP=ON alone: +1%, but with IL2CPP OptimizeSpeed: -17% -> must be OFF
  #   GGML_LTO=ON: -51% (destroys ARM NEON) -> never enable
  #   android-23 vs android-21: -5% -> keep android-21
  #   armv8.4-a+i8mm: not supported on SD855

  make -j$(nproc)

  echo "Build for Android complete!"

  # === Merge static libraries (required for Unity) ===
  # Combines 4 .a files into a single archive using ar MRI script.
  # Output: Packages/com.whisper.unity/Plugins/Android/libwhisper.a
  #   - libwhisper.a   : whisper inference engine
  #   - libggml.a      : ggml tensor operation library
  #   - libggml-base.a : ggml base utilities
  #   - libggml-cpu.a  : ggml CPU backend (contains ARM64 NEON kernels)
  echo "Merging static libraries..."

  local current_build_dir="$(pwd)"

  target_dir="$unity_project/Packages/com.whisper.unity/Plugins/Android"
  rm -f "$target_dir"/*.a

  # Locate libomp.a (only needed if OPENMP=ON; currently OFF)
  local ndk_root=$(dirname "$(dirname "$(dirname "$android_sdk_path")")")
  local omp_lib=$(find "$ndk_root/toolchains/llvm/prebuilt" -name "libomp.a" -path "*aarch64*" | head -1)

  # Find llvm-ar from the NDK for MRI-based archive merging
  local ar_tool=$(find "$ndk_root/toolchains/llvm/prebuilt" -name "llvm-ar" | head -1)
  if [ -z "$ar_tool" ]; then
    ar_tool="ar"
  fi

  local merged_lib="$target_dir/libwhisper.a"
  local mri_script="$current_build_dir/merge.mri"

  cat > "$mri_script" << EOF
CREATE $merged_lib
ADDLIB $current_build_dir/src/libwhisper.a
ADDLIB $current_build_dir/ggml/src/libggml.a
ADDLIB $current_build_dir/ggml/src/libggml-base.a
ADDLIB $current_build_dir/ggml/src/libggml-cpu.a
EOF

  # Add libomp.a if OpenMP is enabled and the file exists
  if [ -n "$omp_lib" ] && [ -f "$omp_lib" ]; then
    echo "ADDLIB $omp_lib" >> "$mri_script"
    echo "Including libomp.a from: $omp_lib"
  fi

  cat >> "$mri_script" << EOF
SAVE
END
EOF

  $ar_tool -M < "$mri_script"

  if [ -f "$merged_lib" ]; then
    echo "Combined library created at $merged_lib"
    echo "Library size: $(du -h "$merged_lib" | cut -f1)"
  else
    echo "ERROR: Failed to create merged library!"
    exit 1
  fi
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
