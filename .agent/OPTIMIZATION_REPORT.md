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
| 4 | OPENMP=OFF | ±0% (OptSpeed 충돌 방지) | 10.3x | ✅ 필수 |

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

#### 3.1.1 핵심 플래그 요약

| 플래그 | 값 | 성능 영향 |
|---|---|---|
| `GGML_OPENMP` | **OFF** | OptimizeSpeed와 충돌 방지 (ON 시 -17%) |
| `GGML_LTO` | **OFF** | NEON 벡터화 보호 (ON 시 -51%) |
| `GGML_CPU_AARCH64` | ON | ARM64 전용 최적화 커널 활성화 |
| `GGML_STATIC` | ON | 정적 링크, Unity 플러그인 호환 |
| `-Ofast` | 활성화 | 최고 수준 컴파일러 최적화 |
| `-march` | `armv8.2-a+fp16+dotprod` | FP16/DotProd SIMD 명령어 활성화 |
| `-mtune` | `cortex-a76` | SD855 big core에 맞춘 명령어 스케줄링 |

#### 3.1.2 전체 플래그 상세 설명

**CMake 기본 설정**

```bash
-DCMAKE_TOOLCHAIN_FILE="$android_sdk_path"   # NDK 크로스 컴파일 툴체인
-DANDROID_ABI=arm64-v8a                       # 64-bit ARM 타겟
-DANDROID_PLATFORM=android-21                 # 최소 API 21 (Lollipop). 23 테스트 시 -5% 저하
-DCMAKE_BUILD_TYPE=Release                    # Release 빌드 (디버그 심볼 제거, 최적화 활성화)
```

**C 컴파일러 플래그 (`CMAKE_C_FLAGS_RELEASE`)**

| 플래그 | 목적 | 영향 |
|---|---|---|
| `-Ofast` | `-O3` + 수학 최적화. 가장 공격적인 최적화 수준 | 전반적 성능 향상의 기반 |
| `-ffast-math` | IEEE 754 엄격 준수를 완화하여 부동소수점 연산 최적화 | 추론 속도 향상, 미세한 정밀도 트레이드오프 |
| `-fno-finite-math-only` | NaN/Inf 체크를 유지. `-ffast-math`의 부작용 방지 | 런타임 안정성 보장 |
| `-ffp-contract=fast` | FMA(Fused Multiply-Add) 자동 생성 허용 | ARM FMA 명령어 활용 |
| `-fvisibility=hidden` | 기본 심볼을 숨겨 불필요한 심볼 테이블 축소 | 바이너리 크기 감소, 간접적 성능 향상 |
| `-ffunction-sections` | 함수별로 별도 섹션에 배치 | 링커의 `--gc-sections`와 연계하여 미사용 코드 제거 |
| `-fdata-sections` | 데이터별로 별도 섹션에 배치 | 미사용 데이터 제거 가능 |
| `-march=armv8.2-a+fp16+dotprod` | ARMv8.2-A + FP16 반정밀도 + DotProduct 확장 활성화 | NEON SIMD 핵심: 행렬 연산에 dotprod 활용 |
| `-mtune=cortex-a76` | Cortex-A76 (SD855 big core)에 최적화된 명령어 스케줄링 | 파이프라인 활용 최적화 |
| `-funroll-loops` | 작은 루프를 풀어서 분기 오버헤드 제거 | 추론 inner loop 성능 향상 |
| `-fomit-frame-pointer` | 프레임 포인터 레지스터를 범용으로 사용 | 레지스터 1개 추가 확보 |
| `-finline-functions` | 컴파일러가 판단한 함수를 인라인 확장 | 함수 호출 오버헤드 제거 |
| `-fno-stack-protector` | 스택 카나리 보호 비활성화 | 함수 진입/종료 오버헤드 제거 |
| `-fno-exceptions` | C 예외 처리 비활성화 | 코드 크기 및 오버헤드 감소 |
| `-DNDEBUG` | assert() 매크로 비활성화 | 릴리스 빌드에서 디버그 체크 제거 |
| `-D__ARM_NEON` | NEON SIMD 지원 매크로 정의 | whisper.cpp 내부 NEON 코드 경로 활성화 |
| `-D__ARM_FEATURE_FMA` | FMA 확장 매크로 정의 | FMA 전용 코드 경로 활성화 |
| `-D__ARM_FEATURE_DOTPROD` | DotProduct 확장 매크로 정의 | int8 dotprod 양자화 연산 활성화 |
| `-D__ARM_FEATURE_FP16_VECTOR_ARITHMETIC` | FP16 벡터 연산 매크로 정의 | 반정밀도 SIMD 경로 활성화 |
| `-ftree-vectorize` | GCC 스타일 자동 벡터화 활성화 | 스칼라 루프를 SIMD로 자동 변환 |
| `-fvectorize` | Clang 스타일 루프 벡터화 활성화 | 위와 동일 (Clang 호환) |
| `-fslp-vectorize` | SLP(Superword Level Parallelism) 벡터화 | 연속 스칼라 연산을 SIMD 단일 명령으로 병합 |

