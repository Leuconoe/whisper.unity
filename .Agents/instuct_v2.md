# Whisper.cpp Unity 라이브러리 최적화 작업 지시서 (v2)

> **이전 버전**: `instuct.md` — 2026-02-06 실행 결과를 반영하여 개선한 지침서  
> **최종 달성 성능**: 12.8x real-time (목표 8.0x 초과 달성)

## 목표

whisper.cpp 기반 Unity Android 라이브러리의 추론 속도를 **8.0x real-time** 이상으로 달성

## 배경

- **원본 저장소**: `whisper.unity.org` — 최적화 대상
- **참조 저장소**: `whisper.unity` — FUTO Voice Input 최적화된 버전 (Unity 버전 상이로 PR 불가)
- **빌드 환경**: WSL2 (Ubuntu-22.04) + Android NDK 25.1.8937393 + Unity 2021.3.3f1

---

## 성능 개선 우선순위 (영향도 순)

| 순위 | 최적화 항목 | 기대 개선폭 | 비고 |
|------|-------------|-------------|------|
| 1 | Audio Context 동적 계산 | **3x~4x** | 짧은 오디오일수록 효과 극대 |
| 2 | 경량 모델 사용 (tiny) | **1.5x~2x** | base→tiny로 4배 적은 파라미터 |
| 3 | 빌드 플래그 강화 | **10~15%** | -Ofast, ARM NEON 명시, 벡터화 |
| 4 | 런타임 파라미터 | **5~10%** | temperatureInc=0, greedyBestOf=1 |

---

## 작업 순서

### 1단계: 빌드 스크립트 최적화 (build_cpp.sh)

`build_android()` 함수의 cmake 명령을 아래와 같이 수정:

```bash
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
make -j$(nproc)
```

**필수 제약 사항**:
| 항목 | 설정 | 이유 |
|------|------|------|
| `ANDROID_PLATFORM` | `android-21` | android-24 이상은 stdout/stderr 심볼이 extern으로 노출되어 Unity IL2CPP 링커와 ABI 불일치 발생 |
| `GGML_OPENMP` | `OFF` | Unity 정적 링킹에서 OpenMP 호환성 문제 |
| `GGML_LTO` | `OFF` | Unity 2021 링커 호환성 |

### 1-1단계: 라이브러리 병합 (필수)

빌드 후 생성되는 4개의 정적 라이브러리를 **반드시 단일 아카이브로 병합**해야 함:

```bash
# 병합 대상: libwhisper.a + libggml-base.a + libggml-cpu.a + libggml.a
# 방법: ar MRI 스크립트 사용 (build_cpp.sh에 이미 구현됨)
```

**병합이 필요한 이유**: Unity IL2CPP 링커는 아카이브를 좌→우 순서로 처리하여, 순환 의존성이 있는 별도 .a 파일을 올바르게 링크하지 못함.

### 1-2단계: .meta 파일 확인

`Packages/com.whisper.unity/Plugins/Android/libwhisper.a.meta` 파일에 Android ARM64 플랫폼이 활성화되어 있는지 확인:

```yaml
# 반드시 포함되어야 하는 설정:
Android:
  enabled: 1
  settings:
    CPU: ARM64
```

- 별도의 `libggml-base.a.meta`, `libggml-cpu.a.meta`, `libggml.a.meta` 파일이 있다면 **삭제** (병합된 단일 libwhisper.a만 사용)

---

### 2단계: 모델 설정

씬 파일(`Assets/Samples/1 - Audio Clip/1 - Audio Clip.unity`)에서:

```yaml
modelPath: Whisper/ggml-tiny.bin    # ggml-base.bin 사용 시 속도 1/4로 저하
```

**모델별 성능 참고**:
| 모델 | 크기 | 예상 성능 (11초 오디오) |
|------|------|------------------------|
| ggml-tiny.bin | 39MB | 12~14x |
| ggml-base.bin | 147MB | 1.7~1.9x |

---

