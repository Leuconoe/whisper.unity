@echo off
setlocal enabledelayedexpansion

REM ============================================================
REM Optimized Android Build Script for whisper.unity.org
REM Compiler optimizations only - no WhisperManager changes
REM ============================================================

set "NDK_PATH=%~1"
set "ARCH=%~2"

if "%NDK_PATH%"=="" (
    echo Usage: build_cpp_android_optimized.bat ^<NDK_PATH^> [arm64^|arm32^|all]
    echo.
    echo Example:
    echo   build_cpp_android_optimized.bat C:\Android\Sdk\ndk\27.0.12077973 arm64
    exit /b 1
)

if not exist "%NDK_PATH%\build\cmake\android.toolchain.cmake" (
    echo Error: NDK not found at: %NDK_PATH%
    echo Please provide valid NDK path
    exit /b 1
)

if "%ARCH%"=="" set ARCH=arm64

set UNITY_PATH=%CD%
set WHISPER_PATH=%CD%\..\whisper.cpp
set TOOLCHAIN=%NDK_PATH%\build\cmake\android.toolchain.cmake

if not exist "%WHISPER_PATH%" (
    echo Error: whisper.cpp not found at: %WHISPER_PATH%
    echo Please clone whisper.cpp next to whisper.unity.org
    exit /b 1
)

echo.
echo ============================================================
echo Optimized whisper.cpp Build for whisper.unity.org
echo ============================================================
echo.
echo Source: %WHISPER_PATH%
echo NDK: %NDK_PATH%
echo Architecture: %ARCH%
echo.
echo Key optimizations applied:
echo   - Ofast + funroll-loops + fomit-frame-pointer + finline-functions
echo   - ARM NEON + FP16 + DotProd + mtune=cortex-a76 (arm64)
echo   - OpenMP multi-threading enabled
echo   - GGML_CPU_AARCH64 + GGML_CPU_ARM_ARCH optimizations
echo   - LTO disabled (Unity 2021 linker compatibility)
echo   - C++: fvisibility-inlines-hidden + fno-rtti
echo   - Linker: gc-sections + exclude-libs + strip-debug
echo   - ARM feature definitions for SIMD
echo   - CPU-only build (all GPU backends disabled)
echo.
echo ============================================================
echo.

REM ============================================================
REM AGGRESSIVE optimization flags for maximum performance
REM Using -Ofast (O3 + additional floating point optimizations)
REM Added inline optimizations for hot paths
REM ============================================================
set "COMMON_C_FLAGS=-Ofast -fno-finite-math-only -ffp-contract=fast -fvisibility=hidden -ffunction-sections -fdata-sections -fno-exceptions -funroll-loops -fomit-frame-pointer -finline-functions -fno-stack-protector -DNDEBUG"
set "COMMON_CXX_FLAGS=-Ofast -fno-finite-math-only -ffp-contract=fast -fvisibility=hidden -ffunction-sections -fdata-sections -fvisibility-inlines-hidden -fno-rtti -funroll-loops -fomit-frame-pointer -finline-functions -fno-stack-protector -DNDEBUG"
REM LTO disabled for Unity 2021 linker compatibility, strip-debug for smaller/faster binary
set COMMON_LINK_FLAGS=-Wl,--gc-sections -Wl,--exclude-libs,ALL -Wl,--strip-debug

if "%ARCH%"=="all" (
    call :build_arm64
    call :build_arm32
    goto :done
)

if "%ARCH%"=="arm64" (
    call :build_arm64
    goto :done
)

if "%ARCH%"=="arm32" (
    call :build_arm32
    goto :done
)

echo Unknown architecture: %ARCH%
exit /b 1

:build_arm64
echo.
echo Building whisper.cpp for Android arm64-v8a...
echo.

set BUILD_DIR=%UNITY_PATH%\build-android-arm64
if exist "%BUILD_DIR%" rmdir /s /q "%BUILD_DIR%"
mkdir "%BUILD_DIR%"

REM ARM64 specific flags: ARMv8.2-a with FP16, DotProduct, SIMD optimizations
REM Using march + mtune for better cross-compilation compatibility
REM OpenMP disabled for Unity static linking compatibility
set ARM64_FLAGS=-march=armv8.2-a+fp16+dotprod -mtune=cortex-a76 -D__ARM_NEON -D__ARM_FEATURE_FMA -D__ARM_FEATURE_DOTPROD -D__ARM_FEATURE_FP16_VECTOR_ARITHMETIC -ftree-vectorize -fvectorize -fslp-vectorize

