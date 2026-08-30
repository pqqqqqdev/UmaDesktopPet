using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Runtime owner for a profile-defined app-owned prop. Geometry can be
    /// authored as boxes or loaded from an app-owned Resources prefab; no
    /// installed game hierarchy or material is retained, copied, or exported.
    /// </summary>
    internal sealed class AppOwnedBoxSceneProp : IDisposable
    {
        private const float OutlineWidthPixels = 1.60f;
        private static readonly Color32 OutlineColor =
            new Color32(9, 29, 34, 255);

        private readonly Dictionary<PetAppOwnedSceneMaterialRole, Material>
            _materials =
                new Dictionary<PetAppOwnedSceneMaterialRole, Material>();
        private readonly float _characterHeight;
        private Bounds _geometryBoundsInCharacterHeights;
        private Material _outlineMaterial;
        private GameObject _root;
        private bool _disposed;

        public AppOwnedBoxSceneProp(
            PetAppOwnedScenePropDefinition definition,
            Transform parent,
            Vector3 characterCenter,
            float characterHeight)
        {
            Definition = definition ?? throw new ArgumentNullException("definition");
            if (parent == null)
            {
                throw new ArgumentNullException("parent");
            }
            if (!IsFinite(characterCenter) || !IsFinitePositive(characterHeight))
            {
                throw new ArgumentOutOfRangeException("characterHeight");
            }
            _characterHeight = characterHeight;

            try
            {
                _root = new GameObject(
                    "App-owned signature prop [" + definition.VisualId + "]");
                _root.hideFlags = HideFlags.DontSave;
                _root.layer = parent.gameObject.layer;
                _root.transform.SetParent(parent, false);
                _root.transform.localPosition = characterCenter +
                    definition.OffsetInCharacterHeights * characterHeight;
                _root.transform.localRotation = Quaternion.Euler(
                    0.0f,
                    definition.YawDegrees,
                    0.0f);
                _root.transform.localScale = Vector3.one;

                Shader shader = ResolveShader();
                if (definition.SourceKind ==
                    PetAppOwnedScenePropSourceKind.BoxParts)
                {
                    BuildBoxParts(shader);
                }
                else if (definition.SourceKind ==
                    PetAppOwnedScenePropSourceKind.ResourcePrefab)
                {
                    BuildResourcePrefab(shader);
                }
                else
                {
                    throw new InvalidDataException(
                        "Unsupported app-owned scene-prop geometry source for " +
                        definition.VisualId + ".");
                }
                BuildOutlineGeometry(ResolveOutlineShader());
                _root.SetActive(false);
                Debug.Log(
                    "Built app-owned signature prop: id=" + definition.VisualId +
                    ", source=" + definition.SourceKind + ".");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public PetAppOwnedScenePropDefinition Definition { get; private set; }

        public bool IsVisible
        {
            get { return !_disposed && _root != null && _root.activeSelf; }
        }

        public void Show()
        {
            ThrowIfDisposed();
            _root.SetActive(true);
        }

        public void Hide()
        {
            if (_disposed || _root == null)
            {
                return;
            }
            _root.SetActive(false);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void PlaceForDiagnosticPreview(
            Vector3 geometryCenter,
            float yawDegrees,
            float pitchDegrees)
        {
            ThrowIfDisposed();
            if (!IsFinite(geometryCenter) || float.IsNaN(yawDegrees) ||
                float.IsInfinity(yawDegrees) || float.IsNaN(pitchDegrees) ||
                float.IsInfinity(pitchDegrees))
            {
                throw new ArgumentOutOfRangeException("geometryCenter");
            }
            Quaternion rotation = Quaternion.Euler(
                pitchDegrees,
                yawDegrees + Definition.YawDegrees,
                0.0f);
            _root.transform.localRotation = rotation;
            _root.transform.localPosition = geometryCenter -
                rotation *
                    (_geometryBoundsInCharacterHeights.center * _characterHeight);
        }
#endif

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_root != null)
            {
                _root.SetActive(false);
                DestroyOwned(_root);
                _root = null;
            }
            foreach (KeyValuePair<PetAppOwnedSceneMaterialRole, Material> pair in
                _materials)
            {
                if (pair.Value != null)
                {
                    DestroyOwned(pair.Value);
                }
            }
            _materials.Clear();
            if (_outlineMaterial != null)
            {
                DestroyOwned(_outlineMaterial);
                _outlineMaterial = null;
            }
        }

        private Material ResolveMaterial(
            Shader shader,
            PetAppOwnedScenePalette palette,
            PetAppOwnedSceneMaterialRole role)
        {
            Material existing;
            if (_materials.TryGetValue(role, out existing))
            {
                return existing;
            }

            Color color = palette.Resolve(role);
            var material = new Material(shader)
            {
                name = "App-owned signature " + role,
                hideFlags = HideFlags.DontSave
            };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            if (material.HasProperty("_UseBackColor"))
            {
                material.SetFloat("_UseBackColor", 0.0f);
            }
            if (material.HasProperty("_FaceShading"))
            {
                material.SetFloat("_FaceShading", 1.0f);
            }
            _materials.Add(role, material);
            return material;
        }

        private void BuildBoxParts(Shader shader)
        {
            if (Definition.ResourcePrefab != null ||
                Definition.Parts == null || Definition.Parts.Count == 0)
            {
                throw new InvalidDataException(
                    "Invalid box-part source for app-owned scene prop " +
                    Definition.VisualId + ".");
            }

            for (int index = 0; index < Definition.Parts.Count; index++)
            {
                PetAppOwnedBoxPartDefinition part = Definition.Parts[index];
                CreateCube(
                    _root.transform,
                    part.PartId,
                    part.LocalCenterInCharacterHeights * _characterHeight,
                    part.SizeInCharacterHeights * _characterHeight,
                    Quaternion.Euler(part.LocalEulerAnglesInDegrees),
                    ResolveMaterial(
                        shader,
                        Definition.Palette,
                        part.MaterialRole));
            }
            _geometryBoundsInCharacterHeights =
                CalculateBoxProfileBounds(Definition);
        }

        private void BuildResourcePrefab(Shader shader)
        {
            PetAppOwnedResourcePrefabDefinition resource =
                Definition.ResourcePrefab;
            if (resource == null || Definition.Parts.Count != 0)
            {
                throw new InvalidDataException(
                    "Invalid resource-prefab source for app-owned scene prop " +
                    Definition.VisualId + ".");
            }

            GameObject source = Resources.Load<GameObject>(resource.ResourcePath);
            if (source == null)
            {
                throw new InvalidDataException(
                    "Missing app-owned Resources prefab " +
                    resource.ResourcePath + ".");
            }

            Bounds sourceBounds = ValidateResourcePrefab(source, resource);
            float uniformScale =
                resource.DesiredHeightInCharacterHeights * _characterHeight /
                sourceBounds.size.y;
            if (!IsFinitePositive(uniformScale))
            {
                throw new InvalidDataException(
                    "App-owned Resources prefab normalization was invalid for " +
                    Definition.VisualId + ".");
            }

            GameObject instance = UnityEngine.Object.Instantiate(
                source,
                _root.transform,
                false);
            if (instance == null)
            {
                throw new InvalidDataException(
                    "Unity could not instantiate app-owned Resources prefab " +
                    resource.ResourcePath + ".");
            }
            instance.name = "Resource geometry [" + resource.ResourcePath + "]";
            instance.transform.localPosition = -sourceBounds.center * uniformScale;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * uniformScale;

            Transform[] transforms = instance.GetComponentsInChildren<Transform>(
                true);
            for (int index = 0; index < transforms.Length; index++)
            {
                transforms[index].gameObject.hideFlags = HideFlags.DontSave;
                transforms[index].gameObject.layer = _root.layer;
            }

            MeshRenderer[] renderers =
                instance.GetComponentsInChildren<MeshRenderer>(true);
            for (int rendererIndex = 0;
                rendererIndex < renderers.Length;
                rendererIndex++)
            {
                MeshRenderer renderer = renderers[rendererIndex];
                Material[] sourceMaterials = renderer.sharedMaterials;
                var paletteMaterials = new Material[sourceMaterials.Length];
                for (int materialIndex = 0;
                    materialIndex < sourceMaterials.Length;
                    materialIndex++)
                {
                    Material sourceMaterial = sourceMaterials[materialIndex];
                    PetAppOwnedSceneMaterialRole role;
                    if (sourceMaterial == null ||
                        !resource.TryResolveMaterialRole(
                            sourceMaterial.name,
                            out role))
                    {
                        throw new InvalidDataException(
                            "Unmapped source material on app-owned Resources " +
                            "prefab " + resource.ResourcePath + ".");
                    }
                    paletteMaterials[materialIndex] = ResolveMaterial(
                        shader,
                        Definition.Palette,
                        role);
                }
                renderer.sharedMaterials = paletteMaterials;
                ConfigureRenderer(renderer);
            }

            float normalizedScale =
                resource.DesiredHeightInCharacterHeights / sourceBounds.size.y;
            _geometryBoundsInCharacterHeights = new Bounds(
                Vector3.zero,
                sourceBounds.size * normalizedScale);
        }

        private static void CreateCube(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.hideFlags = HideFlags.DontSave;
            cube.layer = parent.gameObject.layer;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = localRotation;
            cube.transform.localScale = localScale;

            Collider collider = cube.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                DestroyOwned(collider);
            }

            MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                throw new InvalidOperationException(
                    "Unity did not create a renderer for an app-owned scene box.");
            }
            renderer.sharedMaterial = material;
            ConfigureRenderer(renderer);
        }

        private static void ConfigureRenderer(MeshRenderer renderer)
        {
            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void BuildOutlineGeometry(Shader outlineShader)
        {
            if (outlineShader == null)
            {
                Debug.LogWarning(
                    "The app-owned outline shader is unavailable; continuing " +
                    "without silhouette reinforcement for " +
                    Definition.VisualId + ".");
                return;
            }

            MeshFilter[] sourceFilters =
                _root.GetComponentsInChildren<MeshFilter>(true);
            if (sourceFilters.Length == 0)
            {
                throw new InvalidDataException(
                    "App-owned scene prop " + Definition.VisualId +
                    " contains no mesh geometry to outline.");
            }

            _outlineMaterial = new Material(outlineShader)
            {
                name = "App-owned signature outline",
                hideFlags = HideFlags.DontSave,
                renderQueue = 2001
            };
            if (_outlineMaterial.HasProperty("_OutlineColor"))
            {
                _outlineMaterial.SetColor("_OutlineColor", OutlineColor);
            }
            if (_outlineMaterial.HasProperty("_OutlineWidth"))
            {
                _outlineMaterial.SetFloat(
                    "_OutlineWidth",
                    OutlineWidthPixels);
            }

            int builtCount = 0;
            for (int index = 0; index < sourceFilters.Length; index++)
            {
                MeshFilter sourceFilter = sourceFilters[index];
                Mesh sourceMesh = sourceFilter.sharedMesh;
                MeshRenderer sourceRenderer =
                    sourceFilter.GetComponent<MeshRenderer>();
                if (sourceMesh == null || sourceRenderer == null)
                {
                    throw new InvalidDataException(
                        "App-owned scene prop " + Definition.VisualId +
                        " contains incomplete outline source geometry.");
                }

                var outlineObject = new GameObject(
                    "Outline [" + sourceFilter.gameObject.name + "]");
                outlineObject.hideFlags = HideFlags.DontSave;
                outlineObject.layer = sourceFilter.gameObject.layer;
                outlineObject.transform.SetParent(sourceFilter.transform, false);
                outlineObject.transform.localPosition = Vector3.zero;
                outlineObject.transform.localRotation = Quaternion.identity;
                outlineObject.transform.localScale = Vector3.one;

                MeshFilter outlineFilter =
                    outlineObject.AddComponent<MeshFilter>();
                outlineFilter.hideFlags = HideFlags.DontSave;
                outlineFilter.sharedMesh = sourceMesh;

                MeshRenderer outlineRenderer =
                    outlineObject.AddComponent<MeshRenderer>();
                outlineRenderer.hideFlags = HideFlags.DontSave;
                int materialCount = Mathf.Max(1, sourceMesh.subMeshCount);
                var outlineMaterials = new Material[materialCount];
                for (int materialIndex = 0;
                    materialIndex < outlineMaterials.Length;
                    materialIndex++)
                {
                    outlineMaterials[materialIndex] = _outlineMaterial;
                }
                outlineRenderer.sharedMaterials = outlineMaterials;
                ConfigureRenderer(outlineRenderer);
                builtCount++;
            }

            Debug.Log(
                "Built app-owned silhouette outline: id=" +
                Definition.VisualId + ", renderers=" + builtCount +
                ", widthPixels=" + OutlineWidthPixels.ToString("0.##") + ".");
        }

        private static Shader ResolveShader()
        {
            Shader shader = Resources.Load<Shader>("AppOwnedProp");
            if (shader == null || !shader.isSupported)
            {
                shader = Resources.Load<Shader>("StudyPropUnlit");
            }
            if (shader == null && GraphicsSettings.currentRenderPipeline != null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null || !shader.isSupported)
            {
                throw new InvalidOperationException(
                    "No supported shader is available for app-owned 3D props.");
            }
            return shader;
        }

        private static Shader ResolveOutlineShader()
        {
            Shader shader = Resources.Load<Shader>("AppOwnedPropOutline");
            return shader != null && shader.isSupported ? shader : null;
        }

        private static Bounds ValidateResourcePrefab(
            GameObject source,
            PetAppOwnedResourcePrefabDefinition resource)
        {
            Component[] components = source.GetComponentsInChildren<Component>(
                true);
            for (int index = 0; index < components.Length; index++)
            {
                Component component = components[index];
                if (component == null ||
                    (!(component is Transform) &&
                     !(component is MeshFilter) &&
                     !(component is MeshRenderer)))
                {
                    throw new InvalidDataException(
                        "App-owned Resources prefab " + resource.ResourcePath +
                        " is not a static Transform/MeshFilter/MeshRenderer " +
                        "hierarchy.");
                }
            }

            MeshFilter[] filters = source.GetComponentsInChildren<MeshFilter>(true);
            MeshRenderer[] renderers =
                source.GetComponentsInChildren<MeshRenderer>(true);
            if (filters.Length == 0 || filters.Length != renderers.Length)
            {
                throw new InvalidDataException(
                    "App-owned Resources prefab " + resource.ResourcePath +
                    " must contain paired MeshFilter and MeshRenderer " +
                    "components.");
            }

            for (int index = 0; index < filters.Length; index++)
            {
                MeshFilter filter = filters[index];
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                Mesh mesh = filter.sharedMesh;
                if (renderer == null || mesh == null || mesh.vertexCount == 0)
                {
                    throw new InvalidDataException(
                        "App-owned Resources prefab " + resource.ResourcePath +
                        " contains incomplete static mesh geometry.");
                }
                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    throw new InvalidDataException(
                        "App-owned Resources prefab " + resource.ResourcePath +
                        " contains a renderer without source materials.");
                }
                for (int materialIndex = 0;
                    materialIndex < materials.Length;
                    materialIndex++)
                {
                    PetAppOwnedSceneMaterialRole unusedRole;
                    if (materials[materialIndex] == null ||
                        !resource.TryResolveMaterialRole(
                            materials[materialIndex].name,
                            out unusedRole))
                    {
                        throw new InvalidDataException(
                            "App-owned Resources prefab " + resource.ResourcePath +
                            " contains an unmapped source material.");
                    }
                }
            }
            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index].GetComponent<MeshFilter>() == null)
                {
                    throw new InvalidDataException(
                        "App-owned Resources prefab " + resource.ResourcePath +
                        " contains an unpaired MeshRenderer.");
                }
            }

            Bounds bounds = CalculateStaticMeshBounds(source.transform, filters);
            if (!IsFinite(bounds.center) || !IsFinitePositive(bounds.size))
            {
                throw new InvalidDataException(
                    "App-owned Resources prefab " + resource.ResourcePath +
                    " has invalid or degenerate geometry bounds.");
            }
            return bounds;
        }

        private static Bounds CalculateStaticMeshBounds(
            Transform sourceRoot,
            MeshFilter[] filters)
        {
            Bounds bounds = default(Bounds);
            bool initialized = false;
            for (int index = 0; index < filters.Length; index++)
            {
                MeshFilter filter = filters[index];
                Matrix4x4 relativeMatrix = CalculateRelativeMatrix(
                    sourceRoot,
                    filter.transform);
                Bounds meshBounds = filter.sharedMesh.bounds;
                Vector3 halfSize = meshBounds.extents;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 corner = relativeMatrix.MultiplyPoint3x4(
                                meshBounds.center + new Vector3(
                                    halfSize.x * x,
                                    halfSize.y * y,
                                    halfSize.z * z));
                            if (!IsFinite(corner))
                            {
                                throw new InvalidDataException(
                                    "App-owned resource geometry contains a " +
                                    "non-finite transform.");
                            }
                            if (!initialized)
                            {
                                bounds = new Bounds(corner, Vector3.zero);
                                initialized = true;
                            }
                            else
                            {
                                bounds.Encapsulate(corner);
                            }
                        }
                    }
                }
            }
            return bounds;
        }

        private static Matrix4x4 CalculateRelativeMatrix(
            Transform root,
            Transform descendant)
        {
            Matrix4x4 matrix = Matrix4x4.identity;
            Transform current = descendant;
            while (current != null && current != root)
            {
                matrix = Matrix4x4.TRS(
                    current.localPosition,
                    current.localRotation,
                    current.localScale) * matrix;
                current = current.parent;
            }
            if (current != root)
            {
                throw new InvalidDataException(
                    "App-owned resource geometry escaped its prefab root.");
            }
            return matrix;
        }

        private static Bounds CalculateBoxProfileBounds(
            PetAppOwnedScenePropDefinition definition)
        {
            Bounds bounds = default(Bounds);
            bool initialized = false;
            for (int index = 0; index < definition.Parts.Count; index++)
            {
                PetAppOwnedBoxPartDefinition part = definition.Parts[index];
                Vector3 halfSize = part.SizeInCharacterHeights * 0.5f;
                Quaternion rotation = Quaternion.Euler(
                    part.LocalEulerAnglesInDegrees);
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 corner = part.LocalCenterInCharacterHeights +
                                rotation * new Vector3(
                                    halfSize.x * x,
                                    halfSize.y * y,
                                    halfSize.z * z);
                            if (!initialized)
                            {
                                bounds = new Bounds(corner, Vector3.zero);
                                initialized = true;
                            }
                            else
                            {
                                bounds.Encapsulate(corner);
                            }
                        }
                    }
                }
            }
            return bounds;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed || _root == null)
            {
                throw new ObjectDisposedException("AppOwnedBoxSceneProp");
            }
        }

        private static void DestroyOwned(UnityEngine.Object owned)
        {
            if (owned == null)
            {
                return;
            }
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(owned);
                return;
            }
#endif
            UnityEngine.Object.Destroy(owned);
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                value > 0.0001f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsFinitePositive(Vector3 value)
        {
            return IsFinite(value) && value.x > 0.0001f &&
                value.y > 0.0001f && value.z > 0.0001f;
        }
    }
}
