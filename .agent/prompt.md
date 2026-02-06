# 에이전트 프롬프트 — whisper.unity.2022 최적화

아래 프롬프트를 복사하여 에이전트에게 전달하세요.

---

## 프롬프트

```
당신은 whisper.cpp 기반 Unity Android 라이브러리의 성능 최적화를 수행하는 자동화 에이전트입니다.

## 프로젝트 정보

- 프로젝트: `whisper.unity.2022`
- 경로: `D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.2022`
- Unity: 2022.3.62f3 LTS (`C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe`)
- whisper.cpp: v1.7.5
- 빌드 환경: WSL2 (Ubuntu-22.04) + Android NDK 25.1.8937393
- 타겟: Android ARM64 (IL2CPP, 정적 링킹)

## 목표

Android ARM64 디바이스에서 whisper 추론 속도 **8.0x real-time 이상** 달성.
(참고: 이전 whisper.unity.2021 프로젝트에서 12.8x 달성)

## 작업 지시서

프로젝트 내 `.agent/` 폴더에 두 개의 지시서가 있습니다. **반드시 순서대로 따라 주세요.**

### Phase 1: 원타임 셋업 — `.agent/setup.md`

이 문서는 최적화의 기초를 구축하는 **일회성 작업**입니다.
9단계로 구성되며, 모든 단계를 순서대로 완료해야 합니다:

1. `build_cpp.sh`의 `build_android()` 함수를 최적화된 cmake 플래그 + ar MRI 라이브러리 병합 코드로 교체
2. `.meta` 파일 확인 (libwhisper.a → Android ARM64 활성화, 불필요 .a/.meta 삭제)
3. C# 코드 확인 (WhisperNativeParams 프로퍼티, WhisperManager 파라미터, WhisperOptimization)
4. `AudioClipDemo.cs`에서 `GetTextAsync` → `GetTextAsyncOptimized`로 전환
5. 모델 파일 확인 (`ggml-tiny.bin` 사용)
6. 패키지명 확인 (`ProjectSettings.asset`에서)
7. WSL2 네이티브 빌드 실행
8. Unity 헤드리스 빌드
9. APK 설치 및 baseline 성능 측정

**Phase 1 완료 조건**: APK가 디바이스에서 실행되고, `[Whisper Result]` 로그로 baseline 성능(x.xx real-time)이 측정됨.

### Phase 2: 반복 최적화 — `.agent/optimize.md`

Phase 1 완료 후, 이 문서의 실험들을 **추천 순서대로** 진행합니다.
핵심 원칙:
- **한 번에 하나의 변수만 변경**
- **매 변경마다 full cycle 실행** (네이티브 빌드 → Unity 빌드 → APK 설치 → 성능 측정)
- **결과 추적 테이블 업데이트** (optimize.md 하단)
- **개선 시 유지, 악화 시 원복**
- **whisper.cpp 소스 수정 시 diff 생성** (`.agent/` 폴더에 보관)
- **각 작업 별 결과를 walkthrough에 갱신**

추천 실험 순서:
1. GGML_OPENMP ON (Unity 2022 전용 — Unity 2021에서 실패했던 플래그)
2. GGML_LTO ON (Unity 2022 전용)
3. OPENMP + LTO 동시 (둘 다 단독 성공 시)
4. Thread Count (1, 2, 3, 4, 6, 8)
5. Beam Search vs Greedy
6. Temperature / GreedyBestOf 조합
7. -march 타겟 아키텍처
8. Flash Attention ON/OFF
9. ANDROID_PLATFORM 수준
10. whisper.cpp 소스 수정 (고급)

## 핵심 주의사항

### ⚠️ WhisperNativeParams 구조체 — 절대 금지 사항
`WhisperNativeParams`(`Packages/com.whisper.unity/Runtime/Native/WhisperNativeParams.cs`)는 
`[StructLayout(LayoutKind.Sequential)]`로 C++ `whisper_full_params`와 바이트 단위로 1:1 매핑됩니다.

- **절대 금지**: 필드 추가, 삭제, 순서 변경
- **허용**: 기존 private 필드에 대한 public 프로퍼티 추가
- **위반 시**: 추론 속도 0.1x 이하 급감 또는 SIGABRT 크래시 (메모리 오프셋 불일치)

### ⚠️ ANDROID_PLATFORM
`android-21` 필수. android-24 이상 사용 시 `undefined reference to 'stdout'` 링크 에러 발생.

### ⚠️ 라이브러리 병합
빌드 후 4개 정적 라이브러리(libwhisper.a, libggml.a, libggml-base.a, libggml-cpu.a)를 
ar MRI 스크립트로 단일 libwhisper.a로 병합 **필수**. 미병합 시 `undefined reference to 'ggml_*'` 발생.

### ⚠️ 빌드 로그 모니터링
Unity 헤드리스 빌드는 장시간 소요될 수 있습니다.
- 성공: `"Exiting batchmode successfully now!"` 포함
- 실패: `"BuildFailedException"`, `"clang++: error:"`, `"undefined reference"` 포함
- 60초 이상 로그 변화 없으면 완료/실패로 판단

## 실행 방식

1. Phase 1(setup.md)의 1~9단계를 순서대로 실행
2. baseline 성능 측정 완료 후 Phase 2(optimize.md)로 전환
3. 실험을 추천 순서대로 하나씩 진행하며 결과 기록
4. 중단 요청이 있을 때까지 계속 진행
5. 각 실험 완료 시 현재까지의 최선 성능과 설정을 보고

## 빌드-테스트 사이클 요약

```powershell
# 1. 네이티브 빌드 (WSL2)
wsl -d Ubuntu-22.04 -- bash -c "cd /mnt/d/workspace/___2025___LIGNex1-Drone/_AI/speech-and-text-unity-ios-android/voice-input/whisper.unity.2022 && ./build_cpp.sh ./whisper.cpp android /home/ubuntu/Android/Sdk/ndk/25.1.8937393/build/cmake/android.toolchain.cmake 2>&1"

# 2. Unity 헤드리스 빌드
& "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.2022" -executeMethod AutoBuilder.BuildAndroid -logFile "D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.2022\build.log"

# 3. APK 설치 + 실행
adb install -r "D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.2022\Builds\whisper.2022.apk"
adb shell am force-stop <패키지명>
adb shell am start -n <패키지명>/com.unity3d.player.UnityPlayerActivity

# 4. 성능 측정
adb logcat -c; Start-Sleep -Seconds 25; adb logcat -d -s Unity 2>&1 | Select-String "\[Whisper Result\]" | Select-Object -Last 10
```

지금 Phase 1(setup.md)부터 시작하세요.
```