### 3단계: 런타임 파라미터 최적화

**WhisperManager.cs** — `UpdateParams()` 메서드에서 다음 설정 적용:

```csharp
// FUTO Voice Input 최적화
_params.TemperatureInc = 0.0f;          // 온도 fallback 비활성화 (재시도 제거)
_params.GreedyBestOf = 1;               // 단일 후보 (기본 5 → 1)
_params.ThreadsCount = Math.Min(4, SystemInfo.processorCount);  // 최적 스레드 수
```

**필요한 코드 변경 (WhisperNativeParams.cs)**:
- `greedy_struct.best_of` 필드를 `public`으로 변경
- `TemperatureInc` 프로퍼티 추가 (temperature_inc 필드 접근)
- `GreedyBestOf` 프로퍼티 추가 (greedy.best_of 필드 접근)

**⚠️ 주의**: WhisperNativeParams는 `[StructLayout(LayoutKind.Sequential)]`로 C++ 구조체와 1:1 매핑됨. **필드를 추가하거나 순서를 변경하면 안 됨**. 프로퍼티만 추가할 것.

---

### 4단계: Audio Context 동적 최적화 (핵심)

**AudioClipDemo.cs** — 추론 호출을 최적화 버전으로 변경:

```csharp
// 변경 전
var res = await manager.GetTextAsync(clip);

// 변경 후 — audio_ctx를 오디오 길이에 맞게 자동 계산
var res = await manager.GetTextAsyncOptimized(clip);
```

이 함수는 `WhisperOptimization.CalculateAudioContext(clip.length)`를 호출하여:
- 30초 오디오 → audio_ctx = 1500 (기본값과 동일, 개선 없음)
- 11초 오디오 → audio_ctx = 605 (**인코더 연산 60% 절감**)
- 5초 오디오 → audio_ctx = 275 (**인코더 연산 82% 절감**)

---

### 5단계: WSL2 네이티브 빌드

```bash
# WSL2 배포판 이름 확인
wsl -l -v
# 출력 예: Ubuntu-22.04

# 빌드 실행 (배포판 이름을 정확히 지정)
wsl -d Ubuntu-22.04 -- bash -c "cd /mnt/d/workspace/___2025___LIGNex1-Drone/_AI/speech-and-text-unity-ios-android/voice-input/whisper.unity.org && ./build_cpp.sh ./whisper.cpp android /home/ubuntu/Android/Sdk/ndk/25.1.8937393/build/cmake/android.toolchain.cmake 2>&1"
```

**빌드 성공 확인**:
- `Build for Android complete!` 출력
- `Combined library created at ...libwhisper.a` 출력
- 라이브러리 크기: ~18MB

**빌드 실패 시 확인사항**:
- NDK 경로 확인 (`/home/ubuntu/Android/Sdk/ndk/25.1.8937393` 존재 여부)
- whisper.cpp 하위 폴더 존재 여부
- cmake, make 설치 여부

---

### 6단계: Unity 헤드리스 빌드

```powershell
& "C:\Program Files\Unity\Hub\Editor\2021.3.3f1\Editor\Unity.exe" `
    -batchmode -nographics -quit `
    -projectPath "D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.org" `
    -executeMethod AutoBuilder.BuildAndroid `
    -logFile "D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.org\build.log"
```

**빌드 로그 모니터링**:
```powershell
# 빌드 진행 확인 (30초 간격)
Get-Content "...\build.log" -Tail 20

# 완료 판단 기준
# ✅ 성공: "Exiting batchmode successfully now!" 포함
# ❌ 실패: "BuildFailedException", "clang++: error:", "undefined reference" 포함
# ⏳ 진행중: build.log 줄 수가 계속 증가
# 🔍 정체: 60초 이상 줄 수 변화 없으면 완료/실패로 판단
```

