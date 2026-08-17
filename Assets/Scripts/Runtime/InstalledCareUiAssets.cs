using System;
using System.IO;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Keeps the installed game's five motivation textures alive for the desktop
    /// pet UI. The textures are read in memory from the user's own game install
    /// and are never copied, encoded, or included in the desktop-pet build.
    /// </summary>
    public sealed class InstalledCareUiAssets : IDisposable
    {
        private const string MoodAssetPrefix =
            "uianimation/flash/singlemode/statusicon/utx_ico_motivation_l_0";
        private const string CarrotAssetName = "item/item_icon_00035";
        private const string CarrotFallbackAssetName = "item/item_icon_00034";

        // Shared two-pixel-padded content bounds measured across all five
        // installed 512x256 Mood textures. Cropping this transparent border lets
        // the thin lettering use more screen pixels without changing the art.
        private static readonly Vector2 MoodContentScale =
            new Vector2(0.8828125f, 0.85546875f);
        private static readonly Vector2 MoodContentOffset =
            new Vector2(0.05859375f, 0.078125f);

        private readonly Texture[] _moodTextures;
        private readonly RenderTexture[] _ownedMoodPreviews;
        private BundleLease _moodLease;
        private BundleLease _carrotLease;
        private Texture2D _carrotTexture;

        private InstalledCareUiAssets(
            BundleLease lease,
            Texture[] moodTextures,
            RenderTexture[] ownedMoodPreviews)
        {
            _moodLease = lease;
            _moodTextures = moodTextures;
            _ownedMoodPreviews = ownedMoodPreviews;
        }

        /// <summary>
        /// Tries to load all five installed motivation textures. Missing or
        /// version-shifted UI bundles are non-fatal because the pet can still use
        /// its English text fallback.
        /// </summary>
        public static InstalledCareUiAssets TryLoad(BundleRepository bundles)
        {
            if (bundles == null)
            {
                throw new ArgumentNullException("bundles");
            }

            string[] logicalNames = BuildMoodAssetNames();
            BundleLease lease = null;
            RenderTexture[] ownedPreviews = null;
            try
            {
                lease = bundles.AcquireMany(logicalNames);
                var textures = new Texture[logicalNames.Length];
                ownedPreviews = new RenderTexture[logicalNames.Length];
                for (int index = 0; index < logicalNames.Length; index++)
                {
                    Texture2D installedTexture = LoadRequiredTexture(
                        lease,
                        logicalNames[index]);
                    RenderTexture preview = CreateMoodPreview(installedTexture);
                    ownedPreviews[index] = preview;
                    textures[index] = preview;
                }

                var result = new InstalledCareUiAssets(
                    lease,
                    textures,
                    ownedPreviews);
                lease = null;
                ownedPreviews = null;
                result.TryLoadCarrot(bundles);
                Debug.Log(
                    "Loaded the five Mood icons from the installed Umamusume " +
                    "UI bundles and prepared high-quality in-memory previews. " +
                    "No UI assets were exported.");
                return result;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "The installed Mood icons could not be loaded. " +
                    "The care menu will use its text fallback.\n" + exception);
                return null;
            }
            finally
            {
                ReleaseMoodPreviews(ownedPreviews);
                if (lease != null)
                {
                    try
                    {
                        lease.Dispose();
                    }
                    catch (Exception cleanupError)
                    {
                        Debug.LogWarning(
                            "Could not release the optional Mood UI bundles.\n" +
                            cleanupError);
                    }
                }
            }
        }

        public bool TryGetMoodTexture(PetMood mood, out Texture texture)
        {
            int index = (int)mood - 1;
            if (_moodLease == null ||
                index < 0 ||
                index >= _moodTextures.Length)
            {
                texture = null;
                return false;
            }

            texture = _moodTextures[index];
            return texture != null;
        }

        public bool TryGetCarrotTexture(out Texture texture)
        {
            texture = _carrotTexture;
            return _carrotLease != null && texture != null;
        }

        public void Dispose()
        {
            ReleaseMoodPreviews(_ownedMoodPreviews);
            if (_carrotLease != null)
            {
                _carrotLease.Dispose();
                _carrotLease = null;
                _carrotTexture = null;
            }
            if (_moodLease != null)
            {
                _moodLease.Dispose();
                _moodLease = null;
            }
        }

        private void TryLoadCarrot(BundleRepository bundles)
        {
            string[] candidates =
            {
                CarrotAssetName,
                CarrotFallbackAssetName
            };
            Exception lastError = null;
            for (int index = 0; index < candidates.Length; index++)
            {
                BundleLease lease = null;
                try
                {
                    string logicalName = candidates[index];
                    lease = bundles.Acquire(logicalName);
                    Texture2D texture = LoadRequiredTexture(lease, logicalName);
                    _carrotLease = lease;
                    _carrotTexture = texture;
                    lease = null;
                    Debug.Log(
                        "Loaded Carrot Jelly art from the installed Umamusume " +
                        "item bundle. No item assets were exported.");
                    return;
                }
                catch (Exception exception)
                {
                    lastError = exception;
                }
                finally
                {
                    if (lease != null)
                    {
                        lease.Dispose();
                    }
                }
            }

            Debug.LogWarning(
                "The installed Carrot Jelly art could not be loaded. " +
                "The feed interaction will use its built-in fallback.\n" +
                lastError);
        }

        private static RenderTexture CreateMoodPreview(Texture2D source)
        {
            RenderTexture halfSize = RenderTexture.GetTemporary(
                264,
                128,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            halfSize.filterMode = FilterMode.Bilinear;
            halfSize.wrapMode = TextureWrapMode.Clamp;

            var preview = new RenderTexture(
                132,
                64,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default)
            {
                name = source.name + " desktop UI preview",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1,
                hideFlags = HideFlags.DontSave
            };

            try
            {
                if (!preview.Create())
                {
                    throw new InvalidOperationException(
                        "Unity could not create the in-memory Mood preview.");
                }

                Graphics.Blit(
                    source,
                    halfSize,
                    MoodContentScale,
                    MoodContentOffset);
                Graphics.Blit(halfSize, preview);
                return preview;
            }
            catch
            {
                preview.Release();
                UnityEngine.Object.Destroy(preview);
                throw;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(halfSize);
            }
        }

        private static void ReleaseMoodPreviews(RenderTexture[] previews)
        {
            if (previews == null)
            {
                return;
            }

            for (int index = 0; index < previews.Length; index++)
            {
                RenderTexture preview = previews[index];
                if (preview == null)
                {
                    continue;
                }

                preview.Release();
                UnityEngine.Object.Destroy(preview);
                previews[index] = null;
            }
        }

        private static string[] BuildMoodAssetNames()
        {
            var result = new string[5];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = MoodAssetPrefix + index;
            }
            return result;
        }

        private static Texture2D LoadRequiredTexture(
            BundleLease lease,
            string logicalName)
        {
            AssetBundle bundle = lease.GetRequiredBundle(logicalName);
            string expectedName = Path.GetFileName(logicalName);
            string[] assetNames = bundle.GetAllAssetNames();
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

                Texture2D texture = bundle.LoadAsset<Texture2D>(assetName);
                if (texture != null)
                {
                    return texture;
                }
            }

            Texture2D[] candidates = bundle.LoadAllAssets<Texture2D>();
            for (int index = 0; index < candidates.Length; index++)
            {
                Texture2D candidate = candidates[index];
                if (candidate != null && string.Equals(
                    candidate.name,
                    expectedName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            throw new InvalidDataException(
                "The installed UI bundle contains no matching texture: " +
                logicalName);
        }
    }
}
