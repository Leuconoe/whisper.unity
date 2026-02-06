# whisper.unity.2022 — 원타임 셋업 지시서

> **프로젝트**: whisper.unity.2022  
> **Unity**: 2022.3.62f3 LTS  
> **whisper.cpp**: v1.7.5  
> **목표**: Android ARM64에서 8.0x real-time 이상 달성을 위한 초기 환경 구축  
> **참고**: whisper.unity.2021 (12.8x 달성) 기반 — Unity 2022에서 추가 플래그 탐색 가능

---

## 환경 정보

| 항목 | 값 |
|------|-----|
| Unity Editor | `C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe` |
| 프로젝트 경로 | `D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.2022` |
| WSL 배포판 | `Ubuntu-22.04` (확인: `wsl -l -v`) |
| Android NDK | `/home/ubuntu/Android/Sdk/ndk/25.1.8937393` |
| NDK Toolchain | `/home/ubuntu/Android/Sdk/ndk/25.1.8937393/build/cmake/android.toolchain.cmake` |
| 모델 파일 | `Assets/StreamingAssets/Whisper/ggml-tiny.bin` (39MB) |
| APK 출력 경로 | `Builds/whisper.2022.apk` |
| 패키지명 | `com.DefaultCompany.whisperapp` (확인 필요: `ProjectSettings/ProjectSettings.asset`) |

---

## 1단계: build_cpp.sh 최적화

`build_android()` 함수의 cmake 명령을 아래와 같이 수정:

```bash
build_android() {
  clean_build
  echo "Starting building for Android (Optimized)..."

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

  echo "Build for Android complete!"

  # === 라이브러리 병합 (필수) ===
  echo "Merging static libraries..."

  target_dir="$unity_project/Packages/com.whisper.unity/Plugins/Android"
  rm -f "$target_dir"/*.a

  # ar MRI 스크립트로 4개 라이브러리를 단일 아카이브로 병합
  local ar_tool=$(find "$android_sdk_path/../../../toolchains/llvm/prebuilt" -name "llvm-ar" | head -1)
  if [ -z "$ar_tool" ]; then
    ar_tool="ar"
  fi

  local merged_lib="$target_dir/libwhisper.a"
  local mri_script="$build_path/merge.mri"

  cat > "$mri_script" << EOF
CREATE $merged_lib
ADDLIB $build_path/src/libwhisper.a
ADDLIB $build_path/ggml/src/libggml.a
ADDLIB $build_path/ggml/src/libggml-base.a
ADDLIB $build_path/ggml/src/libggml-cpu.a
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
```

### 필수 제약 사항

| 항목 | 설정 | 이유 |
|------|------|------|
| `ANDROID_PLATFORM` | `android-21` | android-24 이상은 `stdout`/`stderr` 심볼이 extern으로 노출되어 Unity IL2CPP 링커와 ABI 불일치 → `undefined reference to 'stdout'` |
| `GGML_OPENMP` | 초기 `OFF` | 정적 링킹에서 OpenMP 호환성 문제 가능 (Unity 2022에서 ON 테스트 → `optimize.md` 참조) |
| `GGML_LTO` | 초기 `OFF` | Unity 2021에서 링커 호환성 문제 (Unity 2022에서 ON 테스트 → `optimize.md` 참조) |

---

## 2단계: .meta 파일 확인

`Packages/com.whisper.unity/Plugins/Android/libwhisper.a.meta`에 Android ARM64 플랫폼 활성화 확인:

```yaml
# 반드시 포함:
Android:
  enabled: 1
  settings:
    CPU: ARM64
```

**현재 상태**: ✅ 이미 올바르게 설정됨

**병합 후 불필요 .meta 파일 정리**:
- `libggml-base.a.meta`, `libggml-cpu.a.meta`, `libggml.a.meta`가 있다면 **삭제**
- 병합된 단일 `libwhisper.a`만 사용
- 해당 .a 파일(`libggml-base.a`, `libggml-cpu.a`, `libggml.a`)도 삭제

---

## 3단계: C# 코드 확인

### 3-1. WhisperNativeParams.cs — 구조체 레이아웃 확인

현재 상태: ✅ 이미 `TemperatureInc` / `GreedyBestOf` 프로퍼티 존재

