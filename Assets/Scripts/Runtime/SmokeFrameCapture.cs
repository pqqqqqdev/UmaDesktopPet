using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    internal static class SmokeFrameCapture
    {
        public static IEnumerator CaptureIfRequested(Camera camera, bool failed)
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            // Release builds never honor diagnostic paths supplied on the command line.
            yield break;
#else
            string cameraOutputPath = ReadValue("--smoke-frame");
            string windowOutputPath = ReadValue("--smoke-window-frame");
            if (string.IsNullOrWhiteSpace(cameraOutputPath) &&
                string.IsNullOrWhiteSpace(windowOutputPath))
            {
                yield break;
            }

            if (camera == null && !string.IsNullOrWhiteSpace(cameraOutputPath))
            {
                Debug.LogError("Cannot capture a smoke frame because no camera was created.");
                QuitIfRequested(2);
                yield break;
            }

            float delaySeconds = ReadFloat("--smoke-delay", 0.0f);
            if (delaySeconds > 0.0f)
            {
                yield return new WaitForSecondsRealtime(delaySeconds);
            }

            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            if (!string.IsNullOrWhiteSpace(cameraOutputPath))
            {
                cameraOutputPath = Path.GetFullPath(cameraOutputPath);
                string directory = Path.GetDirectoryName(cameraOutputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                int width = Mathf.Max(Screen.width, 1);
                int height = Mathf.Max(Screen.height, 1);
                var renderTexture = new RenderTexture(
                    width,
                    height,
                    24,
                    RenderTextureFormat.ARGB32);
                var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
                RenderTexture previousActive = RenderTexture.active;
                RenderTexture previousTarget = camera.targetTexture;
                try
                {
                    camera.targetTexture = renderTexture;
                    RenderTexture.active = renderTexture;
                    GL.Clear(true, true, Color.clear);
                    camera.Render();
                    RenderTexture.active = renderTexture;
                    pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                    pixels.Apply(false, false);
                    File.WriteAllBytes(cameraOutputPath, pixels.EncodeToPNG());
                    ValidateTransparency(pixels);
                    Debug.Log("Smoke frame: " + cameraOutputPath);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    failed = true;
                }
                finally
                {
                    camera.targetTexture = previousTarget;
                    RenderTexture.active = previousActive;
                    renderTexture.Release();
                    UnityEngine.Object.Destroy(renderTexture);
                    UnityEngine.Object.Destroy(pixels);
                }
            }

            if (!string.IsNullOrWhiteSpace(windowOutputPath))
            {
                windowOutputPath = Path.GetFullPath(windowOutputPath);
                string directory = Path.GetDirectoryName(windowOutputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                try
                {
                    int width = Mathf.Max(Screen.width, 1);
                    int height = Mathf.Max(Screen.height, 1);
                    var pixels = new Texture2D(
                        width,
                        height,
                        TextureFormat.RGBA32,
                        false);
                    RenderTexture previousActive = RenderTexture.active;
                    RenderTexture.active = null;
                    pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                    pixels.Apply(false, false);
                    File.WriteAllBytes(windowOutputPath, pixels.EncodeToPNG());
                    RenderTexture.active = previousActive;
                    UnityEngine.Object.Destroy(pixels);
                    Debug.Log("Window smoke frame: " + windowOutputPath);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    failed = true;
                }
            }

            QuitIfRequested(failed ? 2 : 0);
#endif
        }

        private static void ValidateTransparency(Texture2D pixels)
        {
            Color32[] colors = pixels.GetPixels32();
            int transparentPixels = 0;
            int opaquePixels = 0;
            for (int index = 0; index < colors.Length; index++)
            {
                byte alpha = colors[index].a;
                if (alpha <= 16)
                {
                    transparentPixels++;
                }
                if (alpha >= 240)
                {
                    opaquePixels++;
                }
            }

            byte maximumCornerAlpha = 0;
            int maximumX = pixels.width - 1;
            int maximumY = pixels.height - 1;
            Color32[] corners =
            {
                pixels.GetPixel(0, 0),
                pixels.GetPixel(maximumX, 0),
                pixels.GetPixel(0, maximumY),
                pixels.GetPixel(maximumX, maximumY)
            };
            for (int index = 0; index < corners.Length; index++)
            {
                if (corners[index].a > maximumCornerAlpha)
                {
                    maximumCornerAlpha = corners[index].a;
                }
            }

            Debug.Log(
                "Smoke alpha: transparent=" + transparentPixels +
                ", opaque=" + opaquePixels +
                ", cornerMax=" + maximumCornerAlpha + ".");

            if (maximumCornerAlpha > 16)
            {
                throw new InvalidDataException(
                    "The desktop-pet smoke frame has an opaque background corner.");
            }
            if (transparentPixels < colors.Length / 10 || opaquePixels == 0)
            {
                throw new InvalidDataException(
                    "The desktop-pet smoke frame does not contain both transparent " +
                    "background and opaque character pixels.");
            }
        }

        private static void QuitIfRequested(int exitCode)
        {
            if (HasArgument("--smoke-quit"))
            {
                Application.Quit(exitCode);
            }
        }

        private static string ReadValue(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[index + 1];
                }
            }
            return null;
        }

        private static float ReadFloat(string name, float fallback)
        {
            string value = ReadValue(name);
            float parsed;
            return float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out parsed)
                    ? Mathf.Max(0.0f, parsed)
                    : fallback;
        }

        private static bool HasArgument(string name)
        {
            return Array.Exists(
                Environment.GetCommandLineArgs(),
                argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
