using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UmaDesktopPet.Standalone.Core;
using UmaDesktopPet.Standalone.Runtime;

namespace UmaDesktopPet.Standalone.Editor
{
    /// <summary>
    /// Writes catalog and Unity-object metadata for likely Mood and Energy UI
    /// assets. It never serializes, copies, or exports installed-game objects.
    /// </summary>
    public static class GameUiAssetDiagnostics
    {
        private static readonly string[] SearchFragments =
        {
            "motivation",
            "energy",
            "vital",
            "stamina",
            "gauge",
            "statusicon",
            "carrot"
        };

        [MenuItem("Tools/Uma Desktop Pet/Dump game UI asset candidates")]
        public static void Run()
        {
            string gameRoot = ReadArgument("-umaGameRoot");
            if (string.IsNullOrWhiteSpace(gameRoot))
            {
                throw new InvalidOperationException(
                    "Pass the installed data root with -umaGameRoot.");
            }

            string outputPath = ReadArgument("-umaUiDiagnosticOutput");
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    "..",
                    "..",
                    "..",
                    "artifacts",
                    "standalone",
                    "diagnostics",
                    "game-ui-assets.txt"));
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
                writer.WriteLine("Region: " + catalog.Region);
                writer.WriteLine("Assets: " + catalog.AssetCount);
                writer.WriteLine();

                var candidates = new SortedSet<string>(StringComparer.Ordinal);
                foreach (string fragment in SearchFragments)
                {
                    List<GameAssetRecord> matches = catalog
                        .FindByNameFragment(fragment)
                        .ToList();
                    writer.WriteLine(
                        "=== " + fragment + " (" + matches.Count + ") ===");
                    foreach (GameAssetRecord record in matches.Take(500))
                    {
                        writer.WriteLine(
                            record.Name + " | type=" + record.Type +
                            " | prerequisites=" + record.Prerequisites);
                        candidates.Add(record.Name);
                    }
                    writer.WriteLine();
                }

                string[] motivationNames = Enumerable.Range(0, 5)
                    .Select(index =>
                        "uianimation/flash/singlemode/statusicon/" +
                        "utx_ico_motivation_l_" + index.ToString("00"))
                    .Where(name => candidates.Contains(name) ||
                        catalog.TryGetAsset(name, out _))
                    .ToArray();
                DumpBundleObjects(writer, repository, motivationNames);

                string[] likelyGaugeNames = candidates
                    .Where(name =>
                        name.IndexOf("singlemode", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        (name.IndexOf("gauge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("vital", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("energy", StringComparison.OrdinalIgnoreCase) >= 0))
                    .Take(80)
                    .ToArray();
                DumpBundleObjects(writer, repository, likelyGaugeNames);
                DumpBundleObjects(
                    writer,
                    repository,
                    candidates
                        .Where(name => name.IndexOf(
                            "carrot",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToArray());
                DumpPrefabGraph(
                    writer,
                    repository,
                    "uianimation/flash/singlemode/pf_fl_singlemode_header_hpgauge00");
            }

            Debug.Log("Wrote text-only game UI diagnostics: " + outputPath);
        }

        private static void DumpBundleObjects(
            TextWriter writer,
            BundleRepository repository,
            IReadOnlyList<string> logicalNames)
        {
            writer.WriteLine("=== LOADED OBJECTS (" + logicalNames.Count + ") ===");
            foreach (string logicalName in logicalNames)
            {
                try
                {
                    using (BundleLease lease = repository.Acquire(logicalName))
                    {
                        AssetBundle bundle = lease.GetRequiredBundle(logicalName);
                        writer.WriteLine("BUNDLE " + logicalName);
                        foreach (UnityEngine.Object asset in bundle.LoadAllAssets())
                        {
                            var texture = asset as Texture2D;
                            var sprite = asset as Sprite;
                            string detail = texture != null
                                ? " " + texture.width + "x" + texture.height
                                : sprite != null
                                    ? " rect=" + sprite.rect +
                                        " texture=" + sprite.texture.name
                                    : string.Empty;
                            writer.WriteLine(
                                "  " + asset.GetType().FullName +
                                " | " + asset.name + detail);
                        }
                    }
                }
                catch (Exception exception)
                {
                    writer.WriteLine(
                        "FAILED " + logicalName + " | " + exception.Message);
                }
            }
            writer.WriteLine();
        }

        private static void DumpPrefabGraph(
            TextWriter writer,
            BundleRepository repository,
            string logicalName)
        {
            writer.WriteLine("=== PREFAB GRAPH " + logicalName + " ===");
            using (BundleLease lease = repository.Acquire(logicalName))
            {
                AssetBundle bundle = lease.GetRequiredBundle(logicalName);
                GameObject prefab = bundle.LoadAllAssets<GameObject>().FirstOrDefault();
                if (prefab == null)
                {
                    writer.WriteLine("No GameObject found.");
                    return;
                }

                foreach (Transform transform in
                    prefab.GetComponentsInChildren<Transform>(true))
                {
                    writer.WriteLine(
                        "NODE " + GetPath(prefab.transform, transform) +
                        " | active=" + transform.gameObject.activeSelf +
                        " | localPosition=" + transform.localPosition +
                        " | localScale=" + transform.localScale);
                    foreach (Component component in
                        transform.GetComponents<Component>())
                    {
                        writer.WriteLine(
                            "  COMPONENT " +
                            (component == null
                                ? "<missing>"
                                : component.GetType().FullName));
                    }

                    var image = transform.GetComponent<Image>();
                    if (image != null)
                    {
                        writer.WriteLine(
                            "  IMAGE color=" + image.color +
                            " type=" + image.type +
                            " fill=" + image.fillAmount +
                            " sprite=" + DescribeSprite(image.sprite));
                    }
                    var rawImage = transform.GetComponent<RawImage>();
                    if (rawImage != null)
                    {
                        writer.WriteLine(
                            "  RAWIMAGE color=" + rawImage.color +
                            " uv=" + rawImage.uvRect +
                            " texture=" + DescribeTexture(rawImage.texture));
                    }
                    var renderer = transform.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        foreach (Material material in renderer.sharedMaterials)
                        {
                            writer.WriteLine(
                                "  MATERIAL " +
                                (material == null ? "<null>" : material.name) +
                                " mainTexture=" +
                                (material == null
                                    ? "<null>"
                                    : DescribeTexture(material.mainTexture)));
                        }
                    }
                }
            }
            writer.WriteLine();
        }

        private static string DescribeSprite(Sprite sprite)
        {
            return sprite == null
                ? "<null>"
                : sprite.name + " rect=" + sprite.rect +
                    " texture=" + DescribeTexture(sprite.texture);
        }

        private static string DescribeTexture(Texture texture)
        {
            return texture == null
                ? "<null>"
                : texture.name + " " + texture.width + "x" + texture.height;
        }

        private static string GetPath(Transform root, Transform target)
        {
            if (target == root)
            {
                return root.name;
            }

            var parts = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                parts.Push(current.name);
                current = current.parent;
            }
            return root.name + "/" + string.Join("/", parts.ToArray());
        }

        private static string ReadArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(
                    arguments[index],
                    name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[index + 1];
                }
            }
            return null;
        }
    }
}
