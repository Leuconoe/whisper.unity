# FUTO Voice Input ggml 포팅 보고서

## 1. 개요

### 1.1 목적
- **whisper.unity**의 Android 음성 전사 성능을 **FUTO Voice Input** 수준으로 개선
- FUTO의 최적화된 ggml 구현을 whisper.unity에 적용

### 1.2 성능 결과 (jfk.wav - 영어)
| 구분 | 전사 시간 | Real-Time Factor |
|------|----------|------------------|
| 원본 (whisper.unity) | 18,844 ms | 0.6x |
| FUTO 포팅 후 | 3,340 ms | 3.3x |
| **개선율** | **5.6배 빠름** | - |

### 1.3 성능 결과 (마이크 - 한국어)

**테스트 조건**: "2025년 3월 5일 전시 상황 보고" 발화, VAD Stop, Language Auto

| 구분 | 전사 시간 | RTF | CPU | 메모리 | 전사 결과 | 비고 |
|------|----------|-----|-----|--------|----------|------|
| **FUTO tiny** | 398 ms | 11.1x | 30% | 4.5% | 2015년 3월 5일 전시 사왕 보고 | 한국어 정확도 낮음 |
| FUTO tiny (2회) | 365 ms | 11.2x | 30% | 4.5% | 2015년 3월 5일 전시 상황 보고 | |
| **FUTO base** | 497 ms | 8.6x | 60% | 7.5% | 2025년 3월 5일 전신 상황 보고 | **권장 모델** |
| FUTO base (2회) | 499 ms | 9.3x | 60% | 7.5% | 2025년 3월 5일 전시 상황 보고 | |
| **원본 base** | 18,680 ms | 0.2x | 400% | 7.5% | 2025년 3월 5일 전시상황 보고 | 미튜닝 버전 |
| 원본 base (2회) | 17,840 ms | 0.3x | 400% | 7.5% | 2025년 3월 5일 전시상황 보고 | |
| **FUTO acft** | 529 ms | 8.0x | - | - | 15年3月5일全世界上網撲 | 동적 audio_ctx 작업중 |
| FUTO acft (2회) | 565 ms | 9.1x | - | - | 2025년 3월 5일 전시상황 보고 | |

**FUTO base vs 원본 base 비교**
| 항목 | FUTO base | 원본 base | 개선율 |
|------|-----------|-----------|--------|
| 평균 전사 시간 | 498 ms | 18,260 ms | **36.7배 빠름** |
| CPU 사용률 | 60% | 400% | **6.7배 낮음** |
| 메모리 사용률 | 7.5% | 7.5% | 동일 |

## 2. 프로젝트 구조

### 2.1 참조 프로젝트
```
voice-input/                          # FUTO Voice Input (Android 앱)
├── app/src/main/cpp/
│   ├── ggml/                         # ← FUTO의 최적화된 ggml (참조 소스)
│   │   ├── ggml.c                    # 633 KB - Monolithic 구현
│   │   ├── ggml.h
│   │   ├── ggml-backend.c            # 787 라인 (단순 backend)
│   │   ├── ggml-backend.h
│   │   ├── ggml-alloc.c/h
│   │   ├── ggml-quants.c/h
│   │   ├── whisper.cpp               # 5,331 라인
│   │   └── whisper.h
│   ├── defines.h                     # Android 로깅 매크로
│   └── CMakeLists.txt
```

### 2.2 대상 프로젝트
```
whisper.unity/                        # Unity 프로젝트
├── Packages/com.whisper.unity/
│   ├── Plugins/
│   │   ├── Android/                  # 기존 라이브러리 (비활성화)
│   │   │   ├── libwhisper.a          # 8.3 MB
│   │   │   ├── libggml.a             # 0.6 MB
│   │   │   ├── libggml-base.a        # 6.3 MB
│   │   │   └── libggml-cpu.a         # 3.9 MB
│   │   └── Android-FUTO/             # ← 새로 추가 (FUTO 라이브러리)
│   │       ├── arm64-v8a/
│   │       │   └── libwhisper.a      # 8.2 MB (단일 파일)
│   │       └── armeabi-v7a/
│   │           └── libwhisper.a      # 8.3 MB
│   └── Runtime/Native/
│       └── WhisperNativeParams.cs    # C# P/Invoke 바인딩
├── ggml-futo/                        # ← 새로 생성 (FUTO 소스 + 패치)
│   ├── CMakeLists.txt                # 빌드 설정
│   ├── defines.h                     # 로깅 매크로
│   ├── ggml.c/h                      # FUTO 원본
│   ├── ggml-backend.c/h              # FUTO 원본
│   ├── ggml-alloc.c/h                # FUTO 원본
│   ├── ggml-quants.c/h               # FUTO 원본
│   ├── whisper.cpp                   # 패치됨 (5,298 라인)
│   └── whisper.h                     # 패치됨
```

