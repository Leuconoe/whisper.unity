#if UNITY_EDITOR
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Validates Unity editor optimization settings before every Android build.
/// Implements IPreprocessBuildWithReport to run automatically.
/// Manual: menu Whisper.unity > Validate Optimization Settings.
/// </summary>
public class AndroidPreprocessBuild : IPreprocessBuildWithReport
{
    private const string MenuRoot = "Whisper.unity";
    private const string RecommendedCompilerConfiguration = "Master";
    private const string RecommendedCodeGeneration = "OptimizeSpeed";
    private const string RecommendedManagedStripping = "High (3)";

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android)
            return;

        var results = RunAllChecks();
        int fail = CountFailures(results);

        if (fail == 0)
        {
            Debug.Log(FormatResults(results));
            return;
        }

        Debug.LogWarning(FormatResults(results));

        if (Application.isBatchMode)
        {
            Debug.LogWarning($"[Whisper.unity] {fail} optimization check(s) failed. Continuing because batch mode cannot show dialogs.");
            return;
        }

        var choice = EditorUtility.DisplayDialogComplex(
            "Whisper.unity Optimization Warning",
            $"{fail} optimization check(s) failed.\nSee Console for details.\n\nContinue building anyway, cancel the build, or apply the recommended settings now?",
            "Continue",
            "Cancel Build",
            "Apply Recommended");

        if (choice == 1)
        {
            throw new BuildFailedException(
                $"Build cancelled: {fail} Whisper.unity optimization check(s) failed.");
        }

        if (choice == 2)
        {
            ApplyRecommendedSettings();
            Debug.Log("[Whisper.unity] Applied recommended Android optimization settings.");
        }
    }

    [MenuItem(MenuRoot + "/Validate Optimization Settings", priority = 100)]
    public static void ValidateAll()
    {
        var results = RunAllChecks();
        var message = FormatResults(results);
        var fail = CountFailures(results);

        if (fail == 0)
        {
            Debug.Log(message);
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(
                    "Whisper.unity Optimization Validation",
                    "All Android optimization checks passed. See Console for details.",
                    "OK");
            }

            return;
        }

        Debug.LogWarning(message);

        if (Application.isBatchMode)
            return;

        var choice = EditorUtility.DisplayDialogComplex(
            "Whisper.unity Optimization Validation",
            $"{fail} optimization check(s) failed.\nSee Console for details.\n\nApply the recommended settings now?",
            "Apply Recommended",
            "Close",
            "Copy Summary");

        if (choice == 0)
        {
            ApplyRecommendedSettings();
            Debug.Log("[Whisper.unity] Applied recommended Android optimization settings.");
            return;
        }

        if (choice == 2)
        {
            EditorGUIUtility.systemCopyBuffer = message;
            Debug.Log("[Whisper.unity] Copied optimization validation summary to clipboard.");
        }
    }

    [MenuItem(MenuRoot + "/Apply Recommended Optimization Settings", priority = 101)]
    public static void ApplyRecommendedSettingsMenu()
    {
        ApplyRecommendedSettings();
        Debug.Log("[Whisper.unity] Applied recommended Android optimization settings.");
    }

    // ── Core ──

    private struct CheckResult
    {
        public string category, item, expected, actual;
        public bool passed;
    }

    private static System.Collections.Generic.List<CheckResult> RunAllChecks()
    {
        var r = new System.Collections.Generic.List<CheckResult>();
        CheckIL2CPPCompiler(r);
        CheckIL2CPPCodeGen(r);
        CheckStrippingLevel(r);
        CheckScriptingBackend(r);
        return r;
    }

    private static int CountFailures(System.Collections.Generic.List<CheckResult> results)
    {
        int n = 0; foreach (var r in results) if (!r.passed) n++; return n;
    }

    private static string FormatResults(System.Collections.Generic.List<CheckResult> results)
    {
        int pass = 0, fail = 0;
        var sb = new StringBuilder();
        sb.AppendLine("=== Whisper Android Optimization Validation ===");
        sb.AppendFormat("\n  {0,-16} {1,-22} {2,-22} {3,-22} {4}\n",
            "Category", "Item", "Expected", "Actual", "Status");
        sb.AppendLine("  " + new string('-', 88));

        foreach (var r in results)
        {
            if (r.passed) pass++; else fail++;
            sb.AppendFormat("  {0,-16} {1,-22} {2,-22} {3,-22} {4}\n",
                r.category, r.item, r.expected, r.actual, r.passed ? "PASS" : "FAIL");
        }

        sb.AppendLine("  " + new string('-', 88));
        sb.AppendFormat("  Result: {0} PASS, {1} FAIL (Total {2})\n", pass, fail, results.Count);
        return sb.ToString();
    }

    // ── Unity Editor Settings ──

    private static void CheckIL2CPPCompiler(System.Collections.Generic.List<CheckResult> r)
    {
        var v = PlayerSettings.GetIl2CppCompilerConfiguration(BuildTargetGroup.Android);
        r.Add(new CheckResult {
            category = "IL2CPP", item = "Compiler Config",
            expected = RecommendedCompilerConfiguration, actual = v.ToString(),
            passed = v == Il2CppCompilerConfiguration.Master
        });
    }

    private static void CheckIL2CPPCodeGen(System.Collections.Generic.List<CheckResult> r)
    {
        var runtimeValue = PlayerSettings.GetIl2CppCodeGeneration(UnityEditor.Build.NamedBuildTarget.Android);
        var hasSerializedValue = TryReadProjectSettingInt("il2cppCodeGeneration", "Android", out var serializedValue);
        var serializedText = hasSerializedValue
            ? $"{((UnityEditor.Build.Il2CppCodeGeneration)serializedValue)} ({serializedValue})"
            : "Not Set";

        r.Add(new CheckResult {
            category = "IL2CPP", item = "Code Generation",
            expected = RecommendedCodeGeneration,
            actual = $"Runtime {runtimeValue}, Serialized {serializedText}",
            passed = runtimeValue == UnityEditor.Build.Il2CppCodeGeneration.OptimizeSpeed &&
                     hasSerializedValue &&
                     (UnityEditor.Build.Il2CppCodeGeneration)serializedValue ==
                     UnityEditor.Build.Il2CppCodeGeneration.OptimizeSpeed
        });
    }

    private static void CheckStrippingLevel(System.Collections.Generic.List<CheckResult> r)
    {
        string actual = "Unknown";
        bool passed = false;

        if (TryReadProjectSettingInt("managedStrippingLevel", "Android", out var lv))
        {
            string[] names = { "Disabled", "Low", "Medium", "High" };
            actual = lv < names.Length ? $"{names[lv]} ({lv})" : lv.ToString();
            passed = lv == 3;
        }

        r.Add(new CheckResult {
            category = "Build", item = "Managed Stripping",
            expected = RecommendedManagedStripping, actual = actual, passed = passed
        });
    }

    private static void CheckScriptingBackend(System.Collections.Generic.List<CheckResult> r)
    {
        var v = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android);
        r.Add(new CheckResult {
            category = "Build", item = "Scripting Backend",
            expected = "IL2CPP", actual = v.ToString(),
            passed = v == ScriptingImplementation.IL2CPP
        });
    }

    private static bool TryReadProjectSettingInt(string sectionName, string key, out int value)
    {
        value = default;

        string path = Path.Combine(Application.dataPath, "..", "ProjectSettings", "ProjectSettings.asset");
        if (!File.Exists(path))
            return false;

        var lines = File.ReadAllLines(path);
        bool inSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Replace("\t", "    ");
            var trimmed = line.Trim();

            if (!inSection)
            {
                if (trimmed == $"{sectionName}: {{}}")
                    return false;

                if (trimmed == $"{sectionName}:")
                {
                    inSection = true;
                }

                continue;
            }

            if (!line.StartsWith("    "))
                break;

            var match = Regex.Match(trimmed, $@"^{Regex.Escape(key)}:\s*(\d+)$");
            if (!match.Success)
                continue;

            value = int.Parse(match.Groups[1].Value);
            return true;
        }

        return false;
    }

    private static void ApplyRecommendedSettings()
    {
        PlayerSettings.SetIl2CppCompilerConfiguration(
            BuildTargetGroup.Android,
            Il2CppCompilerConfiguration.Master);
        PlayerSettings.SetIl2CppCodeGeneration(
            NamedBuildTarget.Android,
            Il2CppCodeGeneration.OptimizeSpeed);
        PlayerSettings.SetManagedStrippingLevel(
            BuildTargetGroup.Android,
            ManagedStrippingLevel.High);
        AssetDatabase.SaveAssets();
    }

}
#endif