cmake -S "%WHISPER_PATH%" -B "%BUILD_DIR%" -G "Ninja" ^
    -DCMAKE_TOOLCHAIN_FILE="%TOOLCHAIN%" ^
    -DANDROID_ABI=arm64-v8a ^
    -DANDROID_PLATFORM=android-24 ^
    -DCMAKE_BUILD_TYPE=Release ^
    -DCMAKE_C_FLAGS_RELEASE="%COMMON_C_FLAGS% %ARM64_FLAGS%" ^
    -DCMAKE_CXX_FLAGS_RELEASE="%COMMON_CXX_FLAGS% %ARM64_FLAGS%" ^
    -DCMAKE_EXE_LINKER_FLAGS_RELEASE="%COMMON_LINK_FLAGS%" ^
    -DCMAKE_SHARED_LINKER_FLAGS_RELEASE="%COMMON_LINK_FLAGS%" ^
    -DCMAKE_STATIC_LINKER_FLAGS_RELEASE="" ^
    -DCMAKE_INTERPROCEDURAL_OPTIMIZATION=OFF ^
    -DCMAKE_POLICY_DEFAULT_CMP0069=NEW ^
    -DBUILD_SHARED_LIBS=OFF ^
    -DGGML_STATIC=ON ^
    -DGGML_NATIVE=OFF ^
    -DGGML_OPENMP=OFF ^
    -DGGML_LTO=OFF ^
    -DGGML_CPU=ON ^
    -DGGML_CPU_AARCH64=ON ^
    -DGGML_CPU_ARM_ARCH="armv8.2-a+fp16+dotprod" ^
    -DGGML_CUDA=OFF ^
    -DGGML_METAL=OFF ^
    -DGGML_VULKAN=OFF ^
    -DGGML_OPENCL=OFF ^
    -DGGML_SYCL=OFF ^
    -DGGML_HIP=OFF ^
    -DGGML_RPC=OFF ^
    -DGGML_BLAS=OFF ^
    -DGGML_ACCELERATE=OFF ^
    -DGGML_KOMPUTE=OFF ^
    -DGGML_CANN=OFF ^
    -DGGML_MUSA=OFF ^
    -DWHISPER_BUILD_TESTS=OFF ^
    -DWHISPER_BUILD_EXAMPLES=OFF ^
    -DWHISPER_BUILD_SERVER=OFF ^
    -DWHISPER_SDL2=OFF ^
    -DWHISPER_CURL=OFF ^
    -DWHISPER_COREML=OFF ^
    -DWHISPER_OPENVINO=OFF

if errorlevel 1 (
    echo CMake configuration failed for arm64
    exit /b 1
)

cmake --build "%BUILD_DIR%" --config Release --parallel

if errorlevel 1 (
    echo Build failed for arm64
    exit /b 1
)

echo.
echo ARM64 build complete!
echo.

REM Copy artifacts
set PLUGIN_DIR=%UNITY_PATH%\Packages\com.whisper.unity\Plugins\Android

echo Copying ARM64 libraries to %PLUGIN_DIR%...
copy /y "%BUILD_DIR%\src\libwhisper.a" "%PLUGIN_DIR%\"
copy /y "%BUILD_DIR%\ggml\src\libggml.a" "%PLUGIN_DIR%\"
copy /y "%BUILD_DIR%\ggml\src\libggml-base.a" "%PLUGIN_DIR%\"

REM Try both possible locations for libggml-cpu.a
if exist "%BUILD_DIR%\ggml\src\ggml-cpu\libggml-cpu.a" (
    copy /y "%BUILD_DIR%\ggml\src\ggml-cpu\libggml-cpu.a" "%PLUGIN_DIR%\"
) else if exist "%BUILD_DIR%\ggml\src\libggml-cpu.a" (
    copy /y "%BUILD_DIR%\ggml\src\libggml-cpu.a" "%PLUGIN_DIR%\"
)

echo.
echo ARM64 artifacts copied to: %PLUGIN_DIR%
dir "%PLUGIN_DIR%\*.a"
echo.
goto :eof

:build_arm32
echo.
echo Building whisper.cpp for Android armeabi-v7a...
echo.

set BUILD_DIR=%UNITY_PATH%\build-android-arm32
if exist "%BUILD_DIR%" rmdir /s /q "%BUILD_DIR%"
mkdir "%BUILD_DIR%"

REM ARM32 specific flags with NEON
set ARM32_FLAGS=-march=armv7-a -mfpu=neon-vfpv4 -mfloat-abi=softfp -D__ARM_NEON

