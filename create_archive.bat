@echo off
setlocal

set AR=C:\Users\user\AppData\Local\Android\Sdk\ndk\25.1.8937393\toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-ar.exe
set RANLIB=C:\Users\user\AppData\Local\Android\Sdk\ndk\25.1.8937393\toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-ranlib.exe
set BUILD_DIR=d:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.org\build-android-arm64
set TEMP_DIR=%BUILD_DIR%\temp_obj2
set OUTPUT_DIR=d:\workspace\___2025___LIGNex1-Drone\_AI\speech-and-text-unity-ios-android\voice-input\whisper.unity.org\Packages\com.whisper.unity\Plugins\Android

echo Cleaning temp directory...
if exist "%TEMP_DIR%" rmdir /S /Q "%TEMP_DIR%"
mkdir "%TEMP_DIR%"
cd /d "%TEMP_DIR%"

echo Extracting .o files from libwhisper.a...
"%AR%" x "%BUILD_DIR%\src\libwhisper.a"

echo Extracting .o files from libggml.a...
"%AR%" x "%BUILD_DIR%\ggml\src\libggml.a"

echo Extracting .o files from libggml-base.a...
"%AR%" x "%BUILD_DIR%\ggml\src\libggml-base.a"

echo Extracting .o files from libggml-cpu.a...
"%AR%" x "%BUILD_DIR%\ggml\src\libggml-cpu.a"

echo Counting .o files...
dir *.o /b 2>nul | find /c /v "" 

echo Removing old archive if exists...
if exist "%OUTPUT_DIR%\libwhisper_combined.a" del /F "%OUTPUT_DIR%\libwhisper_combined.a"

echo Creating combined archive...
"%AR%" rcs "%OUTPUT_DIR%\libwhisper_combined.a" *.o

echo Running ranlib...
"%RANLIB%" "%OUTPUT_DIR%\libwhisper_combined.a"

echo Checking archive contents count...
"%AR%" t "%OUTPUT_DIR%\libwhisper_combined.a" | find /c /v ""

echo Done!
dir "%OUTPUT_DIR%\libwhisper_combined.a"
