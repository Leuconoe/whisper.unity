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
    private const bool   RECOMMENDED_FLASH_ATTENTION  = true;
    private const float  RECOMMENDED_TEMPERATURE_INC  = 0.0f;
    private const int    RECOMMENDED_GREEDY_BEST_OF   = 1;
    private const int    RECOMMENDED_THREADS_COUNT    = 0;   // 0 = auto-detect

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

        // 4. threadsCount (public field)
        if (mgr.threadsCount != RECOMMENDED_THREADS_COUNT)
        {
            // threadsCount = 0 means auto-detect (capped at 4).
            // Any explicit value is acceptable but worth noting.
            EditorGUILayout.HelpBox(
                $"[Optimization] threadsCount is {mgr.threadsCount}. " +
                $"Recommended: {RECOMMENDED_THREADS_COUNT} (auto-detect, capped at 4)\n" +
                "0 lets WhisperManager auto-select min(4, CPU cores). " +
                "Manual override may help or hurt depending on the device.",
                MessageType.Info);
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