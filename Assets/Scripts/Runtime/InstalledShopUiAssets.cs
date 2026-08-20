using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Keeps exact installed-game artwork alive for desk-shop entries. The
    /// textures remain in memory only and are never copied, encoded, or included
    /// in the desktop-pet build.
    /// </summary>
    public sealed class InstalledShopUiAssets : IDisposable
    {
        private const string CarrotJellyAsset = "item/item_icon_00035";
        private const string TazunaRedPenAsset = "item/item_icon_00060";
        private const string JapaneseDerbyTrophyAsset =
            "outgame/trophy/trophy_race_1010";

        private static readonly IconDefinition[] IconDefinitions =
        {
            new IconDefinition(
                DeskShopCatalog.CarrotCharmId,
                CarrotJellyAsset),
            new IconDefinition(
                DeskShopCatalog.TazunaRedPenId,
                TazunaRedPenAsset),
            new IconDefinition(
                DeskShopCatalog.DerbyTrophyId,
                JapaneseDerbyTrophyAsset)
        };

        private readonly Dictionary<string, LoadedIcon> _icons =
            new Dictionary<string, LoadedIcon>(StringComparer.Ordinal);
        private bool _disposed;

        private InstalledShopUiAssets()
        {
        }

        /// <summary>
        /// Loads only icons whose identity is exact and shared by the supported
        /// JP and Global catalogs. A missing optional bundle does not prevent the
        /// pet from starting or the shop from using its built-in fallback.
        /// </summary>
        public static InstalledShopUiAssets TryLoad(BundleRepository bundles)
        {
            if (bundles == null)
            {
                throw new ArgumentNullException("bundles");
            }

            var result = new InstalledShopUiAssets();
            for (int index = 0; index < IconDefinitions.Length; index++)
            {
                result.TryLoadIcon(bundles, IconDefinitions[index]);
            }

            if (result._icons.Count == 0)
            {
                result.Dispose();
                Debug.LogWarning(
                    "No exact installed desk-shop icons were available. " +
                    "The shop will use its built-in artwork.");
                return null;
            }

            Debug.Log(
                "Loaded " + result._icons.Count +
                " exact desk-shop icon(s) from the installed Umamusume " +
                "bundles. No UI assets were exported.");
            return result;
        }

        public int LoadedIconCount
        {
            get { return _icons.Count; }
        }

        public bool TryGetTexture(string deskItemId, out Texture texture)
        {
            texture = null;
            if (_disposed || string.IsNullOrEmpty(deskItemId))
            {
                return false;
            }

            LoadedIcon icon;
            if (!_icons.TryGetValue(deskItemId, out icon) ||
                icon == null ||
                icon.Lease == null ||
                icon.Texture == null)
            {
                return false;
            }

            texture = icon.Texture;
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            foreach (KeyValuePair<string, LoadedIcon> pair in _icons)
            {
                LoadedIcon icon = pair.Value;
                if (icon == null || icon.Lease == null)
                {
                    continue;
                }

                try
                {
                    icon.Lease.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "Could not release an optional desk-shop icon bundle " +
                        "for " + pair.Key + ".\n" + exception);
                }
                icon.Lease = null;
                icon.Texture = null;
            }
            _icons.Clear();
        }

        private void TryLoadIcon(
            BundleRepository bundles,
            IconDefinition definition)
        {
            BundleLease lease = null;
            try
            {
                lease = bundles.Acquire(definition.LogicalName);
                Texture texture = LoadRequiredTexture(
                    lease,
                    definition.LogicalName);
                _icons.Add(
                    definition.DeskItemId,
                    new LoadedIcon(lease, texture));
                lease = null;
                Debug.Log(
                    "Loaded exact installed shop icon " +
                    definition.DeskItemId + " <- " +
                    definition.LogicalName + ".");
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "The optional installed shop icon for " +
                    definition.DeskItemId + " could not be loaded. " +
                    "The shop will use its built-in fallback.\n" + exception);
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
                            "Could not release a failed optional shop-icon " +
                            "bundle for " + definition.DeskItemId + ".\n" +
                            cleanupError);
                    }
                }
            }
        }

        private static Texture LoadRequiredTexture(
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

            Texture2D[] textures = bundle.LoadAllAssets<Texture2D>();
            for (int index = 0; index < textures.Length; index++)
            {
                Texture2D texture = textures[index];
                if (texture != null && string.Equals(
                    texture.name,
                    expectedName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return texture;
                }
            }

            throw new InvalidDataException(
                "The installed UI bundle contains no exact matching texture: " +
                logicalName);
        }

        private sealed class LoadedIcon
        {
            public LoadedIcon(BundleLease lease, Texture texture)
            {
                Lease = lease;
                Texture = texture;
            }

            public BundleLease Lease;
            public Texture Texture;
        }

        private struct IconDefinition
        {
            public IconDefinition(string deskItemId, string logicalName)
            {
                DeskItemId = deskItemId;
                LogicalName = logicalName;
            }

            public string DeskItemId;
            public string LogicalName;
        }
    }
}
