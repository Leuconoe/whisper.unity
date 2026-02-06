# whisper.unity.2022 — 최적화 Walkthrough

> **최종 갱신**: 2026-02-06 13:30  
> **목표**: Android ARM64에서 8.0x real-time 이상 달성  
> **현재 최고 성능**: **~10.5x** (OPENMP=ON, LTO=OFF, threadsCount=0/auto=4)

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

### Baseline 측정 결과

```
[Whisper Result] 9.4x, 10.3x, 10.3x, 12.1x, 10.6x, 8.9x, 11.4x, 10.3x, 10.3x, 10.3x
평균: ~10.4x real-time
```

---

## Phase 2: 반복 최적화 — 🔄 진행 중

### 현재 설정 (Best Configuration)

```
GGML_OPENMP=ON      ← 실험 A에서 채택 (Unity 2022 전용)
GGML_LTO=OFF         ← 실험 B에서 악화 확인, OFF 유지
threadsCount=4       ← 실험 F 테스트 중
temperatureInc=0.0f
greedyBestOf=1
flashAttention=true
-march=armv8.2-a+fp16+dotprod
ANDROID_PLATFORM=android-21
```

### 결과 추적 테이블

| 실험 | 변경 내용 | 네이티브 빌드 | Unity 빌드 | 실행 | 성능 (x real-time) | 대비 | 유지 |
|------|-----------|:---:|:---:|:---:|:---:|:---:|:---:|
| Baseline | setup.md 완료 (OPENMP=OFF) | ✅ | ✅ | ✅ | ~10.4x (8.9~12.1) | — | — |
| A-1 | GGML_OPENMP=ON | ✅ | ✅ | ✅ | ~10.5x (9.4~11.3) | +1% | ✅ |
| B-1 | GGML_LTO=ON | ✅ | ✅ | ✅ | ~5.1x (2.5~7.0) | **-51%** | ❌ 원복 |
| C | OPENMP+LTO 동시 | — | — | — | — | — | ⏭️ LTO 실패로 스킵 |
| F-2 | threadsCount=2, OPENMP=ON | ✅ | ✅ | ✅ | ~8.2x (7.3~8.9) | -21% | ❌ |
| F-4 | threadsCount=4, OPENMP=ON | ✅ | ✅ | ⏳ | 측정 대기 | ? | ? |

### 실험 상세

#### 실험 A: GGML_OPENMP=ON — ✅ 채택

- Unity 2021에서는 정적 링킹 시 `omp_*` 심볼 미해결로 실패했던 플래그
- Unity 2022에서는 `libomp.a`를 ar MRI 병합에 포함하여 **빌드 성공**
- 성능: baseline과 거의 동일 (~10.5x vs ~10.4x)
- 라이브러리 크기: 18MB → 20MB (libomp.a 포함)
- **Unity 2022에서 OPENMP 정적 링킹 성공** — 이전 버전 대비 주요 발견

#### 실험 B: GGML_LTO=ON — ❌ 원복

- 네이티브 빌드, Unity 빌드 모두 성공
- 하지만 런타임 성능 **대폭 악화**: ~5.1x (baseline 대비 -51%)
- LTO가 whisper.cpp의 ARM NEON 최적화 코드에 악영향을 미친 것으로 추정
- 즉시 LTO=OFF로 원복

#### 실험 F: Thread Count — 🔄 진행 중

- F-2 (2 threads): ~8.2x — baseline 대비 -21%, 스레드 부족으로 성능 하락
- F-4 (4 threads): 빌드 완료, 측정 대기

---

## 파일 변경 이력

| 파일 | 변경 내용 |
|------|-----------|
| `build_cpp.sh` | `build_android()` 전체 교체: 최적화 cmake 플래그, ar MRI 병합, libomp.a 포함 |
| `Assets/Editor/AutoBuilder.cs` | NDK 경로 자동 설정 코드 추가 |
| `Assets/Samples/1 - Audio Clip/AudioClipDemo.cs` | `GetTextAsyncOptimized(clip, false)` → `GetTextAsyncOptimized(clip)` |
| `Packages/.../Plugins/Android/` | libggml*.a, libggml*.a.meta 삭제 (병합된 libwhisper.a만 유지) |
| `Packages/.../Runtime/WhisperManager.cs` | `threadsCount` 값 변경 (실험 F) |

---

## 빌드 로그 모니터링 가이드

- **성공 판단**: `"Exiting batchmode successfully now!"` 문자열 포함
- **실패 판단**: `"BuildFailedException"`, `"clang++: error:"`, `"undefined reference"` 포함
- **정체 판단**: 30초 이상 build.log 줄 수 변화 없으면 완료/종료로 간주