```csharp
// 프로퍼티만 추가됨 (필드 추가/삭제/재배치 없음 — 올바름)
public float TemperatureInc
{
    get => temperature_inc;
    set => temperature_inc = value;
}

public int GreedyBestOf
{
    get => greedy.best_of;
    set => greedy.best_of = value;
}
```

**⚠️ 절대 금지**: `WhisperNativeParams`에 필드 추가/삭제/순서 변경 → C++ 구조체 `whisper_full_params` (whisper.h L476)와 바이트 레이아웃 불일치 → 추론 속도 0.1x 이하 또는 크래시

### 3-2. WhisperManager.cs — 최적화 파라미터 확인

현재 상태: ✅ 이미 적용됨

```csharp
public float temperatureInc = 0.0f;    // ✅ 온도 fallback 비활성화
public int greedyBestOf = 1;           // ✅ 단일 후보
public int threadsCount = 0;           // ✅ 자동 감지 (min(4, CPU cores))
```

### 3-3. WhisperOptimization.cs — 오디오 컨텍스트 최적화

현재 상태: ✅ 이미 존재

### 3-4. WhisperParams.cs — Decoding Optimization 영역

현재 상태: ✅ `TemperatureInc`, `GreedyBestOf`, `AudioCtx` 프로퍼티 존재

---

## 4단계: AudioClipDemo.cs 수정

**현재**: `GetTextAsync(clip)` 사용 (비최적화)

```csharp
// 변경 전 (현재)
//var res = await manager.GetTextAsyncOptimized(clip);
var res = await manager.GetTextAsync(clip);

// 변경 후
var res = await manager.GetTextAsyncOptimized(clip);
//var res = await manager.GetTextAsync(clip);
```

**효과**: 짧은 오디오에서 audio_ctx 자동 계산 → 인코더 연산 대폭 절감
- 11초 오디오 → audio_ctx = 605 (기본 1500 대비 60% 절감)
- 5초 오디오 → audio_ctx = 275 (82% 절감)

**자동 실행 설정 확인**:
```csharp
public bool autoRunOnStart = true;       // ✅ 이미 true
public int autoRunRepeatCount = 100;     // ✅ 100회 반복
```

---

## 5단계: 모델 경로 확인

`Assets/StreamingAssets/Whisper/ggml-tiny.bin` 존재 확인:
- ✅ 현재 존재 (ggml-tiny.bin)
- 씬 파일에서 modelPath가 `Whisper/ggml-tiny.bin`인지 확인
  - 씬 경로: `Assets/Samples/1 - Audio Clip/1 - Audio Clip.unity`

**모델별 성능 차이**:
| 모델 | 크기 | 예상 속도 (11초 오디오) |
|------|------|------------------------|
| ggml-tiny.bin | 39MB | 12~14x |
| ggml-base.bin | 147MB | 1.7~1.9x |

---

## 6단계: 패키지명 확인

```powershell
# ProjectSettings.asset에서 패키지명 확인
Select-String -Path "whisper.unity.2022\ProjectSettings\ProjectSettings.asset" -Pattern "applicationIdentifier" -Context 0,5
```

APK 설치/실행 시 사용:
```powershell
adb install -r "...\Builds\whisper.2022.apk"
adb shell am start -n <패키지명>/com.unity3d.player.UnityPlayerActivity
```

---

## 7단계: WSL2 네이티브 빌드 실행

```powershell
wsl -d Ubuntu-22.04 -- bash -c "cd /mnt/d/workspace/___2025___LIGNex1-Drone/_AI/speech-and-text-unity-ios-android/voice-input/whisper.unity.2022 && ./build_cpp.sh ./whisper.cpp android /home/ubuntu/Android/Sdk/ndk/25.1.8937393/build/cmake/android.toolchain.cmake 2>&1"
```

**빌드 성공 확인**:
- `Build for Android complete!` 출력
- `Combined library created at ...libwhisper.a` 출력
- 라이브러리 크기: ~18MB

**빌드 실패 시 확인**:

| 에러 | 원인 | 해결 |
|------|------|------|
| `cmake: command not found` | WSL cmake 미설치 | `sudo apt install cmake build-essential` |
| NDK 경로 에러 | 경로 오류 | `ls /home/ubuntu/Android/Sdk/ndk/25.1.8937393` 확인 |
| `llvm-ar: No such file` | ar 도구 경로 | `find /home/ubuntu/Android/Sdk/ndk -name "llvm-ar"` |

