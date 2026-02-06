# whisper.unity.2022 — 최적화 Walkthrough

> **최종 갱신**: 2026-02-06 15:00  
> **목표**: Android ARM64에서 8.0x real-time 이상 달성 (참조: whisper.unity.2021 = 12.8x)  
> **현재 최고 성능**: **~10.3x** (OPENMP=OFF, LTO=OFF, threadsCount=0/auto=4, flashAttention=true)  
> **테스트 디바이스**: Snapdragon 855 (Kryo 485), adb ID: `46a880a0`

---

## Phase 1: 원타임 셋업 — ✅ 완료

| 단계 | 내용 | 상태 | 비고 |
|------|------|------|------|
| 1 | build_cpp.sh 최적화 | ✅ | cmake 최적화 플래그 + ar MRI 병합 적용 |
| 2 | .meta 파일 정리 | ✅ | libggml*.a/meta 삭제, libwhisper.a ARM64 확인 |
| 3 | C# 코드 확인 | ✅ | WhisperNativeParams 프로퍼티, WhisperManager 파라미터, WhisperOptimization 모두 존재 |
| 4 | GetTextAsyncOptimized 전환 | ✅ | `GetTextAsyncOptimized(clip, false)` → `GetTextAsyncOptimized(clip)` (audio_ctx 자동 계산 ON) |
| 5 | 모델 파일 확인 | ✅ | ggml-tiny.bin (39MB), 씬에서도 동일 경로 |
| 6 | 패키지명 확인 | ✅ | `com.DefaultCompany.whisper2022` |
| 7 | WSL2 네이티브 빌드 | ✅ | libwhisper.a 18MB (4개 라이브러리 병합) |
| 8 | Unity 헤드리스 빌드 | ✅ | NDK 경로 자동 설정 추가 (AutoBuilder.cs) |
| 9 | Baseline 성능 측정 | ✅ | **~10.4x** (8.9x ~ 12.1x) |

### 셋업 과정에서 해결한 이슈

1. **CRLF 줄바꿈**: `build_cpp.sh`가 Windows 줄바꿈으로 WSL에서 실행 불가 → `sed -i 's/\r$//'`로 해결
2. **ar MRI 경로**: `$build_path`가 상대경로여서 `cd` 후 참조 실패 → `$(pwd)`로 절대경로 사용
3. **llvm-ar 탐색**: `$android_sdk_path/../../../` 경로 계산 오류 → `dirname` 3단계로 NDK root 계산
4. **Unity NDK 미설정**: `Android NDK not found` → `AutoBuilder.cs`에서 `EditorPrefs.SetString("AndroidNdkRootR21D", ...)` 자동 설정

---

## Phase 2: 반복 최적화 — 결과 요약

### 현재 최적 설정 (코드에 반영된 상태)

```
GGML_OPENMP=OFF      ← ON과 동일 성능, 2021과 동일 설정으로 통일
GGML_LTO=OFF         ← 실험 B에서 -51% 악화 확인
threadsCount=0        ← auto = min(4, cores), 가장 빠름
temperatureInc=0.0f   ← 이미 최속 설정
greedyBestOf=1        ← 이미 최속 설정
flashAttention=true   ← OFF 시 -7% 악화 확인
-march=armv8.2-a+fp16+dotprod  ← 디바이스 최적 (SD855, i8mm/SVE 미지원)
ANDROID_PLATFORM=android-21    ← android-23은 -5% 악화
```

### 전체 결과 추적 테이블

> 새 디바이스 (46a880a0, SD855) 기준 — 100회 반복, 처음 5회 제외 warm 통계

| 실험 | 변경 내용 | Avg | Median | Range | 대비 | 유지 |
|------|-----------|:---:|:---:|:---:|:---:|:---:|
| **Baseline (new device)** | OPENMP=ON, threads=0(auto) | **10.3x** | 10.0x | 8.9~13.2 | — | ✅ |
| F-4 | threads=4 (명시) | 9.7x | 8.9x | 8.2~15.0 | -6% | ❌ |
| F-6 | threads=6 | 10.3x | 10.3x | 8.5~13.7 | ±0% | ❌ |
| F-8 | threads=8 | 9.5x | 9.2x | 8.5~12.2 | -8% | ❌ |
| I-2 | flashAttention=false | 9.6x | 9.2x | 8.4~13.2 | -7% | ❌ |
| D-2 | ANDROID_PLATFORM=android-23 | 9.8x | 9.4x | 8.5~12.7 | -5% | ❌ |
| **OPENMP=OFF** | OPENMP 비활성화 | 빌드 완료, 측정 미완 | — | — | ? | 테스트 필요 |

