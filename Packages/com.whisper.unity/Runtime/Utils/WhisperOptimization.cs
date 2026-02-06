using System;

namespace Whisper.Utils
{
    /// <summary>
    /// Helper for optimizing whisper inference performance
    /// through dynamic audio context calculation.
    /// </summary>
    public static class WhisperOptimization
    {
        /// <summary>
        /// Calculate optimal audio context size based on audio length.
        /// Using a smaller audio_ctx for short audio can significantly
        /// speed up inference (2-6x for audio shorter than 15 seconds).
        /// </summary>
        /// <param name="audioLengthSeconds">Audio length in seconds</param>
        /// <param name="minContext">Minimum context (default: 64)</param>
        /// <param name="maxContext">Maximum context (default: 1500, which is ~30s)</param>
        /// <returns>Optimal audio_ctx value</returns>
        public static int CalculateAudioContext(float audioLengthSeconds, int minContext = 64, int maxContext = 1500)
        {
            // 30 seconds = 1500 audio_ctx in whisper
            int ctx = (int)(audioLengthSeconds / 30f * maxContext);
            
            // Add 10% padding for safety
            ctx = (int)(ctx * 1.1f);
            
            return Math.Clamp(ctx, minContext, maxContext);
        }

        /// <summary>
        /// Calculate optimal audio context from sample count (16kHz sample rate assumed).
        /// </summary>
        /// <param name="sampleCount">Number of PCM samples</param>
        /// <param name="sampleRate">Sample rate (default: 16000 Hz)</param>
        /// <returns>Optimal audio_ctx value</returns>
        public static int CalculateAudioContextFromSamples(int sampleCount, int sampleRate = 16000)
        {
            float seconds = (float)sampleCount / sampleRate;
            return CalculateAudioContext(seconds);
        }

        /// <summary>
        /// Get recommended thread count based on processor count.
        /// </summary>
        /// <param name="maxThreads">Maximum threads to use (default: 4)</param>
        /// <returns>Recommended thread count</returns>
        public static int GetRecommendedThreadCount(int maxThreads = 4)
        {
            return Math.Min(Environment.ProcessorCount, maxThreads);
        }
    }
}
