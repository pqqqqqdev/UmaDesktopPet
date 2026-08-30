using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UmaDesktopPet.Standalone.Core;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Assembles an explicitly profiled local mini-character from game AssetBundles.
    /// The assembler only holds live Unity objects; it never exports game-derived data.
    /// </summary>
    public sealed class MiniCharacterAssembler
    {
        public const int OguriCharacterId = 1006;

        private readonly GameDataCatalog _catalog;
        private readonly BundleRepository _bundles;

        public MiniCharacterAssembler(GameDataCatalog catalog, BundleRepository bundles)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException("catalog");
            }
            if (bundles == null)
            {
                throw new ArgumentNullException("bundles");
            }

            _catalog = catalog;
            _bundles = bundles;
        }

        public MiniCharacterInstance AssembleOguri(Transform parent)
        {
            return AssembleInstalled(
                MiniCharacterAssemblyProfiles.OguriDefault,
                parent);
        }

        public MiniCharacterInstance AssembleInstalled(
            MiniCharacterAssemblyProfile baseProfile,
            Transform parent)
        {
            if (baseProfile == null)
            {
                throw new ArgumentNullException("baseProfile");
            }

            CharacterRecord character = _catalog.GetCharacter(
                baseProfile.CharacterId);
            int skin = ResolveInstalledSkin(
                character.Skin,
                baseProfile.Skin);
            int? tailModelId = character.TailModelId > 0
                ? (int?)character.TailModelId
                : null;
            MiniCharacterAssemblyProfile profile =
                baseProfile.WithInstalledAppearance(
                    skin,
                    tailModelId,
                    null);
            return Assemble(profile, parent);
        }

        public MiniCharacterInstance Assemble(
            MiniCharacterAssemblyProfile profile,
            Transform parent)
        {
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }
            if (profile.HasSeparateModelAttachment)
            {
                // Positive attachment_model_id naming has not been verified yet.
                // Refuse it before opening any bundles rather than guessing a path.
                throw new NotSupportedException(
                    "Separate mini-character model attachments are not supported " +
                    "until their installed naming convention is verified.");
            }

            List<string> headTextureNames = FindMatches(profile.HeadTexturePrefix);
            headTextureNames.AddRange(FindMatches(profile.SharedFaceTexturePrefix));
            headTextureNames = headTextureNames
                .Distinct(StringComparer.Ordinal)
                .ToList();
            List<string> tailTextureNames = profile.HasTail
                ? FindMatches(profile.TailTexturePrefix)
                : new List<string>();

            // Dynamic textures must be loaded before the prefabs that reference them.
            // Keep the texture roots first so the repository opens those bundles first too.
            var rootNames = new List<string>();
            rootNames.AddRange(headTextureNames);
            rootNames.AddRange(tailTextureNames);
            rootNames.AddRange(profile.RequiredRootLogicalNames);
            rootNames = rootNames.Distinct(StringComparer.Ordinal).ToList();

            BundleLease lease = _bundles.AcquireManyWithShaderFirst(rootNames);
            GameObject characterRoot = null;
            bool leaseTransferred = false;
            try
            {
                List<Texture2D> headTextures = LoadTextures(lease, headTextureNames);
                List<Texture2D> tailTextures = LoadTextures(lease, tailTextureNames);

                GameObject bodyPrefab =
                    LoadRequiredAsset<GameObject>(lease, profile.BodyAsset);
                GameObject hairPrefab =
                    LoadRequiredAsset<GameObject>(lease, profile.HairAsset);
                GameObject facePrefab =
                    LoadRequiredAsset<GameObject>(lease, profile.FaceAsset);
                GameObject tailPrefab = profile.HasTail
                    ? LoadRequiredAsset<GameObject>(lease, profile.TailAsset)
                    : null;
                AnimationClip idleClip =
                    LoadRequiredAsset<AnimationClip>(lease, profile.IdleAsset);

                characterRoot = new GameObject(profile.DisplayName);
                if (parent != null)
                {
                    characterRoot.transform.SetParent(parent, false);
                }

                GameObject body = InstantiatePart(bodyPrefab, characterRoot.transform);
                GameObject hair = InstantiatePart(hairPrefab, characterRoot.transform);
                GameObject face = InstantiatePart(facePrefab, characterRoot.transform);
                GameObject tail = tailPrefab != null
                    ? InstantiatePart(tailPrefab, characterRoot.transform)
                    : null;

                AssignMiniFaceTextures(face, headTextures);
                if (tail != null)
                {
                    AssignTailTextures(tail, tailTextures);
                }

                Animator animator = MergeCharacterParts(
                    characterRoot,
                    body,
                    hair,
                    face,
                    tail);

                PlayableIdleController idleController =
                    characterRoot.AddComponent<PlayableIdleController>();
                idleController.Play(animator, idleClip);

                MiniFaceExpressionController faceController =
                    characterRoot.AddComponent<MiniFaceExpressionController>();
                faceController.Initialize(characterRoot, headTextures);

                MiniCharacterInstance instance =
                    characterRoot.AddComponent<MiniCharacterInstance>();
                instance.Initialize(lease, animator, idleController, faceController);
                leaseTransferred = true;
                return instance;
            }
            catch
            {
                if (characterRoot != null)
                {
                    characterRoot.SetActive(false);
                    UnityEngine.Object.Destroy(characterRoot);
                }
                throw;
            }
            finally
            {
                if (!leaseTransferred)
                {
                    lease.Dispose();
                }
            }
        }

        internal static int ResolveInstalledSkin(
            string value,
            int fallbackSkin)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallbackSkin;
            }

            int skin;
            if (!int.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out skin) || skin < 0 || skin > 99)
            {
                throw new InvalidDataException(
                    "The installed character has an unsupported mini skin ID.");
            }
            return skin;
        }

        private List<string> FindMatches(string prefix)
        {
            return _catalog.FindByPrefix(prefix)
                .Select(record => record.Name)
                .ToList();
        }

        private static GameObject InstantiatePart(GameObject prefab, Transform parent)
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            instance.SetActive(true);
            return instance;
        }

        private static Animator MergeCharacterParts(
            GameObject characterRoot,
            GameObject body,
            GameObject hair,
            GameObject face,
            GameObject tail)
        {
            SkinnedMeshRenderer bodyRenderer =
                body.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (bodyRenderer == null || bodyRenderer.bones == null || bodyRenderer.bones.Length == 0)
            {
                throw new InvalidDataException(
                    "The installed mini-character body does not expose a skinned " +
                    "body skeleton.");
            }

            BodySkeleton skeleton = BodySkeleton.FromRenderer(bodyRenderer);
            var retiredBones = new List<Transform>();

            // The body prefab is only a delivery wrapper. Its Position hierarchy must sit
            // directly below the final Animator so the motion clip's binding paths resolve.
            Transform unusedTailControl = body.transform.Find("Position/Hip/Tail_Ctrl");
            if (unusedTailControl != null)
            {
                retiredBones.Add(unusedTailControl);
            }
            MoveRootChildren(body.transform, characterRoot.transform);
            body.SetActive(false);

            RebindRenderers(face, skeleton, retiredBones);
            GameObject faceInfo = new GameObject("Face Info");
            faceInfo.transform.SetParent(characterRoot.transform, false);
            MoveRootChildren(
                face.transform,
                characterRoot.transform,
                faceInfo.transform);
            face.SetActive(false);

            RebindRenderers(hair, skeleton, retiredBones);
            MoveRootChildren(hair.transform, characterRoot.transform);
            hair.SetActive(false);

            if (tail != null)
            {
                RebindRenderers(tail, skeleton, retiredBones);
                MoveRootChildren(tail.transform, characterRoot.transform);
                tail.SetActive(false);
            }

            foreach (Animator prefabAnimator in
                characterRoot.GetComponentsInChildren<Animator>(true))
            {
                prefabAnimator.enabled = false;
            }

            GameObject retiredRoot = new GameObject("Retired Prefab Bones");
            retiredRoot.transform.SetParent(characterRoot.transform, false);
            foreach (Transform retiredBone in retiredBones.Distinct())
            {
                if (retiredBone == null)
                {
                    continue;
                }
                retiredBone.SetParent(retiredRoot.transform, true);
            }
            retiredRoot.SetActive(false);
            UnityEngine.Object.Destroy(retiredRoot);

            Animator animator = characterRoot.AddComponent<Animator>();
            animator.avatar = AvatarBuilder.BuildGenericAvatar(
                characterRoot,
                characterRoot.name);
            if (animator.avatar == null)
            {
                throw new InvalidDataException(
                    "Unity could not create the mini-character's runtime avatar.");
            }
            return animator;
        }

        private static void RebindRenderers(
            GameObject part,
            BodySkeleton skeleton,
            List<Transform> retiredBones)
        {
            foreach (SkinnedMeshRenderer renderer in
                part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Transform sourceRoot = renderer.rootBone;
                Transform targetRoot;
                if (sourceRoot == null || !skeleton.TryGet(sourceRoot.name, out targetRoot))
                {
                    continue;
                }

                Transform[] sourceBones = renderer.bones;
                Transform[] targetBones = new Transform[sourceBones.Length];
                for (int index = 0; index < sourceBones.Length; index++)
                {
                    Transform sourceBone = sourceBones[index];
                    Transform targetBone;
                    if (sourceBone == null || !skeleton.TryGet(sourceBone.name, out targetBone))
                    {
                        targetBones[index] = sourceBone;
                        continue;
                    }

                    // Attached meshes and locators can live below the source skeleton.
                    // Align that source joint, move its children to the live body joint, then
                    // retire only the now-empty duplicate joint.
                    sourceBone.position = targetBone.position;
                    while (sourceBone.childCount > 0)
                    {
                        sourceBone.GetChild(0).SetParent(targetBone, true);
                    }
                    retiredBones.Add(sourceBone);
                    targetBones[index] = targetBone;
                }

                renderer.rootBone = targetRoot;
                renderer.bones = targetBones;
            }
        }

        private static void MoveRootChildren(Transform source, Transform destination)
        {
            while (source.childCount > 0)
            {
                source.GetChild(0).SetParent(destination, true);
            }
        }

        private static void MoveRootChildren(
            Transform source,
            Transform destination,
            Transform infoDestination)
        {
            while (source.childCount > 0)
            {
                Transform child = source.GetChild(0);
                Transform target = child.name.IndexOf(
                    "info",
                    StringComparison.OrdinalIgnoreCase) >= 0
                        ? infoDestination
                        : destination;
                child.SetParent(target, true);
            }
        }

        private static void AssignMiniFaceTextures(
            GameObject face,
            IList<Texture2D> textures)
        {
            foreach (Renderer renderer in face.GetComponentsInChildren<Renderer>(true))
            {
                Texture2D texture = null;
                if (string.Equals(renderer.name, "M_Face", StringComparison.Ordinal))
                {
                    texture = FindTexture(textures, "face", "diff");
                }
                else if (string.Equals(renderer.name, "M_Cheek", StringComparison.Ordinal))
                {
                    texture = FindTexture(textures, "cheek");
                }
                else if (string.Equals(renderer.name, "M_Mouth", StringComparison.Ordinal))
                {
                    texture = FindTexture(textures, "mouth");
                }
                else if (string.Equals(renderer.name, "M_Eye", StringComparison.Ordinal))
                {
                    texture = FindTexture(textures, "eye");
                }
                else if (renderer.name.StartsWith("M_Mayu_", StringComparison.Ordinal))
                {
                    texture = FindTexture(textures, "mayu");
                }

                if (texture != null)
                {
                    SetRendererTexture(renderer, "_MainTex", texture);
                }
            }
        }

        private static void AssignTailTextures(
            GameObject tail,
            IList<Texture2D> textures)
        {
            Texture2D diffuse = FindTexture(textures, "diff");
            Texture2D shade = FindTexture(textures, "shad");
            Texture2D baseMask = FindTexture(textures, "base");
            Texture2D control = FindTexture(textures, "ctrl");

            foreach (Renderer renderer in tail.GetComponentsInChildren<Renderer>(true))
            {
                SetRendererTexture(renderer, "_MainTex", diffuse);
                SetRendererTexture(renderer, "_ToonMap", shade);
                SetRendererTexture(renderer, "_TripleMaskMap", baseMask);
                SetRendererTexture(renderer, "_OptionMaskMap", control);
            }
        }

        private static void SetRendererTexture(
            Renderer renderer,
            string property,
            Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            foreach (Material material in renderer.materials)
            {
                if (material != null && material.HasProperty(property))
                {
                    material.SetTexture(property, texture);
                }
            }
        }

        private static Texture2D FindTexture(
            IEnumerable<Texture2D> textures,
            params string[] requiredTokens)
        {
            foreach (Texture2D texture in textures)
            {
                if (texture == null)
                {
                    continue;
                }

                string name = texture.name;
                bool matches = true;
                for (int index = 0; index < requiredTokens.Length; index++)
                {
                    if (name.IndexOf(
                        requiredTokens[index],
                        StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches)
                {
                    return texture;
                }
            }
            return null;
        }

        private static List<Texture2D> LoadTextures(
            BundleLease lease,
            IEnumerable<string> logicalNames)
        {
            var result = new List<Texture2D>();
            var seen = new HashSet<int>();
            foreach (string logicalName in logicalNames)
            {
                AssetBundle bundle = lease.GetRequiredBundle(logicalName);
                foreach (Texture2D texture in bundle.LoadAllAssets<Texture2D>())
                {
                    if (texture != null && seen.Add(texture.GetInstanceID()))
                    {
                        result.Add(texture);
                    }
                }
            }
            return result;
        }

        private static T LoadRequiredAsset<T>(BundleLease lease, string logicalName)
            where T : UnityEngine.Object
        {
            AssetBundle bundle = lease.GetRequiredBundle(logicalName);
            string expectedName = Path.GetFileName(logicalName);
            string[] assetNames = bundle.GetAllAssetNames();
            string exactAssetName = null;
            for (int index = 0; index < assetNames.Length; index++)
            {
                string assetName = assetNames[index];
                if (!string.Equals(
                    Path.GetFileNameWithoutExtension(assetName),
                    expectedName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (exactAssetName != null && !string.Equals(
                    exactAssetName,
                    assetName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "The bundle contains more than one exact asset named '" +
                        expectedName + "': " + logicalName);
                }
                exactAssetName = assetName;
            }

            if (exactAssetName == null)
            {
                throw new InvalidDataException(
                    "The bundle does not contain the exact " + typeof(T).Name +
                    " asset '" + expectedName + "': " + logicalName);
            }

            T exactAsset = bundle.LoadAsset<T>(exactAssetName);
            if (exactAsset == null)
            {
                throw new InvalidDataException(
                    "The exact installed asset '" + expectedName +
                    "' is not a " + typeof(T).Name + ": " + logicalName);
            }
            return exactAsset;
        }

        private sealed class BodySkeleton
        {
            private readonly Dictionary<string, Transform> _bones;

            private BodySkeleton(Dictionary<string, Transform> bones)
            {
                _bones = bones;
            }

            public static BodySkeleton FromRenderer(SkinnedMeshRenderer renderer)
            {
                var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (Transform bone in renderer.bones)
                {
                    if (bone != null && !bones.ContainsKey(bone.name))
                    {
                        bones.Add(bone.name, bone);
                    }
                }
                return new BodySkeleton(bones);
            }

            public bool TryGet(string name, out Transform bone)
            {
                return _bones.TryGetValue(name, out bone);
            }
        }
    }
}
