# FUTO Voice Input Style ggml Build
# Optimized for Android with minimal overhead
# Ported from: https://github.com/futo-org/voice-input

cmake_minimum_required(VERSION 3.14)
project(whisper-futo C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 17)

# Build options
option(GGML_FUTO_STATIC "Build static library" ON)
option(GGML_FUTO_LTO "Enable Link Time Optimization" OFF)

# Core source files (FUTO style - monolithic)
set(GGML_FUTO_SOURCES
    ggml.c
    ggml-alloc.c
    ggml-backend.c
    ggml-quants.c
    whisper.cpp
)

set(GGML_FUTO_HEADERS
    ggml.h
    ggml-alloc.h
    ggml-backend.h
    ggml-backend-impl.h
    ggml-impl.h
    ggml-quants.h
    unicode.h
    whisper.h
    defines.h
)

# Create the library
if(GGML_FUTO_STATIC)
    add_library(whisper-futo STATIC ${GGML_FUTO_SOURCES} ${GGML_FUTO_HEADERS})
else()
    add_library(whisper-futo SHARED ${GGML_FUTO_SOURCES} ${GGML_FUTO_HEADERS})
endif()

target_include_directories(whisper-futo PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})

# ============================================================
# PLATFORM-SPECIFIC OPTIMIZATIONS
# ============================================================

if(ANDROID)
    message(STATUS "Building FUTO ggml for Android")
    
    # Android-specific optimizations (FUTO style)
    target_compile_options(whisper-futo PRIVATE
        -O3
        -fvisibility=hidden
        -ffunction-sections
        -fdata-sections
        -fno-exceptions
        -fno-rtti
    )
    
    # ARM64 specific optimizations
    if(CMAKE_ANDROID_ARCH_ABI STREQUAL "arm64-v8a")
        target_compile_options(whisper-futo PRIVATE
            -march=armv8.2-a+fp16+dotprod
            -mtune=cortex-a76
        )
        
        # Enable ARM NEON
        target_compile_definitions(whisper-futo PRIVATE
            __ARM_NEON
            __ARM_FEATURE_FMA
            __ARM_FEATURE_DOTPROD
            __ARM_FEATURE_FP16_VECTOR_ARITHMETIC
        )
        
        message(STATUS "ARM64 v8a optimizations enabled (FP16 + DotProd)")
    elseif(CMAKE_ANDROID_ARCH_ABI STREQUAL "armeabi-v7a")
        target_compile_options(whisper-futo PRIVATE
            -mfpu=neon-vfpv4
            -mfloat-abi=softfp
        )
        
        target_compile_definitions(whisper-futo PRIVATE
            __ARM_NEON
        )
        
        message(STATUS "ARM v7a optimizations enabled (NEON + VFPv4)")
    endif()
    
    # Link Time Optimization
    if(GGML_FUTO_LTO)
        set_property(TARGET whisper-futo PROPERTY INTERPROCEDURAL_OPTIMIZATION TRUE)
        target_link_options(whisper-futo PRIVATE
            -flto
            -Wl,--gc-sections
            -Wl,--exclude-libs,ALL
        )
    endif()
    
    # Android system libraries
    find_library(log-lib log)
    target_link_libraries(whisper-futo ${log-lib})
    
elseif(APPLE)
    message(STATUS "Building FUTO ggml for Apple")
    
    target_compile_options(whisper-futo PRIVATE
        -O3
        -fvisibility=hidden
    )
    
    # Enable ARM optimizations on Apple Silicon
    if(CMAKE_OSX_ARCHITECTURES MATCHES "arm64" OR CMAKE_SYSTEM_PROCESSOR MATCHES "arm64")
        target_compile_definitions(whisper-futo PRIVATE
            __ARM_NEON
            __ARM_FEATURE_FMA
        )
    endif()
    
    # Accelerate framework
    find_library(ACCELERATE_FRAMEWORK Accelerate)
    if(ACCELERATE_FRAMEWORK)
        target_compile_definitions(whisper-futo PRIVATE GGML_USE_ACCELERATE)
        target_link_libraries(whisper-futo ${ACCELERATE_FRAMEWORK})
        message(STATUS "Apple Accelerate framework enabled")
    endif()
    
elseif(WIN32)
    message(STATUS "Building FUTO ggml for Windows")
    
    target_compile_options(whisper-futo PRIVATE
        /O2
        /fp:fast
    )
    
else()
    message(STATUS "Building FUTO ggml for Linux/Generic")
    
    target_compile_options(whisper-futo PRIVATE
        -O3
        -fvisibility=hidden
        -ffunction-sections
        -fdata-sections
    )
    
    if(GGML_FUTO_LTO)
        set_property(TARGET whisper-futo PROPERTY INTERPROCEDURAL_OPTIMIZATION TRUE)
    endif()
endif()

# ============================================================
# COMMON DEFINITIONS
# ============================================================

# Disable unused features for minimal overhead
target_compile_definitions(whisper-futo PRIVATE
    GGML_USE_CPU
    # No CUDA, Metal, OpenCL by default - pure CPU for consistency
)

# ============================================================
# WHISPER-SPECIFIC OPTIMIZATIONS (from FUTO)
# ============================================================

# These are performance-critical settings found in FUTO Voice Input
target_compile_definitions(whisper-futo PRIVATE
    # Disable debug assertions in release
    $<$<CONFIG:Release>:NDEBUG>
)

# ============================================================
# OUTPUT CONFIGURATION
# ============================================================

set_target_properties(whisper-futo PROPERTIES
    OUTPUT_NAME "whisper-futo"
    ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib"
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)

# Print build summary
message(STATUS "")
message(STATUS "============================================================")
message(STATUS "FUTO ggml Build Configuration")
message(STATUS "============================================================")
message(STATUS "Static library: ${GGML_FUTO_STATIC}")
message(STATUS "LTO enabled: ${GGML_FUTO_LTO}")
message(STATUS "Build type: ${CMAKE_BUILD_TYPE}")
if(ANDROID)
    message(STATUS "Android ABI: ${CMAKE_ANDROID_ARCH_ABI}")
    message(STATUS "Android Platform: ${ANDROID_PLATFORM}")
endif()
message(STATUS "============================================================")
message(STATUS "")