---

## 3. 성능 개선 핵심 원인

### 3.1 Backend 아키텍처 차이

#### 원본 whisper.unity (최신 whisper.cpp)
```
┌─────────────────────────────────────────────────────┐
│  whisper_full()                                     │
│    └── ggml_backend_sched_graph_compute()           │
│          ├── Tensor 분할 (split)                     │
│          ├── Backend 선택 로직                       │
│          ├── 메모리 이동/복사                        │
│          └── ggml_backend_graph_compute()           │
└─────────────────────────────────────────────────────┘
```

#### FUTO (구버전 whisper.cpp)
```
┌─────────────────────────────────────────────────────┐
│  whisper_full()                                     │
│    └── ggml_graph_compute_helper()                  │
│          └── ggml_backend_graph_compute() ← 직접 호출│
└─────────────────────────────────────────────────────┘
```

### 3.2 주요 차이점 비교

| 항목 | FUTO (구버전) | whisper.unity (최신) |
|------|--------------|---------------------|
| **ggml-backend.c** | 787 라인 | ~1,672 라인 |
| **스케줄러** | 없음 (직접 호출) | ggml_backend_sched |
| **Backend 추상화** | 최소 | 복잡 (multi-backend) |
| **연산자 수** | 77개 | 87개 |
| **라이브러리 구조** | 단일 파일 | 모듈화 (4개 파일) |
| **라이브러리 크기** | ~8 MB | ~19 MB |

### 3.3 성능 저하 원인 (최신 버전)
1. **스케줄러 오버헤드**: 매 그래프 실행마다 텐서 분할, backend 선택 로직 수행
2. **추가 메모리 복사**: multi-backend 지원을 위한 버퍼 관리
3. **함수 호출 체인**: 추상화 레이어가 많아 호출 깊이 증가
4. **불필요한 검사**: 단일 CPU backend에서도 multi-backend 로직 실행

---

## 4. 변경 작업 상세

### 4.1 파일 복사 (FUTO → ggml-futo)

다음 파일들을 `voice-input/app/src/main/cpp/ggml/`에서 `whisper.unity/ggml-futo/`로 복사:

| 파일명 | 크기 | 설명 |
|--------|------|------|
| `ggml.c` | 634 KB | 핵심 텐서 연산 (Monolithic) |
| `ggml.h` | 74 KB | 헤더 |
| `ggml-backend.c` | 37 KB | 단순 backend 구현 |
| `ggml-backend.h` | 6 KB | 헤더 |
| `ggml-backend-impl.h` | 3 KB | 내부 구현 헤더 |
| `ggml-impl.h` | 8 KB | 내부 구현 헤더 |
| `ggml-alloc.c` | 28 KB | 메모리 할당 |
| `ggml-alloc.h` | 4 KB | 헤더 |
| `ggml-quants.c` | 292 KB | 양자화 구현 |
| `ggml-quants.h` | 10 KB | 헤더 |
| `unicode.h` | 48 KB | 유니코드 처리 |
| `whisper.cpp` | 239 KB | Whisper 모델 구현 |
| `whisper.h` | 28 KB | Whisper API 헤더 |

추가 복사:
- `voice-input/app/src/main/cpp/defines.h` → `ggml-futo/defines.h`

### 4.2 헤더 경로 수정

#### ggml.h (라인 210)
```cpp
// 변경 전
#include "../defines.h"

// 변경 후
#include "defines.h"
```

#### whisper.cpp (라인 상단)
```cpp
// 변경 전
#include "../defines.h"

// 변경 후
#include "defines.h"
```

### 4.3 whisper.h 구조체 패치

Unity C# 바인딩(`WhisperNativeParams.cs`)과 일치시키기 위해 `whisper_full_params` 구조체 수정:

