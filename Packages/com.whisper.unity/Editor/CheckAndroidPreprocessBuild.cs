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
/// Manual: menu Whisper > Validate Optimization Settings.
/// </summary>
public class AndroidPreprocessBuild : IPreprocessBuildWithReport
{
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

        if (!EditorUtility.DisplayDialog(
            "Whisper Optimization Warning",
            $"{fail} optimization check(s) failed.\nSee Console for details.\n\nContinue building anyway?",
            "Continue", "Cancel Build"))
        {
            throw new BuildFailedException(
                $"Build cancelled: {fail} Whisper optimization check(s) failed.");
        }
    }

    [MenuItem("Whisper/Validate Optimization Settings", priority = 100)]
    public static void ValidateAll()
    {
        var results = RunAllChecks();
        string msg = FormatResults(results);
        if (CountFailures(results) == 0) Debug.Log(msg); else Debug.LogWarning(msg);
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
            expected = "Master", actual = v.ToString(),
            passed = v == Il2CppCompilerConfiguration.Master
        });
    }

    private static void CheckIL2CPPCodeGen(System.Collections.Generic.List<CheckResult> r)
    {
        var v = PlayerSettings.GetIl2CppCodeGeneration(UnityEditor.Build.NamedBuildTarget.Android);
        r.Add(new CheckResult {
            category = "IL2CPP", item = "Code Generation",
            expected = "OptimizeSpeed", actual = v.ToString(),
            passed = v == UnityEditor.Build.Il2CppCodeGeneration.OptimizeSpeed
        });
    }

    private static void CheckStrippingLevel(System.Collections.Generic.List<CheckResult> r)
    {
        string path = Path.Combine(Application.dataPath, "..", "ProjectSettings", "ProjectSettings.asset");
        string actual = "Unknown";
        bool passed = false;

        if (File.Exists(path))
        {
            var m = Regex.Match(File.ReadAllText(path), @"managedStrippingLevel:\s*\n\s*Android:\s*(\d+)");
            if (m.Success)
            {
                int lv = int.Parse(m.Groups[1].Value);
                string[] names = { "Disabled", "Low", "Medium", "High" };
                actual = lv < names.Length ? $"{names[lv]} ({lv})" : lv.ToString();
                passed = lv == 3;
            }
        }

        r.Add(new CheckResult {
            category = "Build", item = "Managed Stripping",
            expected = "High (3)", actual = actual, passed = passed
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

}
#endif
