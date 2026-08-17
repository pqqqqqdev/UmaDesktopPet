using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UmaDesktopPet.Standalone.Core;

namespace UmaDesktopPet.Standalone.Tools
{
    internal static class Program
    {
        private const int OguriId = 1006;

        private static int Main(string[] args)
        {
            try
            {
                Dictionary<string, string> options = ParseOptions(args);
                string gameRoot = Required(options, "game-root");
                string sqliteLibrary = Required(options, "sqlite3mc");
                using (GameDataCatalog catalog = GameDataCatalog.Open(gameRoot, sqliteLibrary))
                {
                    CharacterRecord oguri = catalog.GetCharacter(OguriId);
                    Console.WriteLine("Region: " + catalog.Region);
                    Console.WriteLine("Catalog assets: " + catalog.AssetCount);
                    Console.WriteLine(
                        "Oguri: id=" + oguri.Id +
                        ", skin=" + oguri.Skin +
                        ", tail=" + oguri.TailModelId);

                    string findFragment;
                    if (options.TryGetValue("find", out findFragment))
                    {
                        List<GameAssetRecord> matches = catalog
                            .FindByNameFragment(findFragment)
                            .ToList();
                        Console.WriteLine(
                            "Asset names containing '" + findFragment + "': " +
                            matches.Count);
                        foreach (GameAssetRecord match in matches)
                        {
                            Console.WriteLine(match.Name);
                        }
                        return 0;
                    }

                    var roots = new List<GameAssetRecord>();
                    AddRequired(catalog, roots, "shader");
                    AddRequired(catalog, roots, "3d/chara/mini/body/mbdy1006_00/pfb_mbdy1006_00");
                    AddRequired(catalog, roots, "3d/chara/mini/head/mchr1006_00/pfb_mchr1006_00_hair");
                    AddRequired(catalog, roots, "3d/chara/mini/head/mchr0001_00/pfb_mchr0001_00_face0");
                    AddRequired(catalog, roots, "3d/motion/mini/event/body/chara/chr1006_00/anm_min_eve_chr1006_00_idle01_loop");

                    if (oguri.TailModelId > 0)
                    {
                        string tail = oguri.TailModelId.ToString("0000");
                        AddRequired(
                            catalog,
                            roots,
                            "3d/chara/mini/tail/mtail" + tail + "_00/pfb_mtail" + tail + "_00");
                    }

                    int textureCount = 0;
                    textureCount += AddPrefix(
                        catalog,
                        roots,
                        "3d/chara/mini/head/mchr1006_00/textures/");
                    textureCount += AddPrefix(
                        catalog,
                        roots,
                        "3d/chara/mini/head/mchr0001_00/textures/tex_mchr0001_00_face0_" + oguri.Skin);
                    Console.WriteLine("Matching dynamic texture bundles: " + textureCount);

                    var loadOrder = new List<GameAssetRecord>();
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (GameAssetRecord root in roots)
                    {
                        foreach (GameAssetRecord item in catalog.ResolveLoadOrder(root))
                        {
                            if (seen.Add(item.Name))
                            {
                                loadOrder.Add(item);
                            }
                        }
                    }

                    int missing = 0;
                    int invalidHeaders = 0;
                    foreach (GameAssetRecord record in loadOrder)
                    {
                        if (!File.Exists(record.FilePath))
                        {
                            missing++;
                            Console.WriteLine("MISSING " + record.Name + " -> " + record.FilePath);
                            continue;
                        }
                        using (Stream stream = record.OpenRead())
                        {
                            byte[] header = new byte[7];
                            int read = stream.Read(header, 0, header.Length);
                            string signature = Encoding.ASCII.GetString(header, 0, read);
                            if (!signature.StartsWith("Unity", StringComparison.Ordinal))
                            {
                                invalidHeaders++;
                                Console.WriteLine("NON-UNITY HEADER " + record.Name + " -> " + signature);
                            }
                        }
                    }

                    Console.WriteLine("Resolved bundle load order: " + loadOrder.Count);
                    Console.WriteLine("Missing local files: " + missing);
                    Console.WriteLine("Unexpected bundle headers: " + invalidHeaders);
                    if (missing != 0 || invalidHeaders != 0)
                    {
                        return 2;
                    }
                }
                Console.WriteLine("Standalone read-only catalog probe passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void AddRequired(
            GameDataCatalog catalog,
            List<GameAssetRecord> records,
            string logicalName)
        {
            GameAssetRecord record = catalog.GetRequiredAsset(logicalName);
            records.Add(record);
            Console.WriteLine("FOUND " + logicalName);
        }

        private static int AddPrefix(
            GameDataCatalog catalog,
            List<GameAssetRecord> records,
            string prefix)
        {
            List<GameAssetRecord> matches = catalog.FindByPrefix(prefix).ToList();
            records.AddRange(matches);
            return matches.Count;
        }

        private static Dictionary<string, string> ParseOptions(string[] args)
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < args.Length; index++)
            {
                string token = args[index];
                if (!token.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                {
                    throw new ArgumentException("Expected --name value arguments.");
                }
                options[token.Substring(2)] = args[++index];
            }
            return options;
        }

        private static string Required(Dictionary<string, string> options, string name)
        {
            string value;
            if (!options.TryGetValue(name, out value) || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Missing required option --" + name);
            }
            return value;
        }
    }
}
