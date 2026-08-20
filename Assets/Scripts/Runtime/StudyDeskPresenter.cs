using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Presents the props used by Study with Oguri. When compatible installed
    /// game prefabs are available they remain loaded in memory for the session;
    /// otherwise a complete procedural study setup is used. Nothing is exported.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StudyDeskPresenter : MonoBehaviour
    {
        private const string DeskAsset =
            "3d/env/set/set10001/prop/pfb_env_set10001_prop001_000";
        private const string DeskPrefabName = "pfb_env_set10001_prop001_000";
        private const string ChairAsset =
            "3d/env/set/set10001/prop/pfb_env_set10001_prop000_000";
        private const string ChairPrefabName = "pfb_env_set10001_prop000_000";
        private const string BookAsset =
            "3d/chara/prop/prop1025_00/pfb_chr_prop1025_00";
        private const string BookPrefabName = "pfb_chr_prop1025_00";
        private const string CarrotAsset =
            "3d/chara/prop/prop1207_00/pfb_chr_prop1207_00";
        private const string CarrotPrefabName = "pfb_chr_prop1207_00";
        private const string TazunaRedPenAsset =
            "3d/chara/prop/prop1008_00/pfb_chr_prop1008_00";
        private const string TazunaRedPenPrefabName = "pfb_chr_prop1008_00";
        private const string DerbyTrophyAsset =
            "3d/env/cutin/cutin1070_00/pfb_env_cutin1070_00_00_derby_trophy";
        private const string DerbyTrophyPrefabName =
            "pfb_env_cutin1070_00_00_derby_trophy";

        private static readonly string[] InstalledBasePropAssets =
        {
            DeskAsset,
            ChairAsset,
            BookAsset
        };

        private readonly List<Material> _ownedMaterials = new List<Material>();
        private readonly Dictionary<Material, Material> _installedMaterialCopies =
            new Dictionary<Material, Material>();
        private readonly Dictionary<string, Transform> _deskRewardRoots =
            new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly Dictionary<string, BundleLease> _deskRewardLeases =
            new Dictionary<string, BundleLease>(StringComparer.Ordinal);

        private BundleLease _installedPropLease;
        private Transform _visualRoot;
        private Transform _carrotCharmRoot;
        private float _characterHeight;
        private bool _initialized;
        private bool _visible;
        private bool _paused;
        private bool _carrotDeskCharmVisible;
        private string _equippedDeskItemId = string.Empty;

        public bool IsVisible { get { return _visible; } }

        public bool IsPaused { get { return _paused; } }

        public bool UsesInstalledStudyProps { get; private set; }

        public bool IsCarrotDeskCharmVisible
        {
            get
            {
                return string.Equals(
                    _equippedDeskItemId,
                    DeskShopCatalog.CarrotCharmId,
                    StringComparison.Ordinal) &&
                    _carrotDeskCharmVisible;
            }
        }

        public string EquippedDeskItemId { get { return _equippedDeskItemId; } }

        /// <summary>
        /// Builds the visual once and leaves it hidden. The supplied root should
        /// expose the assembled character's semantic attachment slots. Furniture
        /// uses the world slot so it follows the desktop-pet visual frame without
        /// being parented beneath an animated body bone.
        /// </summary>
        public void Initialize(
            PetAttachmentRig attachmentRig,
            BundleRepository bundles)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "The study desk presenter is already initialized.");
            }
            if (attachmentRig == null)
            {
                throw new ArgumentNullException("attachmentRig");
            }
            if (bundles == null)
            {
                throw new ArgumentNullException("bundles");
            }

            if (!attachmentRig.IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the attachment rig before the study presenter.");
            }

            Transform worldSlot;
            if (!attachmentRig.TryGetSlot(
                PetAttachmentSlots.World,
                out worldSlot))
            {
                throw new InvalidOperationException(
                    "The study presenter requires the world attachment slot.");
            }

            Bounds bounds = attachmentRig.CharacterLocalBounds;
            _characterHeight = Mathf.Max(0.1f, attachmentRig.CharacterHeight);

            var visualObject = new GameObject("Study Desk Visual");
            _visualRoot = visualObject.transform;
            _visualRoot.SetParent(worldSlot, false);
            _visualRoot.gameObject.SetActive(false);

            BuildVisual(bounds, bundles);
            _initialized = true;
        }

        /// <summary>
        /// Shows the complete study setup and applies the current equipped desk
        /// reward without depending on a character-specific reward type.
        /// </summary>
        public void Show(string equippedDeskItemId)
        {
            EnsureInitialized();
            _visible = true;
            _paused = false;
            SetEquippedDeskItem(equippedDeskItemId);
            _visualRoot.gameObject.SetActive(true);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            LogInstalledRendererState();
#endif
        }

        public void Show(bool showCarrotDeskCharm)
        {
            Show(showCarrotDeskCharm
                ? DeskShopCatalog.CarrotCharmId
                : string.Empty);
        }

        public void Hide()
        {
            if (!_initialized)
            {
                return;
            }

            _visible = false;
            _paused = false;
            _visualRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// Records the paused visual state. The props remain visible so a paused
        /// focus session reads as paused rather than abruptly disappearing.
        /// </summary>
        public void SetPaused(bool paused)
        {
            if (!_initialized)
            {
                return;
            }

            _paused = paused;
        }

        public void SetCarrotDeskCharmVisible(bool visible)
        {
            if (!_initialized && _carrotCharmRoot == null)
            {
                // Show calls this after BuildVisual but immediately before the
                // initialized flag is committed during normal construction.
                if (_visualRoot == null)
                {
                    return;
                }
            }

            SetEquippedDeskItem(visible
                ? DeskShopCatalog.CarrotCharmId
                : string.Empty);
        }

        public bool CanPresentDeskItem(string itemId)
        {
            return !string.IsNullOrEmpty(itemId) &&
                _deskRewardRoots.ContainsKey(itemId);
        }

        public void SetEquippedDeskItem(string itemId)
        {
            string requested = itemId ?? string.Empty;
            if (!string.IsNullOrEmpty(requested) &&
                !_deskRewardRoots.ContainsKey(requested))
            {
                requested = string.Empty;
            }

            foreach (KeyValuePair<string, Transform> reward in _deskRewardRoots)
            {
                if (reward.Value != null)
                {
                    reward.Value.gameObject.SetActive(
                        string.Equals(
                            reward.Key,
                            requested,
                            StringComparison.Ordinal));
                }
            }
            _equippedDeskItemId = requested;
            _carrotDeskCharmVisible = string.Equals(
                requested,
                DeskShopCatalog.CarrotCharmId,
                StringComparison.Ordinal);
        }

        private void BuildVisual(Bounds bounds, BundleRepository bundles)
        {
            if (TryBuildInstalledVisual(bounds, bundles))
            {
                UsesInstalledStudyProps = true;
                Debug.Log(
                    "Study props: using the desk, chair, and notebook from " +
                    "the installed Umamusume files. Available desk rewards were " +
                    "loaded independently. No assets were exported.");
                return;
            }

            UsesInstalledStudyProps = false;
            BuildProceduralVisual(bounds);
            Debug.LogWarning(
                "Study props: compatible installed study prefabs were not " +
                "available, so the built-in procedural fallback is active.");
        }

        private bool TryBuildInstalledVisual(
            Bounds bounds,
            BundleRepository bundles)
        {
            BundleLease lease = null;
            Transform installedRoot = null;
            try
            {
                lease = bundles.AcquireManyWithShaderFirst(
                    InstalledBasePropAssets);

                GameObject deskPrefab = LoadExactPrefab(
                    lease,
                    DeskAsset,
                    DeskPrefabName);
                GameObject chairPrefab = LoadExactPrefab(
                    lease,
                    ChairAsset,
                    ChairPrefabName);
                GameObject bookPrefab = LoadExactPrefab(
                    lease,
                    BookAsset,
                    BookPrefabName);
                var installedObject = new GameObject("Installed Study Props");
                installedRoot = installedObject.transform;
                installedRoot.SetParent(_visualRoot, false);

                float height = _characterHeight;
                float centerX = bounds.center.x;
                float floorY = bounds.min.y;

                float deskWidth = Mathf.Max(bounds.size.x * 0.92f, height * 0.64f);
                float deskHeight = height * 0.31f;
                float deskDepth = height * 0.22f;
                float deskCenterZ = bounds.max.z + height * 0.035f;
                CreateInstalledProp(
                    installedRoot,
                    "Study Desk",
                    deskPrefab,
                    new Vector3(deskWidth, deskHeight, deskDepth),
                    new Vector3(
                        centerX,
                        floorY + deskHeight * 0.5f,
                        deskCenterZ),
                    Quaternion.identity,
                    PropAxisMode.Direct);

                float chairWidth = height * 0.30f;
                float chairHeight = height * 0.34f;
                float chairDepth = height * 0.20f;
                CreateInstalledProp(
                    installedRoot,
                    "Study Chair",
                    chairPrefab,
                    new Vector3(chairWidth, chairHeight, chairDepth),
                    new Vector3(
                        centerX,
                        floorY + chairHeight * 0.5f,
                        bounds.min.z - chairDepth * 0.45f),
                    Quaternion.identity,
                    PropAxisMode.Direct);

                float bookWidth = height * 0.31f;
                float bookHeight = height * 0.16f;
                float bookDepth = height * 0.012f;
                float deskTopY = floorY + deskHeight;
                float bookCenterZ = deskCenterZ - deskDepth * 0.18f;
                // The Wit notebook is authored flat in XZ with its page side on
                // +Y. Tilt that normal up and toward Oguri (-Z), so the viewer
                // sees the cover/edge instead of looking directly at the pages.
                const float bookTiltDegrees = 8.0f;
                Quaternion bookRotation = Quaternion.Euler(-bookTiltDegrees, 0.0f, 0.0f);
                float bookVerticalExtent =
                    Mathf.Abs(Mathf.Sin(bookTiltDegrees * Mathf.Deg2Rad)) *
                        bookHeight * 0.5f +
                    Mathf.Abs(Mathf.Cos(bookTiltDegrees * Mathf.Deg2Rad)) *
                        bookDepth * 0.5f;
                Vector3 bookCenter = new Vector3(
                    centerX,
                    deskTopY + bookVerticalExtent + height * 0.001f,
                    bookCenterZ);
                CreateInstalledProp(
                    installedRoot,
                    "Study Book",
                    bookPrefab,
                    new Vector3(bookWidth, bookDepth, bookHeight),
                    bookCenter,
                    bookRotation,
                    PropAxisMode.Direct);

                _installedPropLease = lease;
                lease = null;

                TryBuildInstalledReward(
                    bounds,
                    installedRoot,
                    bundles,
                    DeskShopCatalog.CarrotCharmId,
                    "Carrot Desk Charm",
                    CarrotAsset,
                    CarrotPrefabName,
                    BuildInstalledCarrotCharm);
                TryBuildInstalledReward(
                    bounds,
                    installedRoot,
                    bundles,
                    DeskShopCatalog.TazunaRedPenId,
                    "Tazuna Red Pen",
                    TazunaRedPenAsset,
                    TazunaRedPenPrefabName,
                    BuildInstalledTazunaRedPen);
                TryBuildInstalledReward(
                    bounds,
                    installedRoot,
                    bundles,
                    DeskShopCatalog.DerbyTrophyId,
                    "Derby Trophy",
                    DerbyTrophyAsset,
                    DerbyTrophyPrefabName,
                    BuildInstalledDerbyTrophy);
                return true;
            }
            catch (Exception exception)
            {
                _carrotCharmRoot = null;
                _deskRewardRoots.Clear();
                if (installedRoot != null)
                {
                    installedRoot.gameObject.SetActive(false);
                    Destroy(installedRoot.gameObject);
                }
                ReleaseProceduralResources();
                Debug.LogWarning(
                    "Could not use the installed study prop set. " +
                    "Falling back without mixing prop sources.\n" + exception);
                return false;
            }
            finally
            {
                if (lease != null)
                {
                    try
                    {
                        lease.Dispose();
                    }
                    catch (Exception cleanupError)
                    {
                        Debug.LogWarning(
                            "Could not release the rejected installed study " +
                            "prop bundles.\n" + cleanupError);
                    }
                }
            }
        }

        private delegate Transform InstalledRewardBuilder(
            Bounds bounds,
            Transform parent,
            GameObject prefab);

        private void TryBuildInstalledReward(
            Bounds bounds,
            Transform installedRoot,
            BundleRepository bundles,
            string itemId,
            string displayName,
            string assetName,
            string prefabName,
            InstalledRewardBuilder builder)
        {
            BundleLease lease = null;
            Transform rewardContainer = null;
            try
            {
                lease = bundles.AcquireManyWithShaderFirst(
                    new[] { assetName });
                GameObject prefab = LoadExactPrefab(
                    lease,
                    assetName,
                    prefabName);

                var containerObject = new GameObject(
                    displayName + " Reward");
                rewardContainer = containerObject.transform;
                rewardContainer.SetParent(installedRoot, false);

                Transform rewardRoot = builder(
                    bounds,
                    rewardContainer,
                    prefab);
                if (rewardRoot == null)
                {
                    throw new InvalidDataException(
                        "The installed " + displayName +
                        " builder returned no visual root.");
                }

                rewardContainer.gameObject.SetActive(false);
                _deskRewardRoots[itemId] = rewardContainer;
                _deskRewardLeases[itemId] = lease;
                lease = null;
            }
            catch (Exception exception)
            {
                _deskRewardRoots.Remove(itemId);
                if (rewardContainer != null)
                {
                    rewardContainer.gameObject.SetActive(false);
                    Destroy(rewardContainer.gameObject);
                }
                Debug.LogWarning(
                    "Study desk reward unavailable: " + displayName +
                    ". The base study setup and other rewards remain usable.\n" +
                    exception);
            }
            finally
            {
                if (lease != null)
                {
                    try
                    {
                        lease.Dispose();
                    }
                    catch (Exception cleanupError)
                    {
                        Debug.LogWarning(
                            "Could not release the unavailable " + displayName +
                            " bundles.\n" + cleanupError);
                    }
                }
            }
        }

        private Transform BuildInstalledCarrotCharm(
            Bounds bounds,
            Transform parent,
            GameObject carrotPrefab)
        {
            float height = _characterHeight;
            float deskWidth = Mathf.Max(bounds.size.x * 0.92f, height * 0.64f);
            float deskTopY = bounds.min.y + height * 0.31f;
            float deskCenterZ = bounds.max.z + height * 0.035f;
            float deskDepth = height * 0.22f;

            // prop1207_00 is the authentic carrot used by Oguri's home eating
            // sequence. Its length is authored along local Z, so rotate +Z to
            // world +Y and keep the full rig/prefab hierarchy intact.
            float carrotLength = height * 0.075f;
            Transform carrotRoot = CreateInstalledProp(
                parent,
                "Carrot Desk Charm",
                carrotPrefab,
                new Vector3(carrotLength, carrotLength, carrotLength),
                new Vector3(
                    bounds.center.x - deskWidth * 0.31f,
                    deskTopY + carrotLength * 0.5f + height * 0.002f,
                    deskCenterZ + deskDepth * 0.28f),
                Quaternion.Euler(-90.0f, 0.0f, 0.0f),
                PropAxisMode.UniformFromSourceZ);
            _carrotCharmRoot = carrotRoot;
            return carrotRoot;
        }

        private Transform BuildInstalledTazunaRedPen(
            Bounds bounds,
            Transform parent,
            GameObject penPrefab)
        {
            float height = _characterHeight;
            float deskWidth = Mathf.Max(bounds.size.x * 0.92f, height * 0.64f);
            float deskTopY = bounds.min.y + height * 0.31f;
            float deskCenterZ = bounds.max.z + height * 0.035f;
            float deskDepth = height * 0.22f;

            // prop1008_00 is Tazuna's red pen. Its long axis is local Z, so
            // normalize uniformly from the measured mesh-local Z extent. Keep
            // it on the world-slot desk and rotate only around Y to make a
            // readable diagonal in the desktop plane; it never follows a hand.
            float penLength = height * 0.12f;
            const float sourceThicknessToLength = 0.01687f / 0.156749f;
            float penThickness = penLength * sourceThicknessToLength;
            return CreateInstalledProp(
                parent,
                "Tazuna Red Pen",
                penPrefab,
                new Vector3(penLength, penLength, penLength),
                new Vector3(
                    bounds.center.x + deskWidth * 0.29f,
                    deskTopY + penThickness * 0.5f + height * 0.002f,
                    deskCenterZ + deskDepth * 0.24f),
                Quaternion.Euler(0.0f, 50.0f, 0.0f),
                PropAxisMode.UniformFromSourceZ);
        }

        private Transform BuildInstalledDerbyTrophy(
            Bounds bounds,
            Transform parent,
            GameObject trophyPrefab)
        {
            float height = _characterHeight;
            float deskWidth = Mathf.Max(bounds.size.x * 0.92f, height * 0.64f);
            float deskTopY = bounds.min.y + height * 0.31f;
            float deskCenterZ = bounds.max.z + height * 0.035f;
            float deskDepth = height * 0.22f;
            float trophyHeight = height * 0.10f;

            return CreateInstalledProp(
                parent,
                "Derby Trophy",
                trophyPrefab,
                new Vector3(trophyHeight, trophyHeight, trophyHeight),
                new Vector3(
                    bounds.center.x + deskWidth * 0.34f,
                    deskTopY + trophyHeight * 0.5f + height * 0.002f,
                    deskCenterZ + deskDepth * 0.28f),
                Quaternion.identity,
                PropAxisMode.UniformFromSourceY,
                KeepDerbyTrophyMeshOnly);
        }

        private static void KeepDerbyTrophyMeshOnly(GameObject instance)
        {
            Transform keptChild = null;
            for (int index = 0; index < instance.transform.childCount; index++)
            {
                Transform child = instance.transform.GetChild(index);
                bool keep = string.Equals(
                    child.name,
                    "derby_trophy",
                    StringComparison.OrdinalIgnoreCase);
                child.gameObject.SetActive(keep);
                if (keep)
                {
                    if (keptChild != null)
                    {
                        throw new InvalidDataException(
                            "The installed Derby Trophy prefab contains more " +
                            "than one derby_trophy child.");
                    }
                    keptChild = child;
                }
            }
            if (keptChild == null)
            {
                throw new InvalidDataException(
                    "The installed Derby Trophy prefab has no direct " +
                    "derby_trophy child.");
            }
        }

        private void BuildProceduralVisual(Bounds bounds)
        {
            Shader shader = ResolveShader();
            Material darkWood = CreateMaterial(
                shader,
                "Study desk dark wood",
                new Color(0.25f, 0.105f, 0.055f, 1.0f));
            Material warmWood = CreateMaterial(
                shader,
                "Study desk warm wood",
                new Color(0.55f, 0.25f, 0.105f, 1.0f));
            Material page = CreateMaterial(
                shader,
                "Study book pages",
                new Color(1.0f, 0.91f, 0.68f, 1.0f));
            Material pageShade = CreateMaterial(
                shader,
                "Study book page shade",
                new Color(0.82f, 0.68f, 0.43f, 1.0f));
            Material ink = CreateMaterial(
                shader,
                "Study book ink",
                new Color(0.23f, 0.34f, 0.39f, 1.0f));
            float height = _characterHeight;
            float centerX = bounds.center.x;
            float deskTopY = bounds.min.y + height * 0.31f;
            float deskWidth = Mathf.Max(bounds.size.x * 0.92f, height * 0.64f);
            float deskThickness = height * 0.052f;
            float frontZ = bounds.max.z + height * 0.035f;

            CreateCube(
                _visualRoot,
                "Desk top",
                new Vector3(centerX, deskTopY, frontZ),
                new Vector3(deskWidth, deskThickness, height * 0.075f),
                Quaternion.identity,
                warmWood);
            CreateCube(
                _visualRoot,
                "Desk front trim",
                new Vector3(
                    centerX,
                    deskTopY - deskThickness * 0.44f,
                    frontZ + height * 0.043f),
                new Vector3(deskWidth, deskThickness * 0.27f, height * 0.012f),
                Quaternion.identity,
                darkWood);

            float floorY = bounds.min.y + height * 0.012f;
            float legHeight = Mathf.Max(height * 0.08f, deskTopY - floorY);
            float legY = deskTopY - deskThickness * 0.5f - legHeight * 0.5f;
            float legOffset = deskWidth * 0.385f;
            float legWidth = height * 0.055f;
            CreateCube(
                _visualRoot,
                "Desk left leg",
                new Vector3(centerX - legOffset, legY, frontZ),
                new Vector3(legWidth, legHeight, height * 0.052f),
                Quaternion.identity,
                darkWood);
            CreateCube(
                _visualRoot,
                "Desk right leg",
                new Vector3(centerX + legOffset, legY, frontZ),
                new Vector3(legWidth, legHeight, height * 0.052f),
                Quaternion.identity,
                darkWood);

            float bookY = deskTopY + height * 0.092f;
            float bookZ = frontZ + height * 0.064f;
            float pageWidth = height * 0.185f;
            float pageHeight = height * 0.135f;
            float pageThickness = height * 0.012f;
            float pageOffset = pageWidth * 0.49f;
            Quaternion leftPageRotation = Quaternion.Euler(0.0f, 0.0f, -5.0f);
            Quaternion rightPageRotation = Quaternion.Euler(0.0f, 0.0f, 5.0f);

            // The camera sits on +Z and Oguri is behind the desk on -Z. Keep
            // the cover toward the viewer and the pages on Oguri's side.
            CreateCube(
                _visualRoot,
                "Book cover",
                new Vector3(centerX, bookY - height * 0.006f, bookZ),
                new Vector3(pageWidth * 2.12f, pageHeight * 1.09f, pageThickness),
                Quaternion.identity,
                darkWood);
            Vector3 leftPageCenter = new Vector3(
                centerX - pageOffset,
                bookY,
                bookZ - pageThickness);
            Vector3 rightPageCenter = new Vector3(
                centerX + pageOffset,
                bookY,
                bookZ - pageThickness);
            CreateCube(
                _visualRoot,
                "Book left page",
                leftPageCenter,
                new Vector3(pageWidth, pageHeight, pageThickness),
                leftPageRotation,
                page);
            CreateCube(
                _visualRoot,
                "Book right page",
                rightPageCenter,
                new Vector3(pageWidth, pageHeight, pageThickness),
                rightPageRotation,
                page);
            CreateCube(
                _visualRoot,
                "Book center fold",
                new Vector3(centerX, bookY, bookZ - pageThickness * 1.9f),
                new Vector3(height * 0.009f, pageHeight * 0.96f, pageThickness * 0.4f),
                Quaternion.identity,
                pageShade);

            AddPageLines(
                leftPageCenter,
                leftPageRotation,
                pageWidth,
                pageHeight,
                pageThickness,
                ink);
            AddPageLines(
                rightPageCenter,
                rightPageRotation,
                pageWidth,
                pageHeight,
                pageThickness,
                ink);
        }

        private void AddPageLines(
            Vector3 pageCenter,
            Quaternion rotation,
            float pageWidth,
            float pageHeight,
            float pageThickness,
            Material material)
        {
            for (int index = -1; index <= 1; index++)
            {
                Vector3 offset = rotation * new Vector3(
                    0.0f,
                    index * pageHeight * 0.22f,
                    -pageThickness * 0.64f);
                CreateCube(
                    _visualRoot,
                    "Book ink line",
                    pageCenter + offset,
                    new Vector3(pageWidth * 0.57f, pageHeight * 0.035f, pageThickness * 0.24f),
                    rotation,
                    material);
            }
        }

        private static GameObject LoadExactPrefab(
            BundleLease lease,
            string logicalName,
            string expectedPrefabName)
        {
            AssetBundle bundle = lease.GetRequiredBundle(logicalName);
            string exactAssetName = null;
            string[] assetNames = bundle.GetAllAssetNames();
            for (int index = 0; index < assetNames.Length; index++)
            {
                string assetName = assetNames[index];
                if (!string.Equals(
                    Path.GetFileNameWithoutExtension(assetName),
                    expectedPrefabName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (exactAssetName != null)
                {
                    throw new InvalidDataException(
                        "The installed bundle contains more than one exact prefab " +
                        "match for " + logicalName + ".");
                }
                exactAssetName = assetName;
            }

            if (exactAssetName == null)
            {
                throw new InvalidDataException(
                    "The installed bundle contains no exact prefab match for " +
                    logicalName + ".");
            }

            GameObject prefab = bundle.LoadAsset<GameObject>(exactAssetName);
            if (prefab == null || !string.Equals(
                prefab.name,
                expectedPrefabName,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The exact installed asset is not the expected GameObject prefab: " +
                    logicalName + ".");
            }
            if (prefab.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                throw new InvalidDataException(
                    "The installed study prefab contains no visible renderers: " +
                    logicalName + ".");
            }
            return prefab;
        }

        private Transform CreateInstalledProp(
            Transform parent,
            string displayName,
            GameObject prefab,
            Vector3 targetSize,
            Vector3 targetCenter,
            Quaternion targetRotation,
            PropAxisMode axisMode,
            Action<GameObject> prepareInstance = null)
        {
            var placementObject = new GameObject(displayName + " Placement");
            Transform placementPivot = placementObject.transform;
            placementPivot.SetParent(parent, false);

            var scaleObject = new GameObject(displayName + " Scale");
            Transform scalePivot = scaleObject.transform;
            scalePivot.SetParent(placementPivot, false);

            // Keep the delivered prefab hierarchy and its root transform intact.
            // All normalization lives on the two app-owned pivots above it.
            GameObject instance = Instantiate(prefab, scalePivot, false);
            instance.SetActive(true);
            if (prepareInstance != null)
            {
                prepareInstance(instance);
            }
            ConfigureInstalledPrefab(instance);

            // Authored SkinnedMeshRenderer culling bounds can be much larger
            // than their actual meshes. Size the prop from real geometry so
            // installed assets are not reduced to tiny specks.
            Bounds sourceBounds = CalculateGeometryLocalBounds(scalePivot);
            ValidateBounds(sourceBounds, displayName);

            Vector3 scale;
            if (axisMode == PropAxisMode.UniformFromSourceZ)
            {
                float uniformScale = targetSize.z / sourceBounds.size.z;
                scale = new Vector3(uniformScale, uniformScale, uniformScale);
            }
            else if (axisMode == PropAxisMode.UniformFromSourceY)
            {
                float uniformScale = targetSize.y / sourceBounds.size.y;
                scale = new Vector3(uniformScale, uniformScale, uniformScale);
            }
            else
            {
                scale = new Vector3(
                    targetSize.x / sourceBounds.size.x,
                    targetSize.y / sourceBounds.size.y,
                    targetSize.z / sourceBounds.size.z);
            }

            ValidateScale(scale, displayName);
            scalePivot.localScale = scale;
            scalePivot.localPosition = -Vector3.Scale(sourceBounds.center, scale);
            placementPivot.localPosition = targetCenter;
            placementPivot.localRotation = targetRotation;
            return placementPivot;
        }

        private void ConfigureInstalledPrefab(GameObject instance)
        {
            foreach (Transform transform in
                instance.GetComponentsInChildren<Transform>(true))
            {
                // Game prefabs retain the source project's layer numbers. The
                // desktop pet has no matching layer table, so render every prop
                // on the same ordinary layer as its app-owned parent.
                transform.gameObject.layer = instance.transform.parent.gameObject.layer;
            }
            foreach (Animator animator in
                instance.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = false;
            }
            foreach (Animation animation in
                instance.GetComponentsInChildren<Animation>(true))
            {
                animation.enabled = false;
            }
            foreach (Collider collider in
                instance.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
            foreach (Collider2D collider in
                instance.GetComponentsInChildren<Collider2D>(true))
            {
                collider.enabled = false;
            }
            foreach (Light light in
                instance.GetComponentsInChildren<Light>(true))
            {
                light.enabled = false;
            }
            foreach (Rigidbody rigidbody in
                instance.GetComponentsInChildren<Rigidbody>(true))
            {
                rigidbody.useGravity = false;
                rigidbody.detectCollisions = false;
                rigidbody.isKinematic = true;
            }
            foreach (Rigidbody2D rigidbody in
                instance.GetComponentsInChildren<Rigidbody2D>(true))
            {
                rigidbody.simulated = false;
            }
            int enabledVisibleRendererCount = 0;
            foreach (Renderer renderer in
                instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsLocallyActive(renderer.transform, instance.transform))
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
                        "An installed study prop renderer has no material: " +
                        renderer.name + ".");
                }
                var runtimeMaterials = new Material[sourceMaterials.Length];
                for (int index = 0; index < sourceMaterials.Length; index++)
                {
                    Material sourceMaterial = sourceMaterials[index];
                    if (sourceMaterial == null)
                    {
                        throw new InvalidDataException(
                            "An installed study prop uses a missing material: " +
                            renderer.name + ".");
                    }
                    runtimeMaterials[index] =
                        GetOrCreateInstalledMaterial(sourceMaterial);
                }
                renderer.sharedMaterials = runtimeMaterials;

                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                if (skinned != null)
                {
                    skinned.updateWhenOffscreen = true;
                }
                if (renderer.enabled && IsLocallyActive(
                    renderer.transform,
                    instance.transform))
                {
                    enabledVisibleRendererCount++;
                }
            }
            if (enabledVisibleRendererCount == 0)
            {
                throw new InvalidDataException(
                    "An installed study prop contains no enabled visible renderer.");
            }
        }

        private Material GetOrCreateInstalledMaterial(Material source)
        {
            Material existing;
            if (_installedMaterialCopies.TryGetValue(source, out existing) &&
                existing != null)
            {
                return existing;
            }

            Shader shader = ResolveInstalledTextureShader();
            var material = new Material(shader)
            {
                name = source.name + " (Desktop Pet)",
                hideFlags = HideFlags.DontSave,
                renderQueue = 2000
            };

            Texture texture = null;
            if (source.HasProperty("_MainTex"))
            {
                texture = source.GetTexture("_MainTex");
            }
            if (texture == null && source.HasProperty("_BaseMap"))
            {
                texture = source.GetTexture("_BaseMap");
            }
            if (texture != null)
            {
                if (material.HasProperty("_MainTex"))
                {
                    material.SetTexture("_MainTex", texture);
                    if (source.HasProperty("_MainTex"))
                    {
                        material.SetTextureScale(
                            "_MainTex",
                            source.GetTextureScale("_MainTex"));
                        material.SetTextureOffset(
                            "_MainTex",
                            source.GetTextureOffset("_MainTex"));
                    }
                }
                if (material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", texture);
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
            if (material.HasProperty("_UseBackColor"))
            {
                bool isWitNotebook = source.name.IndexOf(
                    "prop1025_00",
                    StringComparison.OrdinalIgnoreCase) >= 0;
                material.SetFloat("_UseBackColor", isWitNotebook ? 1.0f : 0.0f);
                if (isWitNotebook && material.HasProperty("_BackColor"))
                {
                    material.SetColor(
                        "_BackColor",
                        new Color(0.55f, 0.16f, 0.09f, 1.0f));
                }
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

            _installedMaterialCopies[source] = material;
            _ownedMaterials.Add(material);
            return material;
        }

        private static Shader ResolveInstalledTextureShader()
        {
            Shader shader = Resources.Load<Shader>("StudyPropUnlit");
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                if (shader == null)
                {
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                }
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
                    "No compatible texture shader is available for the installed " +
                    "study props.");
            }
            return shader;
        }

        private static bool IsLocallyActive(Transform current, Transform root)
        {
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

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private void LogInstalledRendererState()
        {
            if (!UsesInstalledStudyProps || _visualRoot == null)
            {
                return;
            }

            foreach (Renderer renderer in
                _visualRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                string materialState = "none";
                Material material = renderer.sharedMaterial;
                if (material != null)
                {
                    string colorState = "n/a";
                    if (material.HasProperty("_BaseColor"))
                    {
                        colorState = material.GetColor("_BaseColor").ToString("F3");
                    }
                    else if (material.HasProperty("_Color"))
                    {
                        colorState = material.GetColor("_Color").ToString("F3");
                    }
                    materialState = material.name + "/" +
                        (material.shader == null ? "<no shader>" : material.shader.name) +
                        "/queue=" + material.renderQueue + "/color=" + colorState;
                }

                Debug.Log(
                    "Study prop renderer: name=" + renderer.name +
                    ", type=" + renderer.GetType().Name +
                    ", active=" + renderer.gameObject.activeInHierarchy +
                    ", enabled=" + renderer.enabled +
                    ", forceOff=" + renderer.forceRenderingOff +
                    ", layer=" + renderer.gameObject.layer +
                    ", center=" + renderer.bounds.center.ToString("F3") +
                    ", size=" + renderer.bounds.size.ToString("F3") +
                    ", material=" + materialState + ".");
            }
        }
#endif

        private static void ValidateBounds(Bounds bounds, string displayName)
        {
            const float minimumSize = 0.00001f;
            if (bounds.size.x <= minimumSize ||
                bounds.size.y <= minimumSize ||
                bounds.size.z <= minimumSize)
            {
                throw new InvalidDataException(
                    "The installed " + displayName +
                    " prefab has incompatible renderer bounds.");
            }
        }

        private static void ValidateScale(Vector3 scale, string displayName)
        {
            if (float.IsNaN(scale.x) || float.IsInfinity(scale.x) ||
                float.IsNaN(scale.y) || float.IsInfinity(scale.y) ||
                float.IsNaN(scale.z) || float.IsInfinity(scale.z) ||
                scale.x <= 0.0f || scale.y <= 0.0f || scale.z <= 0.0f)
            {
                throw new InvalidDataException(
                    "The installed " + displayName +
                    " prefab could not be normalized safely.");
            }
        }

        private static GameObject CreateCube(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = localRotation;
            cube.transform.localScale = localScale;

            Collider collider = cube.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                UnityEngine.Object.Destroy(collider);
            }

            MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
            ConfigureRenderer(renderer, material);
            return cube;
        }

        private enum PropAxisMode
        {
            Direct,
            UniformFromSourceZ,
            UniformFromSourceY
        }

        private static void ConfigureRenderer(
            MeshRenderer renderer,
            Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private Material CreateMaterial(Shader shader, string name, Color color)
        {
            var material = new Material(shader)
            {
                name = name,
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
            _ownedMaterials.Add(material);
            return material;
        }

        private static Shader ResolveShader()
        {
            Shader shader = null;
            if (GraphicsSettings.currentRenderPipeline != null)
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
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "No built-in shader is available for the procedural study props.");
            }
            return shader;
        }

        private static Bounds CalculateLocalBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException(
                    "The study desk presenter requires a visible character root.");
            }

            Bounds localBounds = default(Bounds);
            bool found = false;
            foreach (Renderer renderer in renderers)
            {
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
                            Vector3 corner = new Vector3(
                                x == 0 ? worldBounds.min.x : worldBounds.max.x,
                                y == 0 ? worldBounds.min.y : worldBounds.max.y,
                                z == 0 ? worldBounds.min.z : worldBounds.max.z);
                            Vector3 localCorner = root.InverseTransformPoint(corner);
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
                    "The study desk presenter could not measure the character.");
            }
            return localBounds;
        }

        private static Bounds CalculateGeometryLocalBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds localBounds = default(Bounds);
            bool found = false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null ||
                    !renderer.enabled ||
                    !IsLocallyActive(renderer.transform, root))
                {
                    continue;
                }

                Bounds geometryBounds;
                Transform geometryTransform = renderer.transform;
                MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                if (skinned != null && skinned.sharedMesh != null)
                {
                    geometryBounds = skinned.sharedMesh.bounds;
                }
                else if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    geometryBounds = meshFilter.sharedMesh.bounds;
                }
                else
                {
                    Bounds worldBounds = renderer.bounds;
                    EncapsulateWorldBounds(
                        root,
                        worldBounds,
                        ref localBounds,
                        ref found);
                    continue;
                }

                for (int x = 0; x <= 1; x++)
                {
                    for (int y = 0; y <= 1; y++)
                    {
                        for (int z = 0; z <= 1; z++)
                        {
                            Vector3 geometryCorner = new Vector3(
                                x == 0 ? geometryBounds.min.x : geometryBounds.max.x,
                                y == 0 ? geometryBounds.min.y : geometryBounds.max.y,
                                z == 0 ? geometryBounds.min.z : geometryBounds.max.z);
                            Vector3 localCorner = root.InverseTransformPoint(
                                geometryTransform.TransformPoint(geometryCorner));
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
                    "The installed study prop contains no measurable geometry.");
            }
            return localBounds;
        }

        private static void EncapsulateWorldBounds(
            Transform root,
            Bounds worldBounds,
            ref Bounds localBounds,
            ref bool found)
        {
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

        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    "Initialize the study desk presenter before showing it.");
            }
        }

        internal void ReleaseResources()
        {
            if (_visualRoot != null)
            {
                _visualRoot.gameObject.SetActive(false);
                Destroy(_visualRoot.gameObject);
                _visualRoot = null;
                _carrotCharmRoot = null;
            }

            _visible = false;
            _paused = false;
            _carrotDeskCharmVisible = false;
            _equippedDeskItemId = string.Empty;
            _deskRewardRoots.Clear();
            _initialized = false;
            UsesInstalledStudyProps = false;

            ReleaseProceduralResources();

            foreach (KeyValuePair<string, BundleLease> rewardLease in
                _deskRewardLeases)
            {
                if (rewardLease.Value == null)
                {
                    continue;
                }
                try
                {
                    rewardLease.Value.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "Could not release the installed desk reward bundles " +
                        "for " + rewardLease.Key + ".\n" + exception);
                }
            }
            _deskRewardLeases.Clear();

            if (_installedPropLease != null)
            {
                BundleLease lease = _installedPropLease;
                _installedPropLease = null;
                try
                {
                    lease.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "Could not release the installed study prop bundles.\n" +
                        exception);
                }
            }
        }

        private void ReleaseProceduralResources()
        {
            foreach (Material material in _ownedMaterials)
            {
                if (material != null)
                {
                    Destroy(material);
                }
            }
            _ownedMaterials.Clear();
            _installedMaterialCopies.Clear();
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }
    }
}
