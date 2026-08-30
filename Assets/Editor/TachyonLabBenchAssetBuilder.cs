using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UmaDesktopPet.Standalone.Editor
{
    /// <summary>
    /// Produces the runtime prefab from Kenney's CC0 solid-top table. The
    /// authored mesh is kept intact while its source materials are replaced at
    /// runtime by Tachyon's app-owned laboratory palette.
    /// </summary>
    public static class TachyonLabBenchAssetBuilder
    {
        private const float RuntimeWidthScale = 0.34f;

        public const string SourceAssetPath =
            "Assets/AppOwned/TachyonLabBench/Source/kenney-table.fbx";
        public const string RuntimePrefabPath =
            "Assets/Resources/AppOwned/TachyonLabBench.prefab";
        public const string RuntimeResourcePath =
            "AppOwned/TachyonLabBench";

        [MenuItem("Uma Desktop Pet/Build Tachyon Lab Bench")]
        public static void Build()
        {
            AssetDatabase.ImportAsset(
                SourceAssetPath,
                ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(
                SourceAssetPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "Tachyon lab-bench source FBX is missing: " +
                    SourceAssetPath);
            }

            GameObject sourceInstance = null;
            GameObject runtimeRoot = null;
            try
            {
                sourceInstance = UnityEngine.Object.Instantiate(source);
                sourceInstance.name = "TachyonLabBenchSource";
                sourceInstance.hideFlags = HideFlags.HideAndDontSave;

                MeshFilter[] sourceFilters = sourceInstance
                    .GetComponentsInChildren<MeshFilter>(true)
                    .Where(item => item.sharedMesh != null)
                    .ToArray();
                if (sourceFilters.Length != 1)
                {
                    throw new InvalidOperationException(
                        "Expected exactly one authored static mesh in the " +
                        "Kenney table source; found " + sourceFilters.Length +
                        ".");
                }

                MeshFilter sourceFilter = sourceFilters[0];
                MeshRenderer sourceRenderer =
                    sourceFilter.GetComponent<MeshRenderer>();
                if (sourceRenderer == null)
                {
                    throw new InvalidOperationException(
                        "The authored table transform must contain one static " +
                        "MeshFilter and MeshRenderer.");
                }

                Mesh mesh = sourceFilter.sharedMesh;
                int triangleCount = mesh.triangles.Length / 3;
                if (mesh.vertexCount < 48 || triangleCount < 80 ||
                    mesh.subMeshCount != 1 ||
                    mesh.bounds.size.sqrMagnitude <= 0.000001f)
                {
                    throw new InvalidOperationException(
                        "The authored table mesh failed its geometry contract: " +
                        "vertices=" + mesh.vertexCount + ", triangles=" +
                        triangleCount + ", submeshes=" + mesh.subMeshCount +
                        ".");
                }

                runtimeRoot = new GameObject("TachyonLabBench");
                var runtimeBench = new GameObject("SolidTopLabTable");
                runtimeBench.transform.SetParent(runtimeRoot.transform, false);
                runtimeBench.transform.position = sourceFilter.transform.position;
                // Kenney authors table with width along X, height along Y,
                // and depth along Z, matching the app-owned support convention.
                runtimeBench.transform.rotation = sourceFilter.transform.rotation;
                runtimeBench.transform.localScale = Vector3.Scale(
                    sourceFilter.transform.lossyScale,
                    new Vector3(RuntimeWidthScale, 1.0f, 1.0f));

                MeshFilter runtimeFilter =
                    runtimeBench.AddComponent<MeshFilter>();
                runtimeFilter.sharedMesh = mesh;
                MeshRenderer runtimeRenderer =
                    runtimeBench.AddComponent<MeshRenderer>();
                runtimeRenderer.sharedMaterials =
                    sourceRenderer.sharedMaterials;
                runtimeRenderer.shadowCastingMode = ShadowCastingMode.Off;
                runtimeRenderer.receiveShadows = false;
                runtimeRenderer.lightProbeUsage = LightProbeUsage.Off;
                runtimeRenderer.reflectionProbeUsage =
                    ReflectionProbeUsage.Off;

                string outputDirectory = Path.GetDirectoryName(
                    RuntimePrefabPath);
                if (string.IsNullOrEmpty(outputDirectory))
                {
                    throw new InvalidOperationException(
                        "The runtime prefab path has no directory.");
                }
                Directory.CreateDirectory(outputDirectory);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    runtimeRoot,
                    RuntimePrefabPath,
                    out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        "Unity failed to save the Tachyon lab-bench prefab.");
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log(
                    "TACHYON_LAB_BENCH_BUILD_OK path=" + RuntimePrefabPath +
                    ", vertices=" + mesh.vertexCount + ", triangles=" +
                    triangleCount + ", sourceMaterials=" +
                    sourceRenderer.sharedMaterials.Length + " [" +
                    string.Join(
                        ", ",
                        sourceRenderer.sharedMaterials.Select(
                            (material, materialIndex) =>
                                DescribeMaterial(
                                    mesh,
                                    material,
                                    materialIndex))) +
                    "], horizontalPlanes=[" +
                    DescribeHorizontalPlanes(mesh) + "].");
            }
            finally
            {
                if (runtimeRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(runtimeRoot);
                }
                if (sourceInstance != null)
                {
                    UnityEngine.Object.DestroyImmediate(sourceInstance);
                }
            }
        }

        private static string DescribeMaterial(
            Mesh mesh,
            Material material,
            int materialIndex)
        {
            string materialName = material != null
                ? material.name
                : "<null>";
            string color = "<none>";
            if (material != null)
            {
                if (material.HasProperty("_BaseColor"))
                {
                    color = material.GetColor("_BaseColor").ToString("F3");
                }
                else if (material.HasProperty("_Color"))
                {
                    color = material.GetColor("_Color").ToString("F3");
                }
            }

            if (materialIndex >= mesh.subMeshCount)
            {
                return materialName + " color=" + color + " submesh=<none>";
            }

            Bounds bounds = CalculateSubMeshBounds(mesh, materialIndex);
            return materialName + " color=" + color + " submesh=" +
                materialIndex + " center=" + bounds.center.ToString("F3") +
                " size=" + bounds.size.ToString("F3") + " triangles=" +
                (mesh.GetIndexCount(materialIndex) / 3);
        }

        private static Bounds CalculateSubMeshBounds(Mesh mesh, int subMesh)
        {
            int[] indices = mesh.GetIndices(subMesh);
            Vector3[] vertices = mesh.vertices;
            if (indices.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.zero);
            }

            Bounds bounds = new Bounds(vertices[indices[0]], Vector3.zero);
            for (int index = 1; index < indices.Length; index++)
            {
                bounds.Encapsulate(vertices[indices[index]]);
            }
            return bounds;
        }

        private static string DescribeHorizontalPlanes(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            var planes = new Dictionary<int, HorizontalPlaneSummary>();
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                int[] indices = mesh.GetIndices(subMesh);
                for (int index = 0; index + 2 < indices.Length; index += 3)
                {
                    Vector3 a = vertices[indices[index]];
                    Vector3 b = vertices[indices[index + 1]];
                    Vector3 c = vertices[indices[index + 2]];
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    float area = cross.magnitude * 0.5f;
                    if (area <= 0.000000001f ||
                        Mathf.Abs(cross.normalized.y) < 0.98f)
                    {
                        continue;
                    }

                    float centerY = (a.y + b.y + c.y) / 3.0f;
                    int heightKey = Mathf.RoundToInt(centerY * 100000.0f);
                    HorizontalPlaneSummary summary;
                    if (!planes.TryGetValue(heightKey, out summary))
                    {
                        summary = new HorizontalPlaneSummary(centerY, subMesh);
                        planes.Add(heightKey, summary);
                    }
                    summary.Add(a, area);
                    summary.Add(b, 0.0f);
                    summary.Add(c, 0.0f);
                }
            }

            Bounds meshBounds = mesh.bounds;
            return string.Join(
                "; ",
                planes.Values
                    .OrderByDescending(item => item.Area)
                    .Take(12)
                    .Select(item =>
                    {
                        float normalizedHeight =
                            (item.Height - meshBounds.min.y) /
                            meshBounds.size.y;
                        return "material=" + item.MaterialIndex +
                            " y=" + item.Height.ToString("F5") +
                            " floor01=" + normalizedHeight.ToString("F3") +
                            " area=" + item.Area.ToString("F6") +
                            " centerXZ=(" + item.CenterX.ToString("F4") +
                            "," + item.CenterZ.ToString("F4") + ")" +
                            " xz=" + item.Width.ToString("F4") + "x" +
                            item.Depth.ToString("F4");
                    }));
        }

        private sealed class HorizontalPlaneSummary
        {
            private bool _hasPoint;
            private float _minX;
            private float _maxX;
            private float _minZ;
            private float _maxZ;

            public HorizontalPlaneSummary(float height, int materialIndex)
            {
                Height = height;
                MaterialIndex = materialIndex;
            }

            public float Height { get; private set; }
            public int MaterialIndex { get; private set; }
            public float Area { get; private set; }
            public float CenterX { get { return (_minX + _maxX) * 0.5f; } }
            public float CenterZ { get { return (_minZ + _maxZ) * 0.5f; } }
            public float Width { get { return _maxX - _minX; } }
            public float Depth { get { return _maxZ - _minZ; } }

            public void Add(Vector3 point, float area)
            {
                Area += area;
                if (!_hasPoint)
                {
                    _minX = point.x;
                    _maxX = point.x;
                    _minZ = point.z;
                    _maxZ = point.z;
                    _hasPoint = true;
                    return;
                }
                _minX = Mathf.Min(_minX, point.x);
                _maxX = Mathf.Max(_maxX, point.x);
                _minZ = Mathf.Min(_minZ, point.z);
                _maxZ = Mathf.Max(_maxZ, point.z);
            }
        }
    }
}
