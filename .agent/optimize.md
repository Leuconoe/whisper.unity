# whisper.unity.2022 — 반복 최적화 지시서

> **전제 조건**: `setup.md`의 모든 단계 완료 후 사용  
> **목표**: baseline 성능(setup.md 결과)에서 **추가 개선** 탐색  
> **Unity**: 2022.3.62f3 LTS (Unity 2021 대비 IL2CPP 링커 개선 → GGML_OPENMP/LTO 테스트 가능)  
> **whisper.cpp**: v1.7.5

---

## 작업 원칙

1. **한 번에 하나의 변수만 변경** — 복수 변경 시 영향 분리 불가
2. **매 변경마다 full cycle 실행** — 빌드 → APK → 설치 → 측정
3. **결과 기록 필수** — 하단 결과 추적 테이블에 기록
4. **개선 확인 시 유지, 악화 시 원복** — 이전 설정으로 복원 후 다음 항목 진행
5. **whisper.cpp 소스 수정 시 반드시 diff 생성** — 원본 대비 변경사항 추적

---

## 빌드-테스트 사이클

매 실험마다 아래 사이클을 반복:

### 1. 네이티브 빌드 (WSL2)

```powershell
wsl -d Ubuntu-22.04 -- bash -c "cd /mnt/d/workspace/___2025___LIGNex1-Drone/_AI/speech-and-text-unity-ios-android/voice-input/whisper.unity.2022 && ./build_cpp.sh ./whisper.cpp android /home/ubuntu/Android/Sdk/ndk/25.1.8937393/build/cmake/android.toolchain.cmake 2>&1"
```

### 2. Unity 헤드리스 빌드

```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" `
    -batchmode -nographics -quit `
    -projectPath "D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.2022" `
    -executeMethod AutoBuilder.BuildAndroid `
    -logFile "D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.2022\build.log"
```

**로그 모니터링:**
```powershell
Get-Content "D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.2022\build.log" -Tail 20
```

30초동안 build.log의 줄 수가 늘어나지 않는다면 빌드가 완료/종료 된것으로 판단하고 다음 작업 진행.

### 3. APK 설치 + 실행

```powershell
adb install -r "D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.2022\Builds\whisper.2022.apk"
adb shell am force-stop <패키지명>
adb shell am start -n <패키지명>/com.unity3d.player.UnityPlayerActivity
```

### 4. 성능 측정

```powershell
adb logcat -c
Start-Sleep -Seconds 25
adb logcat -d -s Unity 2>&1 | Select-String "\[Whisper Result\]" | Select-Object -Last 10
```

---

## 실험 항목

### 실험 A: GGML_OPENMP (Unity 2022 전용 테스트)

**배경**: Unity 2021에서는 정적 링킹 시 OpenMP 호환성 문제로 OFF 고정.  
Unity 2022.3의 IL2CPP 링커는 개선되어 OpenMP 정적 라이브러리 `libomp.a`를 링크할 수 있을 가능성 있음.

**build_cpp.sh 변경**:
```bash
# A-1: OPENMP=ON (기본 스레드 풀링 활성화)
-DGGML_OPENMP=ON

# A-2: OPENMP=OFF (baseline — 현재 설정)
-DGGML_OPENMP=OFF
```

**OPENMP=ON 빌드 실패 시 대응**:
- 링크 에러 `cannot find -lomp` → NDK에 OpenMP 정적 라이브러리 포함 확인:
  ```bash
  find /home/ubuntu/Android/Sdk/ndk/25.1.8937393 -name "libomp*"
  ```
- Unity IL2CPP 링크 에러 `undefined reference to 'omp_*'` → 병합 시 `libomp.a`도 포함:
  ```
  ADDLIB /path/to/libomp.a
  ```
- 그래도 실패 → OPENMP=OFF 유지, 다음 실험으로 진행

