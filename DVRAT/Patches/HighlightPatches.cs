using System.Collections.Generic;
using System.Reflection;
using DV.Customization.Gadgets;
using HarmonyLib;
using UnityEngine;

namespace DVRAT.Patches
{
    /// <summary>
    /// Graphics.DrawMesh draws only the first sub-mesh, so a multi-material gadget highlights only one part.
    /// Swaps such meshes for a single sub-mesh copy, which needs "Read/Write Enabled" on the mesh.
    /// The class-level attribute is required, PatchAll skips classes without one.
    /// </summary>
    [HarmonyPatch]
    public static class HighlightPatches
    {
        // Merged once per source mesh. Null when it cannot be merged.
        private static readonly Dictionary<Mesh, Mesh> Merged = new Dictionary<Mesh, Mesh>();

        // HighlightMesh is a struct with readonly fields, so patch a boxed copy through reflection.
        private static readonly FieldInfo MeshField = AccessTools.Field(typeof(GadgetSystemUtility.HighlightMesh), "mesh");

        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(GadgetSystemUtility), nameof(GadgetSystemUtility.GenerateHighlightMeshes),
                new[] { typeof(Transform), typeof(MeshFilter[]) });

        [HarmonyPostfix]
        public static void MergeSubMeshes(GadgetSystemUtility.HighlightMesh[] __result)
        {
            if (__result == null) return;
            for (int i = 0; i < __result.Length; i++)
            {
                var mesh = __result[i].mesh;
                if (mesh == null || mesh.subMeshCount < 2) continue;
                var merged = MergedCopy(mesh);
                if (merged == null) continue;
                object boxed = __result[i];
                MeshField.SetValue(boxed, merged);
                __result[i] = (GadgetSystemUtility.HighlightMesh)boxed;
            }
        }

        private static Mesh MergedCopy(Mesh mesh)
        {
            if (Merged.TryGetValue(mesh, out var known)) return known;
            Mesh merged = null;
            if (!mesh.isReadable)
            {
                RATMain.LogWarning($"Highlight of mesh \"{mesh.name}\" shows only its first material: turn on " +
                                   "\"Read/Write Enabled\" in the model's import settings");
            }
            else
            {
                var triangles = new List<int>();
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    triangles.AddRange(mesh.GetTriangles(sub));
                merged = new Mesh { name = mesh.name + " (highlight)", indexFormat = mesh.indexFormat };
                merged.vertices = mesh.vertices;
                merged.normals = mesh.normals;
                merged.SetTriangles(triangles, 0);
                merged.RecalculateBounds();
            }
            Merged[mesh] = merged;
            return merged;
        }
    }
}
