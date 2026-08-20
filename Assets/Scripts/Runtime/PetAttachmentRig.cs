using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Stable semantic names used by prop code. Character profiles translate the
    /// body-attached names to the exact bones present in an assembled mini model.
    /// </summary>
    public static class PetAttachmentSlots
    {
        public const string World = "world";
        public const string PetRoot = "pet.root";
        public const string LeftHand = "pet.hand.left";
        public const string RightHand = "pet.hand.right";
        public const string Mouth = "pet.mouth";
    }

    /// <summary>
    /// One character-specific body slot. Position offsets are expressed as a
    /// fraction of rendered character height so the same data remains stable if
    /// the assembled model is uniformly scaled.
    /// </summary>
    public sealed class PetAttachmentSlotProfile
    {
        public PetAttachmentSlotProfile(
            string slotId,
            string exactPath,
            string uniqueNameFallback,
            Vector3 positionOffsetInCharacterHeights,
            Vector3 rotationOffsetEuler)
        {
            if (string.IsNullOrWhiteSpace(slotId))
            {
                throw new ArgumentException("An attachment slot ID is required.", "slotId");
            }

            string normalizedPath = NormalizePath(exactPath);
            string normalizedFallback = NormalizeName(uniqueNameFallback);
            if (string.IsNullOrEmpty(normalizedPath) &&
                string.IsNullOrEmpty(normalizedFallback))
            {
                throw new ArgumentException(
                    "An exact transform path or unique-name fallback is required.",
                    "exactPath");
            }

            SlotId = slotId.Trim();
            ExactPath = normalizedPath;
            UniqueNameFallback = normalizedFallback;
            PositionOffsetInCharacterHeights = positionOffsetInCharacterHeights;
            RotationOffsetEuler = rotationOffsetEuler;
        }

        public string SlotId { get; private set; }

        public string ExactPath { get; private set; }

        public string UniqueNameFallback { get; private set; }

        public Vector3 PositionOffsetInCharacterHeights { get; private set; }

        public Vector3 RotationOffsetEuler { get; private set; }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Trim('/');
        }

        private static string NormalizeName(string name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? string.Empty
                : name.Trim();
        }
    }

    /// <summary>
    /// App-owned attachment calibration for one explicitly supported character.
    /// Unknown characters never inherit another character's body offsets.
    /// </summary>
    public sealed class PetAttachmentProfile
    {
        private readonly IReadOnlyList<PetAttachmentSlotProfile> _slots;
        private readonly IReadOnlyDictionary<string, PetAttachmentSlotProfile> _slotMap;

        public PetAttachmentProfile(
            string characterKey,
            IEnumerable<PetAttachmentSlotProfile> slots)
        {
            if (string.IsNullOrWhiteSpace(characterKey))
            {
                throw new ArgumentException(
                    "A character key is required for an attachment profile.",
                    "characterKey");
            }
            if (slots == null)
            {
                throw new ArgumentNullException("slots");
            }

            var ordered = new List<PetAttachmentSlotProfile>();
            var map = new Dictionary<string, PetAttachmentSlotProfile>(
                StringComparer.Ordinal);
            foreach (PetAttachmentSlotProfile slot in slots)
            {
                if (slot == null)
                {
                    throw new ArgumentException(
                        "Attachment profiles cannot contain a null slot.",
                        "slots");
                }
                if (string.Equals(slot.SlotId, PetAttachmentSlots.World, StringComparison.Ordinal) ||
                    string.Equals(slot.SlotId, PetAttachmentSlots.PetRoot, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "World and pet-root slots are supplied by the rig itself.",
                        "slots");
                }
                if (map.ContainsKey(slot.SlotId))
                {
                    throw new ArgumentException(
                        "The attachment profile repeats slot " + slot.SlotId + ".",
                        "slots");
                }

                ordered.Add(slot);
                map.Add(slot.SlotId, slot);
            }

            CharacterKey = characterKey.Trim();
            _slots = new ReadOnlyCollection<PetAttachmentSlotProfile>(ordered);
            _slotMap = new ReadOnlyDictionary<string, PetAttachmentSlotProfile>(map);
        }

        public string CharacterKey { get; private set; }

        public IReadOnlyList<PetAttachmentSlotProfile> Slots
        {
            get { return _slots; }
        }

        public bool TryGetSlotProfile(
            string slotId,
            out PetAttachmentSlotProfile slot)
        {
            if (string.IsNullOrEmpty(slotId))
            {
                slot = null;
                return false;
            }
            return _slotMap.TryGetValue(slotId, out slot);
        }
    }

    /// <summary>
    /// Explicit attachment profiles supported by this build. Deliberately has no
    /// ResolveOrDefault method: using Oguri's offsets for another character would
    /// turn an unsupported slot into a floating or misplaced prop.
    /// </summary>
    public static class PetAttachmentProfileCatalog
    {
        private const string OguriKey = "oguri-cap";
        private const string LeftHandPath =
            "Position/Hip/Waist/Chest/Shoulder_L/Arm_L/Elbow_L/" +
            "Wrist_L/Hand_Attach_L";
        private const string RightHandPath =
            "Position/Hip/Waist/Chest/Shoulder_R/Arm_R/Elbow_R/" +
            "Wrist_R/Hand_Attach_R";

        public static readonly PetAttachmentProfile OguriCap =
            new PetAttachmentProfile(
                OguriKey,
                new[]
                {
                    new PetAttachmentSlotProfile(
                        PetAttachmentSlots.LeftHand,
                        LeftHandPath,
                        "Hand_Attach_L",
                        Vector3.zero,
                        Vector3.zero),
                    new PetAttachmentSlotProfile(
                        PetAttachmentSlots.RightHand,
                        RightHandPath,
                        "Hand_Attach_R",
                        Vector3.zero,
                        Vector3.zero)
                });

        private static readonly IReadOnlyDictionary<string, PetAttachmentProfile>
            Profiles = new ReadOnlyDictionary<string, PetAttachmentProfile>(
                new Dictionary<string, PetAttachmentProfile>(StringComparer.Ordinal)
                {
                    { OguriCap.CharacterKey, OguriCap }
                });

        public static bool TryGet(
            string characterKey,
            out PetAttachmentProfile profile)
        {
            if (string.IsNullOrWhiteSpace(characterKey))
            {
                profile = null;
                return false;
            }
            return Profiles.TryGetValue(characterKey.Trim(), out profile);
        }
    }

    /// <summary>
    /// Creates app-owned sockets beneath an assembled character's live bones.
    /// Missing or ambiguous bones create no socket. TryAttach additionally hides
    /// a rejected prop without reparenting or moving it to a fallback origin.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetAttachmentRig : MonoBehaviour
    {
        private const float MinimumCharacterHeight = 0.0001f;
        private const string SocketNamePrefix = "Pet Prop Slot - ";

        private readonly Dictionary<string, Transform> _slots =
            new Dictionary<string, Transform>(StringComparer.Ordinal);

        private Transform _worldSocket;
        private bool _initialized;

        public bool IsInitialized
        {
            get { return _initialized; }
        }

        public PetAttachmentProfile Profile { get; private set; }

        public Bounds CharacterLocalBounds { get; private set; }

        public float CharacterHeight { get; private set; }

        public void Initialize(
            Transform characterRoot,
            Transform visualFrame,
            PetAttachmentProfile profile)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "The pet attachment rig is already initialized.");
            }
            if (characterRoot == null)
            {
                throw new ArgumentNullException("characterRoot");
            }
            if (visualFrame == null)
            {
                throw new ArgumentNullException("visualFrame");
            }
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }
            if (characterRoot.parent != visualFrame)
            {
                throw new ArgumentException(
                    "The character root must be a direct child of the visual frame.",
                    "characterRoot");
            }

            Bounds localBounds = CalculateLocalBounds(characterRoot);
            float height = localBounds.size.y;
            if (float.IsNaN(height) || float.IsInfinity(height) ||
                height <= MinimumCharacterHeight)
            {
                throw new InvalidOperationException(
                    "The assembled character has no usable rendered height.");
            }

            // Resolve every source before creating sockets. This prevents an
            // app-owned socket from affecting a later unique-name lookup.
            var resolved = new List<ResolvedSlot>();
            foreach (PetAttachmentSlotProfile slot in profile.Slots)
            {
                Transform source;
                if (TryResolveSource(characterRoot, slot, out source))
                {
                    resolved.Add(new ResolvedSlot(slot, source));
                }
                else
                {
                    Debug.LogWarning(
                        "Pet attachment slot " + slot.SlotId + " is unavailable for " +
                        profile.CharacterKey + ". Props using it will stay hidden.");
                }
            }

            Profile = profile;
            CharacterLocalBounds = localBounds;
            CharacterHeight = height;

            _worldSocket = CreateWorldSocket(characterRoot, visualFrame);
            _slots.Add(PetAttachmentSlots.World, _worldSocket);
            _slots.Add(
                PetAttachmentSlots.PetRoot,
                CreateSocket(PetAttachmentSlots.PetRoot, characterRoot, Vector3.zero, Vector3.zero));

            foreach (ResolvedSlot item in resolved)
            {
                Vector3 localPosition =
                    item.Profile.PositionOffsetInCharacterHeights * CharacterHeight;
                Transform socket = CreateSocket(
                    item.Profile.SlotId,
                    item.Source,
                    localPosition,
                    item.Profile.RotationOffsetEuler);
                _slots.Add(item.Profile.SlotId, socket);
            }

            _initialized = true;
        }

        public bool TryGetSlot(string slotId, out Transform socket)
        {
            if (!_initialized || string.IsNullOrEmpty(slotId))
            {
                socket = null;
                return false;
            }

            if (_slots.TryGetValue(slotId, out socket) && socket != null)
            {
                return true;
            }

            socket = null;
            return false;
        }

        /// <summary>
        /// Parents a placement root at a semantic slot. Failure is deliberately
        /// fail-closed: the object is hidden and its parent/position are untouched.
        /// </summary>
        public bool TryAttach(Transform propRoot, string slotId)
        {
            if (propRoot == null)
            {
                throw new ArgumentNullException("propRoot");
            }

            Transform socket;
            if (!TryGetSlot(slotId, out socket))
            {
                propRoot.gameObject.SetActive(false);
                return false;
            }

            propRoot.SetParent(socket, false);
            propRoot.localPosition = Vector3.zero;
            propRoot.localRotation = Quaternion.identity;
            propRoot.localScale = Vector3.one;
            return true;
        }

        /// <summary>
        /// Converts a point in character-root local space to height-normalized
        /// coordinates measured from the center of the rendered bounds.
        /// </summary>
        public Vector3 ToCharacterHeightUnits(Vector3 characterLocalPoint)
        {
            EnsureInitialized();
            return (characterLocalPoint - CharacterLocalBounds.center) /
                CharacterHeight;
        }

        /// <summary>
        /// Converts center-relative height units back to character-root space.
        /// </summary>
        public Vector3 FromCharacterHeightUnits(Vector3 normalizedPoint)
        {
            EnsureInitialized();
            return CharacterLocalBounds.center + normalizedPoint * CharacterHeight;
        }

        public Vector3 ScaleByCharacterHeight(Vector3 normalizedOffset)
        {
            EnsureInitialized();
            return normalizedOffset * CharacterHeight;
        }

        private static Transform CreateSocket(
            string slotId,
            Transform parent,
            Vector3 localPosition,
            Vector3 localEulerAngles)
        {
            var socketObject = new GameObject(SocketNamePrefix + slotId);
            Transform socket = socketObject.transform;
            socket.SetParent(parent, false);
            socket.localPosition = localPosition;
            socket.localRotation = Quaternion.Euler(localEulerAngles);
            socket.localScale = Vector3.one;
            return socket;
        }

        private static Transform CreateWorldSocket(
            Transform characterRoot,
            Transform visualFrame)
        {
            var socketObject = new GameObject(SocketNamePrefix + PetAttachmentSlots.World);
            Transform socket = socketObject.transform;
            socket.SetParent(visualFrame, false);

            // Match character-root coordinates so existing character-local prop
            // calibrations do not shift when migrated to the sibling world slot.
            // The socket still remains outside all animated body bones.
            socket.localPosition = characterRoot.localPosition;
            socket.localRotation = characterRoot.localRotation;
            socket.localScale = characterRoot.localScale;
            return socket;
        }

        private static bool TryResolveSource(
            Transform characterRoot,
            PetAttachmentSlotProfile profile,
            out Transform source)
        {
            if (!string.IsNullOrEmpty(profile.ExactPath))
            {
                source = characterRoot.Find(profile.ExactPath);
                if (source != null)
                {
                    return true;
                }
            }

            if (!string.IsNullOrEmpty(profile.UniqueNameFallback) &&
                TryFindUniqueDescendant(
                    characterRoot,
                    profile.UniqueNameFallback,
                    out source))
            {
                return true;
            }

            source = null;
            return false;
        }

        private static bool TryFindUniqueDescendant(
            Transform root,
            string expectedName,
            out Transform result)
        {
            result = null;
            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < descendants.Length; index++)
            {
                Transform candidate = descendants[index];
                if (candidate == null || !string.Equals(
                    candidate.name,
                    expectedName,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                if (result != null)
                {
                    result = null;
                    return false;
                }
                result = candidate;
            }
            return result != null;
        }

        private static Bounds CalculateLocalBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds localBounds = default(Bounds);
            bool found = false;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null)
                {
                    continue;
                }

                Bounds worldBounds = renderer.bounds;
                for (int x = 0; x <= 1; x++)
                {
                    for (int y = 0; y <= 1; y++)
                    {
                        for (int z = 0; z <= 1; z++)
                        {
                            Vector3 worldCorner = new Vector3(
                                x == 0 ? worldBounds.min.x : worldBounds.max.x,
                                y == 0 ? worldBounds.min.y : worldBounds.max.y,
                                z == 0 ? worldBounds.min.z : worldBounds.max.z);
                            Vector3 localCorner = root.InverseTransformPoint(worldCorner);
                            if (!found)
                            {
                                localBounds = new Bounds(localCorner, Vector3.zero);
                                found = true;
                            }
                            else
                            {
                                localBounds.Encapsulate(localCorner);
                            }
                        }
                    }
                }
            }

            if (!found)
            {
                throw new InvalidOperationException(
                    "The pet attachment rig requires a rendered character root.");
            }
            return localBounds;
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    "Initialize the pet attachment rig before using normalized coordinates.");
            }
        }

        public void ReleaseResources()
        {
            // World is a visual-frame sibling of the character, so it is not
            // automatically destroyed when only the character root is replaced.
            if (_worldSocket != null && _worldSocket.parent != transform)
            {
                GameObject worldObject = _worldSocket.gameObject;
                _worldSocket = null;
                if (Application.isPlaying)
                {
                    Destroy(worldObject);
                }
                else
                {
                    DestroyImmediate(worldObject);
                }
            }
            _slots.Clear();
            _initialized = false;
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        private sealed class ResolvedSlot
        {
            public ResolvedSlot(
                PetAttachmentSlotProfile profile,
                Transform source)
            {
                Profile = profile;
                Source = source;
            }

            public PetAttachmentSlotProfile Profile { get; private set; }

            public Transform Source { get; private set; }
        }
    }
}