**기대 효과**: 멀티스레드 GGML 연산 개선. 스레드 수 ≥ 2일 때 5~15% 개선 가능성.

---

### 실험 B: GGML_LTO (Link Time Optimization)

**배경**: LTO는 컴파일 단위를 넘는 인라인/최적화 수행. Unity 2021에서 IL2CPP 링커와 충돌 가능성으로 OFF.  
Unity 2022에서 clang LTO 지원 개선되어 테스트 가치 있음.

**build_cpp.sh 변경**:
```bash
# B-1: LTO=ON (링크 시간 최적화 활성화)
-DGGML_LTO=ON

# B-2: LTO=OFF (baseline)
-DGGML_LTO=OFF
```

**LTO=ON 빌드 실패 시 대응**:
- `lto-wrapper: fatal error` → thin LTO 시도:
  ```bash
  -DCMAKE_C_FLAGS_RELEASE="... -flto=thin"
  -DCMAKE_CXX_FLAGS_RELEASE="... -flto=thin"
  ```
- Unity IL2CPP 빌드 실패 → LTO=OFF 유지

**기대 효과**: 3~8% 성능 개선 (크로스 모듈 인라인, dead code elimination 강화)

---

### 실험 C: GGML_OPENMP + GGML_LTO 동시 적용

**전제**: 실험 A, B 각각이 단독 성공한 경우에만 진행

```bash
-DGGML_OPENMP=ON \
-DGGML_LTO=ON \
```

---

### 실험 D: ANDROID_PLATFORM 수준

**배경**: `android-21`은 C 런타임 호환 목적 기본값. 더 높은 API 레벨이 코드 생성 품질에 영향 줄 수 있음.

```bash
# D-1: android-21 (baseline — stdout/stderr 안전)
-DANDROID_PLATFORM=android-21

# D-2: android-23
-DANDROID_PLATFORM=android-23

# D-3: android-24 (주의: stdout/stderr 링크 오류 가능)
-DANDROID_PLATFORM=android-24
```

**D-3에서 `undefined reference to 'stdout'` 발생 시**: android-24 이상 사용 불가 확정 → D-1 또는 D-2 유지

---

### 실험 E: Sampling Strategy (Greedy vs Beam Search)

**배경**: whisper.cpp의 `whisper_full_params`는 두 가지 디코딩 전략 지원:
- `WHISPER_SAMPLING_GREEDY` (기본값): 빠르지만 정확도 낮을 수 있음
- `WHISPER_SAMPLING_BEAM_SEARCH`: 더 정확하지만 느림

**수정 파일**: `WhisperManager.cs`의 `strategy` 필드

```csharp
// E-1: Greedy (baseline)
private WhisperSamplingStrategy strategy = WhisperSamplingStrategy.WHISPER_SAMPLING_GREEDY;

// E-2: Beam Search (beam_size=5 기본값)
private WhisperSamplingStrategy strategy = WhisperSamplingStrategy.WHISPER_SAMPLING_BEAM_SEARCH;
```

**Beam Search 세부 파라미터 조정** (beam_search 사용 시):

`WhisperNativeParams.cs`의 `beam_search_struct`를 public으로 변경하고 프로퍼티 추가:

```csharp
// WhisperNativeParams.cs — 기존 beam_search_struct을 그대로 두되 프로퍼티 추가
public int BeamSize
{
    get => beam_search.beam_size;
    set => beam_search.beam_size = value;
}
```

**⚠️ 구조체 필드 변경 금지**: `beam_search_struct`의 `beam_size`, `patience` 필드를 public으로 변경하거나 프로퍼티만 추가하는 것은 안전. 새 필드 추가/재배치는 절대 금지.

beam_search 테스트 값:

| 항목 | E-2a | E-2b | E-2c |
|------|------|------|------|
| beam_size | 2 | 3 | 5 (기본) |

**기대**: Beam search는 보통 2~5x 느림. 속도 목표 달성이 우선이므로 Greedy보다 느리면 Greedy 유지.

