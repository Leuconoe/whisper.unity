# whisper.unity.2022 — 최적화 지시서 (v2)

> **전제**: `setup.md`의 9단계 + Phase 2 실험 A~I, K 완료 후 사용  
> **현재 성능**: ~10.3x real-time (목표 8.0x 이미 달성)  
> **참조 성능**: whisper.unity.2021 = 12.8x (동일 whisper.cpp v1.7.5, 동일 cmake 플래그)  
> **성능 차이 주 원인**: Unity 2022 IL2CPP 코드 생성이 2021 대비 ~20% 느림 (추정)

---

## 확정된 최적 설정

아래 설정은 실험적으로 검증됨. **변경하지 말 것.**

| 항목 | 값 | 근거 |
|------|-----|------|
| `GGML_OPENMP` | **OFF** | ON과 동일 성능 (+1%). OFF가 2021과 일관, 라이브러리 2MB 작음 |
| `GGML_LTO` | **OFF** | ON 시 -51% 성능 하락 (NEON 최적화 훼손) |
| `ANDROID_PLATFORM` | **android-21** | android-23은 -5% 악화 |
| `-march` | **armv8.2-a+fp16+dotprod** | 타겟 SD855 최적. armv8.4-a/i8mm은 미지원 |
| `-mtune` | **cortex-a76** | SD855 빅코어(Kryo 485) 매칭 |
| `threadsCount` | **0** (auto→min(4,cores)) | 명시적 4/6/8 모두 동등 이하 |
| `flashAttention` | **true** | OFF 시 -7% 악화 |
| `temperatureInc` | **0.0f** | fallback 비활성화 = 최속 |
| `greedyBestOf` | **1** | 디코더 1회 = 최속 |
| `strategy` | **GREEDY** | Beam Search는 2~5x 느림 |
| `GetTextAsyncOptimized` | **사용** | audio_ctx 자동 계산으로 짧은 오디오 최적화 |

---

## 작업 원칙

1. **한 번에 하나의 변수만 변경**
2. **매 변경마다 full cycle 실행** — 빌드 → APK → 측정
3. **개선 시 유지, 악화 시 원복**
4. **whisper.cpp 소스 수정 시 diff 생성**
5. **walkthrough.md 갱신** — 결과 기록

---

## 빌드-테스트 사이클

### 명령어 참조

각 단계를 **개별 명령으로 분리하여 실행**. 복합 명령 금지.

#### 1단계: 네이티브 빌드 (build_cpp.sh 변경 시에만)

```powershell
wsl -d Ubuntu-22.04 -- bash -c "cd /mnt/d/workspace/___2025___LIGNex1-Drone/_AI/speech-and-text-unity-ios-android/voice-input/whisper.unity.2022 && sed -i 's/\r$//' build_cpp.sh && ./build_cpp.sh ./whisper.cpp android /home/ubuntu/Android/Sdk/ndk/25.1.8937393/build/cmake/android.toolchain.cmake 2>&1"
```

- 완료 판단: `Library size:` 출력 확인
- C# 파일만 변경한 경우 이 단계 건너뜀

#### 2단계: Unity 빌드

빌드 시작 (백그라운드):
```powershell
$logFile = "D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.2022\Builds\build.log"
if (Test-Path $logFile) { Remove-Item $logFile }
& "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.2022" -executeMethod AutoBuilder.BuildAndroid -logFile $logFile
```

빌드 완료 대기 (별도 터미널):
```powershell
$logFile = "D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.2022\Builds\build.log"
while (-not (Test-Path $logFile)) { Start-Sleep 5 }
do { $c1=(Get-Content $logFile).Count; Start-Sleep 30; $c2=(Get-Content $logFile).Count; Write-Host "Lines: $c2 (delta: $($c2-$c1))" } while ($c2 -lt 100 -or ($c2-$c1) -ne 0)
Get-Content $logFile -Tail 3
```

- 성공: `"Exiting batchmode successfully now!"`
- 실패: `"BuildFailedException"` 또는 `"error"`

#### 3단계: APK 설치

```powershell
adb install -r "D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.2022\Builds\whisper.2022.apk"
```

#### 4단계: 앱 실행

```powershell
adb shell am force-stop com.DefaultCompany.whisper2022
adb logcat -c
adb shell am start -n com.DefaultCompany.whisper2022/com.unity3d.player.UnityPlayerActivity
```

#### 5단계: 성능 측정 (150초 대기 후)

```powershell
Start-Sleep -Seconds 150
adb logcat -d -s Unity 2>&1 | Select-String "\[Whisper Result\]" | Select-Object -Last 10
```

결과가 100개 미만이면 추가 대기 후 재확인.

#### 6단계: 통계 계산

```powershell
$lines = adb logcat -d -s Unity 2>&1 | Select-String "\[Whisper Result\]"
Write-Host "Total: $($lines.Count)"
$vals = $lines | ForEach-Object { if ($_ -match '(\d+\.?\d*)x') { [double]$Matches[1] } }
$warm = $vals | Select-Object -Skip 5
$sorted = $warm | Sort-Object
Write-Host "Warm: Count=$($warm.Count) Min=$($sorted[0]) Max=$($sorted[-1]) Avg=$([math]::Round(($warm | Measure-Object -Average).Average,1)) Median=$($sorted[[math]::Floor($sorted.Count/2)])"
```

