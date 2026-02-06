# Whisper.cpp Unity 라이브러리 최적화 작업 지시서

## 목표
whisper.cpp를 이용한 유니티용 라이브러리의 추론 속도를 **8.0x real-time** 이상으로 개선

## 배경
- `whisper.unity`는 이전 AI 에이전트와 속도 개선한 저장소
- 원본 저장소(`whisper.unity.org`)와 Unity 버전이 달라 PR 불가
- **핵심 참조**: `whisper.unity/build_cpp_whisper175.bat`의 최적화 플래그

## 작업 폴더
`whisper.unity.org` (whisper.cpp는 하위 폴더에 위치)

---

## 작업 순서

### 1. build_cpp.sh의 build_android() 최적화 플래그 적용

cmake 명령을 아래와 같이 수정:
```bash
cmake -DCMAKE_TOOLCHAIN_FILE="$android_sdk_path" \
    -DANDROID_ABI=arm64-v8a \
    -DANDROID_PLATFORM=android-24 \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_C_FLAGS_RELEASE="-O3 -ffast-math -fno-finite-math-only -ffp-contract=fast -fvisibility=hidden -ffunction-sections -fdata-sections -march=armv8.2-a+fp16+dotprod -fno-exceptions -DNDEBUG" \
    -DCMAKE_CXX_FLAGS_RELEASE="-O3 -ffast-math -fno-finite-math-only -ffp-contract=fast -fvisibility=hidden -ffunction-sections -fdata-sections -march=armv8.2-a+fp16+dotprod -fvisibility-inlines-hidden -fno-rtti -DNDEBUG" \
    -DCMAKE_EXE_LINKER_FLAGS_RELEASE="-Wl,--gc-sections -Wl,--exclude-libs,ALL -Wl,--strip-debug" \
    -DBUILD_SHARED_LIBS=OFF \
    -DGGML_STATIC=ON \
    -DGGML_NATIVE=OFF \
    -DGGML_OPENMP=OFF \
    -DGGML_LTO=OFF \
    -DGGML_CPU=ON \
    -DGGML_CPU_AARCH64=ON \
    -DGGML_CUDA=OFF -DGGML_METAL=OFF -DGGML_VULKAN=OFF -DGGML_OPENCL=OFF \
    -DWHISPER_BUILD_TESTS=OFF -DWHISPER_BUILD_EXAMPLES=OFF \
    ../

**필수 제약:**
- `GGML_OPENMP=OFF`: Unity 정적 링킹 호환성
- `GGML_LTO=OFF`: Unity 2021 링커 호환성
```

### 2. WSL2에서 라이브러리 빌드
```batch
set "WSL_PROJECT=/mnt/d/workspace/___2025___LIGNex1-Drone/_AI/speech-and-text-unity-ios-android/voice-input/whisper.unity.org"
set "WSL_TOOLCHAIN=/home/ubuntu/Android/Sdk/ndk/25.1.8937393/build/cmake/android.toolchain.cmake"
wsl.exe bash -lc "cd %WSL_PROJECT%; ./build_cpp.sh ./whisper.cpp android %WSL_TOOLCHAIN%"
```

### 3. 빌드 실패 시
- 원인 분석 후 재시도
- whisper.cpp 수정이 필요하면 수정

### 4. Unity 헤드리스 빌드 실행
```batch
set "PROJECT_PATH=D:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.org"
set "UNITY_PATH=C:\Program Files\Unity\Hub\Editor\2021.3.3f1\Editor"
"%UNITY_PATH%\Unity.exe" -batchmode -nographics -quit -projectPath "%PROJECT_PATH%" -executeMethod AutoBuilder.BuildAndroid -logFile "%PROJECT_PATH%\build.log"
```

### 5. 빌드 완료 확인
- 로그 파일: build.log
- 완료 표시: `Exiting batchmode successfully now` 문자열
- 에러 확인: `##### Output` 아래 줄 혹은 `BuildFailedException`, `clang++: error:` 단어 포함
- build.log의 줄 수가 60초 이상 증가하지 않는다면 빌드가 완료/실패했다고 판단하고 로그 분석을 진행

### 6. APK 생성 확인
- 출력 경로: `%PROJECT_PATH%\Builds\whisper.new.apk`
- 생성일이 최근인지 확인
- 없거나 오래되었으면 build.log 검토

### 7. APK 설치 및 실행
```batch
adb install -r "%PROJECT_PATH%\Builds\whisper.new.apk"
adb shell am start -n com.DefaultCompany.whisperapp/com.unity3d.player.UnityPlayerActivity
```

### 8. 성능 측정
```batch
adb logcat -c
adb logcat | findstr "[Whisper Result]"
```
- 15초 간격으로 모니터링
- **판단 기준**: 속도가 **5.0x 미만**이면 재최적화
- **목표**: **8.0x real-time** 이상

---

## 참조 파일
- 최적화 플래그 참조: build_cpp_whisper175.bat
- 성능 보고서: WHISPER175_PERFORMANCE_REPORT.md

중단 요청 전까지 계속 반복 작업