---

### 실험 F: Thread Count 최적화

**수정**: `WhisperManager.cs`의 `threadsCount` 값

```csharp
// F-1: 1 thread
public int threadsCount = 1;

// F-2: 2 threads
public int threadsCount = 2;

// F-3: 3 threads
public int threadsCount = 3;

// F-4: 4 threads (baseline)
public int threadsCount = 4;

// F-5: 6 threads
public int threadsCount = 6;

// F-6: 8 threads
public int threadsCount = 8;
```

**참고**: whisper.cpp에서 스레드 수가 물리 코어 수를 초과하면 성능 하락. 대부분의 모바일 SoC는 빅코어 2~4개 + 리틀코어 4개. 빅코어 수에 맞추는 것이 최적.

**이 실험은 GGML_OPENMP 결과에 따라 교차 테스트**:
- OPENMP=OFF + 4 threads → baseline
- OPENMP=ON + 4 threads → 비교

---

### 실험 G: Temperature / GreedyBestOf 파라미터

**수정**: `WhisperManager.cs`

```csharp
// G-1: temperatureInc=0.0, greedyBestOf=1 (baseline — 최소 연산)
public float temperatureInc = 0.0f;
public int greedyBestOf = 1;

// G-2: temperatureInc=0.2, greedyBestOf=1 (fallback 활성화, 후보 1)
public float temperatureInc = 0.2f;
public int greedyBestOf = 1;

// G-3: temperatureInc=0.0, greedyBestOf=2 (fallback 없음, 후보 2)
public float temperatureInc = 0.0f;
public int greedyBestOf = 2;

// G-4: temperatureInc=0.0, greedyBestOf=5 (fallback 없음, 후보 5 — whisper.cpp 기본값)
public float temperatureInc = 0.0f;
public int greedyBestOf = 5;

// G-5: temperatureInc=0.2, greedyBestOf=5 (whisper.cpp 기본값)
public float temperatureInc = 0.2f;
public int greedyBestOf = 5;
```

**whisper.cpp 기본값** (참조: `whisper_full_default_params()`):
- `temperature_inc`: 0.2f (0.2씩 온도 증가하며 재시도)
- `greedy.best_of`: 5 (5개 후보 중 최선 선택)
- `temperature`: 0.0f (초기 온도)

**temperatureInc 동작 원리**:
- `0.0` → 온도 fallback 비활성화. 첫 시도에서 실패해도 재시도 없음 → **가장 빠름**
- `0.2` → 실패 시 온도 0.2씩 증가하며 최대 4~5회 재시도 (1.0까지) → **정확도 높지만 느림**
- fallback은 `entropy_thold`, `logprob_thold` 기준으로 트리거됨

**greedyBestOf 동작 원리**:
- `1` → 디코더 1회 실행 → **가장 빠름**
- `5` → 5번 디코딩하여 최선 선택 → **~5배 느리지만 정확도 향상**

---

### 실험 H: audio_ctx 전략

**수정**: `AudioClipDemo.cs` 및 `WhisperManager.cs`

```csharp
// H-1: GetTextAsyncOptimized (자동 계산 — baseline)
var res = await manager.GetTextAsyncOptimized(clip);

// H-2: GetTextAsync (기본 audio_ctx=1500 사용)
var res = await manager.GetTextAsync(clip);

// H-3: 고정 audio_ctx 값 (WhisperManager의 audioCtx 필드)
public int audioCtx = 500;  // 테스트: 250, 500, 750, 1000
```

**참고**: `GetTextAsyncOptimized`는 오디오 길이에 비례하여 audio_ctx 계산. 30초 오디오에서는 효과 없음.

---

### 실험 I: Flash Attention

**수정**: `WhisperManager.cs`의 `flashAttention` 필드

```csharp
// I-1: flashAttention = true (baseline)
private bool flashAttention = true;

// I-2: flashAttention = false
private bool flashAttention = false;
```

