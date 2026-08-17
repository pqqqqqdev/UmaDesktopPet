using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UmaDesktopPet.Standalone.Core;
using UmaDesktopPet.Standalone.Runtime;

namespace UmaDesktopPet.Standalone.Editor
{
    /// <summary>
    /// Produces a text-only structural report for the installed Oguri mini bundles.
    /// It does not serialize, copy, or export any game-derived Unity object.
    /// </summary>
    public static class OguriBundleDiagnostics
    {
        private const int OguriCharacterId = 1006;
        private const string BodyLogicalName =
            "3d/chara/mini/body/mbdy1006_00/pfb_mbdy1006_00";
        private const string HairLogicalName =
            "3d/chara/mini/head/mchr1006_00/pfb_mchr1006_00_hair";
        private const string FaceLogicalName =
            "3d/chara/mini/head/mchr0001_00/pfb_mchr0001_00_face0";
        private const string IdleLogicalName =
            "3d/motion/mini/event/body/chara/chr1006_00/" +
            "anm_min_eve_chr1006_00_idle01_loop";
        private const string OguriHeadTexturePrefix =
            "3d/chara/mini/head/mchr1006_00/textures/";

        [MenuItem("Tools/Uma Desktop Pet/Dump Oguri bundle structure")]
        public static void Run()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string gameRoot = FindGameRoot();
            string outputPath = ReadArgument("-umaDiagnosticOutput");
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = Path.Combine(
                    projectRoot,
                    "..",
                    "..",
                    "artifacts",
                    "standalone",
                    "diagnostics",
                    "oguri-bundle-structure.txt");
            }
            outputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            string sqliteLibraryPath = Path.Combine(
                Application.dataPath,
                "Plugins",
                "x86_64",
                "sqlite3mc_x64.dll");