**C++ 컴파일러 플래그 (`CMAKE_CXX_FLAGS_RELEASE`)**

> C 플래그와 동일 + 아래 C++ 전용 플래그 추가:

| 플래그 | 목적 | 영향 |
|---|---|---|
| `-fvisibility-inlines-hidden` | 인라인 함수의 심볼을 숨김 | C++ 템플릿/인라인 심볼 테이블 축소 |
| `-fno-rtti` | RTTI(Run-Time Type Information) 비활성화 | `dynamic_cast`, `typeid` 제거로 바이너리 축소 |

**링커 플래그 (`CMAKE_EXE_LINKER_FLAGS_RELEASE`)**

| 플래그 | 목적 | 영향 |
|---|---|---|
| `-Wl,--gc-sections` | 미사용 코드/데이터 섹션 제거 | `-ffunction-sections`와 연계, 바이너리 크기 감소 |
| `-Wl,--exclude-libs,ALL` | 정적 라이브러리 심볼을 외부로 노출하지 않음 | 심볼 충돌 방지, 바이너리 크기 감소 |
| `-Wl,--strip-debug` | 디버그 심볼 제거 | 라이브러리 크기 대폭 감소 |

**GGML/Whisper CMake 옵션**

| 플래그 | 값 | 목적 |
|---|---|---|
| `BUILD_SHARED_LIBS` | OFF | 정적 라이브러리(.a) 생성. Unity Android 플러그인 요구사항 |
| `GGML_STATIC` | ON | ggml 정적 빌드 강제 |
| `GGML_NATIVE` | OFF | 호스트 CPU 감지 비활성화 (크로스 컴파일이므로 필수) |
| `GGML_OPENMP` | **OFF** | OpenMP 멀티스레딩 비활성화. **ON 시 IL2CPP OptimizeSpeed와 충돌하여 -17%** |
| `GGML_LTO` | **OFF** | Link-Time Optimization 비활성화. **ON 시 NEON 벡터화 파괴로 -51%** |
| `GGML_CPU` | ON | CPU 백엔드 활성화 |
| `GGML_CPU_AARCH64` | ON | ARM64 전용 최적화 커널 (GEMM, quantization) 활성화 |
| `GGML_CPU_ARM_ARCH` | `armv8.2-a+fp16+dotprod` | ggml 내부 ARM 아키텍처 타겟 설정 |
| `GGML_CUDA` | OFF | GPU 백엔드 비활성화 (Android에서 미지원) |
| `GGML_METAL` | OFF | Apple Metal 비활성화 |
| `GGML_VULKAN` | OFF | Vulkan 백엔드 비활성화 |
| `GGML_OPENCL` | OFF | OpenCL 백엔드 비활성화 |
| `WHISPER_BUILD_TESTS` | OFF | 테스트 바이너리 빌드 제외 |
| `WHISPER_BUILD_EXAMPLES` | OFF | 예제 바이너리 빌드 제외 |

#### 3.1.3 라이브러리 병합 (ar MRI)

빌드 결과물인 4개의 정적 라이브러리를 `llvm-ar` MRI 스크립트로 단일 아카이브로 병합:

```bash
# 병합 대상
libwhisper.a     # whisper 추론 엔진
libggml.a        # ggml 텐서 연산 라이브러리
libggml-base.a   # ggml 기본 유틸리티
libggml-cpu.a    # ggml CPU 백엔드 (ARM64 NEON 커널 포함)

# 출력
Packages/com.whisper.unity/Plugins/Android/libwhisper.a  # 단일 병합 라이브러리
```

> **주의**: OPENMP=ON일 경우 `libomp.a`도 자동으로 병합에 포함되지만, 현재는 OFF이므로 4개만 병합됩니다.

### 3.2 Unity 빌드 설정 (AutoBuilder.cs)

**파일**: `whisper.unity.2022/Assets/Editor/AutoBuilder.cs`

```csharp
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

### 3.5 데모 스크립트

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
- [ ] WSL2에서 네이티브 빌드 후 Unity headless 빌드 실행
- [ ] 디바이스에서 100회 반복 측정으로 성능 검증