**참고**: Flash Attention은 GPU 가속 시 효과적이지만, CPU 전용(Android)에서도 메모리 접근 패턴 최적화로 성능 향상 가능.
whisper.cpp v1.7.5에서 flash_attn은 `WhisperContextParams`에 포함됨 → 모델 초기화 시 적용.

---

### 실험 J: whisper.cpp 소스 수정 (고급)

**⚠️ 수정 시 반드시 diff 생성**:
```bash
cd whisper.unity.2022/whisper.cpp
git diff > ../whisper_cpp_modifications.patch
```

#### J-1: N_THREAD 하드코딩 최적화

`whisper.cpp/src/whisper.cpp`에서 스레드 관련 동적 할당을 정적으로 변경:

```cpp
// 수정 전
const int n_threads = std::min(params.n_threads, (int)std::thread::hardware_concurrency());

// 수정 후 (4 스레드 고정)
const int n_threads = 4;
```

#### J-2: 불필요한 로깅 제거

성능 임계 경로에서 로깅 코드 제거/비활성화:

```cpp
// WHISPER_LOG_* 매크로를 빈 구현으로 교체
#define WHISPER_LOG_INFO(...)
#define WHISPER_LOG_WARN(...)
```

#### J-3: 메모리 할당 최적화

반복적인 `malloc`/`free` 호출을 사전 할당 버퍼로 교체 (whisper_full 내부).

#### J-4: SIMD 강화

`ggml/src/ggml-cpu/` 내 연산 커널에서 ARM NEON intrinsics 직접 사용 여부 확인 및 최적화.

**diff 생성 절차**:
```bash
# 수정 전 원본 상태 확인
cd whisper.unity.2022/whisper.cpp
git status

# 수정 후 diff 생성
git diff > ../../.agent/whisper_cpp_v1.7.5_modifications.diff

# diff 내용 확인
cat ../../.agent/whisper_cpp_v1.7.5_modifications.diff
```

---

### 실험 K: -march 타겟 아키텍처

**build_cpp.sh의 -march/-mtune 변경**:

```bash
# K-1: armv8.2-a+fp16+dotprod (baseline)
-march=armv8.2-a+fp16+dotprod -mtune=cortex-a76

# K-2: armv8.4-a+fp16+dotprod (더 새로운 ISA)
-march=armv8.4-a+fp16+dotprod -mtune=cortex-a78

# K-3: armv8-a (보수적 — 호환성 최대)
-march=armv8-a -mtune=generic

# K-4: armv8.2-a+fp16+dotprod+i8mm (INT8 matmul 가속)
-march=armv8.2-a+fp16+dotprod+i8mm -mtune=cortex-a78
```

**주의**: 타겟 디바이스의 SoC가 해당 ISA를 지원하는지 확인 필요. 미지원 시 `SIGILL` (Illegal Instruction) 크래시.

---

## 결과 추적 테이블

매 실험 결과를 아래 테이블에 기록:

