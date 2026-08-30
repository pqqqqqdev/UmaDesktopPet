using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Stable material roles used by small app-owned signature-scene geometry.
    /// Character profiles choose colors; the shared runtime applies them to
    /// app-owned box or resource-prefab geometry.
    /// </summary>
    public enum PetAppOwnedSceneMaterialRole
    {
        Worktop,
        Frame,
        Accent,
        Hardware,
        Rubber
    }

    public enum PetAppOwnedScenePropSourceKind
    {
        BoxParts,
        ResourcePrefab
    }

    /// <summary>
    /// Maps one exact material name from an app-owned source prefab to a stable
    /// palette role. The runtime replaces every source material, so the prefab
    /// cannot retain an accidentally imported material at runtime.
    /// </summary>
    public sealed class PetAppOwnedSceneMaterialMappingDefinition
    {
        public PetAppOwnedSceneMaterialMappingDefinition(
            string sourceMaterialName,
            PetAppOwnedSceneMaterialRole materialRole)
        {
            if (string.IsNullOrWhiteSpace(sourceMaterialName))
            {
                throw new ArgumentException(
                    "An app-owned prefab material mapping requires an exact " +
                    "source material name.",
                    "sourceMaterialName");
            }
            if (!Enum.IsDefined(
                typeof(PetAppOwnedSceneMaterialRole),
                materialRole))
            {
                throw new ArgumentOutOfRangeException("materialRole");
            }

            SourceMaterialName = sourceMaterialName.Trim();
            MaterialRole = materialRole;
        }

        public string SourceMaterialName { get; private set; }
        public PetAppOwnedSceneMaterialRole MaterialRole { get; private set; }
    }

    /// <summary>
    /// A Resources-relative, app-owned prefab source. Imported geometry is
    /// uniformly normalized to the requested character-relative height before
    /// its renderer bounds are centered on the owning scene-prop origin.
    /// </summary>
    public sealed class PetAppOwnedResourcePrefabDefinition
    {
        private readonly IReadOnlyList<
            PetAppOwnedSceneMaterialMappingDefinition> _materialMappings;
        private readonly Dictionary<string, PetAppOwnedSceneMaterialRole>
            _materialRolesBySourceName;

        public PetAppOwnedResourcePrefabDefinition(
            string resourcePath,
            float desiredHeightInCharacterHeights,
            IEnumerable<PetAppOwnedSceneMaterialMappingDefinition>
                materialMappings)
        {
            ResourcePath = ValidateResourcePath(resourcePath);
            if (!IsFinite(desiredHeightInCharacterHeights) ||
                desiredHeightInCharacterHeights <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    "desiredHeightInCharacterHeights",
                    "An app-owned resource prefab requires a finite, positive " +
                    "normalized geometry height.");
            }
            if (materialMappings == null)
            {
                throw new ArgumentNullException("materialMappings");
            }

            var copiedMappings =
                new List<PetAppOwnedSceneMaterialMappingDefinition>();
            _materialRolesBySourceName =
                new Dictionary<string, PetAppOwnedSceneMaterialRole>(
                    StringComparer.Ordinal);
            foreach (PetAppOwnedSceneMaterialMappingDefinition mapping in
                materialMappings)
            {
                if (mapping == null ||
                    _materialRolesBySourceName.ContainsKey(
                        mapping.SourceMaterialName))
                {
                    throw new ArgumentException(
                        "App-owned prefab material mappings must be non-null " +
                        "and have unique exact source names.",
                        "materialMappings");
                }
                _materialRolesBySourceName.Add(
                    mapping.SourceMaterialName,
                    mapping.MaterialRole);
                copiedMappings.Add(mapping);
            }
            if (copiedMappings.Count == 0)
            {
                throw new ArgumentException(
                    "An app-owned resource prefab requires at least one source " +
                    "material mapping.",
                    "materialMappings");
            }

            DesiredHeightInCharacterHeights =
                desiredHeightInCharacterHeights;
            _materialMappings = new ReadOnlyCollection<
                PetAppOwnedSceneMaterialMappingDefinition>(copiedMappings);
        }

        public string ResourcePath { get; private set; }
        public float DesiredHeightInCharacterHeights { get; private set; }
        public IReadOnlyList<PetAppOwnedSceneMaterialMappingDefinition>
            MaterialMappings
        {
            get { return _materialMappings; }
        }

        public bool TryResolveMaterialRole(
            string sourceMaterialName,
            out PetAppOwnedSceneMaterialRole materialRole)
        {
            if (string.IsNullOrWhiteSpace(sourceMaterialName))
            {
                materialRole = default(PetAppOwnedSceneMaterialRole);
                return false;
            }
            return _materialRolesBySourceName.TryGetValue(
                sourceMaterialName.Trim(),
                out materialRole);
        }

        private static string ValidateResourcePath(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                throw new ArgumentException(
                    "An app-owned resource prefab requires a Resources-relative " +
                    "path.",
                    "resourcePath");
            }
            string normalized = resourcePath.Trim().Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) ||
                normalized.EndsWith("/", StringComparison.Ordinal) ||
                Path.IsPathRooted(normalized) ||
                Path.HasExtension(normalized))
            {
                throw new ArgumentException(
                    "An app-owned prefab resource path must be relative and omit " +
                    "the file extension.",
                    "resourcePath");
            }
            string[] segments = normalized.Split('/');
            for (int index = 0; index < segments.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(segments[index]) ||
                    string.Equals(segments[index], ".", StringComparison.Ordinal) ||
                    string.Equals(segments[index], "..", StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "An app-owned prefab resource path contains an invalid " +
                        "segment.",
                        "resourcePath");
                }
            }
            return normalized;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public sealed class PetAppOwnedScenePalette
    {
        public PetAppOwnedScenePalette(
            Color32 worktop,
            Color32 frame,
            Color32 accent,
            Color32 hardware,
            Color32 rubber)
        {
            ValidateOpaque(worktop, "worktop");
            ValidateOpaque(frame, "frame");
            ValidateOpaque(accent, "accent");
            ValidateOpaque(hardware, "hardware");
            ValidateOpaque(rubber, "rubber");
            Worktop = worktop;
            Frame = frame;
            Accent = accent;
            Hardware = hardware;
            Rubber = rubber;
        }

        public Color32 Worktop { get; private set; }
        public Color32 Frame { get; private set; }
        public Color32 Accent { get; private set; }
        public Color32 Hardware { get; private set; }
        public Color32 Rubber { get; private set; }

        public Color32 Resolve(PetAppOwnedSceneMaterialRole role)
        {
            switch (role)
            {
                case PetAppOwnedSceneMaterialRole.Worktop:
                    return Worktop;
                case PetAppOwnedSceneMaterialRole.Frame:
                    return Frame;
                case PetAppOwnedSceneMaterialRole.Accent:
                    return Accent;
                case PetAppOwnedSceneMaterialRole.Hardware:
                    return Hardware;
                case PetAppOwnedSceneMaterialRole.Rubber:
                    return Rubber;
                default:
                    throw new ArgumentOutOfRangeException("role");
            }
        }

        private static void ValidateOpaque(Color32 color, string name)
        {
            if (color.a != byte.MaxValue)
            {
                throw new ArgumentException(
                    "App-owned scene palette color " + name +
                    " must be fully opaque.",
                    name);
            }
        }
    }

    public sealed class PetAppOwnedBoxPartDefinition
    {
        public PetAppOwnedBoxPartDefinition(
            string partId,
            Vector3 localCenterInCharacterHeights,
            Vector3 sizeInCharacterHeights,
            PetAppOwnedSceneMaterialRole materialRole)
            : this(
                partId,
                localCenterInCharacterHeights,
                sizeInCharacterHeights,
                Vector3.zero,
                materialRole)
        {
        }

        public PetAppOwnedBoxPartDefinition(
            string partId,
            Vector3 localCenterInCharacterHeights,
            Vector3 sizeInCharacterHeights,
            Vector3 localEulerAnglesInDegrees,
            PetAppOwnedSceneMaterialRole materialRole)
        {
            if (string.IsNullOrWhiteSpace(partId))
            {
                throw new ArgumentException(
                    "An app-owned scene box requires a stable part ID.",
                    "partId");
            }
            if (!IsFinite(localCenterInCharacterHeights) ||
                !IsFinite(localEulerAnglesInDegrees) ||
                !IsFinitePositive(sizeInCharacterHeights))
            {
                throw new ArgumentOutOfRangeException(
                    "sizeInCharacterHeights",
                    "App-owned scene box placement and size must be finite and " +
                    "strictly positive.");
            }
            if (!Enum.IsDefined(
                typeof(PetAppOwnedSceneMaterialRole),
                materialRole))
            {
                throw new ArgumentOutOfRangeException("materialRole");
            }

            PartId = partId.Trim();
            LocalCenterInCharacterHeights = localCenterInCharacterHeights;
            SizeInCharacterHeights = sizeInCharacterHeights;
            LocalEulerAnglesInDegrees = localEulerAnglesInDegrees;
            MaterialRole = materialRole;
        }

        public string PartId { get; private set; }
        public Vector3 LocalCenterInCharacterHeights { get; private set; }
        public Vector3 SizeInCharacterHeights { get; private set; }
        public Vector3 LocalEulerAnglesInDegrees { get; private set; }
        public PetAppOwnedSceneMaterialRole MaterialRole { get; private set; }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinitePositive(Vector3 value)
        {
            return IsFinite(value) && value.x > 0.0f && value.y > 0.0f &&
                value.z > 0.0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// A horizontal support surface on an app-owned box prop. Its center and
    /// width/depth are expressed relative to the owning prop's origin.
    /// </summary>
    public sealed class PetAppOwnedSupportSurfaceDefinition
    {
        public PetAppOwnedSupportSurfaceDefinition(
            string surfaceId,
            Vector3 localCenterInCharacterHeights,
            Vector2 sizeInCharacterHeights)
        {
            if (string.IsNullOrWhiteSpace(surfaceId))
            {
                throw new ArgumentException(
                    "An app-owned support surface requires a stable ID.",
                    "surfaceId");
            }
            if (!IsFinite(localCenterInCharacterHeights) ||
                !IsFinitePositive(sizeInCharacterHeights))
            {
                throw new ArgumentOutOfRangeException(
                    "sizeInCharacterHeights",
                    "App-owned support-surface placement and size must be " +
                    "finite and strictly positive.");
            }

            SurfaceId = surfaceId.Trim();
            LocalCenterInCharacterHeights = localCenterInCharacterHeights;
            SizeInCharacterHeights = sizeInCharacterHeights;
        }

        public string SurfaceId { get; private set; }
        public Vector3 LocalCenterInCharacterHeights { get; private set; }
        public Vector2 SizeInCharacterHeights { get; private set; }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinitePositive(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) &&
                value.x > 0.0f && value.y > 0.0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// A data-defined app-owned 3D prop made from positioned boxes or a sanitized
    /// Resources prefab. This keeps character recipes declarative while avoiding
    /// redistribution of game data.
    /// </summary>
    public sealed class PetAppOwnedScenePropDefinition
    {
        private readonly IReadOnlyList<string> _visibleStepIds;
        private readonly IReadOnlyList<PetAppOwnedBoxPartDefinition> _parts;
        private readonly IReadOnlyList<PetAppOwnedSupportSurfaceDefinition>
            _supportSurfaces;

        public PetAppOwnedScenePropDefinition(
            string visualId,
            bool required,
            IEnumerable<string> visibleStepIds,
            Vector3 offsetInCharacterHeights,
            PetAppOwnedScenePalette palette,
            IEnumerable<PetAppOwnedBoxPartDefinition> parts,
            IEnumerable<PetAppOwnedSupportSurfaceDefinition>
                supportSurfaces = null)
            : this(
                visualId,
                required,
                visibleStepIds,
                offsetInCharacterHeights,
                0.0f,
                palette,
                parts,
                supportSurfaces)
        {
        }

        public static PetAppOwnedScenePropDefinition CreateResourcePrefab(
            string visualId,
            bool required,
            IEnumerable<string> visibleStepIds,
            Vector3 offsetInCharacterHeights,
            PetAppOwnedScenePalette palette,
            PetAppOwnedResourcePrefabDefinition resourcePrefab,
            IEnumerable<PetAppOwnedSupportSurfaceDefinition>
                supportSurfaces = null)
        {
            return CreateResourcePrefab(
                visualId,
                required,
                visibleStepIds,
                offsetInCharacterHeights,
                0.0f,
                palette,
                resourcePrefab,
                supportSurfaces);
        }

        public static PetAppOwnedScenePropDefinition CreateResourcePrefab(
            string visualId,
            bool required,
            IEnumerable<string> visibleStepIds,
            Vector3 offsetInCharacterHeights,
            float yawDegrees,
            PetAppOwnedScenePalette palette,
            PetAppOwnedResourcePrefabDefinition resourcePrefab,
            IEnumerable<PetAppOwnedSupportSurfaceDefinition>
                supportSurfaces = null)
        {
            return new PetAppOwnedScenePropDefinition(
                visualId,
                required,
                visibleStepIds,
                offsetInCharacterHeights,
                yawDegrees,
                palette,
                null,
                resourcePrefab,
                supportSurfaces);
        }

        public PetAppOwnedScenePropDefinition(
            string visualId,
            bool required,
            IEnumerable<string> visibleStepIds,
            Vector3 offsetInCharacterHeights,
            float yawDegrees,
            PetAppOwnedScenePalette palette,
            IEnumerable<PetAppOwnedBoxPartDefinition> parts,
            IEnumerable<PetAppOwnedSupportSurfaceDefinition>
                supportSurfaces = null)
            : this(
                visualId,
                required,
                visibleStepIds,
                offsetInCharacterHeights,
                yawDegrees,
                palette,
                parts,
                null,
                supportSurfaces)
        {
        }

        private PetAppOwnedScenePropDefinition(
            string visualId,
            bool required,
            IEnumerable<string> visibleStepIds,
            Vector3 offsetInCharacterHeights,
            float yawDegrees,
            PetAppOwnedScenePalette palette,
            IEnumerable<PetAppOwnedBoxPartDefinition> parts,
            PetAppOwnedResourcePrefabDefinition resourcePrefab,
            IEnumerable<PetAppOwnedSupportSurfaceDefinition>
                supportSurfaces)
        {
            if (string.IsNullOrWhiteSpace(visualId))
            {
                throw new ArgumentException(
                    "An app-owned scene prop requires a stable visual ID.",
                    "visualId");
            }
            if (visibleStepIds == null)
            {
                throw new ArgumentNullException("visibleStepIds");
            }
            if (palette == null)
            {
                throw new ArgumentNullException("palette");
            }
            bool usesBoxes = parts != null;
            if (usesBoxes == (resourcePrefab != null))
            {
                throw new ArgumentException(
                    "An app-owned scene prop requires exactly one geometry " +
                    "source: box parts or a resource prefab.");
            }
            if (!IsFinite(offsetInCharacterHeights) ||
                !IsFinite(yawDegrees))
            {
                throw new ArgumentOutOfRangeException(
                    "offsetInCharacterHeights");
            }

            var steps = new List<string>();
            var uniqueSteps = new HashSet<string>(StringComparer.Ordinal);
            foreach (string stepId in visibleStepIds)
            {
                if (string.IsNullOrWhiteSpace(stepId) ||
                    !uniqueSteps.Add(stepId.Trim()))
                {
                    throw new ArgumentException(
                        "App-owned scene prop step IDs must be non-empty and " +
                        "unique.",
                        "visibleStepIds");
                }
                steps.Add(stepId.Trim());
            }
            if (steps.Count == 0)
            {
                throw new ArgumentException(
                    "An app-owned scene prop must be visible in at least one step.",
                    "visibleStepIds");
            }

            var copiedParts = new List<PetAppOwnedBoxPartDefinition>();
            var uniquePartIds = new HashSet<string>(StringComparer.Ordinal);
            if (parts != null)
            {
                foreach (PetAppOwnedBoxPartDefinition part in parts)
                {
                    if (part == null || !uniquePartIds.Add(part.PartId))
                    {
                        throw new ArgumentException(
                            "App-owned scene box parts must be non-null and have " +
                            "unique IDs.",
                            "parts");
                    }
                    copiedParts.Add(part);
                }
            }
            if (usesBoxes && copiedParts.Count == 0)
            {
                throw new ArgumentException(
                    "An app-owned scene prop requires at least one 3D box part.",
                    "parts");
            }

            var copiedSurfaces =
                new List<PetAppOwnedSupportSurfaceDefinition>();
            var uniqueSurfaceIds = new HashSet<string>(StringComparer.Ordinal);
            if (supportSurfaces != null)
            {
                foreach (PetAppOwnedSupportSurfaceDefinition surface in
                    supportSurfaces)
                {
                    if (surface == null ||
                        !uniqueSurfaceIds.Add(surface.SurfaceId))
                    {
                        throw new ArgumentException(
                            "App-owned support surfaces must be non-null and " +
                            "have unique IDs.",
                            "supportSurfaces");
                    }
                    copiedSurfaces.Add(surface);
                }
            }

            VisualId = visualId.Trim();
            Required = required;
            OffsetInCharacterHeights = offsetInCharacterHeights;
            YawDegrees = yawDegrees;
            Palette = palette;
            SourceKind = usesBoxes
                ? PetAppOwnedScenePropSourceKind.BoxParts
                : PetAppOwnedScenePropSourceKind.ResourcePrefab;
            ResourcePrefab = resourcePrefab;
            _visibleStepIds = new ReadOnlyCollection<string>(steps);
            _parts = new ReadOnlyCollection<PetAppOwnedBoxPartDefinition>(
                copiedParts);
            _supportSurfaces =
                new ReadOnlyCollection<PetAppOwnedSupportSurfaceDefinition>(
                    copiedSurfaces);
        }

        public string VisualId { get; private set; }
        public bool Required { get; private set; }
        public Vector3 OffsetInCharacterHeights { get; private set; }
        public float YawDegrees { get; private set; }
        public PetAppOwnedScenePalette Palette { get; private set; }
        public PetAppOwnedScenePropSourceKind SourceKind { get; private set; }
        public PetAppOwnedResourcePrefabDefinition ResourcePrefab
        {
            get;
            private set;
        }
        public IReadOnlyList<string> VisibleStepIds
        {
            get { return _visibleStepIds; }
        }
        public IReadOnlyList<PetAppOwnedBoxPartDefinition> Parts
        {
            get { return _parts; }
        }
        public IReadOnlyList<PetAppOwnedSupportSurfaceDefinition> SupportSurfaces
        {
            get { return _supportSurfaces; }
        }

        public bool IsVisibleInStep(string stepId)
        {
            if (string.IsNullOrWhiteSpace(stepId))
            {
                return false;
            }
            string normalized = stepId.Trim();
            for (int index = 0; index < _visibleStepIds.Count; index++)
            {
                if (string.Equals(
                    _visibleStepIds[index],
                    normalized,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public PetAppOwnedSupportSurfaceDefinition FindSupportSurface(
            string surfaceId)
        {
            if (string.IsNullOrWhiteSpace(surfaceId))
            {
                return null;
            }
            string normalized = surfaceId.Trim();
            for (int index = 0; index < _supportSurfaces.Count; index++)
            {
                if (string.Equals(
                    _supportSurfaces[index].SurfaceId,
                    normalized,
                    StringComparison.Ordinal))
                {
                    return _supportSurfaces[index];
                }
            }
            return null;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Places an installed prop on a named app-owned horizontal surface. U and V
    /// are normalized surface coordinates in the inclusive range [-0.5, 0.5].
    /// </summary>
    public sealed class PetSceneSupportPlacementDefinition
    {
        public PetSceneSupportPlacementDefinition(
            string supportVisualId,
            string surfaceId,
            Vector2 normalizedCoordinates,
            float clearanceInCharacterHeights)
        {
            if (string.IsNullOrWhiteSpace(supportVisualId) ||
                string.IsNullOrWhiteSpace(surfaceId))
            {
                throw new ArgumentException(
                    "A support placement requires stable prop and surface IDs.");
            }
            if (!IsFinite(normalizedCoordinates.x) ||
                !IsFinite(normalizedCoordinates.y) ||
                normalizedCoordinates.x < -0.5f ||
                normalizedCoordinates.x > 0.5f ||
                normalizedCoordinates.y < -0.5f ||
                normalizedCoordinates.y > 0.5f ||
                !IsFinite(clearanceInCharacterHeights) ||
                clearanceInCharacterHeights < 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    "normalizedCoordinates",
                    "Support coordinates must be finite and within [-0.5, 0.5], " +
                    "with non-negative clearance.");
            }

            SupportVisualId = supportVisualId.Trim();
            SurfaceId = surfaceId.Trim();
            NormalizedCoordinates = normalizedCoordinates;
            ClearanceInCharacterHeights = clearanceInCharacterHeights;
        }

        public string SupportVisualId { get; private set; }
        public string SurfaceId { get; private set; }
        public Vector2 NormalizedCoordinates { get; private set; }
        public float ClearanceInCharacterHeights { get; private set; }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public sealed class PetSceneSupportPlacementPlan
    {
        internal PetSceneSupportPlacementPlan(
            Vector3 localCenter,
            Vector3 rotatedHalfExtents,
            float uniformScale)
        {
            LocalCenter = localCenter;
            RotatedHalfExtents = rotatedHalfExtents;
            UniformScale = uniformScale;
        }

        public Vector3 LocalCenter { get; private set; }
        public Vector3 RotatedHalfExtents { get; private set; }
        public float UniformScale { get; private set; }
    }

    /// <summary>
    /// Pure placement math shared by production loading and editor smoke tests.
    /// It grounds renderer geometry rather than trusting foreign prefab pivots.
    /// </summary>
    public static class PetSceneSupportPlacementPlanner
    {
        public static PetSceneSupportPlacementPlan CreatePlan(
            Bounds sourceBounds,
            Quaternion rotation,
            float maximumDimensionInCharacterHeights,
            float characterHeight,
            Vector3 characterCenter,
            PetAppOwnedScenePropDefinition supportProp,
            PetAppOwnedSupportSurfaceDefinition supportSurface,
            PetSceneSupportPlacementDefinition placement)
        {
            if (supportProp == null)
            {
                throw new ArgumentNullException("supportProp");
            }
            if (supportSurface == null)
            {
                throw new ArgumentNullException("supportSurface");
            }
            if (placement == null)
            {
                throw new ArgumentNullException("placement");
            }
            if (!string.Equals(
                    supportProp.VisualId,
                    placement.SupportVisualId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    supportSurface.SurfaceId,
                    placement.SurfaceId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The selected app-owned support does not match the placement.");
            }
            if (!IsFinitePositive(sourceBounds.size) ||
                !IsFinite(sourceBounds.center) ||
                !IsFinite(rotation) ||
                Quaternion.Dot(rotation, rotation) <= 0.000001f ||
                !IsFinitePositive(maximumDimensionInCharacterHeights) ||
                !IsFinitePositive(characterHeight) ||
                !IsFinite(characterCenter))
            {
                throw new ArgumentOutOfRangeException(
                    "sourceBounds",
                    "Support placement inputs must be finite and positive.");
            }

            float sourceMaximum = Mathf.Max(
                sourceBounds.size.x,
                Mathf.Max(sourceBounds.size.y, sourceBounds.size.z));
            float uniformScale =
                maximumDimensionInCharacterHeights * characterHeight /
                sourceMaximum;
            Vector3 unrotatedHalfExtents = sourceBounds.extents * uniformScale;
            Quaternion normalizedRotation = rotation.normalized;
            Quaternion supportRotation = Quaternion.Euler(
                0.0f,
                supportProp.YawDegrees,
                0.0f);
            Vector3 rotatedHalfExtents = CalculateRotatedHalfExtents(
                unrotatedHalfExtents,
                normalizedRotation);
            Vector3 supportRelativeHalfExtents = CalculateRotatedHalfExtents(
                unrotatedHalfExtents,
                Quaternion.Inverse(supportRotation) * normalizedRotation);

            float width =
                supportSurface.SizeInCharacterHeights.x * characterHeight;
            float depth =
                supportSurface.SizeInCharacterHeights.y * characterHeight;
            float xOffset = placement.NormalizedCoordinates.x * width;
            float zOffset = placement.NormalizedCoordinates.y * depth;
            float containmentEpsilon = characterHeight * 0.00001f;
            if (Mathf.Abs(xOffset) + supportRelativeHalfExtents.x >
                    width * 0.5f + containmentEpsilon ||
                Mathf.Abs(zOffset) + supportRelativeHalfExtents.z >
                    depth * 0.5f + containmentEpsilon)
            {
                throw new InvalidDataException(
                    "The installed prop would overhang its app-owned support " +
                    "surface.");
            }

            Vector3 propOrigin = characterCenter +
                supportProp.OffsetInCharacterHeights * characterHeight;
            Vector3 surfaceCenter = propOrigin + supportRotation *
                (supportSurface.LocalCenterInCharacterHeights *
                    characterHeight);
            Vector3 localCenter = surfaceCenter + supportRotation *
                (Vector3.right * xOffset + Vector3.forward * zOffset) +
                Vector3.up * (rotatedHalfExtents.y +
                    placement.ClearanceInCharacterHeights * characterHeight);
            return new PetSceneSupportPlacementPlan(
                localCenter,
                rotatedHalfExtents,
                uniformScale);
        }

        private static Vector3 CalculateRotatedHalfExtents(
            Vector3 unrotatedHalfExtents,
            Quaternion rotation)
        {
            Matrix4x4 matrix = Matrix4x4.Rotate(rotation.normalized);
            return new Vector3(
                Mathf.Abs(matrix.m00) * unrotatedHalfExtents.x +
                    Mathf.Abs(matrix.m01) * unrotatedHalfExtents.y +
                    Mathf.Abs(matrix.m02) * unrotatedHalfExtents.z,
                Mathf.Abs(matrix.m10) * unrotatedHalfExtents.x +
                    Mathf.Abs(matrix.m11) * unrotatedHalfExtents.y +
                    Mathf.Abs(matrix.m12) * unrotatedHalfExtents.z,
                Mathf.Abs(matrix.m20) * unrotatedHalfExtents.x +
                    Mathf.Abs(matrix.m21) * unrotatedHalfExtents.y +
                    Mathf.Abs(matrix.m22) * unrotatedHalfExtents.z);
        }

        private static bool IsFinitePositive(Vector3 value)
        {
            return IsFinite(value) && value.x > 0.0f && value.y > 0.0f &&
                value.z > 0.0f;
        }

        private static bool IsFinitePositive(float value)
        {
            return IsFinite(value) && value > 0.0f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) &&
                IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
