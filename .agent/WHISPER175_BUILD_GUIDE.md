# whisper.cpp v1.7.5 최적화 빌드 가이드

## 개요

이 문서는 whisper.cpp v1.7.5를 Android에서 최적화하여 빌드하는 방법과 
성능 최적화 기능 사용법을 설명합니다.

## 빌드 결과물

### ARM64 (arm64-v8a)
| 파일 | 크기 |
|------|------|
| libwhisper.a | 9.0 MB |
| libggml-base.a | 6.9 MB |
| libggml-cpu.a | 4.2 MB |
| libggml.a | 756 KB |
| **합계** | **~20.9 MB** |

### ARM32 (armeabi-v7a)
| 파일 | 크기 |
|------|------|
| libwhisper.a | 5.8 MB |
| libggml-base.a | 4.2 MB |
| libggml-cpu.a | 2.6 MB |
| libggml.a | 485 KB |
| **합계** | **~13.1 MB** |

## 빌드 방법

### Windows

```batch
build_cpp_whisper175.bat <NDK_PATH> [arm64|arm32|all]

# 예시
build_cpp_whisper175.bat C:\Users\user\AppData\Local\Android\Sdk\ndk\27.0.12077973 arm64
build_cpp_whisper175.bat %ANDROID_NDK% all
```

### Linux/macOS

```bash
./build_cpp_whisper175.sh $ANDROID_NDK/build/cmake/android.toolchain.cmake [arm64|arm32|all]
```

## 적용된 최적화

### 컴파일러 플래그
```
-O3                     # 최고 수준 최적화
-ffast-math             # 빠른 수학 연산
-fno-finite-math-only   # NaN/Inf 처리 유지 (ggml 호환)
-ffp-contract=fast      # 부동소수점 축약 최적화
-fvisibility=hidden     # 심볼 가시성 최소화
-ffunction-sections     # 함수별 섹션 분리
-fdata-sections         # 데이터별 섹션 분리
```

### ARM64 전용
```
-march=armv8.2-a+fp16+dotprod  # FP16 + DotProduct 지원
```

### ARM32 전용
```
-mfpu=neon-vfpv4        # NEON VFPv4 지원
-mfloat-abi=softfp      # 소프트 FP ABI
```

### CMake 옵션
```cmake
-DGGML_STATIC=ON             # 정적 라이브러리
-DGGML_CPU=ON                # CPU 백엔드 사용
-DGGML_CPU_AARCH64=ON        # ARM64 최적화 (arm64만)
-DGGML_CUDA=OFF              # GPU 백엔드 비활성화
-DGGML_METAL=OFF
-DGGML_VULKAN=OFF
-DGGML_OPENCL=OFF
-DWHISPER_BUILD_TESTS=OFF    # 테스트 비활성화
-DWHISPER_BUILD_EXAMPLES=OFF # 예제 비활성화
```

## 런타임 최적화 사용법

### 1. Flash Attention 활성화

Flash Attention은 메모리 효율적인 어텐션 연산으로 ~15% 속도 향상을 제공합니다.

```csharp
// C# (Unity)
var cparams = WhisperContextParams.Default;
cparams.flash_attn = true;  // Flash Attention 활성화

var ctx = WhisperWrapper.InitFromFile(modelPath, cparams);
```

### 2. 동적 Audio Context

오디오 길이에 따라 audio_ctx를 동적으로 조절하면 짧은 오디오에서 
2-6배 빠른 추론이 가능합니다.

```csharp
// C# (Unity)
var fparams = WhisperFullParams.Default;

// audio_ctx 계산: 30초 기준 1500
// 오디오 길이에 비례하여 설정
float audioLengthSeconds = samples.Length / 16000f;
int audioCtx = (int)(audioLengthSeconds / 30f * 1500);
audioCtx = Math.Max(audioCtx, 64);  // 최소값 보장

fparams.audio_ctx = audioCtx;
```

### 계산 공식

```
audio_ctx = (오디오_길이_초 / 30) × 1500

예시:
- 5초 오디오 → audio_ctx = 250
- 10초 오디오 → audio_ctx = 500
- 15초 오디오 → audio_ctx = 750
```

⚠️ **주의**: 동적 audio_ctx는 표준 모델에서 품질 저하 가능.
FUTO의 acft 파인튜닝 모델 사용 시 최적 결과.

## 예상 성능 향상

| 최적화 | 예상 향상 | 비고 |
|--------|----------|------|
| O3 + ffast-math | 15-25% | 기본 컴파일러 최적화 |
| Flash Attention | 30-40% | flash_attn=true |
| 동적 audio_ctx | 2-6x | 짧은 오디오에서 효과적 |

## FUTO ggml vs whisper.cpp v1.7.5 비교

| 항목 | FUTO ggml | whisper.cpp v1.7.5 |
|------|-----------|---------------------|
| 아키텍처 | 모노리식 | 모듈러 |
| 백엔드 호출 | 직접 호출 | 스케줄러 경유 |
| Flash Attention | 미지원 | **지원** |
| 동적 audio_ctx | 지원 | 지원 |
| 유지보수성 | 낮음 (포크) | 높음 (업스트림) |
| 라이브러리 크기 | ~8.2 MB (arm64) | ~20.9 MB (arm64) |

## C# 바인딩 변경사항

whisper.cpp v1.7.5는 다음 API 변경이 있습니다:

```csharp
// 이전 (whisper.unity 기존)
bool suppress_non_speech_tokens;

// v1.7.5
bool suppress_nst;  // 이름 변경됨
```

## 파일 구조

```
whisper.unity/
├── build_cpp_whisper175.bat    # Windows 빌드 스크립트
├── build_cpp_whisper175.sh     # Unix 빌드 스크립트
├── Packages/com.whisper.unity/Plugins/
│   ├── Android-whisper175/     # v1.7.5 최적화 빌드
│   │   ├── arm64-v8a/
│   │   │   ├── libwhisper.a
│   │   │   ├── libggml.a
│   │   │   ├── libggml-base.a
│   │   │   └── libggml-cpu.a
│   │   └── armeabi-v7a/
│   │       └── ... (동일)
│   └── Android-FUTO/           # FUTO 포팅 빌드 (이전)
└── ggml-futo/                  # FUTO 소스 (보존)
```

## 주의사항

1. **라이브러리 링크 순서**: Unity에서 여러 .a 파일 사용 시 링크 순서 주의
   ```
   libwhisper.a → libggml-cpu.a → libggml-base.a → libggml.a
   ```

2. **LTO 비활성화**: Unity 링커 호환성을 위해 LTO는 비활성화됨

3. **예외 처리**: C++ 코드에서 예외 사용됨 (whisper.cpp 요구사항)

## 버전 정보

- whisper.cpp: v1.7.5
- NDK: 27.0.12077973
- 빌드 날짜: 2026-02-04