| 실험 | 변경 내용 | 빌드 성공 | 실행 성공 | 성능 (x real-time) | 대비 (%) | 유지 |
|------|-----------|-----------|-----------|-------------------|----------|------|
| Baseline | setup.md 완료 상태 | ✅ | ✅ | ?.?x | - | - |
| A-1 | OPENMP=ON | ? | ? | ?.?x | ?% | ? |
| A-2 | OPENMP=OFF | ✅ | ✅ | ?.?x | 0% | ✅ |
| B-1 | LTO=ON | ? | ? | ?.?x | ?% | ? |
| C-1 | OPENMP+LTO | ? | ? | ?.?x | ?% | ? |
| D-2 | android-23 | ? | ? | ?.?x | ?% | ? |
| E-1 | Greedy (baseline) | ✅ | ✅ | ?.?x | 0% | ? |
| E-2a | Beam size=2 | ? | ? | ?.?x | ?% | ? |
| F-1 | 1 thread | ? | ? | ?.?x | ?% | ? |
| F-2 | 2 threads | ? | ? | ?.?x | ?% | ? |
| F-4 | 4 threads | ✅ | ✅ | ?.?x | 0% | ? |
| F-5 | 6 threads | ? | ? | ?.?x | ?% | ? |
| G-1 | tempInc=0,bestOf=1 | ✅ | ✅ | ?.?x | 0% | ? |
| G-5 | tempInc=0.2,bestOf=5 | ? | ? | ?.?x | ?% | ? |
| H-1 | Optimized (auto ctx) | ✅ | ✅ | ?.?x | 0% | ? |
| H-2 | Default (ctx=1500) | ? | ? | ?.?x | ?% | ? |
| I-2 | flashAttn=false | ? | ? | ?.?x | ?% | ? |
| K-2 | armv8.4-a | ? | ? | ?.?x | ?% | ? |

---

## 추천 실험 순서

영향도가 큰 항목을 먼저 테스트:

1. **A: GGML_OPENMP** — Unity 2022 전용, 실패해도 빨리 판별 가능
2. **B: GGML_LTO** — 빌드 시간만 증가, 실행 성능 개선 가능
3. **C: OPENMP+LTO** — A, B 성공 시에만
4. **F: Thread Count** — 디바이스 특성에 따라 최적값 다름
5. **E: Beam Search** — 속도 대비 정확도 trade-off 판단
6. **G: Temperature/BestOf** — 소폭 개선 가능
7. **K: -march 타겟** — 빌드 레벨 미세 조정
8. **I: Flash Attention** — CPU 모드에서 효과 확인
9. **D: ANDROID_PLATFORM** — 미세 차이
10. **J: 소스 수정** — 가장 복잡, 마지막에 시도

---

## 주의사항 (다시 강조)

### ⚠️ WhisperNativeParams 구조체 레이아웃
- `[StructLayout(LayoutKind.Sequential)]`로 C++ `whisper_full_params`와 바이트 정렬
- **절대 금지**: 필드 추가/삭제/재배치
- **허용**: 기존 private 필드 접근용 **프로퍼티** 추가
- 위반 시: 추론 속도 0.1x 이하 또는 SIGABRT 크래시

### ⚠️ whisper.h (L476~L573) vs WhisperNativeParams.cs 대응 확인
whisper.cpp v1.7.5의 `whisper_full_params` 구조체 필드 순서:
```
strategy → n_threads → n_max_text_ctx → offset_ms → duration_ms →
translate → no_context → no_timestamps → single_segment → print_special →
print_progress → print_realtime → print_timestamps →
token_timestamps → thold_pt → thold_ptsum → max_len → split_on_word → max_tokens →
debug_mode → audio_ctx →
tdrz_enable → suppress_regex →
initial_prompt → prompt_tokens → prompt_n_tokens →
language → detect_language →
suppress_blank → suppress_nst →
temperature → max_initial_ts → length_penalty →
temperature_inc → entropy_thold → logprob_thold → no_speech_thold →
greedy { best_of } → beam_search { beam_size, patience } →
callbacks (new_segment, progress, encoder_begin, abort, logits_filter) →
grammar_rules → n_grammar_rules → i_start_rule → grammar_penalty
```

### ⚠️ 하나의 실험 실패가 다른 실험에 영향 주지 않도록
- 실패한 변경은 반드시 원복
- 빌드 실패 시 이전 성공 설정으로 복원 후 다음 실험 진행

---

## 실행 방식

**중단 요청 전까지 위 실험들을 순서대로 반복 실행하며 성능 개선을 탐색함.**
- 각 실험마다 빌드-테스트 사이클 전체 수행
- 결과 추적 테이블 업데이트
- 최상의 조합을 찾아 최종 설정으로 결정
- whisper.cpp 소스 수정 시 diff 파일 생성 및 보관