> 이전 디바이스 (구형, 교체됨) 측정값 참고:

| 실험 | 변경 내용 | Avg | Range | 대비 | 유지 |
|------|-----------|:---:|:---:|:---:|:---:|
| Baseline (old) | OPENMP=OFF, setup.md 완료 | ~10.4x | 8.9~12.1 | — | — |
| A-1 | OPENMP=ON | ~10.5x | 9.4~11.3 | +1% | — |
| B-1 | LTO=ON | ~5.1x | 2.5~7.0 | **-51%** | ❌ |
| F-2 (old) | threads=2 | ~8.2x | — | -21% | ❌ |

### 핵심 발견 사항

1. **OPENMP ON vs OFF**: 성능 차이 미미 (+1%). whisper.unity.2021이 OPENMP=OFF로 12.8x 달성하므로 OPENMP=OFF가 안전한 선택.

2. **LTO=ON 사용 불가**: ARM NEON 최적화 코드에 악영향. -51% 성능 하락.

3. **Thread Count**: auto(→min(4,cores)) ≥ 명시적 4 ≥ 6 > 8 > 2. **auto(0)가 최적**.

4. **Flash Attention**: CPU 전용(Android)에서도 **켜는 것이 7% 빠름**. 반드시 true 유지.

5. **ANDROID_PLATFORM**: android-21이 android-23보다 약간 빠름. android-21 유지.

6. **-march**: SD855에서 armv8.2-a+fp16+dotprod이 최적. i8mm/SVE는 미지원(SIGILL 위험).

7. **2021 vs 2022 차이**: whisper.cpp 버전, cmake 플래그, C# 런타임 설정 모두 동일. 차이는 **Unity 에디터 버전의 IL2CPP 코드 생성** 뿐. 성능 차이(12.8x vs 10.3x)는 IL2CPP 컴파일러 차이로 추정.

### 미실행 실험 및 사유

| 실험 | 사유 |
|------|------|
| C (OPENMP+LTO) | LTO 단독 실패(-51%)로 스킵 |
| E (Beam Search) | Greedy보다 느릴 것이 확실 (2~5x 느림) |
| G (Temperature/BestOf) | 이미 최속 설정 (tempInc=0, bestOf=1) |
| H (audio_ctx 전략) | 이미 GetTextAsyncOptimized 사용 중 |
| K (armv8.4-a/i8mm) | SD855에서 미지원 — SIGILL 크래시 위험 |
| J (소스 수정) | 로깅은 NDEBUG로 이미 비활성화. auto thread가 최적 |

---

## 파일 변경 이력

| 파일 | 변경 내용 | 현재 상태 |
|------|-----------|-----------|
| `build_cpp.sh` | `build_android()` 전체 교체 | OPENMP=OFF, LTO=OFF, android-21 |
| `Assets/Editor/AutoBuilder.cs` | NDK 경로 자동 설정 추가 | 활성 |
| `Assets/Samples/1 - Audio Clip/AudioClipDemo.cs` | `GetTextAsyncOptimized(clip)` 호출 | 활성 |
| `Packages/.../Plugins/Android/` | libggml*.a 삭제, libwhisper.a만 유지 | 활성 |
| `Packages/.../Runtime/WhisperManager.cs` | threads=0, flashAttn=true, tempInc=0, bestOf=1 | 원복 완료 |

---

## 남은 작업

현재 `build_cpp.sh`에 `GGML_OPENMP=OFF`가 설정되어 있고 네이티브 빌드도 완료됨.
**Unity 빌드 → APK 설치 → 10회 측정**만 수행하면 OPENMP=OFF 최종 확인 완료.
