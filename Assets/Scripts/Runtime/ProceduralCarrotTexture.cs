using System.Collections.Generic;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Creates the small carrot used by desktop-pet interactions without
    /// depending on an installed-game or packaged image asset.
    ///
    /// The returned texture is owned by the caller. Pass it to
    /// <see cref="Destroy"/> when the owning component is disposed.
    /// </summary>
    public static class ProceduralCarrotTexture
    {
        public const int Size = 96;

        private const int Supersampling = 4;

        /// <summary>
        /// Creates a transparent 96-by-96 carrot icon. The texture has no
        /// mipmaps, uses bilinear filtering, and is marked DontSave.
        /// </summary>
        public static Texture2D Create()
        {
            int workingSize = Size * Supersampling;
            var pixels = new Color[workingSize * workingSize];

            DrawLeaves(pixels, workingSize);
            DrawRoot(pixels, workingSize);

            Color[] resolved = Downsample(pixels, workingSize);
            var texture = new Texture2D(
                Size,
                Size,
                TextureFormat.RGBA32,
                false,
                false);
            texture.name = "Procedural Carrot";
            texture.hideFlags = HideFlags.DontSave;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 1;
            texture.SetPixels(resolved);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>
        /// Releases a texture returned by <see cref="Create"/>. Null is safe.
        /// </summary>
        public static void Destroy(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(texture);
            }
            else
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static void DrawLeaves(Color[] pixels, int size)
        {
            Color outline = FromHex(0x174A2FFF);
            Color deepGreen = FromHex(0x258E4BFF);
            Color green = FromHex(0x48C95FFF);
            Color lightGreen = FromHex(0x87E678FF);

            DrawShape(
                pixels,
                size,
                ClosedBezier(
                    new Vector2(41f, 63f),
                    new Vector2(34f, 68f),
                    new Vector2(23f, 74f),
                    new Vector2(18f, 87f),
                    new Vector2(29f, 84f),
                    new Vector2(39f, 77f),
                    new Vector2(45f, 67f)),
                outline);
            DrawShape(
                pixels,
                size,
                ClosedBezier(
                    new Vector2(40f, 66f),
                    new Vector2(34f, 70f),
                    new Vector2(27f, 76f),
                    new Vector2(22f, 83f),
                    new Vector2(31f, 80f),
                    new Vector2(38f, 74f),
                    new Vector2(42f, 68f)),
                green);

            DrawShape(
                pixels,
                size,
                ClosedBezier(
                    new Vector2(39f, 65f),
                    new Vector2(36f, 75f),
                    new Vector2(36f, 85f),
                    new Vector2(41f, 92f),
                    new Vector2(48f, 82f),
                    new Vector2(48f, 72f),
                    new Vector2(45f, 63f)),
                outline);
            DrawShape(
                pixels,
                size,
                ClosedBezier(
                    new Vector2(41f, 67f),
                    new Vector2(39f, 75f),
                    new Vector2(39f, 83f),
                    new Vector2(42f, 88f),
                    new Vector2(46f, 80f),
                    new Vector2(46f, 72f),
                    new Vector2(44f, 66f)),
                lightGreen);

            DrawShape(
                pixels,
                size,
                ClosedBezier(
                    new Vector2(43f, 65f),
                    new Vector2(50f, 67f),
                    new Vector2(59f, 75f),
                    new Vector2(64f, 86f),
                    new Vector2(54f, 83f),
                    new Vector2(45f, 77f),
                    new Vector2(40f, 69f)),
                outline);
            DrawShape(
                pixels,
                size,
                ClosedBezier(
                    new Vector2(45f, 67f),
                    new Vector2(50f, 70f),
                    new Vector2(57f, 76f),
                    new Vector2(60f, 82f),
                    new Vector2(53f, 79f),
                    new Vector2(47f, 74f),
                    new Vector2(43f, 69f)),
                deepGreen);

            DrawLine(pixels, size, new Vector2(23f, 82f), new Vector2(40f, 68f), 1.1f, lightGreen);
            DrawLine(pixels, size, new Vector2(42f, 87f), new Vector2(43f, 67f), 1.0f, deepGreen);
            DrawLine(pixels, size, new Vector2(59f, 81f), new Vector2(45f, 69f), 0.9f, lightGreen);
        }

        private static void DrawRoot(Color[] pixels, int size)
        {
            Color outline = FromHex(0x71371BFF);
            Color orange = FromHex(0xF47A20FF);
            Color shadow = FromHex(0xC84D18A8);
            Color highlight = FromHex(0xFFB24CB8);
            Color ridge = FromHex(0xA83C1788);

            DrawShape(
                pixels,
                size,
                ClosedBezier(
                    new Vector2(27f, 59f),
                    new Vector2(27f, 52f),
                    new Vector2(33f, 43f),
                    new Vector2(38f, 34f),
                    new Vector2(45f, 23f),
                    new Vector2(53f, 14f),
                    new Vector2(62f, 6f),
                    new Vector2(58f, 21f),
                    new Vector2(58f, 36f),
                    new Vector2(59f, 47f),
                    new Vector2(58f, 55f),
                    new Vector2(51f, 62f),
                    new Vector2(40f, 66f),
                    new Vector2(31f, 64f)),
                outline);

            DrawShape(
                pixels,
                size,
                ClosedBezier(
                    new Vector2(31f, 58f),
                    new Vector2(31f, 52f),
                    new Vector2(36f, 43f),
                    new Vector2(41f, 34f),
                    new Vector2(47f, 24f),
                    new Vector2(53f, 16f),
                    new Vector2(58f, 11f),
                    new Vector2(55f, 23f),
                    new Vector2(55f, 36f),
                    new Vector2(56f, 47f),
                    new Vector2(54f, 54f),
                    new Vector2(49f, 59f),
                    new Vector2(40f, 62f),
                    new Vector2(33f, 61f)),
                orange);

            DrawShape(
                pixels,
                size,
                ClosedBezier(
                    new Vector2(49f, 59f),
                    new Vector2(54f, 54f),
                    new Vector2(54f, 45f),
                    new Vector2(53f, 32f),
                    new Vector2(54f, 21f),
                    new Vector2(58f, 11f),
                    new Vector2(55f, 31f),
                    new Vector2(57f, 47f),
                    new Vector2(54f, 55f)),
                shadow);

            DrawShape(
                pixels,
                size,
                ClosedBezier(
                    new Vector2(34f, 55f),
                    new Vector2(35f, 49f),
                    new Vector2(40f, 40f),
                    new Vector2(45f, 31f),
                    new Vector2(48f, 24f),
                    new Vector2(43f, 37f),
                    new Vector2(39f, 48f),
                    new Vector2(38f, 57f)),
                highlight);

            DrawLine(pixels, size, new Vector2(37f, 49f), new Vector2(45f, 47f), 1.25f, ridge);
            DrawLine(pixels, size, new Vector2(42f, 38f), new Vector2(49f, 36f), 1.15f, ridge);
            DrawLine(pixels, size, new Vector2(47f, 27f), new Vector2(52f, 25f), 1.0f, ridge);
            DrawLine(pixels, size, new Vector2(33f, 57f), new Vector2(48f, 61f), 1.0f, FromHex(0xFFB15D88));
        }

        private static List<Vector2> ClosedBezier(params Vector2[] anchors)
        {
            // Catmull-Rom interpolation provides a compact, smooth closed path
            // while keeping the authored points easy to inspect and adjust.
            const int stepsPerSpan = 8;
            var points = new List<Vector2>(anchors.Length * stepsPerSpan);
            for (int index = 0; index < anchors.Length; index++)
            {
                Vector2 p0 = anchors[(index - 1 + anchors.Length) % anchors.Length];
                Vector2 p1 = anchors[index];
                Vector2 p2 = anchors[(index + 1) % anchors.Length];
                Vector2 p3 = anchors[(index + 2) % anchors.Length];
                for (int step = 0; step < stepsPerSpan; step++)
                {
                    float t = step / (float)stepsPerSpan;
                    float t2 = t * t;
                    float t3 = t2 * t;
                    points.Add(0.5f * (
                        (2f * p1) +
                        ((-p0 + p2) * t) +
                        ((2f * p0 - 5f * p1 + 4f * p2 - p3) * t2) +
                        ((-p0 + 3f * p1 - 3f * p2 + p3) * t3)));
                }
            }

            return points;
        }

        private static void DrawShape(
            Color[] pixels,
            int size,
            IList<Vector2> polygon,
            Color color)
        {
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            for (int index = 0; index < polygon.Count; index++)
            {
                Vector2 point = polygon[index];
                minX = Mathf.Min(minX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxX = Mathf.Max(maxX, point.x);
                maxY = Mathf.Max(maxY, point.y);
            }

            int x0 = Mathf.Clamp(Mathf.FloorToInt(minX * Supersampling), 0, size - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(minY * Supersampling), 0, size - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(maxX * Supersampling), 0, size - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(maxY * Supersampling), 0, size - 1);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 point = new Vector2(
                        (x + 0.5f) / Supersampling,
                        (y + 0.5f) / Supersampling);
                    if (Contains(polygon, point))
                    {
                        Blend(pixels, (y * size) + x, color);
                    }
                }
            }
        }

        private static void DrawLine(
            Color[] pixels,
            int size,
            Vector2 from,
            Vector2 to,
            float width,
            Color color)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(from.x, to.x) - width) * Supersampling), 0, size - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(from.y, to.y) - width) * Supersampling), 0, size - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(from.x, to.x) + width) * Supersampling), 0, size - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(from.y, to.y) + width) * Supersampling), 0, size - 1);
            Vector2 direction = to - from;
            float lengthSquared = direction.sqrMagnitude;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 point = new Vector2(
                        (x + 0.5f) / Supersampling,
                        (y + 0.5f) / Supersampling);
                    float t = Mathf.Clamp01(Vector2.Dot(point - from, direction) / lengthSquared);
                    if ((point - (from + (direction * t))).sqrMagnitude <= width * width)
                    {
                        Blend(pixels, (y * size) + x, color);
                    }
                }
            }
        }

        private static bool Contains(IList<Vector2> polygon, Vector2 point)
        {
            bool inside = false;
            int previous = polygon.Count - 1;
            for (int current = 0; current < polygon.Count; current++)
            {
                Vector2 a = polygon[current];
                Vector2 b = polygon[previous];
                bool crosses = ((a.y > point.y) != (b.y > point.y)) &&
                    (point.x < ((b.x - a.x) * (point.y - a.y) / (b.y - a.y)) + a.x);
                if (crosses)
                {
                    inside = !inside;
                }

                previous = current;
            }

            return inside;
        }

        private static Color[] Downsample(Color[] source, int sourceSize)
        {
            var result = new Color[Size * Size];
            float sampleCount = Supersampling * Supersampling;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    Color premultiplied = Color.clear;
                    float alpha = 0f;
                    for (int sy = 0; sy < Supersampling; sy++)
                    {
                        for (int sx = 0; sx < Supersampling; sx++)
                        {
                            Color sample = source[
                                ((y * Supersampling + sy) * sourceSize) +
                                (x * Supersampling + sx)];
                            premultiplied.r += sample.r * sample.a;
                            premultiplied.g += sample.g * sample.a;
                            premultiplied.b += sample.b * sample.a;
                            alpha += sample.a;
                        }
                    }

                    alpha /= sampleCount;
                    if (alpha > 0.0001f)
                    {
                        float inverse = 1f / (alpha * sampleCount);
                        result[(y * Size) + x] = new Color(
                            premultiplied.r * inverse,
                            premultiplied.g * inverse,
                            premultiplied.b * inverse,
                            alpha);
                    }
                }
            }

            return result;
        }

        private static void Blend(Color[] pixels, int index, Color source)
        {
            Color destination = pixels[index];
            float alpha = source.a + (destination.a * (1f - source.a));
            if (alpha <= 0f)
            {
                pixels[index] = Color.clear;
                return;
            }

            pixels[index] = new Color(
                ((source.r * source.a) + (destination.r * destination.a * (1f - source.a))) / alpha,
                ((source.g * source.a) + (destination.g * destination.a * (1f - source.a))) / alpha,
                ((source.b * source.a) + (destination.b * destination.a * (1f - source.a))) / alpha,
                alpha);
        }

        private static Color FromHex(uint rgba)
        {
            const float scale = 1f / 255f;
            return new Color(
                ((rgba >> 24) & 0xFF) * scale,
                ((rgba >> 16) & 0xFF) * scale,
                ((rgba >> 8) & 0xFF) * scale,
                (rgba & 0xFF) * scale);
        }
    }
}
