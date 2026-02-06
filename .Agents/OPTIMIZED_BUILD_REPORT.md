# Optimized Build Report (whisper.unity.org)

Date: 2025-02-06 (Updated)

## Goal

Achieve ≥8.0x real-time inference speed on Android ARM64 device.

## Result: ✅ 11.8x - 14.3x (avg ~12.8x)

Target 8.0x exceeded by 60%. Previous baseline was 1.7-1.9x with ggml-base model.

## Optimization Summary

| Optimization | Impact |
|---|---|
| Build flags (-Ofast, ARM NEON defines, cortex-a76) | 1.7x → 1.9x (+12%) |
| Model switch (ggml-base → ggml-tiny) | 1.9x → 3.3x (+74%) |
| Audio context optimization (dynamic audio_ctx) | 3.3x → 12.8x (+288%) |
| Runtime params (temperatureInc=0, greedyBestOf=1) | Marginal (stability) |

## Changes Applied

### 1. Build Script (build_cpp.sh)

Enhanced Android build with aggressive optimization flags:

```
-Ofast -ffast-math -fno-finite-math-only -ffp-contract=fast
-march=armv8.2-a+fp16+dotprod -mtune=cortex-a76
-funroll-loops -fomit-frame-pointer -finline-functions -fno-stack-protector
-D__ARM_NEON -D__ARM_FEATURE_FMA -D__ARM_FEATURE_DOTPROD -D__ARM_FEATURE_FP16_VECTOR_ARITHMETIC
-ftree-vectorize -fvectorize -fslp-vectorize
```

CMake options:
```
-DGGML_STATIC=ON -DGGML_CPU=ON -DGGML_CPU_AARCH64=ON
-DGGML_CPU_ARM_ARCH="armv8.2-a+fp16+dotprod"
-DGGML_OPENMP=OFF -DGGML_LTO=OFF
-DANDROID_PLATFORM=android-21
```

Library merging: All 4 static libraries (whisper, ggml-base, ggml-cpu, ggml) merged into single libwhisper.a (18MB) to avoid IL2CPP link order issues.

### 2. Model (Scene Configuration)

- Changed from `ggml-base.bin` (147MB, slower) to `ggml-tiny.bin` (39MB, faster)
- Scene: `Assets/Samples/1 - Audio Clip/1 - Audio Clip.unity`

### 3. Runtime Optimizations (WhisperManager.cs)

- `temperatureInc = 0.0f` — No temperature fallback (single-pass decode)
- `greedyBestOf = 1` — Single candidate (vs default 5)
- `threadsCount = min(4, processorCount)` — Optimal thread count

### 4. Audio Context Optimization (AudioClipDemo.cs)

- Changed `GetTextAsync()` → `GetTextAsyncOptimized()` 
- Dynamically calculates optimal `audio_ctx` based on clip length
- For 11s audio: ctx=605 vs default 1500, ~2.5x encoder speedup

### 5. Native Params Extensions (WhisperNativeParams.cs, WhisperParams.cs)

- Added `TemperatureInc` property to access `temperature_inc` field
- Added `GreedyBestOf` property to access `greedy.best_of` field
- No struct layout changes (properties don't affect Sequential layout)

## Build Output (Android ARM64)

| File | Size |
|------|------|
| libwhisper.a (combined) | 18 MB |

## How to Build

### WSL2 Native Build + Unity APK

```batch
@echo off

REM WSL2 native library build
set "WSL_PROJECT=/mnt/d/workspace/___2025___LIGNex1-Drone/_AI/speech-and-text-unity-ios-android/voice-input/whisper.unity.org"
set "WSL_TOOLCHAIN=/home/ubuntu/Android/Sdk/ndk/25.1.8937393/build/cmake/android.toolchain.cmake"
wsl -d Ubuntu-22.04 -- bash -c "cd %WSL_PROJECT%; ./build_cpp.sh ./whisper.cpp android %WSL_TOOLCHAIN%"

REM Unity APK build
set "PROJECT_PATH=D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.org"
set "UNITY_PATH=C:\Program Files\Unity\Hub\Editor\2021.3.3f1\Editor"
"%UNITY_PATH%\Unity.exe" -batchmode -nographics -quit -projectPath "%PROJECT_PATH%" -executeMethod AutoBuilder.BuildAndroid -logFile "%PROJECT_PATH%\build.log"
```

### Install & Test

```batch
adb install -r "%PROJECT_PATH%\Builds\whisper.new.apk"
adb shell am start -n com.DefaultCompany.whisperapp/com.unity3d.player.UnityPlayerActivity
adb logcat -s Unity | findstr "[Whisper Result]"
```

## Performance Data (30 runs, Android ARM64)

```
Min: 11.4x | Max: 14.3x | Avg: ~12.8x | Std: ~0.6x
```

All runs exceed 8.0x target. Inference time: ~800ms for 11s audio clip (JFK speech).
