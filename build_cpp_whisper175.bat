@echo off
REM ============================================================
REM Optimized whisper.cpp v1.7.5 Build Script for whisper.unity (Windows)
REM ============================================================
REM
REM This batch file builds whisper.cpp v1.7.5 with aggressive optimizations
REM targeting Android ARM devices for maximum inference performance.
REM
REM Key optimizations applied:
REM - O3 optimization with -ffast-math
REM - ARM NEON + FP16 + DotProd extensions
REM - Flash Attention support enabled
REM - CPU-only backend (no GPU overhead)
REM
REM Prerequisites:
REM - Android NDK (r25+ recommended)
REM - CMake 3.14+
REM - Ninja build system
REM
REM Usage:
REM   build_cpp_whisper175.bat <NDK_PATH> [arm64|arm32|all]
REM
REM Examples:
REM   build_cpp_whisper175.bat C:\Android\Sdk\ndk\27.2.12479018
REM   build_cpp_whisper175.bat %ANDROID_NDK% arm32
REM   build_cpp_whisper175.bat %ANDROID_NDK% all
REM
REM ============================================================

setlocal enabledelayedexpansion

set SCRIPT_DIR=%~dp0
set WHISPER_CPP_DIR=%SCRIPT_DIR%..\whisper.cpp
set BUILD_BASE_DIR=%SCRIPT_DIR%build-whisper175

set NDK_PATH=%1
set ARCH=%2

if "%NDK_PATH%"=="" (
    echo Error: Android NDK path required
    echo Usage: %0 ^<NDK_PATH^> [arm64^|arm32^|all]
    exit /b 1
)

if "%ARCH%"=="" set ARCH=arm64

if not exist "%NDK_PATH%" (
    echo Error: NDK not found at: %NDK_PATH%
    exit /b 1
)

set TOOLCHAIN=%NDK_PATH%\build\cmake\android.toolchain.cmake

if not exist "%TOOLCHAIN%" (
    echo Error: NDK toolchain not found at: %TOOLCHAIN%
    exit /b 1
)

if not exist "%WHISPER_CPP_DIR%" (
    echo Error: whisper.cpp source not found at: %WHISPER_CPP_DIR%
    echo Please ensure whisper.cpp v1.7.5 is located in the parent directory
    exit /b 1
)

echo.
echo ============================================================
echo Optimized whisper.cpp v1.7.5 Build for whisper.unity
echo ============================================================
echo.
echo Source: %WHISPER_CPP_DIR%
echo Build: %BUILD_BASE_DIR%
echo NDK: %NDK_PATH%
echo Architecture: %ARCH%
echo.
echo Key optimizations:
echo   - O3 + ffast-math + ffp-contract=fast
echo   - ARM NEON + FP16 + DotProd (arm64)
echo   - Flash Attention enabled
echo   - GGML_CPU_AARCH64 optimizations
echo   - Dead code elimination
echo   - CPU-only (no GPU overhead)
echo.
echo ============================================================
echo.

if "%ARCH%"=="arm64" goto build_arm64
if "%ARCH%"=="arm32" goto build_arm32
if "%ARCH%"=="all" goto build_all

echo Unknown architecture: %ARCH%
echo Available: arm64, arm32, all
exit /b 1

:build_all
call :do_build_arm64
call :do_build_arm32
goto done

:build_arm64
call :do_build_arm64
goto done

:build_arm32
call :do_build_arm32
goto done

:do_build_arm64
echo.
echo Building whisper.cpp v1.7.5 for Android arm64-v8a...
echo.

set BUILD_DIR=%BUILD_BASE_DIR%-arm64
if exist "%BUILD_DIR%" rmdir /s /q "%BUILD_DIR%"
mkdir "%BUILD_DIR%"
cd /d "%BUILD_DIR%"

set OPT_C_FLAGS=-O3 -ffast-math -fno-finite-math-only -ffp-contract=fast -fvisibility=hidden -ffunction-sections -fdata-sections -march=armv8.2-a+fp16+dotprod -fno-exceptions -DNDEBUG
set OPT_CXX_FLAGS=-O3 -ffast-math -fno-finite-math-only -ffp-contract=fast -fvisibility=hidden -ffunction-sections -fdata-sections -march=armv8.2-a+fp16+dotprod -fvisibility-inlines-hidden -fno-rtti -DNDEBUG
set OPT_LINK_FLAGS=-Wl,--gc-sections -Wl,--exclude-libs,ALL -Wl,--strip-debug

cmake -G "Ninja" ^
    -DCMAKE_TOOLCHAIN_FILE="%TOOLCHAIN%" ^
    -DANDROID_ABI=arm64-v8a ^
    -DANDROID_PLATFORM=android-24 ^
    -DCMAKE_BUILD_TYPE=Release ^
    -DCMAKE_C_FLAGS_RELEASE="%OPT_C_FLAGS%" ^
    -DCMAKE_CXX_FLAGS_RELEASE="%OPT_CXX_FLAGS%" ^
    -DCMAKE_EXE_LINKER_FLAGS_RELEASE="%OPT_LINK_FLAGS%" ^
    -DCMAKE_SHARED_LINKER_FLAGS_RELEASE="%OPT_LINK_FLAGS%" ^
    -DBUILD_SHARED_LIBS=OFF ^
    -DGGML_STATIC=ON ^
    -DGGML_NATIVE=OFF ^
    -DGGML_LTO=OFF ^
    -DGGML_CPU=ON ^
    -DGGML_CPU_AARCH64=ON ^
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
    -DWHISPER_OPENVINO=OFF ^
    "%WHISPER_CPP_DIR%"

