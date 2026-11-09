using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SurviveI95.EditorTools
{
    public static class PlayerBuilder
    {
        [MenuItem("Survive I-95/Build Windows Game")]
        public static void Build()
        {
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/Generated/FloridaSlice.unity" }, "Build/SurviveI95.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
            Debug.Log("[PlayerBuild] " + report.summary.result + " " + report.summary.totalSize / 1048576 + " MB, errors " + report.summary.totalErrors);
            if (report.summary.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
