using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Owner for one exact installed static prefab. Production signature scenes
    /// and the development gallery share this fail-closed loader. Assets remain in
    /// the user's bundles, are never copied or exported, and stay leased until
    /// disposal.
    /// </summary>
    public sealed class InstalledVignettePrefab : IDisposable
    {
        // Only the globe is authored for Agnes Tachyon. The cutin1080 pieces are
        // an optional JP research-corner enhancement, while the study pieces are
        // deliberately generic cross-region fallbacks.
        public const string TachyonCelestialGlobeId =
            "tachyon-celestial-globe";
        public const string TechRackId = "tech-rack";
        public const string TechMonitorId = "tech-monitor";
        public const string TechDeskId = "tech-desk";
        public const string TechChairId = "tech-chair";
        public const string ResearchWhiteboardId = "research-whiteboard";
        public const string StudyDeskId = "study-desk";
        public const string StudyChairId = "study-chair";
        public const string StudyBookId = "study-book";

        private const float MinimumBoundsSize = 0.000001f;

        private readonly string _diagnosticId;
        private BundleLease _lease;
        private GameObject _root;
        private Transform _placementPivot;
        private Transform _scalePivot;
        private Renderer[] _renderers;
        private Material[] _ownedMaterials;
        private Bounds _sourceBounds;
        private Vector3 _baseLocalPosition;
        private Quaternion _baseLocalRotation = Quaternion.identity;
        private bool _disposed;

        private InstalledVignettePrefab(
            string diagnosticId,
            BundleLease lease,
            GameObject root,
            Transform placementPivot,
            Transform scalePivot,
            Renderer[] renderers,
            Material[] ownedMaterials,
            Bounds sourceBounds)
        {
            _diagnosticId = diagnosticId;
            _lease = lease;
            _root = root;
            _placementPivot = placementPivot;
            _scalePivot = scalePivot;
            _renderers = renderers;
            _ownedMaterials = ownedMaterials;
            _sourceBounds = sourceBounds;
        }

        public Bounds SourceBounds
        {
            get
            {
                ThrowIfDisposed();
                return _sourceBounds;
            }
        }

        public int RendererCount
        {
            get { return _renderers != null ? _renderers.Length : 0; }
        }

        public bool IsVisible
        {
            get { return !_disposed && _root != null && _root.activeSelf; }
        }

        public string AssetId
        {
            get { return _diagnosticId; }
        }

        /// <summary>
        /// Attempts to load one gallery-approved prefab. Missing assets and
        /// incompatible installed data fail only this optional preview item.
        /// </summary>
        public static bool TryLoadAllowlisted(
            BundleRepository bundles,
            Transform parent,
            string assetId,
            out InstalledVignettePrefab prefab)
        {
            prefab = null;
            if (bundles == null)
            {
                throw new ArgumentNullException("bundles");
            }
            if (parent == null)
            {
                throw new ArgumentNullException("parent");
            }

            string normalizedId = assetId == null ? string.Empty : assetId.Trim();
            AllowlistedPrefabDefinition definition;
            if (!TryResolveAllowlisted(normalizedId, out definition))
            {
                Debug.LogWarning(
                    "Installed vignette prefab rejected unsupported asset ID " +
                    (normalizedId.Length == 0 ? "<empty>" : normalizedId) + ".");
                return false;
            }

            try
            {
                prefab = LoadResolved(
                    bundles,
                    parent,
                    definition.LogicalName,
                    definition.PrefabName,
                    definition.Id);
                return true;
            }
            catch (Exception exception)
            {
                if (prefab != null)
                {
                    prefab.Dispose();
                    prefab = null;
                }
                Debug.LogWarning(
                    "Optional installed vignette prefab unavailable: id=" +
                    definition.Id + " (" + exception.GetType().Name + "): " +
                    exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Development gallery entry point for exact candidates discovered during
        /// bounded installed-data diagnostics.
        /// </summary>
        public static InstalledVignettePrefab LoadExact(
            BundleRepository bundles,
            Transform parent,
            string logicalName,
            string expectedPrefabName,
            string diagnosticId)
        {
            return LoadResolved(
                bundles,
                parent,
                logicalName,
                expectedPrefabName,
                diagnosticId);
        }

        private static InstalledVignettePrefab LoadResolved(
            BundleRepository bundles,
            Transform parent,
            string logicalName,
            string expectedPrefabName,
            string diagnosticId)
        {
            if (bundles == null)
            {
                throw new ArgumentNullException("bundles");
            }
            if (parent == null)
            {
                throw new ArgumentNullException("parent");
            }
            if (string.IsNullOrWhiteSpace(logicalName))
            {
                throw new ArgumentException(
                    "An installed prefab logical name is required.",
                    "logicalName");
            }
            if (!IsPathFreeName(expectedPrefabName))
            {
                throw new ArgumentException(
                    "An exact installed prefab basename is required.",
                    "expectedPrefabName");
            }
            if (!IsPathFreeName(diagnosticId))
            {
                throw new ArgumentException(
                    "A path-free installed prefab diagnostic ID is required.",
                    "diagnosticId");
            }

            string normalizedLogicalName = logicalName.Trim();
            string normalizedPrefabName = expectedPrefabName.Trim();
            string normalizedId = diagnosticId.Trim();
            BundleLease lease = null;
            GameObject root = null;
            var ownedMaterials = new List<Material>();
            InstalledVignettePrefab result = null;
            try
            {
                lease = bundles.AcquireWithShaderFirst(normalizedLogicalName);
                AssetBundle bundle = lease.GetRequiredBundle(normalizedLogicalName);
                GameObject prefab = LoadUniqueExactPrefab(
                    bundle,
                    normalizedPrefabName,
                    normalizedId);

                int appLayer = parent.gameObject.layer;
                root = CreatePivot(
                    "Installed Vignette Prefab " + normalizedId,
                    parent,
                    appLayer).gameObject;
                root.SetActive(false);
                Transform placementPivot = CreatePivot(
                    "Placement",
                    root.transform,
                    appLayer);
                Transform scalePivot = CreatePivot(
                    "Scale",
                    placementPivot,
                    appLayer);

                // Read renderer data directly from the loaded prefab asset into
                // an app-owned Transform/MeshRenderer hierarchy. The foreign
                // GameObject is never instantiated, so none of its scripts or
                // lifecycle callbacks can run.
                GameObject instance = CreateRenderOnlyClone(
                    prefab,
                    scalePivot,
                    appLayer,
                    normalizedId,
                    ownedMaterials);

                Renderer[] renderers =
                    instance.GetComponentsInChildren<Renderer>(true);
                Bounds sourceBounds = CalculateFiniteGeometryBounds(
                    scalePivot,
                    instance.transform,
                    renderers,
                    normalizedId);
                result = new InstalledVignettePrefab(
                    normalizedId,
                    lease,
                    root,
                    placementPivot,
                    scalePivot,
                    renderers,
                    ownedMaterials.ToArray(),
                    sourceBounds);
                lease = null;
                root = null;
                result.LogSummary("loaded");
                return result;
            }
            catch
            {
                if (result != null)
                {
                    result.Dispose();
                }
                else
                {
                    DestroyOwnedObject(root);
                    DestroyOwnedMaterials(ownedMaterials);
                    if (lease != null)
                    {
                        try
                        {
                            lease.Dispose();
                        }
                        catch (Exception cleanupError)
                        {
                            Debug.LogWarning(
                                "Installed prefab load cleanup failed (" +
                                cleanupError.GetType().Name + ").");
                        }
                    }
                }
                throw;
            }
        }

        /// <summary>
        /// Uniformly fits the complete renderer geometry around an app-owned
        /// center without changing the installed instance's authored transform.
        /// </summary>
        public void FitAndCenter(Vector3 localCenter, float maximumDimension)
        {
            FitAndPlace(localCenter, Quaternion.identity, maximumDimension);
        }

        /// <summary>
        /// Uniformly fits the authored geometry around an app-owned local center
        /// and rotation without changing the installed instance transform.
        /// </summary>
        public void FitAndPlace(
            Vector3 localCenter,
            Quaternion localRotation,
            float maximumDimension)
        {
            ThrowIfDisposed();
            if (!IsFinite(localCenter) || !IsFinite(localRotation) ||
                !IsFinitePositive(maximumDimension) ||
                !IsFinitePositive(Quaternion.Dot(localRotation, localRotation)))
            {
                throw new ArgumentOutOfRangeException(
                    "maximumDimension",
                    "Installed prefab placement must be finite with positive scale.");
            }

            float sourceMaximum = Mathf.Max(
                _sourceBounds.size.x,
                Mathf.Max(_sourceBounds.size.y, _sourceBounds.size.z));
            if (!IsFinitePositive(sourceMaximum))
            {
                throw new InvalidDataException(
                    "The installed prefab has no finite geometry to fit.");
            }

            float uniformScale = maximumDimension / sourceMaximum;
            if (!IsFinitePositive(uniformScale))
            {
                throw new InvalidDataException(
                    "The installed prefab calculated an invalid gallery scale.");
            }

            _baseLocalPosition = localCenter;
            _baseLocalRotation = localRotation.normalized;
            _scalePivot.localPosition = -_sourceBounds.center * uniformScale;
            _scalePivot.localRotation = Quaternion.identity;
            _scalePivot.localScale = Vector3.one * uniformScale;
            SetDisplayOffset(Vector3.zero, Quaternion.identity, 1.0f);
        }

        /// <summary>
        /// Places the prefab by its authored pivot with a uniform app-owned scale.
        /// </summary>
        public void SetLocalPose(
            Vector3 localPosition,
            Quaternion localRotation,
            float uniformScale)
        {
            ThrowIfDisposed();
            if (!IsFinite(localPosition) || !IsFinite(localRotation) ||
                !IsFinitePositive(uniformScale) ||
                !IsFinitePositive(Quaternion.Dot(localRotation, localRotation)))
            {
                throw new ArgumentOutOfRangeException(
                    "uniformScale",
                    "Installed prefab pose must be finite with positive scale.");
            }

            _baseLocalPosition = localPosition;
            _baseLocalRotation = localRotation.normalized;
            _scalePivot.localPosition = Vector3.zero;
            _scalePivot.localRotation = Quaternion.identity;
            _scalePivot.localScale = Vector3.one * uniformScale;
            SetDisplayOffset(Vector3.zero, Quaternion.identity, 1.0f);
        }

        /// <summary>
        /// Applies a reversible app-owned motion on top of the calibrated pose.
        /// It never mutates the installed instance hierarchy or authored pivot.
        /// </summary>
        public void SetDisplayOffset(
            Vector3 localPositionOffset,
            Quaternion localRotationOffset,
            float scaleMultiplier)
        {
            ThrowIfDisposed();
            if (!IsFinite(localPositionOffset) ||
                !IsFinite(localRotationOffset) ||
                !IsFinitePositive(scaleMultiplier) ||
                !IsFinitePositive(Quaternion.Dot(
                    localRotationOffset,
                    localRotationOffset)))
            {
                throw new ArgumentOutOfRangeException(
                    "scaleMultiplier",
                    "Installed prefab display offsets must be finite with " +
                    "positive scale.");
            }

            _placementPivot.localPosition =
                _baseLocalPosition + localPositionOffset;
            _placementPivot.localRotation =
                _baseLocalRotation * localRotationOffset.normalized;
            _placementPivot.localScale = Vector3.one * scaleMultiplier;
        }

        public void Show()
        {
            ThrowIfDisposed();
            if (_root.activeSelf)
            {
                return;
            }
            _root.SetActive(true);
            LogSummary("visible");
        }

        public void Hide()
        {
            if (_disposed || _root == null)
            {
                return;
            }
            _root.SetActive(false);
        }

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
                GameObject root = _root;
                _root = null;
                _placementPivot = null;
                _scalePivot = null;
                DestroyOwnedObject(root);
            }
            _renderers = null;

            DestroyOwnedMaterials(_ownedMaterials);
            _ownedMaterials = null;

            if (_lease != null)
            {
                BundleLease lease = _lease;
                _lease = null;
                try
                {
                    lease.Dispose();
                }
                catch (Exception cleanupError)
                {
                    Debug.LogWarning(
                        "Installed vignette prefab lease cleanup failed: id=" +
                        _diagnosticId + " (" +
                        cleanupError.GetType().Name + ").");
                }
            }
        }

        private static bool TryResolveAllowlisted(
            string assetId,
            out AllowlistedPrefabDefinition definition)
        {
            switch (assetId)
            {
                case TachyonCelestialGlobeId:
                    definition = new AllowlistedPrefabDefinition(
                        TachyonCelestialGlobeId,
                        "3d/env/cutin/cutin1032_02/" +
                            "pfb_env_cutin1032_02_00_celestialglobe00",
                        "pfb_env_cutin1032_02_00_celestialglobe00");
                    return true;
                case TechRackId:
                    definition = CreateCutin1080Definition(
                        TechRackId,
                        "rack");
                    return true;
                case TechMonitorId:
                    definition = CreateCutin1080Definition(
                        TechMonitorId,
                        "monitor");
                    return true;
                case TechDeskId:
                    definition = CreateCutin1080Definition(
                        TechDeskId,
                        "desk");
                    return true;
                case TechChairId:
                    definition = CreateCutin1080Definition(
                        TechChairId,
                        "chair");
                    return true;
                case ResearchWhiteboardId:
                    definition = new AllowlistedPrefabDefinition(
                        ResearchWhiteboardId,
                        "3d/env/mini/mini10005/main/" +
                            "pfb_env_mini10005_main000_000_white_board",
                        "pfb_env_mini10005_main000_000_white_board");
                    return true;
                case StudyDeskId:
                    definition = new AllowlistedPrefabDefinition(
                        StudyDeskId,
                        "3d/env/set/set10001/prop/" +
                            "pfb_env_set10001_prop001_000",
                        "pfb_env_set10001_prop001_000");
                    return true;
                case StudyChairId:
                    definition = new AllowlistedPrefabDefinition(
                        StudyChairId,
                        "3d/env/set/set10001/prop/" +
                            "pfb_env_set10001_prop000_000",
                        "pfb_env_set10001_prop000_000");
                    return true;
                case StudyBookId:
                    definition = new AllowlistedPrefabDefinition(
                        StudyBookId,
                        "3d/chara/prop/prop1025_00/pfb_chr_prop1025_00",
                        "pfb_chr_prop1025_00");
                    return true;
                default:
                    definition = null;
                    return false;
            }
        }

        private static AllowlistedPrefabDefinition CreateCutin1080Definition(
            string id,
            string suffix)
        {
            string prefabName = "pfb_env_cutin1080_00_00_" + suffix;
            return new AllowlistedPrefabDefinition(
                id,
                "3d/env/cutin/cutin1080_00/" + prefabName,
                prefabName);
        }

        private sealed class AllowlistedPrefabDefinition
        {
            public AllowlistedPrefabDefinition(
                string id,
                string logicalName,
                string prefabName)
            {
                Id = id;
                LogicalName = logicalName;
                PrefabName = prefabName;
            }

            public string Id { get; private set; }
            public string LogicalName { get; private set; }
            public string PrefabName { get; private set; }
        }

        private static GameObject LoadUniqueExactPrefab(
            AssetBundle bundle,
            string expectedPrefabName,
            string diagnosticId)
        {
            if (bundle == null)
            {
                throw new InvalidDataException(
                    "The installed prefab root bundle is unavailable for " +
                    diagnosticId + ".");
            }

            string exactAssetName = null;
            string[] assetNames = bundle.GetAllAssetNames();
            for (int index = 0; index < assetNames.Length; index++)
            {
                string candidate = assetNames[index];
                if (!string.Equals(
                    Path.GetFileNameWithoutExtension(candidate),
                    expectedPrefabName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (exactAssetName != null)
                {
                    throw new InvalidDataException(
                        "The installed prefab has multiple exact basename " +
                        "matches for " + diagnosticId + ".");
                }
                exactAssetName = candidate;
            }

            if (exactAssetName == null)
            {
                throw new InvalidDataException(
                    "The installed prefab has no exact basename match for " +
                    diagnosticId + ".");
            }

            GameObject prefab = bundle.LoadAsset<GameObject>(exactAssetName);
            if (prefab == null || !string.Equals(
                prefab.name,
                expectedPrefabName,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The exact installed asset is not the expected GameObject " +
                    "prefab for " + diagnosticId + ".");
            }
            return prefab;
        }

        private static GameObject CreateRenderOnlyClone(
            GameObject prefabAsset,
            Transform parent,
            int appLayer,
            string diagnosticId,
            List<Material> ownedMaterials)
        {
            if (prefabAsset == null)
            {
                throw new ArgumentNullException("prefabAsset");
            }
            if (parent == null)
            {
                throw new ArgumentNullException("parent");
            }
            if (ownedMaterials == null)
            {
                throw new ArgumentNullException("ownedMaterials");
            }

            var transformMap = new Dictionary<Transform, Transform>();
            Transform cloneRoot = CloneTransformTree(
                prefabAsset.transform,
                parent,
                appLayer,
                transformMap,
                true);

            int meshRendererCount = 0;
            int skinnedRendererCount = 0;
            Renderer[] sourceRenderers =
                prefabAsset.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < sourceRenderers.Length; index++)
            {
                Renderer source = sourceRenderers[index];
                if (source == null || !source.enabled ||
                    source.forceRenderingOff ||
                    !IsLocallyActive(source.transform, prefabAsset.transform))
                {
                    continue;
                }

                Transform targetTransform;
                if (!transformMap.TryGetValue(
                    source.transform,
                    out targetTransform))
                {
                    throw new InvalidDataException(
                        "The installed prefab renderer has no cloned transform " +
                        "for " + diagnosticId + ".");
                }

                SkinnedMeshRenderer sourceSkinned =
                    source as SkinnedMeshRenderer;
                if (sourceSkinned != null)
                {
                    CloneSkinnedRenderer(
                        sourceSkinned,
                        targetTransform.gameObject,
                        transformMap,
                        diagnosticId);
                    skinnedRendererCount++;
                    continue;
                }

                MeshRenderer sourceMeshRenderer = source as MeshRenderer;
                if (sourceMeshRenderer != null)
                {
                    CloneMeshRenderer(
                        sourceMeshRenderer,
                        targetTransform.gameObject,
                        diagnosticId);
                    meshRendererCount++;
                }
            }

            if (meshRendererCount + skinnedRendererCount == 0)
            {
                throw new InvalidDataException(
                    "The installed prefab has no enabled authored mesh renderer " +
                    "for " + diagnosticId + ".");
            }

            ReplaceInstalledMaterials(
                cloneRoot.gameObject,
                ownedMaterials,
                diagnosticId);

            Debug.Log(
                "Installed vignette render-only clone created: id=" +
                diagnosticId + ", meshRenderers=" + meshRendererCount +
                ", skinnedRenderers=" + skinnedRendererCount + ".");
            return cloneRoot.gameObject;
        }

        private static Transform CloneTransformTree(
            Transform source,
            Transform parent,
            int appLayer,
            IDictionary<Transform, Transform> transformMap,
            bool isRoot)
        {
            var targetObject = new GameObject(
                isRoot ? source.name + " (Render Only)" : source.name)
            {
                hideFlags = HideFlags.DontSave,
                layer = appLayer,
                tag = "Untagged"
            };
            Transform target = targetObject.transform;
            target.SetParent(parent, false);
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
            transformMap.Add(source, target);

            for (int index = 0; index < source.childCount; index++)
            {
                CloneTransformTree(
                    source.GetChild(index),
                    target,
                    appLayer,
                    transformMap,
                    false);
            }

            targetObject.SetActive(source.gameObject.activeSelf);
            return target;
        }

        private static void CloneMeshRenderer(
            MeshRenderer source,
            GameObject targetObject,
            string diagnosticId)
        {
            MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
            if (sourceFilter == null || sourceFilter.sharedMesh == null)
            {
                throw new InvalidDataException(
                    "The installed prefab MeshRenderer has no shared mesh for " +
                    diagnosticId + ".");
            }

            MeshFilter targetFilter = targetObject.AddComponent<MeshFilter>();
            targetFilter.sharedMesh = sourceFilter.sharedMesh;

            MeshRenderer target = targetObject.AddComponent<MeshRenderer>();
            target.additionalVertexStreams = source.additionalVertexStreams;
            CopyRendererSettings(source, target);
        }

        private static void CloneSkinnedRenderer(
            SkinnedMeshRenderer source,
            GameObject targetObject,
            IDictionary<Transform, Transform> transformMap,
            string diagnosticId)
        {
            if (source.sharedMesh == null)
            {
                throw new InvalidDataException(
                    "The installed prefab SkinnedMeshRenderer has no shared " +
                    "mesh for " + diagnosticId + ".");
            }

            var targetBones = new Transform[source.bones.Length];
            for (int index = 0; index < source.bones.Length; index++)
            {
                Transform sourceBone = source.bones[index];
                if (sourceBone == null)
                {
                    targetBones[index] = null;
                    continue;
                }

                Transform targetBone;
                if (!transformMap.TryGetValue(sourceBone, out targetBone))
                {
                    throw new InvalidDataException(
                        "The installed prefab SkinnedMeshRenderer references a " +
                        "bone outside its prefab hierarchy for " +
                        diagnosticId + ".");
                }
                targetBones[index] = targetBone;
            }

            Transform targetRootBone = null;
            if (source.rootBone != null &&
                !transformMap.TryGetValue(
                    source.rootBone,
                    out targetRootBone))
            {
                throw new InvalidDataException(
                    "The installed prefab SkinnedMeshRenderer root bone is " +
                    "outside its prefab hierarchy for " + diagnosticId + ".");
            }

            SkinnedMeshRenderer target =
                targetObject.AddComponent<SkinnedMeshRenderer>();
            target.sharedMesh = source.sharedMesh;
            target.bones = targetBones;
            target.rootBone = targetRootBone;
            target.localBounds = source.localBounds;
            target.quality = source.quality;
            target.updateWhenOffscreen = true;
            target.skinnedMotionVectors = false;
            target.forceMatrixRecalculationPerRender = true;
            CopyRendererSettings(source, target);

            int blendShapeCount = source.sharedMesh.blendShapeCount;
            for (int index = 0; index < blendShapeCount; index++)
            {
                target.SetBlendShapeWeight(
                    index,
                    source.GetBlendShapeWeight(index));
            }
        }

        private static void CopyRendererSettings(
            Renderer source,
            Renderer target)
        {
            target.enabled = true;
            target.sharedMaterials = source.sharedMaterials;
            target.sortingLayerID = source.sortingLayerID;
            target.sortingOrder = source.sortingOrder;
            target.allowOcclusionWhenDynamic = false;
            target.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            target.forceRenderingOff = false;
        }

        private static void ReplaceInstalledMaterials(
            GameObject instance,
            List<Material> ownedMaterials,
            string diagnosticId)
        {
            Shader shader = ResolveInstalledTextureShader();
            var replacements = new Dictionary<Material, Material>();
            int visibleRendererCount = 0;
            foreach (Renderer renderer in
                instance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer is ParticleSystemRenderer ||
                    !IsLocallyActive(renderer.transform, instance.transform))
                {
                    continue;
                }

                renderer.forceRenderingOff = false;
                renderer.renderingLayerMask = uint.MaxValue;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

                Material[] sourceMaterials = renderer.sharedMaterials;
                if (sourceMaterials == null || sourceMaterials.Length == 0)
                {
                    throw new InvalidDataException(
                        "The installed prefab renderer has no material for " +
                        diagnosticId + ".");
                }

                var runtimeMaterials = new Material[sourceMaterials.Length];
                for (int index = 0; index < sourceMaterials.Length; index++)
                {
                    Material source = sourceMaterials[index];
                    if (source == null)
                    {
                        throw new InvalidDataException(
                            "The installed prefab uses a missing material for " +
                            diagnosticId + ".");
                    }

                    Material runtime;
                    if (!replacements.TryGetValue(source, out runtime))
                    {
                        runtime = CreateInstalledMaterial(shader, source);
                        replacements.Add(source, runtime);
                        ownedMaterials.Add(runtime);
                    }
                    runtimeMaterials[index] = runtime;
                }
                renderer.sharedMaterials = runtimeMaterials;
                if (renderer.enabled)
                {
                    visibleRendererCount++;
                }
            }

            if (visibleRendererCount == 0)
            {
                throw new InvalidDataException(
                    "The installed prefab has no enabled static renderer for " +
                    diagnosticId + ".");
            }
        }

        private static Material CreateInstalledMaterial(
            Shader shader,
            Material source)
        {
            var material = new Material(shader)
            {
                name = source.name + " (Desktop Pet)",
                hideFlags = HideFlags.DontSave,
                renderQueue = 2000
            };

            Texture texture = null;
            string sourceTextureProperty = null;
            if (source.HasProperty("_MainTex"))
            {
                texture = source.GetTexture("_MainTex");
                sourceTextureProperty = "_MainTex";
            }
            if (texture == null && source.HasProperty("_BaseMap"))
            {
                texture = source.GetTexture("_BaseMap");
                sourceTextureProperty = "_BaseMap";
            }
            if (texture != null)
            {
                Vector2 scale = source.GetTextureScale(sourceTextureProperty);
                Vector2 offset = source.GetTextureOffset(sourceTextureProperty);
                if (material.HasProperty("_MainTex"))
                {
                    material.SetTexture("_MainTex", texture);
                    material.SetTextureScale("_MainTex", scale);
                    material.SetTextureOffset("_MainTex", offset);
                }
                if (material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", texture);
                    material.SetTextureScale("_BaseMap", scale);
                    material.SetTextureOffset("_BaseMap", offset);
                }
            }
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 0.0f);
            }
            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 1.0f);
            }
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            return material;
        }

        private static Shader ResolveInstalledTextureShader()
        {
            Shader shader = Resources.Load<Shader>("StudyPropUnlit");
            if (GraphicsSettings.currentRenderPipeline != null && shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Texture");
            }
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null || !shader.isSupported)
            {
                throw new InvalidOperationException(
                    "No compatible texture shader is available for the " +
                    "installed vignette prefab.");
            }
            return shader;
        }

        private static Bounds CalculateFiniteGeometryBounds(
            Transform boundsRoot,
            Transform instanceRoot,
            Renderer[] renderers,
            string diagnosticId)
        {
            Bounds combined = default(Bounds);
            bool found = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled ||
                    renderer.forceRenderingOff ||
                    renderer is ParticleSystemRenderer ||
                    !IsLocallyActive(renderer.transform, instanceRoot))
                {
                    continue;
                }

                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                if (skinned != null)
                {
                    Mesh skinnedMesh = skinned.sharedMesh;
                    if (skinnedMesh == null)
                    {
                        continue;
                    }
                    EncapsulateLocalBounds(
                        boundsRoot,
                        skinned.transform,
                        skinnedMesh.bounds,
                        ref combined,
                        ref found);
                    continue;
                }

                Mesh mesh = null;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null)
                {
                    mesh = filter.sharedMesh;
                }

                if (mesh != null)
                {
                    EncapsulateLocalBounds(
                        boundsRoot,
                        renderer.transform,
                        mesh.bounds,
                        ref combined,
                        ref found);
                    continue;
                }

                Bounds worldBounds = renderer.bounds;
                if (!IsFinite(worldBounds.center) ||
                    !IsFinite(worldBounds.size))
                {
                    continue;
                }
                EncapsulateWorldBounds(
                    boundsRoot,
                    worldBounds,
                    ref combined,
                    ref found);
            }

            if (!found || !IsFinite(combined.center) ||
                !IsFinite(combined.size) ||
                Mathf.Max(
                    combined.size.x,
                    Mathf.Max(combined.size.y, combined.size.z)) <=
                    MinimumBoundsSize)
            {
                throw new InvalidDataException(
                    "The installed prefab has no finite visible renderer bounds " +
                    "for " + diagnosticId + ".");
            }
            return combined;
        }

        private static void EncapsulateLocalBounds(
            Transform boundsRoot,
            Transform geometryTransform,
            Bounds geometryBounds,
            ref Bounds combined,
            ref bool found)
        {
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 localCorner = new Vector3(
                            x == 0 ? geometryBounds.min.x : geometryBounds.max.x,
                            y == 0 ? geometryBounds.min.y : geometryBounds.max.y,
                            z == 0 ? geometryBounds.min.z : geometryBounds.max.z);
                        Vector3 rootCorner = boundsRoot.InverseTransformPoint(
                            geometryTransform.TransformPoint(localCorner));
                        EncapsulatePoint(rootCorner, ref combined, ref found);
                    }
                }
            }
        }

        private static void EncapsulateWorldBounds(
            Transform boundsRoot,
            Bounds worldBounds,
            ref Bounds combined,
            ref bool found)
        {
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 worldCorner = new Vector3(
                            x == 0 ? worldBounds.min.x : worldBounds.max.x,
                            y == 0 ? worldBounds.min.y : worldBounds.max.y,
                            z == 0 ? worldBounds.min.z : worldBounds.max.z);
                        EncapsulatePoint(
                            boundsRoot.InverseTransformPoint(worldCorner),
                            ref combined,
                            ref found);
                    }
                }
            }
        }

        private static void EncapsulatePoint(
            Vector3 point,
            ref Bounds combined,
            ref bool found)
        {
            if (!IsFinite(point))
            {
                return;
            }
            if (!found)
            {
                combined = new Bounds(point, Vector3.zero);
                found = true;
            }
            else
            {
                combined.Encapsulate(point);
            }
        }

        private static Transform CreatePivot(
            string name,
            Transform parent,
            int layer)
        {
            var pivot = new GameObject(name)
            {
                hideFlags = HideFlags.DontSave,
                layer = layer
            };
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.localRotation = Quaternion.identity;
            pivot.transform.localScale = Vector3.one;
            return pivot.transform;
        }

        private void LogSummary(string state)
        {
            Debug.Log(
                "Installed vignette prefab " + state + ": id=" +
                _diagnosticId + ", renderers=" + RendererCount +
                ", sourceCenter=" + FormatVector(_sourceBounds.center) +
                ", sourceSize=" + FormatVector(_sourceBounds.size) + ".");
        }

        private static bool IsLocallyActive(Transform child, Transform root)
        {
            Transform current = child;
            while (current != null)
            {
                if (!current.gameObject.activeSelf)
                {
                    return false;
                }
                if (current == root)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        private static bool IsPathFreeName(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                value.IndexOf('/') < 0 && value.IndexOf('\\') < 0;
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                value > 0.0f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z) &&
                !float.IsNaN(value.w) && !float.IsInfinity(value.w);
        }

        private static string FormatVector(Vector3 value)
        {
            return "(" + value.x.ToString("0.###") + "," +
                value.y.ToString("0.###") + "," +
                value.z.ToString("0.###") + ")";
        }

        private static void DestroyOwnedObject(GameObject value)
        {
            if (value == null)
            {
                return;
            }
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(value);
                return;
            }
#endif
            UnityEngine.Object.Destroy(value);
        }

        private static void DestroyOwnedMaterials(
            IEnumerable<Material> materials)
        {
            if (materials == null)
            {
                return;
            }
            foreach (Material material in materials)
            {
                if (material == null)
                {
                    continue;
                }
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    UnityEngine.Object.DestroyImmediate(material);
                    continue;
                }
#endif
                UnityEngine.Object.Destroy(material);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed || _root == null)
            {
                throw new ObjectDisposedException("InstalledVignettePrefab");
            }
        }
    }
}