#### 제거된 필드 (FUTO 전용 기능)
```cpp
// 삭제됨
bool speed_up;                           // Phase Vocoder 속도 향상
const int * allowed_langs;               // 허용 언어 목록
size_t allowed_langs_size;               // 허용 언어 수
whisper_partial_text_callback partial_text_callback;
void * partial_text_callback_user_data;
```

#### 추가된 필드 (최신 API 호환)
```cpp
// 추가됨
const char * suppress_regex;             // 정규식 토큰 억제
```

#### 변경된 필드명
```cpp
// 변경 전 (FUTO)
bool suppress_non_speech_tokens;

// 변경 후 (whisper.unity 호환)
bool suppress_nst;
```

### 4.4 whisper.cpp 패치

#### 함수 시그니처 수정
```cpp
// 변경 전
int whisper_lang_auto_detect_with_state(
    struct whisper_context * ctx,
    struct whisper_state * state,
    int offset_ms, int n_threads,
    float * lang_probs,
    const int * allowed_langs,      // 제거
    size_t allowed_langs_size);     // 제거

// 변경 후
int whisper_lang_auto_detect_with_state(
    struct whisper_context * ctx,
    struct whisper_state * state,
    int offset_ms, int n_threads,
    float * lang_probs);
```

#### speed_up 관련 코드 제거
```cpp
// 제거된 조건문 예시
if (params.speed_up) {
    // Phase Vocoder 로직 제거
}

// 타임스탬프 계산 단순화
// 변경 전: speed_up ? 2*t : t
// 변경 후: t
```

#### partial_text_callback 제거
```cpp
// 제거된 콜백 호출
if (params.partial_text_callback) {
    params.partial_text_callback(ctx, state, ...);
}
```

#### 기본값 초기화 수정
```cpp
// whisper_full_default_params() 내부
// 제거됨
/*.speed_up                 =*/ false,
/*.allowed_langs            =*/ nullptr,
/*.allowed_langs_size       =*/ 0,

// 추가됨
/*.suppress_regex           =*/ nullptr,

// 변경됨
/*.suppress_nst             =*/ false,  // 이전: suppress_non_speech_tokens
```

### 4.5 CMakeLists.txt 작성

```cmake
# 핵심 설정
cmake_minimum_required(VERSION 3.14)
project(whisper-futo C CXX)

# 소스 파일
set(GGML_FUTO_SOURCES
    ggml.c
    ggml-alloc.c
    ggml-backend.c
    ggml-quants.c
    whisper.cpp
)

# ARM64 최적화
if(CMAKE_ANDROID_ARCH_ABI STREQUAL "arm64-v8a")
    target_compile_options(whisper-futo PRIVATE
        -O3
        -march=armv8.2-a+fp16+dotprod
        -mtune=cortex-a76
        -fvisibility=hidden
        -ffunction-sections
        -fdata-sections
        -fno-exceptions
        -fno-rtti
    )
    target_compile_definitions(whisper-futo PRIVATE
        __ARM_NEON
        __ARM_FEATURE_FMA
        __ARM_FEATURE_DOTPROD
        __ARM_FEATURE_FP16_VECTOR_ARITHMETIC
    )
endif()
```

### 4.6 Unity 플러그인 메타파일 설정

#### Android-FUTO/arm64-v8a/libwhisper.a.meta
```yaml
platformData:
  - first:
      Android: Android
    second:
      enabled: 1
      settings:
        AndroidSharedLibraryType: Executable
        CPU: ARM64
```

#### 기존 플러그인 비활성화
`Plugins/Android/` 폴더의 모든 `.a.meta` 파일에서:
```yaml
# 변경 전
Exclude Android: 0
enabled: 1

# 변경 후
Exclude Android: 1
enabled: 0
```

---

## 5. 빌드 방법

### 5.1 요구사항
- Android NDK (Unity 프로젝트와 동일 버전 권장)
- CMake 3.14+
- Ninja 빌드 시스템

### 5.2 빌드 스크립트 (PowerShell)

