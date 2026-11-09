using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SurviveI95.EditorTools
{
    /// <summary>Dumps the parts of the player car at game scale, measured the way the slice builder places it.</summary>
    public static class ModelInspector
    {
        public static void Dump()
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/Sketchfab/corolla/scene.gltf"));
            var rs = inst.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float scale = 4.63f / Mathf.Max(b.size.x, b.size.z);
            inst.transform.localScale *= scale;
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            inst.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
            var sb = new StringBuilder();
            foreach (var r in rs.OrderBy(x => x.bounds.center.y))
            {
                var c = r.bounds.center; var s = r.bounds.size;
                sb.AppendLine($"{Path(r.transform)} | mats {string.Join(",", r.sharedMaterials.Select(m => m ? m.name : "null"))} | c ({c.x:F2},{c.y:F2},{c.z:F2}) s ({s.x:F2},{s.y:F2},{s.z:F2}) tris {(r.GetComponent<MeshFilter>()?.sharedMesh.triangles.Length ?? 0) / 3}");
            }
            File.WriteAllText(System.Environment.GetCommandLineArgs().SkipWhile(a => a != "-out").Skip(1).First(), sb.ToString());
        }

        static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    }
}
