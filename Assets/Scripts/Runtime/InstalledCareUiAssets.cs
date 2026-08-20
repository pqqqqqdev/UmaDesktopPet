using System;
using System.Collections.Generic;
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
        private const string MoodAnimationAssetName =
            "uianimation/flash/singlemode/pf_fl_singlemode_icon_motivation00";
        private const string MoodAnimationTextureName =
            "tx_uTex_fl_singlemode_icon_motivation00_0_C";
        private const string CarrotAssetName = "item/item_icon_00035";
        private const string CarrotFallbackAssetName = "item/item_icon_00034";

        // Exact bottom-left atlas rectangles shared by the installed JP and
        // Global Flash prefab. The badge frames are packed clockwise, so each
        // crop is rotated counter-clockwise into its logical 184x69 shape.
        private static readonly RectInt[] MoodAnimationFrameRects =
        {
            new RectInt(224, 326, 69, 184),
            new RectInt(2, 224, 69, 184),
            new RectInt(73, 224, 69, 184),
            new RectInt(144, 224, 69, 184),
            new RectInt(215, 140, 69, 184)
        };

        // The arrows are separate transparent, upright sprites. Keeping them
        // separate lets the app reproduce the Flash timeline without executing
        // or depending on the game's stripped Flash runtime.
        private static readonly RectInt[] MoodAnimationArrowRects =
        {
            new RectInt(2, 166, 56, 56),
            new RectInt(2, 108, 56, 56),
            new RectInt(60, 166, 56, 56),
            new RectInt(60, 108, 56, 56),
            new RectInt(118, 166, 56, 56)
        };

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
        private BundleLease _moodAnimationLease;
        private Texture2D[] _ownedMoodAnimationFrames;
        private Texture2D[] _ownedMoodAnimationArrows;
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
                result.TryLoadMoodAnimation(bundles);
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

        public bool TryGetMoodAnimationTextures(
            PetMood mood,
            out Texture frame,
            out Texture arrow)
        {
            int index = (int)mood - 1;
            if (_moodAnimationLease == null ||
                _ownedMoodAnimationFrames == null ||
                _ownedMoodAnimationArrows == null ||
                index < 0 ||
                index >= _ownedMoodAnimationFrames.Length ||
                index >= _ownedMoodAnimationArrows.Length)
            {
                frame = null;
                arrow = null;
                return false;
            }

            frame = _ownedMoodAnimationFrames[index];
            arrow = _ownedMoodAnimationArrows[index];
            return frame != null && arrow != null;
        }

        public bool TryGetCarrotTexture(out Texture texture)
        {
            texture = _carrotTexture;
            return _carrotLease != null && texture != null;
        }

        public void Dispose()
        {
            ReleaseTexturePreviews(_ownedMoodAnimationFrames);
            _ownedMoodAnimationFrames = null;
            ReleaseTexturePreviews(_ownedMoodAnimationArrows);
            _ownedMoodAnimationArrows = null;
            ReleaseMoodPreviews(_ownedMoodPreviews);
            if (_moodAnimationLease != null)
            {
                _moodAnimationLease.Dispose();
                _moodAnimationLease = null;
            }
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

        private void TryLoadMoodAnimation(BundleRepository bundles)
        {
            BundleLease lease = null;
            Texture2D[] frames = null;
            Texture2D[] arrows = null;
            try
            {
                // Snapshot before loading the bundle so eager preloaded atlas
                // objects are still attributable to this exact acquisition.
                HashSet<int> existingTextureIds = CaptureExpectedTextureIds(
                    MoodAnimationTextureName,
                    512,
                    512);
                lease = bundles.Acquire(MoodAnimationAssetName);
                string atlasResolutionRoute;
                Texture2D atlas = LoadRequiredNamedTexture(
                    lease,
                    MoodAnimationAssetName,
                    MoodAnimationTextureName,
                    existingTextureIds,
                    out atlasResolutionRoute);
                if (atlas.width != 512 || atlas.height != 512)
                {
                    throw new InvalidDataException(
                        "The installed Mood animation atlas has an unexpected " +
                        "size: " + atlas.width + "x" + atlas.height + ".");
                }

                frames = new Texture2D[MoodAnimationFrameRects.Length];
                arrows = new Texture2D[MoodAnimationArrowRects.Length];
                for (int index = 0; index < frames.Length; index++)
                {
                    frames[index] = CreateAtlasPreview(
                        atlas,
                        MoodAnimationFrameRects[index],
                        true,
                        "Mood animation frame " + index);
                    arrows[index] = CreateAtlasPreview(
                        atlas,
                        MoodAnimationArrowRects[index],
                        false,
                        "Mood animation arrow " + index);
                }

                _moodAnimationLease = lease;
                _ownedMoodAnimationFrames = frames;
                _ownedMoodAnimationArrows = arrows;
                lease = null;
                frames = null;
                arrows = null;
                Debug.Log(
                    "Loaded the installed Mood Flash atlas and prepared " +
                    "in-memory badge and arrow previews via " +
                    atlasResolutionRoute + ". No UI assets were exported.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "The installed Mood arrow animation could not be loaded. " +
                    "The care menu will use its static Mood art.\n" + exception);
            }
            finally
            {
                ReleaseTexturePreviews(frames);
                ReleaseTexturePreviews(arrows);
                if (lease != null)
                {
                    lease.Dispose();
                }
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

        private static Texture2D CreateAtlasPreview(
            Texture2D source,
            RectInt sourceRect,
            bool rotateCounterClockwise,
            string previewName)
        {
            RenderTexture temporary = RenderTexture.GetTemporary(
                sourceRect.width,
                sourceRect.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            temporary.filterMode = FilterMode.Bilinear;
            temporary.wrapMode = TextureWrapMode.Clamp;

            RenderTexture previous = RenderTexture.active;
            Texture2D readable = null;
            Texture2D preview = null;
            try
            {
                Graphics.Blit(
                    source,
                    temporary,
                    new Vector2(
                        sourceRect.width / (float)source.width,
                        sourceRect.height / (float)source.height),
                    new Vector2(
                        sourceRect.x / (float)source.width,
                        sourceRect.y / (float)source.height));

                RenderTexture.active = temporary;
                readable = new Texture2D(
                    sourceRect.width,
                    sourceRect.height,
                    TextureFormat.RGBA32,
                    false)
                {
                    hideFlags = HideFlags.DontSave
                };
                readable.ReadPixels(
                    new Rect(
                        0.0f,
                        0.0f,
                        sourceRect.width,
                        sourceRect.height),
                    0,
                    0,
                    false);
                readable.Apply(false, false);

                Color32[] sourcePixels = readable.GetPixels32();
                int outputWidth = rotateCounterClockwise
                    ? sourceRect.height
                    : sourceRect.width;
                int outputHeight = rotateCounterClockwise
                    ? sourceRect.width
                    : sourceRect.height;
                var outputPixels = new Color32[outputWidth * outputHeight];
                if (rotateCounterClockwise)
                {
                    for (int sourceY = 0;
                        sourceY < sourceRect.height;
                        sourceY++)
                    {
                        for (int sourceX = 0;
                            sourceX < sourceRect.width;
                            sourceX++)
                        {
                            int outputX =
                                sourceRect.height - 1 - sourceY;
                            int outputY = sourceX;
                            outputPixels[outputY * outputWidth + outputX] =
                                sourcePixels[
                                    sourceY * sourceRect.width + sourceX];
                        }
                    }
                }
                else
                {
                    Array.Copy(sourcePixels, outputPixels, sourcePixels.Length);
                }

                preview = new Texture2D(
                    outputWidth,
                    outputHeight,
                    TextureFormat.RGBA32,
                    false)
                {
                    name = previewName,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave
                };
                preview.SetPixels32(outputPixels);
                preview.Apply(false, true);
                Texture2D completedPreview = preview;
                preview = null;
                return completedPreview;
            }
            finally
            {
                RenderTexture.active = previous;
                if (preview != null)
                {
                    UnityEngine.Object.Destroy(preview);
                }
                if (readable != null)
                {
                    UnityEngine.Object.Destroy(readable);
                }
                RenderTexture.ReleaseTemporary(temporary);
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

        private static void ReleaseTexturePreviews(Texture2D[] previews)
        {
            if (previews == null)
            {
                return;
            }

            for (int index = 0; index < previews.Length; index++)
            {
                if (previews[index] == null)
                {
                    continue;
                }

                UnityEngine.Object.Destroy(previews[index]);
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

        private static Texture2D LoadRequiredNamedTexture(
            BundleLease lease,
            string logicalName,
            string expectedName,
            HashSet<int> existingTextureIds,
            out string resolutionRoute)
        {
            AssetBundle bundle = lease.GetRequiredBundle(logicalName);

            // Some Gallop Flash atlases are embedded references rather than
            // named bundle roots. Prefer authoritative direct routes first.
            Texture2D direct = bundle.LoadAsset<Texture2D>(expectedName);
            if (IsExpectedTexture(direct, expectedName, 512, 512))
            {
                resolutionRoute = "direct Texture2D name";
                return direct;
            }
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
                if (IsExpectedTexture(texture, expectedName, 512, 512))
                {
                    resolutionRoute = "listed Texture2D asset path";
                    return texture;
                }
            }

            Texture2D[] candidates = bundle.LoadAllAssets<Texture2D>();
            Texture2D typedMatch = null;
            for (int index = 0; index < candidates.Length; index++)
            {
                Texture2D candidate = candidates[index];
                if (!IsExpectedTexture(candidate, expectedName, 512, 512))
                {
                    continue;
                }

                if (typedMatch != null &&
                    typedMatch.GetInstanceID() != candidate.GetInstanceID())
                {
                    throw new InvalidDataException(
                        "The installed UI bundle contains multiple exact " +
                        "Texture2D candidates named " + expectedName + ".");
                }
                typedMatch = candidate;
            }
            if (typedMatch != null)
            {
                resolutionRoute = "typed bundle scan";
                return typedMatch;
            }

            Texture2D embedded = FindUniqueNewExpectedTexture(
                existingTextureIds,
                expectedName,
                512,
                512);
            if (embedded != null)
            {
                resolutionRoute = "embedded reference from typed bundle scan";
                return embedded;
            }

            // Loading every named root materializes textures referenced only by
            // the Flash prefab's serialized data. Resources is used solely to
            // find exact newly-created objects, never arbitrary same-name state.
            for (int index = 0; index < assetNames.Length; index++)
            {
                bundle.LoadAsset<UnityEngine.Object>(assetNames[index]);
            }
            bundle.LoadAllAssets();

            embedded = FindUniqueNewExpectedTexture(
                existingTextureIds,
                expectedName,
                512,
                512);
            if (embedded != null)
            {
                resolutionRoute = "exact embedded prefab reference";
                return embedded;
            }

            throw new InvalidDataException(
                "The installed UI bundle contains no matching texture: " +
                expectedName);
        }

        private static HashSet<int> CaptureExpectedTextureIds(
            string expectedName,
            int expectedWidth,
            int expectedHeight)
        {
            var result = new HashSet<int>();
            Texture2D[] textures =
                Resources.FindObjectsOfTypeAll<Texture2D>();
            for (int index = 0; index < textures.Length; index++)
            {
                Texture2D texture = textures[index];
                if (IsExpectedTexture(
                    texture,
                    expectedName,
                    expectedWidth,
                    expectedHeight))
                {
                    result.Add(texture.GetInstanceID());
                }
            }
            return result;
        }

        private static Texture2D FindUniqueNewExpectedTexture(
            HashSet<int> existingTextureIds,
            string expectedName,
            int expectedWidth,
            int expectedHeight)
        {
            Texture2D match = null;
            Texture2D[] textures =
                Resources.FindObjectsOfTypeAll<Texture2D>();
            for (int index = 0; index < textures.Length; index++)
            {
                Texture2D candidate = textures[index];
                if (!IsExpectedTexture(
                    candidate,
                    expectedName,
                    expectedWidth,
                    expectedHeight) ||
                    existingTextureIds.Contains(candidate.GetInstanceID()))
                {
                    continue;
                }

                if (match != null &&
                    match.GetInstanceID() != candidate.GetInstanceID())
                {
                    throw new InvalidDataException(
                        "Loading the installed Flash prefab materialized " +
                        "multiple exact Texture2D candidates named " +
                        expectedName + ".");
                }
                match = candidate;
            }
            return match;
        }

        private static bool IsExpectedTexture(
            Texture2D texture,
            string expectedName,
            int expectedWidth,
            int expectedHeight)
        {
            return texture != null &&
                texture.width == expectedWidth &&
                texture.height == expectedHeight &&
                string.Equals(
                    texture.name,
                    expectedName,
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}