**빌드 실패 시 주요 에러 유형**:
| 에러 | 원인 | 해결 |
|------|------|------|
| `undefined reference to 'whisper_*'` | .meta 파일 Android 미설정 | libwhisper.a.meta에 Android ARM64 활성화 |
| `undefined reference to 'ggml_*'` | 라이브러리 미병합 | build_cpp.sh의 ar 병합 단계 확인 |
| `undefined reference to 'stdout'` | ANDROID_PLATFORM 버전 | android-21로 설정 |
| C# 컴파일 에러 | 프로퍼티/필드 변경 오류 | WhisperNativeParams 구조체 레이아웃 확인 |

---

### 7단계: APK 설치 및 실행

```powershell
# APK 존재 확인
Test-Path "...\Builds\whisper.new.apk"

# 설치
adb install -r "...\Builds\whisper.new.apk"

# 실행
adb shell am force-stop com.DefaultCompany.whisperapp
adb shell am start -n com.DefaultCompany.whisperapp/com.unity3d.player.UnityPlayerActivity
```

---

### 8단계: 성능 측정

```powershell
# 로그 초기화 후 모니터링
adb logcat -c
Start-Sleep -Seconds 20
adb logcat -d -s Unity 2>&1 | Select-String "\[Whisper Result\]" | Select-Object -Last 10
```

**자동 실행**: AudioClipDemo.cs에 `autoRunOnStart = true` 설정 시, 앱 시작 후 3초 대기 → 100회 자동 반복 추론.

**판단 기준**:
| 속도 | 판단 | 조치 |
|------|------|------|
| ≥ 8.0x | ✅ 목표 달성 | 완료 |
| 5.0x ~ 7.9x | ⚠️ 미달 | 4단계(audio_ctx) 적용 확인, 모델 확인 |
| < 5.0x | ❌ 부족 | 1~4단계 전체 재검토 |
| < 1.0x | 🔴 심각한 저하 | 구조체 레이아웃 불일치 의심 → 런타임 파라미터 제거 후 재테스트 |

---

## 알려진 함정 (주의사항)

### ⚠️ WhisperNativeParams 구조체 레이아웃
- `[StructLayout(LayoutKind.Sequential)]`로 C++ `whisper_full_params` 구조체와 바이트 정렬됨
- **절대로 필드를 추가/삭제/재배치하면 안 됨** — 프로퍼티만 추가 가능
- 위반 시: 추론 속도 0.1x 이하로 급감하거나 크래시 발생

### ⚠️ ggml-base vs ggml-tiny
- 성능 차이 ~4배. 목표 달성에 모델 선택이 결정적
- 씬 파일(`.unity`)에서 모델 경로 오버라이드되므로, 코드의 기본값 변경만으로는 불충분

### ⚠️ WSL 배포판 이름
- `wsl -d Ubuntu` 실패 시 `wsl -l -v`로 정확한 이름 확인 (예: `Ubuntu-22.04`)

### ⚠️ audio_ctx 최적화와 긴 오디오
- 30초 오디오에서는 `GetTextAsyncOptimized()`와 `GetTextAsync()` 성능 차이 없음
- 30초 이상 오디오에서는 스트리밍(`WhisperStream`)을 사용해야 함

---

## 참조 파일

| 파일 | 설명 |
|------|------|
| `whisper.unity/Packages/com.whisper.unity/Runtime/WhisperManager.cs` | FUTO 최적화 파라미터 참조 |
| `whisper.unity/build_cpp_whisper175.bat` | 빌드 플래그 원본 참조 |
| `whisper.unity.org/OPTIMIZED_BUILD_REPORT.md` | 최적화 결과 보고서 |
| `whisper.unity.org/.Agents/optimization_report.md` | 상세 작업 보고서 |

---

## 실행 방식

**중단 요청 전까지 1~8단계를 반복 실행하며 성능 개선을 지속함.**
- 목표(8.0x) 달성 시에도 추가 개선 시도
- 각 반복마다 성능 측정 결과를 기록
- 빌드 실패 시 원인 분석 → 수정 → 재빌드 자동 수행