```powershell
# 경로 설정
$ndk = "C:\Users\user\AppData\Local\Android\Sdk\ndk\27.0.12077973\build\cmake\android.toolchain.cmake"
$src = "d:\workspace\whisper.unity\ggml-futo"
$plugin = "d:\workspace\whisper.unity\Packages\com.whisper.unity\Plugins\Android-FUTO"

# ARM64 빌드
$build64 = "d:\workspace\whisper.unity\build-futo-test"
Remove-Item $build64 -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $build64 | Out-Null
Set-Location $build64
cmake -G "Ninja" `
    -DCMAKE_TOOLCHAIN_FILE="$ndk" `
    -DANDROID_ABI=arm64-v8a `
    -DANDROID_PLATFORM=android-24 `
    -DCMAKE_BUILD_TYPE=Release `
    -DGGML_FUTO_LTO=OFF `
    "$src"
cmake --build . --config Release -j 8

# ARM32 빌드
$build32 = "d:\workspace\whisper.unity\build-futo-arm32"
Remove-Item $build32 -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $build32 | Out-Null
Set-Location $build32
cmake -G "Ninja" `
    -DCMAKE_TOOLCHAIN_FILE="$ndk" `
    -DANDROID_ABI=armeabi-v7a `
    -DANDROID_PLATFORM=android-24 `
    -DCMAKE_BUILD_TYPE=Release `
    -DGGML_FUTO_LTO=OFF `
    "$src"
cmake --build . --config Release -j 8

# 플러그인 복사
New-Item -ItemType Directory -Path "$plugin\arm64-v8a" -Force | Out-Null
New-Item -ItemType Directory -Path "$plugin\armeabi-v7a" -Force | Out-Null
Copy-Item "$build64\lib\libwhisper-futo.a" "$plugin\arm64-v8a\libwhisper.a" -Force
Copy-Item "$build32\lib\libwhisper-futo.a" "$plugin\armeabi-v7a\libwhisper.a" -Force
```

### 5.3 주의사항
- **LTO 비활성화 필수**: `-DGGML_FUTO_LTO=OFF`
  - LTO 활성화 시 Unity 링커가 비트코드 객체를 인식하지 못함
- **NDK 버전 일치**: Unity 프로젝트와 동일한 NDK 버전 사용 권장
  - Unity 2021.3: NDK r21d
  - Unity 6000+: NDK r27

---

## 6. 파일 변경 요약

### 6.1 새로 생성된 파일
| 파일 경로 | 설명 |
|----------|------|
| `ggml-futo/` | FUTO ggml 소스 폴더 |
| `ggml-futo/CMakeLists.txt` | 빌드 설정 |
| `ggml-futo/defines.h` | 로깅 매크로 |
| `Plugins/Android-FUTO/` | FUTO 플러그인 폴더 |
| `Plugins/Android-FUTO/arm64-v8a/libwhisper.a` | ARM64 라이브러리 |
| `Plugins/Android-FUTO/armeabi-v7a/libwhisper.a` | ARM32 라이브러리 |
| `Assets/Samples/0 - Benchmark/WhisperBenchmark.cs` | 벤치마크 스크립트 |

### 6.2 수정된 파일
| 파일 경로 | 변경 내용 |
|----------|----------|
| `ggml-futo/ggml.h` | `#include` 경로 수정 |
| `ggml-futo/whisper.h` | 구조체 레이아웃 패치 |
| `ggml-futo/whisper.cpp` | API 호환성 패치 |
| `ggml-futo/ggml-backend.c` | printf 포맷 수정 (%lu → %zu) |
| `Plugins/Android/*.meta` | Android 비활성화 |
| `Plugins/Android-FUTO/*/*.meta` | CPU 타입, 설정 추가 |

---

## 7. 향후 고려사항

### 7.1 유지보수
- FUTO Voice Input 업데이트 시 ggml 파일 동기화 필요
- whisper.unity C# 바인딩 변경 시 whisper.h 패치 재검토

### 7.2 추가 최적화 가능성
- `audio_ctx` 동적 계산 (FUTO 방식)
- `temperature_inc = 0` 설정 (fallback 비활성화)
- `greedy.best_of = 1` 설정 (단일 샘플링)

### 7.3 플랫폼 확장
- iOS: 동일한 방식으로 포팅 가능 (Metal backend 유지)
- Windows/Linux: 데스크톱 빌드 지원 가능

---

## 8. 참고 자료

### 8.1 원본 저장소
- FUTO Voice Input: https://github.com/futo-org/voice-input
- whisper.cpp: https://github.com/ggerganov/whisper.cpp
- whisper.unity: https://github.com/Macoron/whisper.unity

### 8.2 관련 이슈
- ggml backend scheduler overhead
- whisper.cpp mobile optimization

---

**작성일**: 2026-02-04
**작성자**: AI Assistant (GitHub Copilot)
