using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Keeps exact installed-game artwork alive for food entries. Textures stay
    /// in memory only and are never copied, encoded, or included in the build.
    /// </summary>
    public sealed class InstalledFoodUiAssets : IDisposable
    {
        private const string CarrotJellyAsset = "item/item_icon_00035";

        private static readonly IconDefinition[] IconDefinitions =
        {
            new IconDefinition(
                FoodCatalog.CarrotJellyId,
                CarrotJellyAsset)
        };

        private readonly Dictionary<string, LoadedIcon> _icons =
            new Dictionary<string, LoadedIcon>(StringComparer.Ordinal);
        private bool _disposed;

        private InstalledFoodUiAssets()
        {
        }

        /// <summary>
        /// Independently loads each exact food icon available in the supported
        /// installed catalog. Missing optional bundles never block other foods.
        /// </summary>
        public static InstalledFoodUiAssets TryLoad(BundleRepository bundles)
        {
            if (bundles == null)
            {
                throw new ArgumentNullException("bundles");
            }

            var result = new InstalledFoodUiAssets();
            for (int index = 0; index < IconDefinitions.Length; index++)
            {
                result.TryLoadIcon(bundles, IconDefinitions[index]);
            }

            if (result._icons.Count == 0)
            {
                result.Dispose();
                Debug.LogWarning(
                    "No exact installed food icons were available. " +
                    "The food UI will use its built-in artwork.");
                return null;
            }

            Debug.Log(
                "Loaded " + result._icons.Count +
                " exact food icon(s) from the installed Umamusume bundles. " +
                "No UI assets were exported.");
            return result;
        }

        public int LoadedIconCount
        {
            get { return _icons.Count; }
        }

        public bool TryGetTexture(string foodId, out Texture texture)
        {
            texture = null;
            if (_disposed || string.IsNullOrEmpty(foodId))
            {
                return false;
            }

            LoadedIcon icon;
            if (!_icons.TryGetValue(foodId, out icon) ||
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
                        "Could not release an optional food icon bundle for " +
                        pair.Key + ".\n" + exception);
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
                    definition.FoodId,
                    new LoadedIcon(lease, texture));
                lease = null;
                Debug.Log(
                    "Loaded exact installed food icon " +
                    definition.FoodId + " <- " +
                    definition.LogicalName + ".");
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "The optional installed food icon for " +
                    definition.FoodId + " could not be loaded. " +
                    "The food UI will use its built-in fallback.\n" +
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
                            "Could not release a failed optional food-icon " +
                            "bundle for " + definition.FoodId + ".\n" +
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
            public IconDefinition(string foodId, string logicalName)
            {
                FoodId = foodId;
                LogicalName = logicalName;
            }

            public string FoodId;
            public string LogicalName;
        }
    }
}
