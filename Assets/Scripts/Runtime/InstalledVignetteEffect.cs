using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Inert input to the deterministic installed-particle selection planner.
    /// </summary>
    public sealed class InstalledParticleSystemDescriptor
    {
        public InstalledParticleSystemDescriptor(
            string stableKey,
            bool authoredActive,
            bool playOnAwake,
            bool rendererEnabled,
            int authoredMaxParticles)
        {
            if (string.IsNullOrWhiteSpace(stableKey))
            {
                throw new ArgumentException(
                    "A stable particle-system key is required.",
                    "stableKey");
            }
            StableKey = stableKey.Trim();
            AuthoredActive = authoredActive;
            PlayOnAwake = playOnAwake;
            RendererEnabled = rendererEnabled;
            AuthoredMaxParticles = authoredMaxParticles;
        }

        public string StableKey { get; private set; }
        public bool AuthoredActive { get; private set; }
        public bool PlayOnAwake { get; private set; }
        public bool RendererEnabled { get; private set; }
        public int AuthoredMaxParticles { get; private set; }
    }

    public sealed class InstalledParticleSystemPlanEntry
    {
        internal InstalledParticleSystemPlanEntry(
            string stableKey,
            int appliedMaxParticles,
            uint randomSeed,
            bool externallyTriggered)
        {
            StableKey = stableKey;
            AppliedMaxParticles = appliedMaxParticles;
            RandomSeed = randomSeed;
            ExternallyTriggered = externallyTriggered;
        }

        public string StableKey { get; private set; }
        public int AppliedMaxParticles { get; private set; }
        public uint RandomSeed { get; private set; }
        public bool ExternallyTriggered { get; private set; }
    }

    public sealed class InstalledParticleSystemPlan
    {
        internal InstalledParticleSystemPlan(
            int discoveredCount,
            int eligibleCount,
            int externallyTriggeredCount,
            IList<InstalledParticleSystemPlanEntry> entries,
            bool systemsTruncated,
            bool particleBudgetTruncated,
            int appliedParticleBudget)
        {
            DiscoveredCount = discoveredCount;
            EligibleCount = eligibleCount;
            ExternallyTriggeredCount = externallyTriggeredCount;
            Entries = new ReadOnlyCollection<InstalledParticleSystemPlanEntry>(
                entries);
            SystemsTruncated = systemsTruncated;
            ParticleBudgetTruncated = particleBudgetTruncated;
            AppliedParticleBudget = appliedParticleBudget;
        }

        public int DiscoveredCount { get; private set; }
        public int EligibleCount { get; private set; }
        public int ExternallyTriggeredCount { get; private set; }
        public IReadOnlyList<InstalledParticleSystemPlanEntry> Entries
        {
            get;
            private set;
        }
        public bool SystemsTruncated { get; private set; }
        public bool ParticleBudgetTruncated { get; private set; }
        public int AppliedParticleBudget { get; private set; }
    }

    /// <summary>
    /// Pure selection and budget policy shared by runtime and smoke tests.
    /// </summary>
    public static class InstalledParticleEffectPlanner
    {
        public const int MaximumParticleSystems = 12;
        public const int MaximumParticlesPerSystem = 512;
        public const int MaximumTotalParticles = 2048;

        public static InstalledParticleSystemPlan BuildPlan(
            string logicalName,
            IEnumerable<InstalledParticleSystemDescriptor> descriptors,
            PetInstalledSceneEffectActivation activation)
        {
            if (string.IsNullOrWhiteSpace(logicalName) ||
                !logicalName.Trim().StartsWith(
                    "3d/effect/",
                    StringComparison.Ordinal) ||
                logicalName.IndexOf('\\') >= 0 ||
                logicalName.IndexOf(':') >= 0)
            {
                throw new ArgumentException(
                    "A safe installed 3D effect logical name is required.",
                    "logicalName");
            }
            if (descriptors == null)
            {
                throw new ArgumentNullException("descriptors");
            }
            if (!Enum.IsDefined(
                typeof(PetInstalledSceneEffectActivation),
                activation))
            {
                throw new ArgumentOutOfRangeException("activation");
            }

            var all = new List<InstalledParticleSystemDescriptor>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (InstalledParticleSystemDescriptor descriptor in descriptors)
            {
                if (descriptor == null || !keys.Add(descriptor.StableKey))
                {
                    throw new InvalidDataException(
                        "Installed particle descriptors must be non-null and " +
                        "use unique stable keys.");
                }
                all.Add(descriptor);
            }
            var eligible = all.FindAll(value =>
                value.AuthoredActive && value.RendererEnabled &&
                (value.PlayOnAwake ||
                    activation == PetInstalledSceneEffectActivation
                        .IncludeExternallyTriggered));
            eligible.Sort((left, right) => string.Compare(
                left.StableKey,
                right.StableKey,
                StringComparison.Ordinal));

            var entries = new List<InstalledParticleSystemPlanEntry>();
            int remaining = MaximumTotalParticles;
            bool particleBudgetTruncated = false;
            int systemLimit = Math.Min(
                eligible.Count,
                MaximumParticleSystems);
            for (int index = 0; index < systemLimit; index++)
            {
                if (remaining <= 0)
                {
                    particleBudgetTruncated = true;
                    break;
                }
                InstalledParticleSystemDescriptor descriptor = eligible[index];
                int authoredMaximum = Math.Max(
                    1,
                    descriptor.AuthoredMaxParticles);
                int appliedMaximum = Math.Min(
                    authoredMaximum,
                    Math.Min(MaximumParticlesPerSystem, remaining));
                particleBudgetTruncated |= appliedMaximum < authoredMaximum;
                entries.Add(new InstalledParticleSystemPlanEntry(
                    descriptor.StableKey,
                    appliedMaximum,
                    CreateStableSeed(logicalName.Trim(), descriptor.StableKey),
                    !descriptor.PlayOnAwake));
                remaining -= appliedMaximum;
            }
            int externallyTriggeredCount = 0;
            for (int index = 0; index < entries.Count; index++)
            {
                if (entries[index].ExternallyTriggered)
                {
                    externallyTriggeredCount++;
                }
            }
            bool systemsTruncated = entries.Count < eligible.Count;
            if (systemsTruncated && remaining <= 0)
            {
                particleBudgetTruncated = true;
            }
            return new InstalledParticleSystemPlan(
                all.Count,
                eligible.Count,
                externallyTriggeredCount,
                entries,
                systemsTruncated,
                particleBudgetTruncated,
                MaximumTotalParticles - remaining);
        }

        public static uint CreateStableSeed(
            string logicalName,
            string stableKey)
        {
            if (logicalName == null)
            {
                throw new ArgumentNullException("logicalName");
            }
            if (stableKey == null)
            {
                throw new ArgumentNullException("stableKey");
            }
            unchecked
            {
                uint hash = 2166136261u;
                string value = logicalName + "\n" + stableKey;
                for (int index = 0; index < value.Length; index++)
                {
                    hash ^= value[index];
                    hash *= 16777619u;
                }
                return hash == 0u ? 1u : hash;
            }
        }
    }

    /// <summary>
    /// Release-safe installed particle effect. The exact foreign prefab is only
    /// inspected as an inactive AssetBundle asset; it is never instantiated or
    /// activated. A bounded subset of its particle data is copied into fresh,
    /// app-owned GameObjects, modules, renderers, and materials.
    /// </summary>
    public sealed class InstalledVignetteEffect : IDisposable
    {
        private const float MaximumWarmupSeconds = 5.0f;
        private const float MaximumDurationSeconds = 8.0f;
        private const float MaximumSimulationSpeed = 2.0f;
        private const int MaximumBurstsPerSystem = 8;
        private const int MaximumMaterialsPerRenderer = 2;
        private const int MaximumOwnedMaterials = 16;
        private const int MaximumTextureSheetSprites = 16;
        private const int MaximumNoiseOctaves = 3;
        private const int MaximumTrailRibbons = 4;

        private readonly string _diagnosticId;
        private readonly List<ParticleSystem> _particleSystems =
            new List<ParticleSystem>();
        private readonly List<ParticleSystemRenderer> _renderers =
            new List<ParticleSystemRenderer>();
        private readonly List<Material> _ownedMaterials =
            new List<Material>();
        private readonly Dictionary<Material, Material> _materialReplacements =
            new Dictionary<Material, Material>();

        private BundleLease _lease;
        private GameObject _root;
        private bool _disposed;

        private InstalledVignetteEffect(
            string diagnosticId,
            BundleLease lease,
            GameObject root)
        {
            _diagnosticId = diagnosticId;
            _lease = lease;
            _root = root;
        }

        public bool IsVisible
        {
            get
            {
                return !_disposed && _root != null && _root.activeSelf;
            }
        }

        public Transform Root
        {
            get
            {
                ThrowIfDisposed();
                return _root.transform;
            }
        }

        public int ParticleSystemCount { get { return _particleSystems.Count; } }
        public int AnimatorCount { get { return 0; } }
        public int PlayableDirectorCount { get { return 0; } }
        public int RendererCount { get { return _renderers.Count; } }

        public static InstalledVignetteEffect LoadExact(
            BundleRepository bundles,
            Transform parent,
            string logicalName,
            string expectedPrefabName,
            string diagnosticId)
        {
            return LoadExact(
                bundles,
                parent,
                logicalName,
                expectedPrefabName,
                diagnosticId,
                PetInstalledSceneEffectActivation.AuthoredPlayOnAwakeOnly);
        }

        public static InstalledVignetteEffect LoadExact(
            BundleRepository bundles,
            Transform parent,
            string logicalName,
            string expectedPrefabName,
            string diagnosticId,
            PetInstalledSceneEffectActivation activation)
        {
            ValidateLoadArguments(
                bundles,
                parent,
                logicalName,
                expectedPrefabName,
                diagnosticId,
                activation);

            string normalizedLogicalName = logicalName.Trim();
            string normalizedPrefabName = expectedPrefabName.Trim();
            string normalizedDiagnosticId = diagnosticId.Trim();
            BundleLease lease = null;
            GameObject root = null;
            InstalledVignetteEffect effect = null;
            try
            {
                lease = bundles.AcquireWithShaderFirst(normalizedLogicalName);
                AssetBundle bundle = lease.GetRequiredBundle(
                    normalizedLogicalName);
                GameObject prefab = LoadUniqueExactPrefab(
                    bundle,
                    normalizedPrefabName,
                    normalizedDiagnosticId);
                root = new GameObject(
                    "Installed Particle Effect " + normalizedDiagnosticId)
                {
                    hideFlags = HideFlags.DontSave,
                    layer = parent.gameObject.layer
                };
                root.transform.SetParent(parent, false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                root.SetActive(false);

                effect = new InstalledVignetteEffect(
                    normalizedDiagnosticId,
                    lease,
                    root);
                lease = null;
                root = null;
                effect.CloneFromPrefab(
                    prefab,
                    normalizedLogicalName,
                    activation,
                    parent.gameObject.layer);
                effect.LogSummary("loaded");
                return effect;
            }
            catch
            {
                if (effect != null)
                {
                    effect.Dispose();
                }
                else
                {
                    DestroyOwnedObject(root);
                    DisposeLeaseSafely(lease, "effect load rollback");
                }
                throw;
            }
        }

        public void SetLocalPlacement(
            Vector3 localPosition,
            Quaternion localRotation,
            float uniformScale)
        {
            ThrowIfDisposed();
            if (!IsFinite(localPosition) || !IsFinite(localRotation) ||
                !IsFinitePositive(uniformScale) ||
                Quaternion.Dot(localRotation, localRotation) <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    "uniformScale",
                    "Installed effect placement must be finite and positive.");
            }
            _root.transform.localPosition = localPosition;
            _root.transform.localRotation = localRotation.normalized;
            _root.transform.localScale = Vector3.one * uniformScale;
        }

        public void ShowRestart(float warmupSeconds = 0.0f)
        {
            ThrowIfDisposed();
            if (float.IsNaN(warmupSeconds) || float.IsInfinity(warmupSeconds) ||
                warmupSeconds < 0.0f || warmupSeconds > MaximumWarmupSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    "warmupSeconds",
                    "Installed effect warmup must be between 0 and 5 seconds.");
            }
            StopAndClear();
            _root.SetActive(true);
            for (int index = 0; index < _particleSystems.Count; index++)
            {
                ParticleSystem particles = _particleSystems[index];
                if (particles == null || !particles.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (warmupSeconds > 0.0f)
                {
                    particles.Simulate(
                        warmupSeconds,
                        false,
                        true,
                        true);
                }
                particles.Play(false);
            }
            LogSummary("visible");
        }

        /// <summary>
        /// Simulates to one deterministic authored frame and holds it until the
        /// scene hides or releases the effect. This keeps coordinated one-shot
        /// particles from expiring on unrelated source-prefab timelines.
        /// </summary>
        public void ShowWarmupSnapshot(float snapshotSeconds)
        {
            ThrowIfDisposed();
            if (float.IsNaN(snapshotSeconds) ||
                float.IsInfinity(snapshotSeconds) ||
                snapshotSeconds <= 0.0f ||
                snapshotSeconds > MaximumWarmupSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    "snapshotSeconds",
                    "Installed effect snapshots must be between 0 and 5 seconds.");
            }
            StopAndClear();
            _root.SetActive(true);
            for (int index = 0; index < _particleSystems.Count; index++)
            {
                ParticleSystem particles = _particleSystems[index];
                if (particles == null || !particles.gameObject.activeInHierarchy)
                {
                    continue;
                }
                particles.Simulate(
                    snapshotSeconds,
                    false,
                    true,
                    true);
                particles.Pause(false);
            }
            LogSummary("visible snapshot");
        }

        public void Hide()
        {
            if (_disposed || _root == null)
            {
                return;
            }
            StopAndClear();
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
                StopAndClear();
                _root.SetActive(false);
                GameObject root = _root;
                _root = null;
                DestroyOwnedObject(root);
            }
            _particleSystems.Clear();
            _renderers.Clear();
            for (int index = _ownedMaterials.Count - 1; index >= 0; index--)
            {
                DestroyOwnedObject(_ownedMaterials[index]);
            }
            _ownedMaterials.Clear();
            _materialReplacements.Clear();

            BundleLease lease = _lease;
            _lease = null;
            DisposeLeaseSafely(lease, "effect disposal");
        }

        private void CloneFromPrefab(
            GameObject prefab,
            string logicalName,
            PetInstalledSceneEffectActivation activation,
            int layer)
        {
            ParticleSystem[] sources =
                prefab.GetComponentsInChildren<ParticleSystem>(true);
            var referencedSubEmitters = FindReferencedSubEmitters(sources);
            var descriptors = new List<InstalledParticleSystemDescriptor>(
                sources.Length);
            var byKey = new Dictionary<string, SourceParticleSystem>(
                StringComparer.Ordinal);
            for (int index = 0; index < sources.Length; index++)
            {
                ParticleSystem source = sources[index];
                ParticleSystemRenderer renderer =
                    source.GetComponent<ParticleSystemRenderer>();
                string stableKey = CreateStableSystemKey(
                    source,
                    prefab.transform);
                bool rendererEnabled = renderer != null && renderer.enabled &&
                    !renderer.forceRenderingOff &&
                    renderer.renderMode != ParticleSystemRenderMode.None &&
                    !referencedSubEmitters.Contains(source);
                ParticleSystem.MainModule main = source.main;
                descriptors.Add(new InstalledParticleSystemDescriptor(
                    stableKey,
                    IsAuthoredActive(source.transform, prefab.transform),
                    main.playOnAwake,
                    rendererEnabled,
                    main.maxParticles));
                byKey.Add(stableKey, new SourceParticleSystem(source, renderer));
            }

            InstalledParticleSystemPlan plan =
                InstalledParticleEffectPlanner.BuildPlan(
                    logicalName,
                    descriptors,
                    activation);
            if (plan.Entries.Count == 0)
            {
                throw new InvalidDataException(
                    "The exact installed effect contains no authored-active, " +
                    "renderer-enabled top-level particle system allowed by its " +
                    "activation policy for " + _diagnosticId + ".");
            }
            for (int index = 0; index < plan.Entries.Count; index++)
            {
                InstalledParticleSystemPlanEntry entry = plan.Entries[index];
                CloneParticleSystem(
                    byKey[entry.StableKey],
                    prefab.transform,
                    entry,
                    layer,
                    index);
            }
            if (_particleSystems.Count == 0)
            {
                throw new InvalidDataException(
                    "The installed particle clone produced no bounded systems " +
                    "for " + _diagnosticId + ".");
            }
            if (plan.SystemsTruncated || plan.ParticleBudgetTruncated)
            {
                Debug.Log(
                    "Installed particle caps applied: id=" + _diagnosticId +
                    ", systems=" + plan.Entries.Count + "/" +
                    plan.EligibleCount + ", particles=" +
                    plan.AppliedParticleBudget + ".");
            }
        }

        private void CloneParticleSystem(
            SourceParticleSystem source,
            Transform prefabRoot,
            InstalledParticleSystemPlanEntry entry,
            int layer,
            int ordinal)
        {
            var ownedObject = new GameObject(
                "Particle " + ordinal.ToString("00") + " " +
                SanitizeObjectName(source.System.gameObject.name))
            {
                hideFlags = HideFlags.DontSave,
                layer = layer
            };
            ownedObject.SetActive(false);
            ownedObject.transform.SetParent(_root.transform, false);
            ApplyRelativeTransform(
                prefabRoot,
                source.System.transform,
                ownedObject.transform);
            try
            {
                ParticleSystem destination =
                    ownedObject.AddComponent<ParticleSystem>();
                ParticleSystemRenderer destinationRenderer =
                    ownedObject.GetComponent<ParticleSystemRenderer>();
                CopySafeModules(source.System, destination, entry);
                CopyRenderer(
                    source.Renderer,
                    destinationRenderer,
                    destination.trails.enabled);
                destination.useAutoRandomSeed = false;
                destination.randomSeed = entry.RandomSeed;
                ownedObject.SetActive(true);
                _particleSystems.Add(destination);
                _renderers.Add(destinationRenderer);
            }
            catch
            {
                DestroyOwnedObject(ownedObject);
                throw;
            }
        }

        private static HashSet<ParticleSystem> FindReferencedSubEmitters(
            ParticleSystem[] systems)
        {
            var referenced = new HashSet<ParticleSystem>();
            for (int systemIndex = 0;
                systemIndex < systems.Length;
                systemIndex++)
            {
                ParticleSystem.SubEmittersModule subEmitters =
                    systems[systemIndex].subEmitters;
                for (int subIndex = 0;
                    subIndex < subEmitters.subEmittersCount;
                    subIndex++)
                {
                    ParticleSystem candidate =
                        subEmitters.GetSubEmitterSystem(subIndex);
                    if (candidate != null)
                    {
                        referenced.Add(candidate);
                    }
                }
            }
            return referenced;
        }

        private static void CopySafeModules(
            ParticleSystem source,
            ParticleSystem destination,
            InstalledParticleSystemPlanEntry entry)
        {
            CopyMain(source.main, destination.main, entry.AppliedMaxParticles);
            CopyEmission(source.emission, destination.emission);
            CopyShape(source.shape, destination.shape);
            CopyVelocity(
                source.velocityOverLifetime,
                destination.velocityOverLifetime);
            CopyLimitVelocity(
                source.limitVelocityOverLifetime,
                destination.limitVelocityOverLifetime);
            CopyInheritVelocity(source.inheritVelocity, destination.inheritVelocity);
            CopyLifetimeByEmitterSpeed(
                source.lifetimeByEmitterSpeed,
                destination.lifetimeByEmitterSpeed);
            CopyForce(source.forceOverLifetime, destination.forceOverLifetime);
            CopyColorOverLifetime(
                source.colorOverLifetime,
                destination.colorOverLifetime);
            CopyColorBySpeed(source.colorBySpeed, destination.colorBySpeed);
            CopySizeOverLifetime(
                source.sizeOverLifetime,
                destination.sizeOverLifetime);
            CopySizeBySpeed(source.sizeBySpeed, destination.sizeBySpeed);
            CopyRotationOverLifetime(
                source.rotationOverLifetime,
                destination.rotationOverLifetime);
            CopyRotationBySpeed(
                source.rotationBySpeed,
                destination.rotationBySpeed);
            CopyNoise(source.noise, destination.noise);
            CopyTextureSheet(
                source.textureSheetAnimation,
                destination.textureSheetAnimation);
            CopyTrails(source.trails, destination.trails);
            CopyCustomData(source.customData, destination.customData);

            // These modules can call arbitrary scene objects, spawn unselected
            // systems, deliver messages, or create app-owned lights.
            ParticleSystem.SubEmittersModule subEmitters =
                destination.subEmitters;
            subEmitters.enabled = false;
            ParticleSystem.CollisionModule collision = destination.collision;
            collision.enabled = false;
            collision.sendCollisionMessages = false;
            ParticleSystem.TriggerModule trigger = destination.trigger;
            trigger.enabled = false;
            ParticleSystem.ExternalForcesModule externalForces =
                destination.externalForces;
            externalForces.enabled = false;
            externalForces.RemoveAllInfluences();
            ParticleSystem.LightsModule lights = destination.lights;
            lights.enabled = false;
            lights.light = null;
        }

        private static void CopyMain(
            ParticleSystem.MainModule source,
            ParticleSystem.MainModule destination,
            int maximumParticles)
        {
            destination.duration = Mathf.Clamp(
                source.duration,
                0.05f,
                MaximumDurationSeconds);
            destination.loop = source.loop;
            destination.prewarm = false;
            destination.startDelay = source.startDelay;
            destination.startLifetime = source.startLifetime;
            destination.startSpeed = source.startSpeed;
            destination.startSize3D = source.startSize3D;
            destination.startSize = source.startSize;
            destination.startSizeX = source.startSizeX;
            destination.startSizeY = source.startSizeY;
            destination.startSizeZ = source.startSizeZ;
            destination.startRotation3D = source.startRotation3D;
            destination.startRotation = source.startRotation;
            destination.startRotationX = source.startRotationX;
            destination.startRotationY = source.startRotationY;
            destination.startRotationZ = source.startRotationZ;
            destination.flipRotation = Mathf.Clamp01(source.flipRotation);
            destination.startColor = source.startColor;
            destination.gravityModifier = source.gravityModifier;
            destination.gravitySource = source.gravitySource;
            destination.simulationSpace = SanitizeSpace(source.simulationSpace);
            destination.customSimulationSpace = null;
            destination.simulationSpeed = Mathf.Clamp(
                source.simulationSpeed,
                0.05f,
                MaximumSimulationSpeed);
            destination.useUnscaledTime = true;
            destination.scalingMode = ParticleSystemScalingMode.Hierarchy;
            destination.playOnAwake = false;
            destination.emitterVelocityMode =
                ParticleSystemEmitterVelocityMode.Transform;
            destination.maxParticles = maximumParticles;
            destination.stopAction = ParticleSystemStopAction.None;
            destination.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            destination.ringBufferMode = ParticleSystemRingBufferMode.Disabled;
        }

        private static void CopyEmission(
            ParticleSystem.EmissionModule source,
            ParticleSystem.EmissionModule destination)
        {
            destination.enabled = source.enabled;
            destination.rateOverTime = source.rateOverTime;
            destination.rateOverDistance = source.rateOverDistance;
            int burstCount = Math.Min(
                source.burstCount,
                MaximumBurstsPerSystem);
            if (burstCount > 0)
            {
                var bursts = new ParticleSystem.Burst[burstCount];
                source.GetBursts(bursts);
                for (int index = 0; index < bursts.Length; index++)
                {
                    ParticleSystem.Burst burst = bursts[index];
                    ParticleSystem.MinMaxCurve count = burst.count;
                    count.constant = Mathf.Clamp(
                        count.constant,
                        0.0f,
                        InstalledParticleEffectPlanner
                            .MaximumParticlesPerSystem);
                    count.constantMin = Mathf.Clamp(
                        count.constantMin,
                        0.0f,
                        InstalledParticleEffectPlanner
                            .MaximumParticlesPerSystem);
                    count.constantMax = Mathf.Clamp(
                        count.constantMax,
                        0.0f,
                        InstalledParticleEffectPlanner
                            .MaximumParticlesPerSystem);
                    count.curveMultiplier = Mathf.Clamp(
                        count.curveMultiplier,
                        0.0f,
                        InstalledParticleEffectPlanner
                            .MaximumParticlesPerSystem);
                    burst.count = count;
                    burst.cycleCount = Mathf.Clamp(burst.cycleCount, 1, 8);
                    burst.repeatInterval = Mathf.Max(
                        0.05f,
                        burst.repeatInterval);
                    burst.probability = Mathf.Clamp01(burst.probability);
                    bursts[index] = burst;
                }
                destination.SetBursts(bursts, burstCount);
            }
        }

        private static void CopyShape(
            ParticleSystem.ShapeModule source,
            ParticleSystem.ShapeModule destination)
        {
            if (!source.enabled)
            {
                destination.enabled = false;
                return;
            }
            ParticleSystemShapeType shapeType = source.shapeType;
            Mesh mesh = source.mesh;
            Sprite sprite = source.sprite;
            if (shapeType == ParticleSystemShapeType.MeshRenderer)
            {
                MeshRenderer meshRenderer = source.meshRenderer;
                MeshFilter filter = meshRenderer == null
                    ? null
                    : meshRenderer.GetComponent<MeshFilter>();
                mesh = filter == null ? null : filter.sharedMesh;
                shapeType = ParticleSystemShapeType.Mesh;
            }
            else if (shapeType == ParticleSystemShapeType.SkinnedMeshRenderer)
            {
                SkinnedMeshRenderer skinned = source.skinnedMeshRenderer;
                mesh = skinned == null ? null : skinned.sharedMesh;
                shapeType = ParticleSystemShapeType.Mesh;
            }
            else if (shapeType == ParticleSystemShapeType.SpriteRenderer)
            {
                SpriteRenderer spriteRenderer = source.spriteRenderer;
                sprite = spriteRenderer == null ? null : spriteRenderer.sprite;
                shapeType = ParticleSystemShapeType.Sprite;
            }
            if (shapeType == ParticleSystemShapeType.Mesh && mesh == null ||
                shapeType == ParticleSystemShapeType.Sprite && sprite == null)
            {
                destination.enabled = false;
                return;
            }

            destination.enabled = true;
            destination.shapeType = shapeType;
            destination.angle = source.angle;
            destination.radius = source.radius;
            destination.radiusThickness = source.radiusThickness;
            destination.radiusMode = source.radiusMode;
            destination.radiusSpread = source.radiusSpread;
            destination.radiusSpeed = source.radiusSpeed;
            destination.arc = source.arc;
            destination.arcMode = source.arcMode;
            destination.arcSpread = source.arcSpread;
            destination.arcSpeed = source.arcSpeed;
            destination.length = source.length;
            destination.boxThickness = source.boxThickness;
            destination.donutRadius = source.donutRadius;
            destination.position = source.position;
            destination.rotation = source.rotation;
            destination.scale = source.scale;
            destination.alignToDirection = source.alignToDirection;
            destination.randomDirectionAmount = source.randomDirectionAmount;
            destination.sphericalDirectionAmount =
                source.sphericalDirectionAmount;
            destination.randomPositionAmount = source.randomPositionAmount;
            destination.meshShapeType = source.meshShapeType;
            destination.mesh = mesh;
            destination.meshMaterialIndex = source.meshMaterialIndex;
            destination.useMeshColors = source.useMeshColors;
            destination.useMeshMaterialIndex = source.useMeshMaterialIndex;
            destination.normalOffset = source.normalOffset;
            destination.meshSpawnMode = source.meshSpawnMode;
            destination.meshSpawnSpread = source.meshSpawnSpread;
            destination.meshSpawnSpeed = source.meshSpawnSpeed;
            destination.sprite = sprite;
            destination.texture = source.texture;
            destination.textureClipChannel = source.textureClipChannel;
            destination.textureClipThreshold = source.textureClipThreshold;
            destination.textureColorAffectsParticles =
                source.textureColorAffectsParticles;
            destination.textureAlphaAffectsParticles =
                source.textureAlphaAffectsParticles;
            destination.textureBilinearFiltering =
                source.textureBilinearFiltering;
        }

        private static void CopyVelocity(
            ParticleSystem.VelocityOverLifetimeModule source,
            ParticleSystem.VelocityOverLifetimeModule destination)
        {
            destination.enabled = source.enabled;
            destination.space = SanitizeSpace(source.space);
            destination.x = source.x;
            destination.y = source.y;
            destination.z = source.z;
            destination.orbitalX = source.orbitalX;
            destination.orbitalY = source.orbitalY;
            destination.orbitalZ = source.orbitalZ;
            destination.orbitalOffsetX = source.orbitalOffsetX;
            destination.orbitalOffsetY = source.orbitalOffsetY;
            destination.orbitalOffsetZ = source.orbitalOffsetZ;
            destination.radial = source.radial;
            destination.speedModifier = source.speedModifier;
        }

        private static void CopyLimitVelocity(
            ParticleSystem.LimitVelocityOverLifetimeModule source,
            ParticleSystem.LimitVelocityOverLifetimeModule destination)
        {
            destination.enabled = source.enabled;
            destination.separateAxes = source.separateAxes;
            destination.space = SanitizeSpace(source.space);
            destination.limit = source.limit;
            destination.limitX = source.limitX;
            destination.limitY = source.limitY;
            destination.limitZ = source.limitZ;
            destination.dampen = Mathf.Clamp01(source.dampen);
            destination.drag = source.drag;
            destination.multiplyDragByParticleSize =
                source.multiplyDragByParticleSize;
            destination.multiplyDragByParticleVelocity =
                source.multiplyDragByParticleVelocity;
        }

        private static void CopyInheritVelocity(
            ParticleSystem.InheritVelocityModule source,
            ParticleSystem.InheritVelocityModule destination)
        {
            destination.enabled = source.enabled;
            destination.mode = source.mode;
            destination.curve = source.curve;
        }

        private static void CopyLifetimeByEmitterSpeed(
            ParticleSystem.LifetimeByEmitterSpeedModule source,
            ParticleSystem.LifetimeByEmitterSpeedModule destination)
        {
            destination.enabled = source.enabled;
            destination.curve = source.curve;
            destination.range = source.range;
        }

        private static void CopyForce(
            ParticleSystem.ForceOverLifetimeModule source,
            ParticleSystem.ForceOverLifetimeModule destination)
        {
            destination.enabled = source.enabled;
            destination.space = SanitizeSpace(source.space);
            destination.x = source.x;
            destination.y = source.y;
            destination.z = source.z;
            destination.randomized = source.randomized;
        }

        private static void CopyColorOverLifetime(
            ParticleSystem.ColorOverLifetimeModule source,
            ParticleSystem.ColorOverLifetimeModule destination)
        {
            destination.enabled = source.enabled;
            destination.color = source.color;
        }

        private static void CopyColorBySpeed(
            ParticleSystem.ColorBySpeedModule source,
            ParticleSystem.ColorBySpeedModule destination)
        {
            destination.enabled = source.enabled;
            destination.color = source.color;
            destination.range = source.range;
        }

        private static void CopySizeOverLifetime(
            ParticleSystem.SizeOverLifetimeModule source,
            ParticleSystem.SizeOverLifetimeModule destination)
        {
            destination.enabled = source.enabled;
            destination.separateAxes = source.separateAxes;
            destination.size = source.size;
            destination.x = source.x;
            destination.y = source.y;
            destination.z = source.z;
        }

        private static void CopySizeBySpeed(
            ParticleSystem.SizeBySpeedModule source,
            ParticleSystem.SizeBySpeedModule destination)
        {
            destination.enabled = source.enabled;
            destination.separateAxes = source.separateAxes;
            destination.size = source.size;
            destination.x = source.x;
            destination.y = source.y;
            destination.z = source.z;
            destination.range = source.range;
        }

        private static void CopyRotationOverLifetime(
            ParticleSystem.RotationOverLifetimeModule source,
            ParticleSystem.RotationOverLifetimeModule destination)
        {
            destination.enabled = source.enabled;
            destination.separateAxes = source.separateAxes;
            destination.x = source.x;
            destination.y = source.y;
            destination.z = source.z;
        }

        private static void CopyRotationBySpeed(
            ParticleSystem.RotationBySpeedModule source,
            ParticleSystem.RotationBySpeedModule destination)
        {
            destination.enabled = source.enabled;
            destination.separateAxes = source.separateAxes;
            destination.x = source.x;
            destination.y = source.y;
            destination.z = source.z;
            destination.range = source.range;
        }

        private static void CopyNoise(
            ParticleSystem.NoiseModule source,
            ParticleSystem.NoiseModule destination)
        {
            destination.enabled = source.enabled;
            destination.separateAxes = source.separateAxes;
            destination.strength = source.strength;
            destination.strengthX = source.strengthX;
            destination.strengthY = source.strengthY;
            destination.strengthZ = source.strengthZ;
            destination.frequency = Mathf.Clamp(source.frequency, 0.0f, 4.0f);
            destination.damping = source.damping;
            destination.octaveCount = Mathf.Clamp(
                source.octaveCount,
                1,
                MaximumNoiseOctaves);
            destination.octaveMultiplier = source.octaveMultiplier;
            destination.octaveScale = source.octaveScale;
            destination.quality = source.quality;
            destination.scrollSpeed = source.scrollSpeed;
            destination.remapEnabled = source.remapEnabled;
            destination.remap = source.remap;
            destination.remapX = source.remapX;
            destination.remapY = source.remapY;
            destination.remapZ = source.remapZ;
            destination.positionAmount = source.positionAmount;
            destination.rotationAmount = source.rotationAmount;
            destination.sizeAmount = source.sizeAmount;
        }

        private static void CopyTextureSheet(
            ParticleSystem.TextureSheetAnimationModule source,
            ParticleSystem.TextureSheetAnimationModule destination)
        {
            destination.enabled = source.enabled;
            destination.mode = source.mode;
            destination.animation = source.animation;
            destination.numTilesX = Mathf.Clamp(source.numTilesX, 1, 32);
            destination.numTilesY = Mathf.Clamp(source.numTilesY, 1, 32);
            destination.timeMode = source.timeMode;
            destination.fps = Mathf.Clamp(source.fps, 1.0f, 60.0f);
            destination.rowMode = source.rowMode;
            destination.rowIndex = Mathf.Max(0, source.rowIndex);
            destination.frameOverTime = source.frameOverTime;
            destination.startFrame = source.startFrame;
            destination.cycleCount = Mathf.Clamp(source.cycleCount, 1, 8);
            destination.speedRange = source.speedRange;
            destination.uvChannelMask = source.uvChannelMask;
            int spriteCount = Math.Min(
                source.spriteCount,
                MaximumTextureSheetSprites);
            for (int index = 0; index < spriteCount; index++)
            {
                Sprite sprite = source.GetSprite(index);
                if (sprite != null)
                {
                    destination.AddSprite(sprite);
                }
            }
        }

        private static void CopyTrails(
            ParticleSystem.TrailModule source,
            ParticleSystem.TrailModule destination)
        {
            destination.enabled = source.enabled;
            destination.ratio = Mathf.Clamp01(source.ratio);
            destination.lifetime = source.lifetime;
            destination.minVertexDistance = Mathf.Max(
                0.0025f,
                source.minVertexDistance);
            destination.textureMode = source.textureMode;
            destination.textureScale = source.textureScale;
            destination.worldSpace = source.worldSpace;
            destination.dieWithParticles = source.dieWithParticles;
            destination.sizeAffectsWidth = source.sizeAffectsWidth;
            destination.sizeAffectsLifetime = source.sizeAffectsLifetime;
            destination.inheritParticleColor = source.inheritParticleColor;
            destination.colorOverLifetime = source.colorOverLifetime;
            destination.widthOverTrail = source.widthOverTrail;
            destination.colorOverTrail = source.colorOverTrail;
            destination.generateLightingData = false;
            destination.ribbonCount = Mathf.Clamp(
                source.ribbonCount,
                1,
                MaximumTrailRibbons);
            destination.shadowBias = 0.0f;
            destination.splitSubEmitterRibbons = false;
            destination.attachRibbonsToTransform = false;
        }

        private static void CopyCustomData(
            ParticleSystem.CustomDataModule source,
            ParticleSystem.CustomDataModule destination)
        {
            destination.enabled = source.enabled;
            CopyCustomDataChannel(
                source,
                destination,
                ParticleSystemCustomData.Custom1);
            CopyCustomDataChannel(
                source,
                destination,
                ParticleSystemCustomData.Custom2);
        }

        private static void CopyCustomDataChannel(
            ParticleSystem.CustomDataModule source,
            ParticleSystem.CustomDataModule destination,
            ParticleSystemCustomData channel)
        {
            ParticleSystemCustomDataMode mode = source.GetMode(channel);
            destination.SetMode(channel, mode);
            if (mode == ParticleSystemCustomDataMode.Vector)
            {
                int componentCount = Mathf.Clamp(
                    source.GetVectorComponentCount(channel),
                    1,
                    4);
                destination.SetVectorComponentCount(channel, componentCount);
                for (int index = 0; index < componentCount; index++)
                {
                    destination.SetVector(
                        channel,
                        index,
                        source.GetVector(channel, index));
                }
            }
            else if (mode == ParticleSystemCustomDataMode.Color)
            {
                destination.SetColor(channel, source.GetColor(channel));
            }
        }

        private void CopyRenderer(
            ParticleSystemRenderer source,
            ParticleSystemRenderer destination,
            bool trailsEnabled)
        {
            if (source == null || destination == null)
            {
                throw new InvalidDataException(
                    "An eligible installed particle system has no renderer.");
            }
            destination.renderMode = source.renderMode;
            destination.alignment = source.alignment;
            destination.sortMode = source.sortMode;
            destination.sortingFudge = Mathf.Clamp(
                source.sortingFudge,
                -10.0f,
                10.0f);
            destination.minParticleSize = Mathf.Clamp01(
                source.minParticleSize);
            destination.maxParticleSize = Mathf.Clamp(
                source.maxParticleSize,
                0.01f,
                1.0f);
            destination.pivot = source.pivot;
            destination.flip = source.flip;
            destination.normalDirection = source.normalDirection;
            destination.cameraVelocityScale = source.cameraVelocityScale;
            destination.velocityScale = source.velocityScale;
            destination.lengthScale = source.lengthScale;
            destination.freeformStretching = source.freeformStretching;
            destination.rotateWithStretchDirection =
                source.rotateWithStretchDirection;
            destination.allowRoll = source.allowRoll;
            destination.meshDistribution = source.meshDistribution;
            if (source.renderMode == ParticleSystemRenderMode.Mesh)
            {
                Mesh mesh = source.mesh;
                if (mesh == null)
                {
                    throw new InvalidDataException(
                        "An installed mesh particle renderer has no mesh.");
                }
                destination.mesh = mesh;
            }

            Material[] sources = source.sharedMaterials;
            int materialCount = sources == null || sources.Length == 0
                ? 1
                : Math.Min(sources.Length, MaximumMaterialsPerRenderer);
            var replacements = new Material[materialCount];
            for (int index = 0; index < materialCount; index++)
            {
                Material sourceMaterial = sources == null || sources.Length == 0
                    ? null
                    : sources[index];
                replacements[index] = GetOrCreateOwnedMaterial(sourceMaterial);
            }
            destination.sharedMaterials = replacements;
            if (trailsEnabled && source.trailMaterial != null)
            {
                destination.trailMaterial =
                    GetOrCreateOwnedMaterial(source.trailMaterial);
            }

            var vertexStreams = new List<ParticleSystemVertexStream>();
            source.GetActiveVertexStreams(vertexStreams);
            destination.SetActiveVertexStreams(vertexStreams);
            var trailStreams = new List<ParticleSystemVertexStream>();
            source.GetActiveTrailVertexStreams(trailStreams);
            destination.SetActiveTrailVertexStreams(trailStreams);

            destination.shadowCastingMode = ShadowCastingMode.Off;
            destination.receiveShadows = false;
            destination.lightProbeUsage = LightProbeUsage.Off;
            destination.reflectionProbeUsage = ReflectionProbeUsage.Off;
            destination.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            destination.allowOcclusionWhenDynamic = false;
            destination.enableGPUInstancing = false;
            destination.renderingLayerMask = uint.MaxValue;
            destination.sortingLayerID = source.sortingLayerID;
            destination.sortingOrder = Mathf.Clamp(
                source.sortingOrder,
                -100,
                100);
            destination.forceRenderingOff = false;
            destination.enabled = true;
        }

        private Material GetOrCreateOwnedMaterial(Material source)
        {
            Material existing;
            if (source != null &&
                _materialReplacements.TryGetValue(source, out existing))
            {
                return existing;
            }
            if (_ownedMaterials.Count >= MaximumOwnedMaterials)
            {
                throw new InvalidDataException(
                    "The installed effect exceeded its app-owned material cap.");
            }
            Shader shader = ResolveParticleShader();
            var material = new Material(shader)
            {
                name = (source == null ? "Particle" : source.name) +
                    " (Desktop Pet Safe)",
                hideFlags = HideFlags.DontSave,
                renderQueue = 3000
            };
            CopyMaterialTextureAndColor(source, material);
            _ownedMaterials.Add(material);
            if (source != null)
            {
                _materialReplacements.Add(source, material);
            }
            return material;
        }

        private static void CopyMaterialTextureAndColor(
            Material source,
            Material destination)
        {
            Texture texture = null;
            string textureProperty = null;
            Color color = Color.white;
            if (source != null)
            {
                string[] textureProperties =
                {
                    "_MainTex",
                    "_BaseMap",
                    "_BaseColorMap",
                    "_UnlitColorMap",
                    "_BaseTex",
                    "_Texture",
                    "_Tex"
                };
                for (int index = 0; index < textureProperties.Length; index++)
                {
                    string candidate = textureProperties[index];
                    if (source.HasProperty(candidate) &&
                        source.GetTexture(candidate) != null)
                    {
                        texture = source.GetTexture(candidate);
                        textureProperty = candidate;
                        break;
                    }
                }
                if (texture == null)
                {
                    string[] fallbackProperties =
                        source.GetTexturePropertyNames();
                    int fallbackCount = Math.Min(
                        fallbackProperties.Length,
                        16);
                    for (int index = 0; index < fallbackCount; index++)
                    {
                        string candidate = fallbackProperties[index];
                        Texture candidateTexture = source.GetTexture(candidate);
                        if (candidateTexture != null)
                        {
                            texture = candidateTexture;
                            textureProperty = candidate;
                            break;
                        }
                    }
                }
                if (source.HasProperty("_Color"))
                {
                    color = source.GetColor("_Color");
                }
                else if (source.HasProperty("_BaseColor"))
                {
                    color = source.GetColor("_BaseColor");
                }
                else if (source.HasProperty("_TintColor"))
                {
                    color = source.GetColor("_TintColor");
                }
            }
            if (texture != null)
            {
                Vector2 scale = source.GetTextureScale(textureProperty);
                Vector2 offset = source.GetTextureOffset(textureProperty);
                if (destination.HasProperty("_MainTex"))
                {
                    destination.SetTexture("_MainTex", texture);
                    destination.SetTextureScale("_MainTex", scale);
                    destination.SetTextureOffset("_MainTex", offset);
                }
                if (destination.HasProperty("_BaseMap"))
                {
                    destination.SetTexture("_BaseMap", texture);
                    destination.SetTextureScale("_BaseMap", scale);
                    destination.SetTextureOffset("_BaseMap", offset);
                }
            }
            if (destination.HasProperty("_Color"))
            {
                destination.SetColor("_Color", color);
            }
            if (destination.HasProperty("_BaseColor"))
            {
                destination.SetColor("_BaseColor", color);
            }
            if (destination.HasProperty("_Surface"))
            {
                destination.SetFloat("_Surface", 1.0f);
            }
            if (destination.HasProperty("_ZWrite"))
            {
                destination.SetFloat("_ZWrite", 0.0f);
            }
        }

        private static Shader ResolveParticleShader()
        {
            Shader shader = Resources.Load<Shader>("InstalledParticleUnlit");
            if (shader == null || !shader.isSupported)
            {
                shader = Shader.Find(
                    "Universal Render Pipeline/Particles/Unlit");
            }
            if (shader == null || !shader.isSupported)
            {
                shader = Shader.Find("Particles/Standard Unlit");
            }
            if (shader == null || !shader.isSupported)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null || !shader.isSupported)
            {
                throw new InvalidOperationException(
                    "No app-compatible particle shader is available.");
            }
            return shader;
        }

        private static GameObject LoadUniqueExactPrefab(
            AssetBundle bundle,
            string expectedPrefabName,
            string diagnosticId)
        {
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
                        "The installed effect basename is ambiguous for " +
                        diagnosticId + ".");
                }
                exactAssetName = candidate;
            }
            if (exactAssetName == null)
            {
                throw new FileNotFoundException(
                    "The exact installed effect prefab was not present for " +
                    diagnosticId + ".",
                    expectedPrefabName);
            }
            GameObject prefab = bundle.LoadAsset<GameObject>(exactAssetName);
            if (prefab == null)
            {
                throw new InvalidDataException(
                    "The exact installed effect asset is not a GameObject for " +
                    diagnosticId + ".");
            }
            return prefab;
        }

        private static void ValidateLoadArguments(
            BundleRepository bundles,
            Transform parent,
            string logicalName,
            string expectedPrefabName,
            string diagnosticId,
            PetInstalledSceneEffectActivation activation)
        {
            if (bundles == null)
            {
                throw new ArgumentNullException("bundles");
            }
            if (parent == null)
            {
                throw new ArgumentNullException("parent");
            }
            if (string.IsNullOrWhiteSpace(logicalName) ||
                !logicalName.Trim().StartsWith(
                    "3d/effect/",
                    StringComparison.Ordinal) ||
                logicalName.IndexOf('\\') >= 0 || logicalName.IndexOf(':') >= 0)
            {
                throw new ArgumentException(
                    "A safe exact installed effect logical name is required.",
                    "logicalName");
            }
            if (string.IsNullOrWhiteSpace(expectedPrefabName) ||
                expectedPrefabName.IndexOf('/') >= 0 ||
                expectedPrefabName.IndexOf('\\') >= 0)
            {
                throw new ArgumentException(
                    "An exact path-free installed effect basename is required.",
                    "expectedPrefabName");
            }
            if (string.IsNullOrWhiteSpace(diagnosticId) ||
                diagnosticId.IndexOf('/') >= 0 ||
                diagnosticId.IndexOf('\\') >= 0)
            {
                throw new ArgumentException(
                    "A path-free installed effect diagnostic ID is required.",
                    "diagnosticId");
            }
            if (!Enum.IsDefined(
                typeof(PetInstalledSceneEffectActivation),
                activation))
            {
                throw new ArgumentOutOfRangeException("activation");
            }
        }

        private static string CreateStableSystemKey(
            ParticleSystem system,
            Transform prefabRoot)
        {
            var segments = new List<string>();
            Transform cursor = system.transform;
            while (cursor != null)
            {
                segments.Add(
                    cursor.GetSiblingIndex().ToString("000") + "-" +
                    SanitizeObjectName(cursor.name));
                if (cursor == prefabRoot)
                {
                    break;
                }
                cursor = cursor.parent;
            }
            if (cursor != prefabRoot)
            {
                throw new InvalidDataException(
                    "An installed particle system escaped its prefab root.");
            }
            segments.Reverse();
            int componentIndex = 0;
            ParticleSystem[] peers = system.GetComponents<ParticleSystem>();
            for (int index = 0; index < peers.Length; index++)
            {
                if (peers[index] == system)
                {
                    componentIndex = index;
                    break;
                }
            }
            return string.Join("/", segments.ToArray()) + "#" +
                componentIndex.ToString("00");
        }

        private static bool IsAuthoredActive(
            Transform value,
            Transform prefabRoot)
        {
            Transform cursor = value;
            while (cursor != null)
            {
                if (!cursor.gameObject.activeSelf)
                {
                    return false;
                }
                if (cursor == prefabRoot)
                {
                    return true;
                }
                cursor = cursor.parent;
            }
            return false;
        }

        private static void ApplyRelativeTransform(
            Transform prefabRoot,
            Transform source,
            Transform destination)
        {
            destination.localPosition = prefabRoot.InverseTransformPoint(
                source.position);
            destination.localRotation =
                Quaternion.Inverse(prefabRoot.rotation) * source.rotation;
            Vector3 rootScale = prefabRoot.lossyScale;
            Vector3 sourceScale = source.lossyScale;
            destination.localScale = new Vector3(
                SafeScaleRatio(sourceScale.x, rootScale.x),
                SafeScaleRatio(sourceScale.y, rootScale.y),
                SafeScaleRatio(sourceScale.z, rootScale.z));
        }

        private static float SafeScaleRatio(float numerator, float denominator)
        {
            if (float.IsNaN(numerator) || float.IsInfinity(numerator) ||
                float.IsNaN(denominator) || float.IsInfinity(denominator) ||
                Mathf.Abs(denominator) < 0.000001f)
            {
                return 1.0f;
            }
            float ratio = numerator / denominator;
            return float.IsNaN(ratio) || float.IsInfinity(ratio)
                ? 1.0f
                : ratio;
        }

        private void StopAndClear()
        {
            for (int index = 0; index < _particleSystems.Count; index++)
            {
                ParticleSystem particles = _particleSystems[index];
                if (particles != null)
                {
                    particles.Stop(
                        false,
                        ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed || _root == null)
            {
                throw new ObjectDisposedException("InstalledVignetteEffect");
            }
        }

        private void LogSummary(string state)
        {
            Debug.Log(
                "Installed particle effect " + state + ": id=" +
                _diagnosticId + ", systems=" + _particleSystems.Count +
                ", renderers=" + _renderers.Count +
                ", ownedMaterials=" + _ownedMaterials.Count + ".");
        }

        private static ParticleSystemSimulationSpace SanitizeSpace(
            ParticleSystemSimulationSpace value)
        {
            return value == ParticleSystemSimulationSpace.Custom
                ? ParticleSystemSimulationSpace.Local
                : value;
        }

        private static string SanitizeObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unnamed";
            }
            return value.Replace('/', '-').Replace('\\', '-').Trim();
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

        private static void DisposeLeaseSafely(BundleLease lease, string context)
        {
            if (lease == null)
            {
                return;
            }
            try
            {
                lease.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Installed particle lease cleanup failed during " + context +
                    " (" + exception.GetType().Name + ").");
            }
        }

        private static void DestroyOwnedObject(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(value);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }

        private sealed class SourceParticleSystem
        {
            public SourceParticleSystem(
                ParticleSystem system,
                ParticleSystemRenderer renderer)
            {
                System = system;
                Renderer = renderer;
            }

            public ParticleSystem System { get; private set; }
            public ParticleSystemRenderer Renderer { get; private set; }
        }
    }
}