---

## 8단계: Unity 헤드리스 빌드

```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" `
    -batchmode -nographics -quit `
    -projectPath "D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.2022" `
    -executeMethod AutoBuilder.BuildAndroid `
    -logFile "D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.2022\build.log"
```

**빌드 로그 모니터링**:
```powershell
# 진행 확인 (30초 간격)
Get-Content "...\whisper.unity.2022\build.log" -Tail 20

# 완료 판단
# ✅ 성공: "Exiting batchmode successfully now!" 포함
# ❌ 실패: "BuildFailedException", "clang++: error:", "undefined reference" 포함
# ⏳ 진행중: build.log 줄 수가 계속 증가
# 🔍 정체: 60초 이상 줄 수 변화 없으면 완료/실패로 판단
```

**주요 링커 에러 대응**:

| 에러 | 원인 | 해결 |
|------|------|------|
| `undefined reference to 'whisper_*'` | .meta 파일 미설정 | libwhisper.a.meta에 Android ARM64 활성화 |
| `undefined reference to 'ggml_*'` | 라이브러리 미병합 | build_cpp.sh의 ar 병합 확인 |
| `undefined reference to 'stdout'` | ANDROID_PLATFORM 버전 | android-21로 설정 |
| C# 컴파일 에러 | 구조체 레이아웃 불일치 | WhisperNativeParams 필드 변경 금지 |

---

## 9단계: APK 설치 및 성능 측정

```powershell
# APK 확인
Test-Path "...\whisper.unity.2022\Builds\whisper.2022.apk"

# 설치
adb install -r "...\whisper.unity.2022\Builds\whisper.2022.apk"

# 실행
adb shell am force-stop <패키지명>
adb shell am start -n <패키지명>/com.unity3d.player.UnityPlayerActivity

# 성능 측정 (앱 시작 후 20초 대기)
adb logcat -c
Start-Sleep -Seconds 20
adb logcat -d -s Unity 2>&1 | Select-String "\[Whisper Result\]" | Select-Object -Last 10
```

**baseline 판단기준**:
| 속도 | 판단 |
|------|------|
| ≥ 8.0x | ✅ 목표 달성 → optimize.md로 추가 개선 탐색 |
| 5.0x ~ 7.9x | ⚠️ 미달 → audio_ctx 적용 확인, 모델 확인 |
| < 5.0x | ❌ 부족 → 전체 재검토 |
| < 1.0x | 🔴 구조체 레이아웃 불일치 의심 |

---

## 완료 조건

이 셋업이 완료되면:
1. ✅ build_cpp.sh가 최적화 플래그 + 라이브러리 병합 포함
2. ✅ 불필요한 개별 .a 파일 및 .meta 삭제
3. ✅ AudioClipDemo가 `GetTextAsyncOptimized` 사용
4. ✅ APK가 빌드되어 디바이스에서 실행 가능
5. ✅ baseline 성능 측정 완료 (x.xx real-time)

baseline 측정 후 → `optimize.md`의 반복 최적화 작업으로 전환

---

## 참조 파일

| 파일 | 설명 |
|------|------|
| `whisper.unity.2022/build_cpp.sh` | 빌드 스크립트 (수정 대상) |
| `whisper.unity.2022/Packages/com.whisper.unity/Runtime/WhisperManager.cs` | 런타임 파라미터 |
| `whisper.unity.2022/Packages/com.whisper.unity/Runtime/Native/WhisperNativeParams.cs` | 네이티브 구조체 |
| `whisper.unity.2022/Packages/com.whisper.unity/Runtime/WhisperParams.cs` | 파라미터 래퍼 |
| `whisper.unity.2022/Assets/Samples/1 - Audio Clip/AudioClipDemo.cs` | 오디오 추론 데모 |
| `whisper.unity.2022/Assets/Editor/AutoBuilder.cs` | 헤드리스 빌드 |
| `whisper.unity.2022/whisper.cpp/include/whisper.h` | C++ 구조체 원본 (L476~L573) |
| `whisper.unity.2021/.Agents/instuct_v2.md` | 이전 버전 지시서 참조 |
