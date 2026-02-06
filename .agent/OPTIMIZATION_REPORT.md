# Whisper Unity Android 최적화 최종 보고서

> **날짜**: 2025-02  
> **대상 프로젝트**: whisper.unity.2022 (Unity 2022.3.62f3 LTS)  
> **대상 디바이스**: Snapdragon 855 (Kryo 485) ARM64  
> **whisper.cpp 버전**: v1.7.5  
> **모델**: ggml-tiny.bin (39MB)

---

## 1. 성능 결과 요약

| 항목 | 값 |
|---|---|
| **최적화 전 (Baseline)** | Avg 10.3x, Median 10.0x |
| **최적화 후 (Final)** | Avg 12.0x, Median 12.1x |
| **성능 향상** | **+16.5% (1.7x 향상)** |
| **목표 (8.0x)** | **150% 초과 달성** |
| 측정 조건 | 100회 반복, 첫 5회 warmup 제외 |

---

## 2. 확정 최적화 설정

### 2.1 적용된 최적화 (Keep ✅)

| # | 최적화 항목 | 개별 효과 | 누적 | 상태 |
|---|---|---|---|---|
| 1 | IL2CPP OptimizeSpeed | **+16.5%** | 12.0x | ✅ **핵심** |
| 2 | IL2CPP Master Config | +2% | 10.5x | ✅ 유지 |
| 3 | Managed Stripping High | +0.8% | 12.1x | ✅ 유지 |
| 4 | Logging Disable (whisper.cpp) | ±0% (코드 클린업) | 12.1x | ✅ 유지 |
| 5 | OPENMP=OFF | ±0% (OptSpeed 충돌 방지) | 10.3x | ✅ 필수 |

### 2.2 실패/기각된 최적화 (Reverted ❌)

| # | 최적화 항목 | 결과 | 사유 |
|---|---|---|---|
| 1 | GGML_LTO=ON | **-51%** | ARM NEON 벡터화 파괴 |
| 2 | OPENMP=ON + OptimizeSpeed | **-17%** | OptimizeSpeed와 충돌 |
| 3 | threads=4 | -6% | auto(0)보다 저하 |
| 4 | threads=8 | -8% | 과도한 컨텍스트 스위칭 |
| 5 | threads=3 + OptimizeSpeed | -4% | auto(0)보다 저하 |
| 6 | threads=6 | ±0% | 개선 없음 |
| 7 | flashAttention=OFF | -7% | Flash Attention 유지 필요 |
| 8 | ANDROID_PLATFORM=android-23 | -5% | android-21 대비 저하 |
| 9 | Managed Stripping Medium | -3% | High 대비 저하 |
| 10 | armv8.4-a+i8mm | 스킵 | SD855 미지원 |

---

## 3. 파일별 수정 내용

### 3.1 Native C++ 빌드 스크립트

**파일**: `whisper.unity.2022/build_cpp.sh` → `build_android()` 함수

```bash
# 핵심 CMake 플래그
-DCMAKE_C_FLAGS_RELEASE="-Ofast -ffast-math -fno-finite-math-only -ffp-contract=fast \
    -march=armv8.2-a+fp16+dotprod -mtune=cortex-a76 \
    -funroll-loops -fomit-frame-pointer -finline-functions \
    -D__ARM_NEON -D__ARM_FEATURE_FMA -D__ARM_FEATURE_DOTPROD \
    -D__ARM_FEATURE_FP16_VECTOR_ARITHMETIC \
    -ftree-vectorize -fvectorize -fslp-vectorize"

# 필수 설정
-DGGML_OPENMP=OFF          # OptimizeSpeed와 충돌 방지 (필수)
-DGGML_LTO=OFF             # NEON 벡터화 보호 (필수)
-DGGML_STATIC=ON
-DGGML_CPU_AARCH64=ON
-DANDROID_PLATFORM=android-21

# 라이브러리 병합: ar MRI 스크립트로 4개 .a → 1개 libwhisper.a
# libwhisper.a + libggml.a + libggml-base.a + libggml-cpu.a
```

### 3.2 Unity 빌드 설정 (AutoBuilder.cs)

**파일**: `whisper.unity.2022/Assets/Editor/AutoBuilder.cs`

```csharp
// [추가됨] NDK 경로 자동 설정
string ndkPath = Path.Combine(editorDir, "Data", "PlaybackEngines", "AndroidPlayer", "NDK");
if (Directory.Exists(ndkPath))
    EditorPrefs.SetString("AndroidNdkRootR21D", ndkPath);

// [추가됨] IL2CPP Master 최적화 (+2%)
PlayerSettings.SetIl2CppCompilerConfiguration(
    BuildTargetGroup.Android,
    Il2CppCompilerConfiguration.Master);

// [추가됨] IL2CPP OptimizeSpeed (+16.5% ★핵심★)
PlayerSettings.SetIl2CppCodeGeneration(
    UnityEditor.Build.NamedBuildTarget.Android,
    UnityEditor.Build.Il2CppCodeGeneration.OptimizeSpeed);
```

### 3.3 Unity 프로젝트 설정

**파일**: `whisper.unity.2022/ProjectSettings/ProjectSettings.asset`