---

## 남은 실험 항목

### 1. OPENMP=OFF 최종 측정 ⚡ 우선

**상태**: 네이티브 빌드 완료, Unity 빌드+측정 필요  
**현재 build_cpp.sh**: `GGML_OPENMP=OFF` (이미 설정됨)

- 2단계부터 시작 (1단계 불필요)
- 결과가 baseline(10.3x)과 동등 이상이면 OFF 확정

---

### 2. whisper.cpp 소스 수정 (실험 J)

OPENMP/LTO/threads/flashAttn 등 빌드 플래그 최적화는 완료됨.  
추가 개선은 **whisper.cpp 소스 수준 수정**에서만 가능.

#### J-2: 로깅 매크로 비활성화

`whisper.cpp/src/whisper.cpp` L124~126:

```cpp
// 수정 전
#define WHISPER_LOG_ERROR(...) whisper_log_internal(GGML_LOG_LEVEL_ERROR, __VA_ARGS__)
#define WHISPER_LOG_WARN(...)  whisper_log_internal(GGML_LOG_LEVEL_WARN , __VA_ARGS__)
#define WHISPER_LOG_INFO(...)  whisper_log_internal(GGML_LOG_LEVEL_INFO , __VA_ARGS__)

// 수정 후
#define WHISPER_LOG_ERROR(...) do {} while(0)
#define WHISPER_LOG_WARN(...)  do {} while(0)
#define WHISPER_LOG_INFO(...)  do {} while(0)
```

기대: 소폭 개선 (로깅 함수 호출 + sprintf 제거)  
위험: 낮음 (디버그 출력만 사라짐)

#### J-3: 메모리 사전 할당

`whisper_full()` 내부 반복 `std::vector` 재할당을 `reserve()`로 최적화.
복잡도 높아 J-2 결과 확인 후 진행.

#### J-4: GGML 커널 최적화 탐색

`ggml/src/ggml-cpu/ggml-cpu.c`에서 ARM NEON 경로 분기 확인.
`GGML_CPU_AARCH64=ON`이면 전용 NEON 커널 사용됨 — 추가 여지 적음.

**diff 생성 절차**:
```bash
cd whisper.unity.2022/whisper.cpp
git diff > ../../.agent/whisper_cpp_v1.7.5.diff
```

---

### 3. Unity IL2CPP 최적화 (실험 NEW)

2021 vs 2022 성능 차이(12.8x vs 10.3x)의 주 원인이 IL2CPP인 경우:

#### N-1: IL2CPP Managed Stripping Level

`ProjectSettings/ProjectSettings.asset`에서:
```
managedStrippingLevel: 1  (Low)
→ managedStrippingLevel: 2  (Medium 또는 High)
```

기대: 불필요한 코드 제거로 바이너리 크기/캐시 효율 개선

#### N-2: IL2CPP Code Generation (Faster Runtime)

`ProjectSettings/ProjectSettings.asset`에서 `il2CppCodeGeneration` 확인.  
Unity 2022는 `Faster (smaller) builds` vs `Faster runtime` 옵션 있음.

```csharp
// AutoBuilder.cs 또는 ProjectSettings에서
PlayerSettings.SetIl2CppCodeGeneration(
    BuildTargetGroup.Android,
    Il2CppCodeGeneration.OptimizeSpeed  // Faster runtime
);
```

기대: IL2CPP 생성 코드의 런타임 최적화 강화

#### N-3: IL2CPP Compiler Configuration (Master)

```csharp
// AutoBuilder.cs에서
PlayerSettings.SetIl2CppCompilerConfiguration(
    BuildTargetGroup.Android,
    Il2CppCompilerConfiguration.Master  // Release보다 공격적 최적화
);
```

기대: 3~10% 성능 개선 (인라인, 루프 최적화 강화)

---

## 추천 실험 순서

1. **OPENMP=OFF 측정** — 이미 빌드 완료, 측정만 필요
2. **N-3: IL2CPP Master** — C# 변경 없이 빌드 설정만 변경
3. **N-2: IL2CPP OptimizeSpeed** — 코드 생성 전략 변경
4. **N-1: Managed Stripping** — 바이너리 경량화
5. **J-2: 로깅 비활성화** — 소스 수정 (네이티브 재빌드 필요)

---

## ⚠️ 주의사항

### WhisperNativeParams 구조체 레이아웃
- `[StructLayout(LayoutKind.Sequential)]`로 C++ `whisper_full_params`와 바이트 정렬
- **절대 금지**: 필드 추가/삭제/재배치
- **허용**: 기존 private 필드 접근용 프로퍼티 추가

### 실험 실패 시
- 즉시 원복 후 다음 실험 진행
- 빌드 실패 시 이전 성공 상태로 복원