            using (var writer = new StreamWriter(
                outputPath,
                false,
                new UTF8Encoding(false)))
            using (GameDataCatalog catalog = GameDataCatalog.Open(
                gameRoot,
                sqliteLibraryPath))
            using (var repository = new BundleRepository(catalog))
            {
                WriteHeader(writer, "RUN");
                writer.WriteLine("Generated UTC: " + DateTime.UtcNow.ToString("O"));
                writer.WriteLine("Unity: " + Application.unityVersion);
                writer.WriteLine("Region: " + catalog.Region);
                writer.WriteLine("Game root: " + gameRoot);
                writer.WriteLine();

                CharacterRecord character = catalog.GetCharacter(OguriCharacterId);
                string skin = string.IsNullOrWhiteSpace(character.Skin)
                    ? "0"
                    : character.Skin;
                string tailId = character.TailModelId.ToString("0000");
                string tailLogicalName =
                    "3d/chara/mini/tail/mtail" + tailId +
                    "_00/pfb_mtail" + tailId + "_00";
                string tailClothLogicalName =
                    "3d/chara/mini/tail/mtail" + tailId +
                    "_00/clothes/pfb_mtail" + tailId + "_00_cloth00";
                string sharedFaceTexturePrefix =
                    "3d/chara/mini/head/mchr0001_00/textures/" +
                    "tex_mchr0001_00_face0_" + skin;
                string tailTexturePrefix =
                    "3d/chara/mini/tail/mtail" + tailId + "_00/textures/" +
                    "tex_mtail" + tailId + "_00_" +
                    OguriCharacterId.ToString("0000") + "_diff";

                var rootNames = new List<string>
                {
                    BodyLogicalName,
                    HairLogicalName,
                    FaceLogicalName,
                    tailLogicalName,
                    IdleLogicalName
                };
                GameAssetRecord tailClothRecord;
                if (catalog.TryGetAsset(tailClothLogicalName, out tailClothRecord))
                {
                    rootNames.Add(tailClothLogicalName);
                }
                AddMatches(catalog, rootNames, OguriHeadTexturePrefix);
                AddMatches(catalog, rootNames, sharedFaceTexturePrefix);
                AddMatches(catalog, rootNames, tailTexturePrefix);
                rootNames = rootNames.Distinct(StringComparer.Ordinal).ToList();

                writer.WriteLine("Character id: " + character.Id);
                writer.WriteLine("Skin: " + skin);
                writer.WriteLine("Tail model id: " + character.TailModelId);
                writer.WriteLine("Requested roots: " + rootNames.Count);
                foreach (string rootName in rootNames)
                {
                    writer.WriteLine("  " + rootName);
                }
                writer.WriteLine();

                using (BundleLease lease = repository.AcquireManyWithShaderFirst(rootNames))
                {
                    WriteHeader(writer, "LOAD ORDER");
                    for (int index = 0; index < lease.LoadedBundleNames.Count; index++)
                    {
                        writer.WriteLine(
                            index.ToString("000", CultureInfo.InvariantCulture) +
                            "  " + lease.LoadedBundleNames[index]);
                    }
                    writer.WriteLine();

                    var requestedBundles = new HashSet<string>(rootNames, StringComparer.Ordinal);
                    requestedBundles.Add(BundleRepository.DefaultShaderBundleName);
                    foreach (string logicalName in requestedBundles.OrderBy(
                        value => value,
                        StringComparer.Ordinal))
                    {
                        AssetBundle bundle;
                        if (lease.TryGetBundle(logicalName, out bundle))
                        {
                            DumpBundleContents(writer, logicalName, bundle);
                        }
                    }

                    GameObject body = LoadRequiredAsset<GameObject>(
                        lease,
                        BodyLogicalName);
                    GameObject hair = LoadRequiredAsset<GameObject>(
                        lease,
                        HairLogicalName);
                    GameObject face = LoadRequiredAsset<GameObject>(
                        lease,
                        FaceLogicalName);
                    GameObject tail = LoadRequiredAsset<GameObject>(
                        lease,
                        tailLogicalName);
                    AnimationClip idle = LoadRequiredAsset<AnimationClip>(
                        lease,
                        IdleLogicalName);

                    DumpPrefab(writer, "BODY", BodyLogicalName, body);
                    DumpPrefab(writer, "HAIR", HairLogicalName, hair);
                    DumpPrefab(writer, "FACE", FaceLogicalName, face);
                    DumpPrefab(writer, "TAIL", tailLogicalName, tail);
                    if (catalog.TryGetAsset(tailClothLogicalName, out tailClothRecord))
                    {
                        GameObject cloth = LoadRequiredAsset<GameObject>(
                            lease,
                            tailClothLogicalName);
                        DumpPrefab(
                            writer,
                            "TAIL CLOTH",
                            tailClothLogicalName,
                            cloth);
                    }

                    DumpSkeletonCompatibility(writer, body, hair, face, tail);
                    DumpAnimation(writer, body, idle);
                    DumpNameOnlyRebindSimulation(
                        writer,
                        body,
                        hair,
                        face,
                        tail,
                        idle);
                }
            }