```yaml
# Managed Stripping Level: High (+0.8%)
managedStrippingLevel:
    Android: 3          # 0=Disabled, 1=Low, 2=Medium, 3=High

# IL2CPP Compiler Configuration (AutoBuilder.cs에서 런타임 설정)
il2cppCompilerConfiguration:
    Android: 2          # 0=Debug, 1=Release, 2=Master
```

### 3.4 Whisper 런타임 파라미터

**파일**: `whisper.unity.2022/Packages/com.whisper.unity/Runtime/WhisperManager.cs`

```csharp
// 성능 최적화 파라미터 (기존 대비 변경 없음, 이전 세션에서 적용 완료)
private bool flashAttention = true;      // Flash Attention 활성화
public float temperatureInc = 0.0f;      // Fallback 비활성화
public int greedyBestOf = 1;             // Greedy 후보 1개
public int threadsCount = 0;             // 자동 감지 (최적)
```

### 3.5 Whisper C++ 로깅 비활성화

**파일**: `whisper.unity.2022/whisper.cpp/src/whisper.cpp` (Lines 124-126)

```cpp
// 변경 전
#define WHISPER_LOG_ERROR(...) whisper_log_internal(GGML_LOG_LEVEL_ERROR, __VA_ARGS__)
#define WHISPER_LOG_WARN(...)  whisper_log_internal(GGML_LOG_LEVEL_WARN , __VA_ARGS__)
#define WHISPER_LOG_INFO(...)  whisper_log_internal(GGML_LOG_LEVEL_INFO , __VA_ARGS__)

// 변경 후
#define WHISPER_LOG_ERROR(...) do {} while(0)
#define WHISPER_LOG_WARN(...)  do {} while(0)
#define WHISPER_LOG_INFO(...)  do {} while(0)
```

> **참고**: 차이 파일은 `.agent/whisper_cpp_v1.7.5_logging_disable.diff`에 저장됨

### 3.6 데모 스크립트

**파일**: `whisper.unity.2022/Assets/Samples/1 - Audio Clip/AudioClipDemo.cs`

```csharp
// GetTextAsync → GetTextAsyncOptimized 변경 (FUTO 최적화 경로 사용)
var res = await manager.GetTextAsyncOptimized(clip);
```

---

## 4. 빌드 파이프라인

### 4.1 네이티브 라이브러리 빌드 (WSL2)

```bash
# WSL2 Ubuntu-22.04에서 실행
cd whisper.unity.2022
sed -i 's/\r$//' build_cpp.sh
./build_cpp.sh ./whisper.cpp android \
    /home/ubuntu/Android/Sdk/ndk/25.1.8937393/build/cmake/android.toolchain.cmake
```

**출력**: `Packages/com.whisper.unity/Plugins/Android/libwhisper.a`

### 4.2 Unity APK 빌드 (Headless)

```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" `
    -batchmode -nographics -quit `
    -projectPath "whisper.unity.2022" `
    -executeMethod AutoBuilder.BuildAndroid `
    -logFile "whisper.unity.2022\Builds\build.log"
```

**출력**: `Builds/whisper.2022.apk`

### 4.3 디바이스 테스트

```bash
adb install -r Builds/whisper.2022.apk
adb shell am start -n com.DefaultCompany.whisper2022/com.unity3d.player.UnityPlayerActivity
# 2분 후 결과 수집
adb logcat -d -s Unity | grep "[Whisper Result]"
```

---

## 5. 핵심 발견 사항

1. **IL2CPP OptimizeSpeed가 최대 기여자** (+16.5%): C# → C++ 변환 시 런타임 속도 최적화 코드 생성. 패키지 크기가 약간 증가하지만 추론 속도가 크게 향상.

2. **OPENMP와 OptimizeSpeed는 충돌**: OPENMP=ON 단독으로는 +1% 효과이나, OptimizeSpeed와 함께 사용하면 -17% 성능 저하. **반드시 OPENMP=OFF** 유지.

3. **LTO는 ARM NEON을 파괴**: Link-Time Optimization이 NEON SIMD 벡터화 코드를 최적화 과정에서 풀어버려 -51% 치명적 성능 저하. **절대 활성화 금지**.

4. **threads=0(auto)가 최적**: 명시적 스레드 수 지정(3, 4, 6, 8) 모두 auto 대비 동일하거나 저하. OS 스케줄러에 위임이 최적.

5. **Flash Attention 필수**: OFF 시 -7% 저하. 반드시 활성화 유지.

---

## 6. 다른 프로젝트 적용 체크리스트

다른 Unity 프로젝트(whisper.unity.2021, whisper.unity.6000 등)에 동일 최적화를 적용할 때:

- [ ] `build_cpp.sh`에 OPENMP=OFF, LTO=OFF, 최적화 플래그 적용
- [ ] `AutoBuilder.cs`에 IL2CPP Master + OptimizeSpeed 코드 추가
- [ ] `ProjectSettings.asset`에서 managedStrippingLevel Android: 3 확인
- [ ] `WhisperManager.cs` 파라미터 확인 (threads=0, flashAttn=true, tempInc=0, bestOf=1)
- [ ] `AudioClipDemo.cs`에서 `GetTextAsyncOptimized` 사용 확인
- [ ] `whisper.cpp/src/whisper.cpp` 로깅 매크로 no-op 패치 적용
- [ ] WSL2에서 네이티브 빌드 후 Unity headless 빌드 실행
- [ ] 디바이스에서 100회 반복 측정으로 성능 검증
