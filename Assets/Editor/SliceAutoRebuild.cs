using System.IO;
using UnityEditor;
using UnityEngine;

namespace SurviveI95.EditorTools
{
    /// <summary>Keeps the generated scene in step with the builder, so nobody has to remember a menu click.</summary>
    [InitializeOnLoad]
    public static class SliceAutoRebuild
    {
        public const int Version = 11;
        const string Stamp = "Assets/Generated/build_version.txt";

        static SliceAutoRebuild()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += Check;
        }

        static void Check()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (File.Exists(Stamp) && File.ReadAllText(Stamp).Trim() == Version.ToString()) return;
            Debug.Log("[FloridaSlice] Scene is out of date, rebuilding automatically...");
            FloridaSliceBuilder.Build();
        }

        public static void WriteStamp() => File.WriteAllText(Stamp, Version.ToString());
    }
}