            Debug.Log("Wrote text-only Oguri bundle diagnostics: " + outputPath);
        }

        private static void AddMatches(
            GameDataCatalog catalog,
            ICollection<string> names,
            string prefix)
        {
            foreach (GameAssetRecord record in catalog.FindByPrefix(prefix))
            {
                names.Add(record.Name);
            }
        }

        private static void DumpBundleContents(
            TextWriter writer,
            string logicalName,
            AssetBundle bundle)
        {
            WriteHeader(writer, "BUNDLE " + logicalName);
            writer.WriteLine("Unity bundle name: " + bundle.name);
            string[] assetNames = bundle.GetAllAssetNames();
            writer.WriteLine("Assets: " + assetNames.Length);
            foreach (string assetName in assetNames.OrderBy(
                value => value,
                StringComparer.Ordinal))
            {
                writer.WriteLine("  " + assetName);
            }
            writer.WriteLine();
        }

        private static void DumpPrefab(
            TextWriter writer,
            string label,
            string logicalName,
            GameObject prefab)
        {
            WriteHeader(writer, label + " PREFAB");
            writer.WriteLine("Logical name: " + logicalName);
            writer.WriteLine("Object name: " + prefab.name);
            writer.WriteLine("Active: " + prefab.activeSelf);
            writer.WriteLine("Root transform: " + FormatTransform(prefab.transform));
            writer.WriteLine();

            writer.WriteLine("HIERARCHY");
            Transform[] transforms = prefab.GetComponentsInChildren<Transform>(true);
            foreach (Transform transform in transforms.OrderBy(
                item => TransformPath(prefab.transform, item),
                StringComparer.Ordinal))
            {
                Component[] components = transform.GetComponents<Component>();
                string componentTypes = string.Join(
                    ", ",
                    components.Select(
                        component => component == null
                            ? "<missing-script>"
                            : component.GetType().FullName).ToArray());
                writer.WriteLine(
                    "  " + TransformPath(prefab.transform, transform) +
                    " | active=" + transform.gameObject.activeSelf +
                    " | " + FormatTransform(transform) +
                    " | components=" + componentTypes);
            }
            writer.WriteLine();

            Animator[] animators = prefab.GetComponentsInChildren<Animator>(true);
            writer.WriteLine("ANIMATORS: " + animators.Length);
            foreach (Animator animator in animators)
            {
                writer.WriteLine(
                    "  path=" + TransformPath(prefab.transform, animator.transform) +
                    " enabled=" + animator.enabled +
                    " avatar=" + ObjectName(animator.avatar) +
                    " controller=" + ObjectName(animator.runtimeAnimatorController) +
                    " rootPosition=" + FormatVector3(animator.rootPosition) +
                    " rootRotation=" + FormatQuaternion(animator.rootRotation));
            }
            writer.WriteLine();

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            writer.WriteLine("MESH FILTERS: " + filters.Length);
            foreach (MeshFilter filter in filters)
            {
                writer.WriteLine(
                    "  path=" + TransformPath(prefab.transform, filter.transform) +
                    " mesh=" + FormatMesh(filter.sharedMesh));
            }
            writer.WriteLine();

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            writer.WriteLine("RENDERERS: " + renderers.Length);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                DumpRenderer(writer, prefab.transform, rendererIndex, renderers[rendererIndex]);
            }
            writer.WriteLine();
        }

        private static void DumpRenderer(
            TextWriter writer,
            Transform prefabRoot,
            int rendererIndex,
            Renderer renderer)
        {
            writer.WriteLine(
                "  RENDERER " + rendererIndex +
                " type=" + renderer.GetType().FullName +
                " path=" + TransformPath(prefabRoot, renderer.transform));
            writer.WriteLine(
                "    enabled=" + renderer.enabled +
                " forceOff=" + renderer.forceRenderingOff +
                " bounds=" + FormatBounds(renderer.bounds) +
                " sortingLayer=" + renderer.sortingLayerName +
                " sortingOrder=" + renderer.sortingOrder);

            var skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null)
            {
                writer.WriteLine(
                    "    mesh=" + FormatMesh(skinned.sharedMesh) +
                    " localBounds=" + FormatBounds(skinned.localBounds) +
                    " updateWhenOffscreen=" + skinned.updateWhenOffscreen +
                    " quality=" + skinned.quality +
                    " rootBone=" + TransformPathOrExternal(prefabRoot, skinned.rootBone));
                Transform[] bones = skinned.bones;
                writer.WriteLine("    bones=" + bones.Length);
                for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
                {
                    writer.WriteLine(
                        "      [" + boneIndex.ToString("000", CultureInfo.InvariantCulture) +
                        "] " + TransformPathOrExternal(prefabRoot, bones[boneIndex]));
                }
            }

            Material[] materials = renderer.sharedMaterials;
            writer.WriteLine("    materials=" + materials.Length);
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                DumpMaterial(writer, materialIndex, materials[materialIndex]);
            }
        }

        private static void DumpMaterial(
            TextWriter writer,
            int materialIndex,
            Material material)
        {
            if (material == null)
            {
                writer.WriteLine("      MATERIAL " + materialIndex + " <null>");
                return;
            }

            Shader shader = material.shader;
            writer.WriteLine(
                "      MATERIAL " + materialIndex +
                " name=" + material.name +
                " shader=" + ObjectName(shader) +
                " shaderSupported=" + (shader != null && shader.isSupported) +
                " passCount=" + material.passCount +
                " queue=" + material.renderQueue +
                " instancing=" + material.enableInstancing +
                " keywords=" + string.Join(",", material.shaderKeywords));
            if (shader == null)
            {
                return;
            }

            for (int passIndex = 0; passIndex < material.passCount; passIndex++)
            {
                string passName = material.GetPassName(passIndex);
                writer.WriteLine(
                    "        pass[" + passIndex + "] name=" + passName +
                    " enabled=" + material.GetShaderPassEnabled(passName) +
                    " lightMode=" + shader.FindPassTagValue(
                        passIndex,
                        new ShaderTagId("LightMode")).name);
            }

            try
            {
                int propertyCount = ShaderUtil.GetPropertyCount(shader);
                for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
                {
                    string propertyName = ShaderUtil.GetPropertyName(shader, propertyIndex);
                    ShaderUtil.ShaderPropertyType propertyType =
                        ShaderUtil.GetPropertyType(shader, propertyIndex);
                    switch (propertyType)
                    {
                        case ShaderUtil.ShaderPropertyType.TexEnv:
                            Texture texture = material.GetTexture(propertyName);
                            writer.WriteLine(
                                "        texture " + propertyName + "=" +
                                FormatTexture(texture) +
                                " scale=" + FormatVector2(
                                    material.GetTextureScale(propertyName)) +
                                " offset=" + FormatVector2(
                                    material.GetTextureOffset(propertyName)));
                            break;
                        case ShaderUtil.ShaderPropertyType.Color:
                            Color color = material.GetColor(propertyName);
                            writer.WriteLine(
                                "        color " + propertyName + "=(" +
                                color.r.ToString("R", CultureInfo.InvariantCulture) + "," +
                                color.g.ToString("R", CultureInfo.InvariantCulture) + "," +
                                color.b.ToString("R", CultureInfo.InvariantCulture) + "," +
                                color.a.ToString("R", CultureInfo.InvariantCulture) + ")");
                            break;
                        case ShaderUtil.ShaderPropertyType.Vector:
                            Vector4 vector = material.GetVector(propertyName);
                            writer.WriteLine(
                                "        vector " + propertyName + "=(" +
                                vector.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                                vector.y.ToString("R", CultureInfo.InvariantCulture) + "," +
                                vector.z.ToString("R", CultureInfo.InvariantCulture) + "," +
                                vector.w.ToString("R", CultureInfo.InvariantCulture) + ")");
                            break;
                        default:
                            writer.WriteLine(
                                "        float " + propertyName + "=" +
                                material.GetFloat(propertyName).ToString(
                                    "R",
                                    CultureInfo.InvariantCulture));
                            break;
                    }
                }
            }
            catch (Exception exception)
            {
                writer.WriteLine(
                    "        <shader property inspection failed: " +
                    exception.GetType().Name + ": " + exception.Message + ">");
            }
        }

        private static void DumpSkeletonCompatibility(
            TextWriter writer,
            GameObject body,
            params GameObject[] parts)
        {
            WriteHeader(writer, "SKELETON COMPATIBILITY");
            Dictionary<string, List<Transform>> bodyByName = body
                .GetComponentsInChildren<Transform>(true)
                .GroupBy(transform => transform.name, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    StringComparer.Ordinal);

            writer.WriteLine("Body transforms: " + bodyByName.Values.Sum(list => list.Count));
            var duplicates = bodyByName
                .Where(pair => pair.Value.Count > 1)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToList();
            writer.WriteLine("Duplicate body bone names: " + duplicates.Count);
            foreach (KeyValuePair<string, List<Transform>> duplicate in duplicates)
            {
                writer.WriteLine("  " + duplicate.Key + " => " + string.Join(
                    " | ",
                    duplicate.Value.Select(
                        transform => TransformPath(body.transform, transform)).ToArray()));
            }
            writer.WriteLine();

            foreach (GameObject part in parts)
            {
                writer.WriteLine("PART " + part.name);
                int matched = 0;
                var unmatched = new SortedSet<string>(StringComparer.Ordinal);
                var ambiguous = new SortedSet<string>(StringComparer.Ordinal);
                foreach (SkinnedMeshRenderer renderer in
                    part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    writer.WriteLine(
                        "  renderer=" + TransformPath(part.transform, renderer.transform) +
                        " rootBone=" + TransformPathOrExternal(part.transform, renderer.rootBone));
                    for (int index = 0; index < renderer.bones.Length; index++)
                    {
                        Transform source = renderer.bones[index];
                        if (source == null)
                        {
                            unmatched.Add("<null>");
                            continue;
                        }

                        List<Transform> targets;
                        if (!bodyByName.TryGetValue(source.name, out targets))
                        {
                            unmatched.Add(
                                source.name + " @ " +
                                TransformPathOrExternal(part.transform, source));
                            continue;
                        }

                        matched++;
                        if (targets.Count > 1)
                        {
                            ambiguous.Add(source.name);
                        }

                        Transform target = targets[0];
                        Vector3 sourceRelativePosition =
                            part.transform.InverseTransformPoint(source.position);
                        Vector3 targetRelativePosition =
                            body.transform.InverseTransformPoint(target.position);
                        float positionDelta = Vector3.Distance(
                            sourceRelativePosition,
                            targetRelativePosition);
                        if (positionDelta > 0.0001f)
                        {
                            writer.WriteLine(
                                "    bind-space difference bone=" + source.name +
                                " sourcePath=" + TransformPathOrExternal(
                                    part.transform,
                                    source) +
                                " targetPath=" + TransformPath(body.transform, target) +
                                " sourcePos=" + FormatVector3(sourceRelativePosition) +
                                " targetPos=" + FormatVector3(targetRelativePosition) +
                                " delta=" + FormatFloat(positionDelta));
                        }
                    }
                }
                writer.WriteLine("  matched renderer bones=" + matched);
                writer.WriteLine("  unmatched renderer bones=" + unmatched.Count);
                foreach (string item in unmatched)
                {
                    writer.WriteLine("    " + item);
                }
                writer.WriteLine("  ambiguous renderer bone names=" + ambiguous.Count);
                foreach (string item in ambiguous)
                {
                    writer.WriteLine("    " + item);
                }
                writer.WriteLine();
            }
        }

        private static void DumpAnimation(
            TextWriter writer,
            GameObject body,
            AnimationClip clip)
        {
            WriteHeader(writer, "IDLE ANIMATION");
            writer.WriteLine("Name: " + clip.name);
            writer.WriteLine("Length: " + FormatFloat(clip.length));
            writer.WriteLine("Frame rate: " + FormatFloat(clip.frameRate));
            writer.WriteLine("Legacy: " + clip.legacy);
            writer.WriteLine("Human motion: " + clip.humanMotion);
            writer.WriteLine("Empty: " + clip.empty);
            writer.WriteLine("Looping: " + clip.isLooping);
            writer.WriteLine("Has generic root transform: " + clip.hasGenericRootTransform);
            writer.WriteLine("Has motion curves: " + clip.hasMotionCurves);
            writer.WriteLine("Has root curves: " + clip.hasRootCurves);
            writer.WriteLine("Local bounds: " + FormatBounds(clip.localBounds));
            writer.WriteLine("Average speed: " + FormatVector3(clip.averageSpeed));
            writer.WriteLine(
                "Average angular speed: " + FormatFloat(clip.averageAngularSpeed));
            writer.WriteLine("Wrap mode: " + clip.wrapMode);

            Animator animator = body.GetComponentInChildren<Animator>(true);
            writer.WriteLine(
                "Body animator path: " +
                (animator == null
                    ? "<none>"
                    : TransformPath(body.transform, animator.transform)));

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            writer.WriteLine("Float curve bindings: " + bindings.Length);
            var missingPaths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (EditorCurveBinding binding in bindings.OrderBy(
                item => item.path + "\0" + item.propertyName,
                StringComparer.Ordinal))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                writer.WriteLine(
                    "  path='" + binding.path +
                    "' type=" + (binding.type == null ? "<null>" : binding.type.FullName) +
                    " property=" + binding.propertyName +
                    " keys=" + (curve == null ? 0 : curve.length) +
                    " time=" + CurveTimeRange(curve));
                if (animator != null &&
                    !string.IsNullOrEmpty(binding.path) &&
                    animator.transform.Find(binding.path) == null)
                {
                    missingPaths.Add(binding.path);
                }
            }

            EditorCurveBinding[] objectBindings =
                AnimationUtility.GetObjectReferenceCurveBindings(clip);
            writer.WriteLine("Object-reference bindings: " + objectBindings.Length);
            foreach (EditorCurveBinding binding in objectBindings.OrderBy(
                item => item.path + "\0" + item.propertyName,
                StringComparer.Ordinal))
            {
                ObjectReferenceKeyframe[] keys =
                    AnimationUtility.GetObjectReferenceCurve(clip, binding);
                writer.WriteLine(
                    "  path='" + binding.path +
                    "' type=" + (binding.type == null ? "<null>" : binding.type.FullName) +
                    " property=" + binding.propertyName +
                    " keys=" + (keys == null ? 0 : keys.Length));
            }

            writer.WriteLine("Missing binding paths under body Animator: " + missingPaths.Count);
            foreach (string missingPath in missingPaths)
            {
                writer.WriteLine("  " + missingPath);
            }

            AnimationEvent[] events = AnimationUtility.GetAnimationEvents(clip);
            writer.WriteLine("Events: " + events.Length);
            foreach (AnimationEvent animationEvent in events)
            {
                writer.WriteLine(
                    "  t=" + FormatFloat(animationEvent.time) +
                    " function=" + animationEvent.functionName +
                    " string=" + animationEvent.stringParameter +
                    " int=" + animationEvent.intParameter +
                    " float=" + FormatFloat(animationEvent.floatParameter));
            }
            writer.WriteLine();
        }

        private static void DumpNameOnlyRebindSimulation(
            TextWriter writer,
            GameObject bodyPrefab,
            GameObject hairPrefab,
            GameObject facePrefab,
            GameObject tailPrefab,
            AnimationClip idle)
        {
            WriteHeader(writer, "CURRENT NAME-ONLY REBIND SIMULATION");
            writer.WriteLine(
                "Transient in-memory clones only; no scene or game object is serialized.");

            var assemblyRoot = new GameObject("Diagnostic Assembly");
            try
            {
                GameObject body = InstantiateForDiagnostic(
                    bodyPrefab,
                    "Body",
                    assemblyRoot.transform);
                GameObject hair = InstantiateForDiagnostic(
                    hairPrefab,
                    "Hair",
                    assemblyRoot.transform);
                GameObject face = InstantiateForDiagnostic(
                    facePrefab,
                    "Face",
                    assemblyRoot.transform);
                GameObject tail = InstantiateForDiagnostic(
                    tailPrefab,
                    "Tail",
                    assemblyRoot.transform);

                Dictionary<string, Transform> bodyBones = body
                    .GetComponentsInChildren<Transform>(true)
                    .ToDictionary(transform => transform.name, StringComparer.Ordinal);
                ApplyNameOnlyRebind(hair, bodyBones);
                ApplyNameOnlyRebind(face, bodyBones);
                ApplyNameOnlyRebind(tail, bodyBones);

                Animator bodyAnimator = body.GetComponentInChildren<Animator>(true);
                if (bodyAnimator != null)
                {
                    bodyAnimator.runtimeAnimatorController = null;
                    bodyAnimator.Rebind();
                }

                DumpAssemblyBounds(writer, "After rebind, bind pose", body, hair, face, tail);
                idle.SampleAnimation(body, 0.0f);
                DumpAssemblyBounds(writer, "After sampling idle t=0", body, hair, face, tail);
                idle.SampleAnimation(body, Math.Min(1.0f, idle.length));
                DumpAssemblyBounds(writer, "After sampling idle t=1", body, hair, face, tail);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(assemblyRoot);
            }
            writer.WriteLine();
        }

        private static GameObject InstantiateForDiagnostic(
            GameObject prefab,
            string name,
            Transform parent)
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            instance.name = name;
            instance.SetActive(true);
            return instance;
        }

        private static void ApplyNameOnlyRebind(
            GameObject part,
            IDictionary<string, Transform> bodyBones)
        {
            foreach (Animator animator in part.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = false;
            }

            foreach (SkinnedMeshRenderer renderer in
                part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Transform[] rebound = renderer.bones.ToArray();
                for (int index = 0; index < rebound.Length; index++)
                {
                    Transform source = rebound[index];
                    Transform target;
                    if (source != null && bodyBones.TryGetValue(source.name, out target))
                    {
                        rebound[index] = target;
                    }
                }
                renderer.bones = rebound;

                Transform rootTarget;
                if (renderer.rootBone != null &&
                    bodyBones.TryGetValue(renderer.rootBone.name, out rootTarget))
                {
                    renderer.rootBone = rootTarget;
                }
            }
        }

        private static void DumpAssemblyBounds(
            TextWriter writer,
            string label,
            params GameObject[] parts)
        {
            writer.WriteLine(label);
            bool hasCombinedBounds = false;
            Bounds combinedBounds = new Bounds();
            foreach (GameObject part in parts)
            {
                writer.WriteLine(
                    "  PART " + part.name +
                    " root=" + FormatTransform(part.transform));
                foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
                {
                    Bounds bounds = renderer.bounds;
                    writer.WriteLine(
                        "    " + TransformPath(part.transform, renderer.transform) +
                        " " + renderer.GetType().Name +
                        " worldBounds=" + FormatBounds(bounds) +
                        BakedBounds(renderer));
                    if (!hasCombinedBounds)
                    {
                        combinedBounds = bounds;
                        hasCombinedBounds = true;
                    }
                    else
                    {
                        combinedBounds.Encapsulate(bounds);
                    }
                }
            }
            writer.WriteLine(
                "  COMBINED " +
                (hasCombinedBounds ? FormatBounds(combinedBounds) : "<none>"));
        }

        private static string BakedBounds(Renderer renderer)
        {
            var skinned = renderer as SkinnedMeshRenderer;
            if (skinned == null)
            {
                return string.Empty;
            }

            var baked = new Mesh();
            try
            {
                skinned.BakeMesh(baked);
                return " bakedLocalBounds=" + FormatBounds(baked.bounds);
            }
            catch (Exception exception)
            {
                return " bakedLocalBounds=<failed:" + exception.GetType().Name + ">";
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(baked);
            }
        }

        private static T LoadRequiredAsset<T>(BundleLease lease, string logicalName)
            where T : UnityEngine.Object
        {
            AssetBundle bundle = lease.GetRequiredBundle(logicalName);
            string expectedName = Path.GetFileName(logicalName);
            foreach (string assetName in bundle.GetAllAssetNames())
            {
                if (!string.Equals(
                    Path.GetFileNameWithoutExtension(assetName),
                    expectedName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                T exact = bundle.LoadAsset<T>(assetName);
                if (exact != null)
                {
                    return exact;
                }
            }

            T[] candidates = bundle.LoadAllAssets<T>();
            if (candidates == null || candidates.Length == 0)
            {
                throw new InvalidDataException(
                    "No " + typeof(T).Name + " in bundle " + logicalName);
            }
            return candidates.FirstOrDefault(
                candidate => string.Equals(
                    candidate.name,
                    expectedName,
                    StringComparison.OrdinalIgnoreCase)) ?? candidates[0];
        }

        private static string FindGameRoot()
        {
            var candidates = new List<string>
            {
                ReadArgument("-umaGameRoot"),
                Environment.GetEnvironmentVariable("UMA_DESKTOP_PET_GAME_ROOT")
            };
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                {
                    candidates.Add(Path.Combine(
                        drive.RootDirectory.FullName,
                        "Umamusume",
                        "umamusume_Data",
                        "Persistent"));
                }
            }

            foreach (string candidate in candidates.Where(
                value => !string.IsNullOrWhiteSpace(value)))
            {
                string fullPath = Path.GetFullPath(candidate);
                if (File.Exists(Path.Combine(fullPath, "meta")) &&
                    File.Exists(Path.Combine(fullPath, "master", "master.mdb")) &&
                    Directory.Exists(Path.Combine(fullPath, "dat")))
                {
                    return fullPath;
                }
            }

            throw new DirectoryNotFoundException(
                "Pass -umaGameRoot with the installed game's Persistent directory.");
        }

        private static string ReadArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[index + 1];
                }
            }
            return null;
        }

        private static string TransformPath(Transform root, Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }
            if (transform == root)
            {
                return "<root>";
            }
            return AnimationUtility.CalculateTransformPath(transform, root);
        }

        private static string TransformPathOrExternal(Transform root, Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }
            if (transform == root || transform.IsChildOf(root))
            {
                return TransformPath(root, transform);
            }
            return "<external:" + transform.name + ">";
        }

        private static string FormatTransform(Transform transform)
        {
            return "localPosition=" + FormatVector3(transform.localPosition) +
                " localRotation=" + FormatQuaternion(transform.localRotation) +
                " localEuler=" + FormatVector3(transform.localEulerAngles) +
                " localScale=" + FormatVector3(transform.localScale);
        }

        private static string FormatMesh(Mesh mesh)
        {
            return mesh == null
                ? "<null>"
                : mesh.name +
                    " vertices=" + mesh.vertexCount +
                    " subMeshes=" + mesh.subMeshCount +
                    " bindposes=" + mesh.bindposes.Length +
                    " bounds=" + FormatBounds(mesh.bounds);
        }

        private static string FormatTexture(Texture texture)
        {
            if (texture == null)
            {
                return "<null>";
            }

            var texture2D = texture as Texture2D;
            string format = texture2D == null ? string.Empty : " format=" + texture2D.format;
            return texture.name +
                " type=" + texture.GetType().Name +
                " size=" + texture.width + "x" + texture.height +
                " dimension=" + texture.dimension + format;
        }

        private static string FormatBounds(Bounds bounds)
        {
            return "center=" + FormatVector3(bounds.center) +
                " size=" + FormatVector3(bounds.size);
        }

        private static string FormatVector2(Vector2 value)
        {
            return "(" + FormatFloat(value.x) + "," + FormatFloat(value.y) + ")";
        }

        private static string FormatVector3(Vector3 value)
        {
            return "(" + FormatFloat(value.x) + "," + FormatFloat(value.y) + "," +
                FormatFloat(value.z) + ")";
        }

        private static string FormatQuaternion(Quaternion value)
        {
            return "(" + FormatFloat(value.x) + "," + FormatFloat(value.y) + "," +
                FormatFloat(value.z) + "," + FormatFloat(value.w) + ")";
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string CurveTimeRange(AnimationCurve curve)
        {
            return curve == null || curve.length == 0
                ? "<none>"
                : FormatFloat(curve.keys[0].time) + ".." +
                    FormatFloat(curve.keys[curve.length - 1].time);
        }

        private static string ObjectName(UnityEngine.Object value)
        {
            return value == null
                ? "<null>"
                : value.name + " (" + value.GetType().FullName + ")";
        }

        private static void WriteHeader(TextWriter writer, string text)
        {
            writer.WriteLine(new string('=', 80));
            writer.WriteLine(text);
            writer.WriteLine(new string('=', 80));
        }
    }
}