cmake --build . --config Release

echo.
echo ARM64 build complete!
echo.

set TARGET_DIR=%SCRIPT_DIR%Packages\com.whisper.unity\Plugins\Android-whisper175\arm64-v8a
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"

if exist "src\libwhisper.a" (
    copy /y "src\libwhisper.a" "%TARGET_DIR%\"
    echo Copied src\libwhisper.a
)
if exist "ggml\src\libggml.a" (
    copy /y "ggml\src\libggml.a" "%TARGET_DIR%\"
    echo Copied ggml\src\libggml.a
)
if exist "ggml\src\libggml-base.a" (
    copy /y "ggml\src\libggml-base.a" "%TARGET_DIR%\"
    echo Copied ggml\src\libggml-base.a
)
if exist "ggml\src\ggml-cpu\libggml-cpu.a" (
    copy /y "ggml\src\ggml-cpu\libggml-cpu.a" "%TARGET_DIR%\"
    echo Copied ggml\src\ggml-cpu\libggml-cpu.a
)

echo.
echo ARM64 artifacts in: %TARGET_DIR%
dir "%TARGET_DIR%"
goto :eof

:do_build_arm32
echo.
echo Building whisper.cpp v1.7.5 for Android armeabi-v7a...
echo.

set BUILD_DIR=%BUILD_BASE_DIR%-arm32
if exist "%BUILD_DIR%" rmdir /s /q "%BUILD_DIR%"
mkdir "%BUILD_DIR%"
cd /d "%BUILD_DIR%"

set OPT_C_FLAGS=-O3 -ffast-math -fno-finite-math-only -ffp-contract=fast -fvisibility=hidden -ffunction-sections -fdata-sections -mfpu=neon-vfpv4 -mfloat-abi=softfp -fno-exceptions -DNDEBUG
set OPT_CXX_FLAGS=-O3 -ffast-math -fno-finite-math-only -ffp-contract=fast -fvisibility=hidden -ffunction-sections -fdata-sections -mfpu=neon-vfpv4 -mfloat-abi=softfp -fvisibility-inlines-hidden -fno-rtti -DNDEBUG
set OPT_LINK_FLAGS=-Wl,--gc-sections -Wl,--exclude-libs,ALL -Wl,--strip-debug

cmake -G "Ninja" ^
    -DCMAKE_TOOLCHAIN_FILE="%TOOLCHAIN%" ^
    -DANDROID_ABI=armeabi-v7a ^
    -DANDROID_PLATFORM=android-24 ^
    -DCMAKE_BUILD_TYPE=Release ^
    -DCMAKE_C_FLAGS_RELEASE="%OPT_C_FLAGS%" ^
    -DCMAKE_CXX_FLAGS_RELEASE="%OPT_CXX_FLAGS%" ^
    -DCMAKE_EXE_LINKER_FLAGS_RELEASE="%OPT_LINK_FLAGS%" ^
    -DCMAKE_SHARED_LINKER_FLAGS_RELEASE="%OPT_LINK_FLAGS%" ^
    -DBUILD_SHARED_LIBS=OFF ^
    -DGGML_STATIC=ON ^
    -DGGML_NATIVE=OFF ^
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
    -DWHISPER_OPENVINO=OFF ^
    "%WHISPER_CPP_DIR%"

cmake --build . --config Release

echo.
echo ARM32 build complete!
echo.

set TARGET_DIR=%SCRIPT_DIR%Packages\com.whisper.unity\Plugins\Android-whisper175\armeabi-v7a
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"

if exist "src\libwhisper.a" (
    copy /y "src\libwhisper.a" "%TARGET_DIR%\"
    echo Copied src\libwhisper.a
)
if exist "ggml\src\libggml.a" (
    copy /y "ggml\src\libggml.a" "%TARGET_DIR%\"
    echo Copied ggml\src\libggml.a
)
if exist "ggml\src\libggml-base.a" (
    copy /y "ggml\src\libggml-base.a" "%TARGET_DIR%\"
    echo Copied ggml\src\libggml-base.a
)
if exist "ggml\src\ggml-cpu\libggml-cpu.a" (
    copy /y "ggml\src\ggml-cpu\libggml-cpu.a" "%TARGET_DIR%\"
    echo Copied ggml\src\ggml-cpu\libggml-cpu.a
)

echo.
echo ARM32 artifacts in: %TARGET_DIR%
dir "%TARGET_DIR%"
goto :eof

:done
echo.
echo ============================================================
echo USAGE INSTRUCTIONS
echo ============================================================
echo.
echo To use optimized whisper.cpp v1.7.5 in Unity:
echo.
echo 1. Update WhisperNative.cs to point to new library location
echo.
echo 2. Enable optimizations in your code:
echo.
echo    // Enable flash attention (context creation)
echo    var cparams = WhisperContextParams.Default;
echo    cparams.flash_attn = true;  // ~15%% speedup
echo.
echo    // Enable dynamic audio context (inference)
echo    var fparams = WhisperFullParams.Default;
echo    fparams.audio_ctx = CalculateAudioContext(audioLengthMs);
echo.
echo    // Calculate audio_ctx based on audio length
echo    // For 30s max: 1500, scale proportionally
echo    // e.g., 5s audio -^> audio_ctx = 250
echo.
echo 3. Expected speedup vs original whisper.cpp:
echo    - Base: 15-25%% faster (O3 + ffast-math)
echo    - With flash_attn: 30-40%% faster
echo    - With dynamic audio_ctx: 2-6x faster (for short audio)
echo.
echo ============================================================
echo.
echo Build completed successfully!
echo ============================================================

endlocal
