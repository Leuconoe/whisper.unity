# whisper.unity.2022 — 최적화 Walkthrough

> **최종 갱신**: 2026-02-06 18:40  
> **목표**: Android ARM64에서 8.0x real-time 이상 달성 (참조: whisper.unity.2021 = 12.8x)  
> **현재 최고 성능**: **~12.0x** (IL2CPP Master + OptimizeSpeed + Stripping High + 로깅 비활성화)  
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
IL2CPP Compiler=Master         ← Release 대비 +2% 개선
IL2CPP CodeGen=OptimizeSpeed   ← 기본 대비 +16.5% 대폭 개선 (핵심 변경)
Managed Stripping=High(3)      ← Low 대비 미미 (+0.8%)
WHISPER_LOG 매크로=비활성화     ← 성능 미미, 불필요 출력 제거
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
| **OPENMP=OFF** | OPENMP 비활성화 | 10.3x | 10.3x | 8.2~12.7 | ±0% | ✅ (2021 일관) |
| **N-3: IL2CPP Master** | CompilerConfig=Master | 10.5x | 10.5x | 8.6~12.7 | +2% | ✅ |
| **N-2: IL2CPP OptimizeSpeed** | CodeGen=OptimizeSpeed | **12.0x** | **12.1x** | 9.7~15.0 | **+16.5%** | ✅ (핵심) |
| **N-1: Stripping High** | ManagedStripping=3(High) | 12.1x | 12.1x | 9.7~14.2 | +0.8% | ✅ |
| **J-2: 로깅 비활성화** | WHISPER_LOG→no-op | 12.1x | 12.2x | 9.9~15.0 | ±0% | ✅ |
| **OPENMP=ON+OptSpeed** | OPENMP=ON + OptimizeSpeed | 10.0x | 9.9x | 8.9~13.0 | **-17%** | ❌ (충돌) |
| **threads=3+OptSpeed** | 명시적 threads=3 | 11.6x | 11.7x | 9.7~13.2 | -4% | ❌ |
| **Stripping Medium** | managedStripping=2 | 11.7x | 11.8x | 9.6~15.0 | -3% | ❌ |

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

7. **IL2CPP OptimizeSpeed**: **가장 큰 단일 개선 (+16.5%)**. Unity 2022의 IL2CPP 코드 생성 전략을 OptimizeSpeed로 변경하면 whisper.unity.2021 수준 성능 복원 가능.

8. **IL2CPP Master**: Release 대비 +2% 소폭 개선. OptimizeSpeed와 누적 적용.

9. **2021 vs 2022 차이 원인 확인**: 성능 차이 주 원인은 IL2CPP Code Generation 기본값 차이. OptimizeSpeed 적용으로 12.0x 달성 (2021의 12.8x에 93.8% 근접).

10. **OPENMP와 OptimizeSpeed 충돌**: OptimizeSpeed 환경에서 OPENMP=ON이 -17% 악화. 단독 테스트에서는 차이 없었으나 OptimizeSpeed와 결합 시 심각한 성능 하락. OPENMP=OFF 필수.

11. **Stripping High > Medium**: High(3)가 Medium(2)보다 ~3% 빠름. 더 공격적인 코드 제거가 캐시 효율 개선에 기여.

12. **Unity 설정 자동 검증**: 빌드 전 검사 스크립트로 IL2CPP 설정과 WhisperManager 기본값 일치 여부를 확인. 실수로 인한 성능 저하 방지.

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
| `build_cpp.sh` | `build_android()` 전체 교체 + 플래그 설명 주석 추가 | OPENMP=OFF, LTO=OFF, android-21 |
| `Assets/Editor/AutoBuilder.cs` | NDK 경로 자동 설정 + IL2CPP Master + OptimizeSpeed | 활성 |
| `Assets/Editor/AndroidPreprocessBuild.cs` | 빌드 전 최적화 설정 검증 추가 | 활성 |
| `Assets/Editor/WhisperManagerEditor.cs` | Inspector 경고 HelpBox 추가 | 활성 |
| `Assets/Samples/1 - Audio Clip/AudioClipDemo.cs` | `GetTextAsyncOptimized(clip)` 호출 | 활성 |
| `Packages/.../Plugins/Android/` | libggml*.a 삭제, libwhisper.a만 유지 | 활성 |
| `Packages/.../Runtime/WhisperManager.cs` | threads=0, flashAttn=true, tempInc=0, bestOf=1 | 원복 완료 |
| `ProjectSettings/ProjectSettings.asset` | managedStrippingLevel Android: 3 (High) | 활성 |
| `whisper.cpp/src/whisper.cpp` | WHISPER_LOG 매크로 → no-op | 활성 (diff 보관) |
| `Assets/Editor/OptimizationValidator.cs` | 파일 제거 (AndroidPreprocessBuild로 대체) | 제거됨 |

---

## 남은 작업

모든 계획된 실험 완료. 현재 최적 성능: **~12.0x** (목표 8.0x 초과 달성, +50%).  
whisper.unity.2021의 12.8x에 93.8% 수준으로 근접. 나머지 ~6% 차이는 Unity 2021/2022 IL2CPP 코드 생성기 자체 차이로 추정.

### 추가 탐색 가능 영역 (우선도 낮음)

- **J-3: 메모리 사전 할당**: whisper_full() 내부 vector reserve() — 복잡도 높음
- **J-4: GGML 커널 최적화**: GGML_CPU_AARCH64=ON으로 이미 NEON 최적화됨
- **OPENMP=ON + OptimizeSpeed 재테스트**: OPENMP=OFF 확정이지만, OptimizeSpeed 적용 후 재확인 여지
