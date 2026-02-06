#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

public static class AutoBuilder
{
    // Define a static method that can be called from the command line
    public static void BuildAndroid()
    {
        // Get command line arguments (optional, useful for CI/CD)
        string[] args = System.Environment.GetCommandLineArgs();
        string outputPath = "Builds/whisper.new.apk"; // Default output path
        
        // Example of reading output path from arguments if needed
        // For simplicity, we stick with the default path here

        // Ensure Android platform is selected
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        // Configure build options (can be customized)
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.scenes = GetEnabledScenes(); // Get all scenes enabled in build settings
        buildPlayerOptions.locationPathName = outputPath;
        buildPlayerOptions.target = BuildTarget.Android;
        buildPlayerOptions.options = BuildOptions.None; // Or BuildOptions.Development, etc.

        // Optional: Set to build as AAB instead of APK
        // EditorUserBuildSettings.buildAppBundle = true;

        // Start the build process
        BuildPipeline.BuildPlayer(buildPlayerOptions);

        Debug.Log("Android build complete: " + outputPath);
    }

    // Helper method to get the paths of all scenes enabled in the Build Settings window
    private static string[] GetEnabledScenes()
    {
        System.Collections.Generic.List<string> scenes = new System.Collections.Generic.List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled)
            {
                scenes.Add(scene.path);
            }
        }
        return scenes.ToArray();
    }
}

#endif