using UnityEditor;
using UnityEngine;
using Whisper;

namespace com.whisper.unity.editor
{
    
/// <summary>
/// Custom Inspector for WhisperManager.
/// Shows warning HelpBox when performance-critical fields
/// differ from the recommended optimized values.
/// </summary>
[CustomEditor(typeof(WhisperManager))]
public class WhisperManagerEditor : Editor
{
    private const bool  RECOMMENDED_FLASH_ATTENTION = true;
    private const float RECOMMENDED_TEMPERATURE_INC  = 0.0f;
    private const int   RECOMMENDED_GREEDY_BEST_OF   = 1;
    // Benchmarked on METALENSE2 / Snapdragon XR2 (2×Cortex-A78 + 6×Cortex-A55):
    //   3 threads = 16.60x  vs  4 threads = 14.12x  (-15% regression at thread 4)
    // The 4th thread lands on an A55 efficiency core, causing scheduling contention.
    // NOTE: This is SoC-specific. Different big.LITTLE configs need their own validation.
    private const int   RECOMMENDED_THREADS_XR2      = 3;   // validated on Snapdragon XR2

    public override void OnInspectorGUI()
    {
        // Draw the default inspector first
        DrawDefaultInspector(); 
        #if !UNITY_ANDROID
        return;
        #endif

        WhisperManager mgr = (WhisperManager)target;

        EditorGUILayout.Space(8);

        // ── Collect warnings ──
        int warningCount = 0;

        // 1. flashAttention (private [SerializeField] → use SerializedProperty)
        SerializedProperty flashAttnProp = serializedObject.FindProperty("flashAttention");
        if (flashAttnProp != null && flashAttnProp.boolValue != RECOMMENDED_FLASH_ATTENTION)
        {
            EditorGUILayout.HelpBox(
                $"[Optimization] flashAttention is {flashAttnProp.boolValue}. " +
                $"Recommended: {RECOMMENDED_FLASH_ATTENTION}\n" +
                "Flash Attention reduces memory bandwidth and improves inference speed.",
                MessageType.Warning);
            warningCount++;
        }

        // 2. temperatureInc (public field)
        if (!Mathf.Approximately(mgr.temperatureInc, RECOMMENDED_TEMPERATURE_INC))
        {
            EditorGUILayout.HelpBox(
                $"[Optimization] temperatureInc is {mgr.temperatureInc:F2}. " +
                $"Recommended: {RECOMMENDED_TEMPERATURE_INC:F1}\n" +
                "Non-zero value triggers fallback re-decoding passes, increasing latency.",
                MessageType.Warning);
            warningCount++;
        }
 
        // 3. greedyBestOf (public field)
        if (mgr.greedyBestOf != RECOMMENDED_GREEDY_BEST_OF)
        {
            EditorGUILayout.HelpBox(
                $"[Optimization] greedyBestOf is {mgr.greedyBestOf}. " +
                $"Recommended: {RECOMMENDED_GREEDY_BEST_OF}\n" +
                "Values > 1 run multiple greedy passes and pick the best, multiplying inference time.",
                MessageType.Warning);
            warningCount++;
        }

        // 4. threadsCount — SoC-specific guidance
        // Validated only on Snapdragon XR2 (METALENSE2): 3 > 4 due to big.LITTLE contention.
        // For other SoCs, auto (0) is a safe starting point; benchmark to confirm.
        if (mgr.threadsCount == 0)
        {
            // auto → min(4, coreCount) = 4 on most Android devices.
            // On XR2, thread 4 lands on an A55 efficiency core → -15% throughput.
            EditorGUILayout.HelpBox(
                "[Optimization] threadsCount = 0 (auto → min(4, coreCount) = 4 on most Android).\n" +
                $"If targeting Snapdragon XR2 (e.g. METALENSE2): set to {RECOMMENDED_THREADS_XR2}.\n" +
                $"  Benchmark result — 3 threads: 16.60x  vs  4 threads: 14.12x  (−15% at thread 4)\n" +
                "  Cause: 4th thread is scheduled on an A55 efficiency core (2×A78 + 6×A55 topology).\n" +
                "For other SoCs (Snapdragon 8 Gen2/3, Dimensity, Exynos, etc.) optimal count differs —\n" +
                "run a benchmark on the target device before overriding.",
                MessageType.Warning);
            warningCount++;
        }
        else if (mgr.threadsCount != RECOMMENDED_THREADS_XR2)
        {
            EditorGUILayout.HelpBox(
                $"[Optimization] threadsCount = {mgr.threadsCount}.\n" +
                $"Validated optimum for Snapdragon XR2 (METALENSE2): {RECOMMENDED_THREADS_XR2}\n" +
                $"  3 threads: 16.60x  vs  4 threads: 14.12x  (−15%) — confirmed by benchmark.\n" +
                "This value is SoC-specific (big.LITTLE core topology). " +
                "For other SoCs, benchmark on the target device to find the actual optimum.",
                MessageType.Info);
            // Only a warning if it looks like an intentionally suboptimal value
            if (mgr.threadsCount == 4)
            {
                EditorGUILayout.HelpBox(
                    $"threadsCount = 4 is known to be SLOWER than 3 on Snapdragon XR2 (A78×2 + A55×6).\n" +
                    $"Change to {RECOMMENDED_THREADS_XR2} if this is an XR2 device.",
                    MessageType.Warning);
                warningCount++;
            }
        }

        // 5. initialPrompt — non-empty prompts risk hallucination on tiny/small models
        if (!string.IsNullOrEmpty(mgr.initialPrompt))
        {
            EditorGUILayout.HelpBox(
                $"[Optimization] initialPrompt is set (\"{ (mgr.initialPrompt.Length > 30 ? mgr.initialPrompt.Substring(0, 30) + "..." : mgr.initialPrompt) }\").\n" +
                "Recommended: leave empty (null).\n" +
                "Korean language prompts have been observed to cause hallucination (repeating tokens) " +
                "and empty output on tiny/small models. Clear unless you have a specific validated use case.",
                MessageType.Warning);
            warningCount++;
        }

        // 6. translateToEnglish — adds a decoder pass + degrades non-English accuracy
        if (mgr.translateToEnglish)
        {
            EditorGUILayout.HelpBox(
                "[Optimization] translateToEnglish is enabled.\n" +
                "Recommended: false for Korean (or any non-English) transcription.\n" +
                "Translation forces an extra decoding pass and reduces accuracy for source-language output.",
                MessageType.Warning);
            warningCount++;
        }

        // ── Summary ──
        if (warningCount == 0)
        {
            EditorGUILayout.HelpBox(
                "All performance-critical settings match the recommended optimized values.",
                MessageType.Info);
        }
    }
}

}