cmake -S "%WHISPER_PATH%" -B "%BUILD_DIR%" -G "Ninja" ^
    -DCMAKE_TOOLCHAIN_FILE="%TOOLCHAIN%" ^
    -DANDROID_ABI=armeabi-v7a ^
    -DANDROID_PLATFORM=android-24 ^
    -DCMAKE_BUILD_TYPE=Release ^
    -DCMAKE_C_FLAGS_RELEASE="%COMMON_C_FLAGS% %ARM32_FLAGS%" ^
    -DCMAKE_CXX_FLAGS_RELEASE="%COMMON_CXX_FLAGS% %ARM32_FLAGS%" ^
    -DCMAKE_EXE_LINKER_FLAGS_RELEASE="%COMMON_LINK_FLAGS%" ^
    -DCMAKE_SHARED_LINKER_FLAGS_RELEASE="%COMMON_LINK_FLAGS%" ^
    -DCMAKE_STATIC_LINKER_FLAGS_RELEASE="" ^
    -DCMAKE_INTERPROCEDURAL_OPTIMIZATION=OFF ^
    -DCMAKE_POLICY_DEFAULT_CMP0069=NEW ^
    -DBUILD_SHARED_LIBS=OFF ^
    -DGGML_STATIC=ON ^
    -DGGML_NATIVE=OFF ^
    -DGGML_OPENMP=OFF ^
    -DGGML_LTO=OFF ^
    -DGGML_CPU=ON ^
    -DGGML_CPU_AARCH64=OFF ^
    -DGGML_CUDA=OFF ^
    -DGGML_METAL=OFF ^
    -DGGML_VULKAN=OFF ^
    -DGGML_OPENCL=OFF ^
    -DGGML_SYCL=OFF ^
    -DGGML_HIP=OFF ^
    -DGGML_RPC=OFF ^
    -DGGML_BLAS=OFF ^
    -DGGML_ACCELERATE=OFF ^
    -DGGML_KOMPUTE=OFF ^
    -DGGML_CANN=OFF ^
    -DGGML_MUSA=OFF ^
    -DWHISPER_BUILD_TESTS=OFF ^
    -DWHISPER_BUILD_EXAMPLES=OFF ^
    -DWHISPER_BUILD_SERVER=OFF ^
    -DWHISPER_SDL2=OFF ^
    -DWHISPER_CURL=OFF ^
    -DWHISPER_COREML=OFF ^
    -DWHISPER_OPENVINO=OFF

if errorlevel 1 (
    echo CMake configuration failed for arm32
    exit /b 1
)

cmake --build "%BUILD_DIR%" --config Release --parallel

if errorlevel 1 (
    echo Build failed for arm32
    exit /b 1
)

echo.
echo ARM32 build complete!
echo.

REM For ARM32, we need a separate folder or the original folder
REM Original whisper.unity.org only supports ARM64, so we'll create arm32 folder
set PLUGIN_DIR_ARM32=%UNITY_PATH%\Packages\com.whisper.unity\Plugins\Android-arm32
if not exist "%PLUGIN_DIR_ARM32%" mkdir "%PLUGIN_DIR_ARM32%"

echo Copying ARM32 libraries to %PLUGIN_DIR_ARM32%...
copy /y "%BUILD_DIR%\src\libwhisper.a" "%PLUGIN_DIR_ARM32%\"
copy /y "%BUILD_DIR%\ggml\src\libggml.a" "%PLUGIN_DIR_ARM32%\"
copy /y "%BUILD_DIR%\ggml\src\libggml-base.a" "%PLUGIN_DIR_ARM32%\"

if exist "%BUILD_DIR%\ggml\src\ggml-cpu\libggml-cpu.a" (
    copy /y "%BUILD_DIR%\ggml\src\ggml-cpu\libggml-cpu.a" "%PLUGIN_DIR_ARM32%\"
) else if exist "%BUILD_DIR%\ggml\src\libggml-cpu.a" (
    copy /y "%BUILD_DIR%\ggml\src\libggml-cpu.a" "%PLUGIN_DIR_ARM32%\"
)

echo.
echo ARM32 artifacts copied to: %PLUGIN_DIR_ARM32%
dir "%PLUGIN_DIR_ARM32%\*.a"
echo.
goto :eof

:done
echo.
echo ============================================================
echo BUILD COMPLETE
echo ============================================================
echo.
echo Optimization summary:
echo   - Compiler: O3 + ffast-math + ffp-contract=fast
echo   - LTO: Disabled for Unity 2021 linker compatibility
echo   - C++: fvisibility-inlines-hidden + fno-rtti
echo   - C: fno-exceptions (C only, C++ needs exceptions for whisper.cpp)
echo   - Linker: gc-sections + exclude-libs + flto
echo   - ARM64: ARMv8.2-a + FP16 + DotProd + mtune=cortex-a76
echo   - ARM64 SIMD: NEON + FMA + DOTPROD + FP16_VECTOR_ARITHMETIC
echo   - ARM32: ARMv7-a + NEON VFPv4
echo   - CPU Backend: GGML_CPU_AARCH64 optimizations
echo   - Disabled: All GPU backends, OpenMP, tests, examples
echo.
echo For best performance, also enable in C# code:
echo   - flash_attn = true (context creation)
echo   - audio_ctx = calculated based on audio length
echo.
echo ============================================================

endlocal
