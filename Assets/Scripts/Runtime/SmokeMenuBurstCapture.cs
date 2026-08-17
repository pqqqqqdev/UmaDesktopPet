using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Diagnostic-only consecutive-frame capture for native menu resize transitions.
    /// It is dormant unless --smoke-menu-burst supplies an output directory.
    /// </summary>
    internal static class SmokeMenuBurstCapture
    {
        private const string OutputArgument = "--smoke-menu-burst";
        private const int DefaultFramesPerPhase = 8;
        private const int DefaultCycles = 2;
        private const int TopUiExclusionPixels = 72;
        private const byte VisibleAlphaThreshold = 16;

        public static bool IsRequested
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return !string.IsNullOrWhiteSpace(ReadValue(OutputArgument));
#else
                return false;
#endif
            }
        }

        public static IEnumerator CaptureIfRequested(
            PetInteractionController interaction,
            bool failed)
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            // Release builds never honor diagnostic output directories.
            yield break;
#else
            string requestedDirectory = ReadValue(OutputArgument);
            if (string.IsNullOrWhiteSpace(requestedDirectory))
            {
                yield break;
            }

            string outputDirectory;
            try
            {
                outputDirectory = Path.GetFullPath(requestedDirectory);
                Directory.CreateDirectory(outputDirectory);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                QuitIfRequested(2);
                yield break;
            }

            if (interaction == null)
            {
                Debug.LogError(
                    "Cannot capture a menu burst because pet interaction was not created.");
                WriteUnavailableResult(
                    outputDirectory,
                    "Pet interaction was not created.");
                QuitIfRequested(2);
                yield break;
            }

            int framesPerPhase = ReadInt(
                "--smoke-burst-frames",
                DefaultFramesPerPhase,
                2,
                30);
            int cycles = ReadInt(
                "--smoke-burst-cycles",
                DefaultCycles,
                1,
                10);
            var frames = new List<BurstFrame>(1 + (framesPerPhase * cycles * 2));

            yield return new WaitForEndOfFrame();
            frames.Add(CaptureFrame("baseline.png", "baseline", -1, 0, interaction));

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                // Resume in the normal coroutine phase before rendering. This keeps
                // the smoke transition close to the timing of a real input-triggered
                // menu change, then samples every completed render that follows.
                yield return null;
                interaction.OpenMenuForSmokeTest();
                for (int index = 0; index < framesPerPhase; index++)
                {
                    yield return new WaitForEndOfFrame();
                    frames.Add(CaptureFrame(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "cycle-{0:00}-open-{1:000}.png",
                            cycle,
                            index),
                        "open",
                        cycle,
                        index,
                        interaction));
                }

                yield return null;
                interaction.CloseMenuForSmokeTest();
                for (int index = 0; index < framesPerPhase; index++)
                {
                    yield return new WaitForEndOfFrame();
                    frames.Add(CaptureFrame(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "cycle-{0:00}-close-{1:000}.png",
                            cycle,
                            index),
                        "close",
                        cycle,
                        index,
                        interaction));
                }
            }

            try
            {
                ValidateFrames(frames, interaction.MenuSidecarWidthForSmokeTest);
                SaveFrames(outputDirectory, frames);
                WriteReports(outputDirectory, frames, framesPerPhase, cycles);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                failed = true;
            }

            for (int index = 0; index < frames.Count; index++)
            {
                BurstFrame frame = frames[index];
                if (frame.Issues.Count == 0)
                {
                    continue;
                }

                failed = true;
                Debug.LogError(
                    "Menu burst " + frame.FileName + ": " +
                    string.Join("; ", frame.Issues.ToArray()));
            }

            Debug.Log(
                "Menu burst smoke " + (failed ? "FAILED" : "passed") +
                ": " + frames.Count + " end-of-frame captures in " +
                outputDirectory + ".");
            QuitIfRequested(failed ? 2 : 0);
#endif
        }

        private static BurstFrame CaptureFrame(
            string fileName,
            string phase,
            int cycle,
            int phaseIndex,
            PetInteractionController interaction)
        {
            var frame = new BurstFrame
            {
                FileName = fileName,
                Phase = phase,
                Cycle = cycle,
                PhaseIndex = phaseIndex,
                UnityFrame = Time.frameCount,
                RealtimeSeconds = Time.realtimeSinceStartup,
                Width = Mathf.Max(Screen.width, 1),
                Height = Mathf.Max(Screen.height, 1),
                MenuOpen = interaction != null && interaction.IsMenuOpen
            };

            Texture2D pixels = null;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                pixels = new Texture2D(
                    frame.Width,
                    frame.Height,
                    TextureFormat.RGBA32,
                    false);
                RenderTexture.active = null;
                pixels.ReadPixels(
                    new Rect(0, 0, frame.Width, frame.Height),
                    0,
                    0,
                    false);
                pixels.Apply(false, false);
                frame.Colors = pixels.GetPixels32();
            }
            catch (Exception exception)
            {
                frame.Issues.Add("capture failed: " + exception.Message);
                Debug.LogException(exception);
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (pixels != null)
                {
                    UnityEngine.Object.Destroy(pixels);
                }
            }

            return frame;
        }

        private static void ValidateFrames(
            IList<BurstFrame> frames,
            int sidecarWidth)
        {
            if (frames.Count == 0)
            {
                throw new InvalidDataException("The menu burst captured no frames.");
            }

            BurstFrame baseline = frames[0];
            baseline.ExpectedWidth = baseline.Width;
            baseline.ExpectedHeight = baseline.Height;
            int petViewportWidth = DesktopWindowController.PetViewportWidth;
            baseline.Metrics = MeasurePet(
                baseline,
                sidecarWidth,
                petViewportWidth);
            baseline.TotalVisiblePixels = CountVisible(
                baseline,
                0,
                baseline.Width,
                0,
                baseline.Height);
            baseline.SidecarVisiblePixels = CountVisible(
                baseline,
                0,
                sidecarWidth,
                0,
                baseline.Height);
            if (baseline.MenuOpen)
            {
                baseline.Issues.Add("menu was already open in the baseline frame");
            }
            if (!baseline.Metrics.Found)
            {
                baseline.Issues.Add("baseline pet region is blank");
            }

            int fixedWidth = baseline.Width;
            int fixedHeight = baseline.Height;
            PetMetrics baselineMetrics = baseline.Metrics;

            for (int index = 1; index < frames.Count; index++)
            {
                BurstFrame frame = frames[index];
                bool opening = string.Equals(
                    frame.Phase,
                    "open",
                    StringComparison.Ordinal);
                frame.ExpectedWidth = fixedWidth;
                frame.ExpectedHeight = fixedHeight;

                if (frame.Width != frame.ExpectedWidth ||
                    frame.Height != frame.ExpectedHeight)
                {
                    frame.Issues.Add(
                        "geometry was " + frame.Width + "x" + frame.Height +
                        ", expected " + frame.ExpectedWidth + "x" +
                        frame.ExpectedHeight);
                }
                if (frame.MenuOpen != opening)
                {
                    frame.Issues.Add(
                        "menu state was " + (frame.MenuOpen ? "open" : "closed") +
                        ", expected " + (opening ? "open" : "closed"));
                }

                frame.Metrics = MeasurePet(
                    frame,
                    sidecarWidth,
                    petViewportWidth);
                frame.TotalVisiblePixels = CountVisible(
                    frame,
                    0,
                    frame.Width,
                    0,
                    frame.Height);
                frame.SidecarVisiblePixels = CountVisible(
                    frame,
                    0,
                    sidecarWidth,
                    0,
                    frame.Height);
                if (opening)
                {
                    int minimumSidecarPixels = Math.Max(
                        1000,
                        (sidecarWidth * fixedHeight) / 20);
                    if (frame.SidecarVisiblePixels < minimumSidecarPixels)
                    {
                        frame.Issues.Add(
                            "sidecar is blank or incomplete (" +
                            frame.SidecarVisiblePixels + " visible pixels)");
                    }
                }
                else
                {
                    int maximumSidecarPixels = Math.Max(
                        32,
                        (sidecarWidth * fixedHeight) / 1000);
                    if (frame.SidecarVisiblePixels > maximumSidecarPixels)
                    {
                        frame.Issues.Add(
                            "hidden sidecar retained " +
                            frame.SidecarVisiblePixels + " visible pixels");
                    }
                }

                ComparePetMetrics(frame, baselineMetrics);
            }
        }

        private static void ComparePetMetrics(
            BurstFrame frame,
            PetMetrics baseline)
        {
            PetMetrics current = frame.Metrics;
            if (!baseline.Found)
            {
                frame.Issues.Add("cannot compare pet framing without a baseline");
                return;
            }
            if (!current.Found)
            {
                frame.Issues.Add("pet viewport is blank");
                return;
            }

            int minimumPetPixels = Math.Max(250, baseline.VisiblePixels / 3);
            if (current.VisiblePixels < minimumPetPixels)
            {
                frame.Issues.Add(
                    "pet viewport lost pixels (" + current.VisiblePixels +
                    ", baseline " + baseline.VisiblePixels + ")");
            }

            float maximumCenterDeltaX = Math.Max(18.0f, baseline.Width * 0.12f);
            float maximumCenterDeltaY = Math.Max(18.0f, baseline.Height * 0.08f);
            float deltaX = Mathf.Abs(current.CenterX - baseline.CenterX);
            float deltaY = Mathf.Abs(current.CenterY - baseline.CenterY);
            if (deltaX > maximumCenterDeltaX || deltaY > maximumCenterDeltaY)
            {
                frame.Issues.Add(
                    "pet moved inside its viewport by " +
                    deltaX.ToString("0.0", CultureInfo.InvariantCulture) + "," +
                    deltaY.ToString("0.0", CultureInfo.InvariantCulture) +
                    " pixels");
            }

            float widthRatio = current.Width / (float)Math.Max(1, baseline.Width);
            float heightRatio = current.Height / (float)Math.Max(1, baseline.Height);
            if (widthRatio < 0.78f || widthRatio > 1.22f ||
                heightRatio < 0.78f || heightRatio > 1.22f)
            {
                frame.Issues.Add(
                    "pet bounds changed scale (width " +
                    widthRatio.ToString("0.00", CultureInfo.InvariantCulture) +
                    "x, height " +
                    heightRatio.ToString("0.00", CultureInfo.InvariantCulture) +
                    "x)");
            }
        }

        private static PetMetrics MeasurePet(
            BurstFrame frame,
            int viewportX,
            int viewportWidth)
        {
            var result = new PetMetrics();
            if (frame.Colors == null || frame.Colors.Length == 0)
            {
                return result;
            }

            int startX = Mathf.Clamp(viewportX, 0, frame.Width);
            int endX = Mathf.Clamp(viewportX + viewportWidth, 0, frame.Width);
            int endY = Mathf.Max(0, frame.Height - TopUiExclusionPixels);
            int minimumX = int.MaxValue;
            int minimumY = int.MaxValue;
            int maximumX = int.MinValue;
            int maximumY = int.MinValue;
            int visible = 0;

            for (int y = 0; y < endY; y++)
            {
                int row = y * frame.Width;
                for (int x = startX; x < endX; x++)
                {
                    if (frame.Colors[row + x].a <= VisibleAlphaThreshold)
                    {
                        continue;
                    }

                    visible++;
                    minimumX = Math.Min(minimumX, x);
                    minimumY = Math.Min(minimumY, y);
                    maximumX = Math.Max(maximumX, x);
                    maximumY = Math.Max(maximumY, y);
                }
            }

            if (visible == 0)
            {
                return result;
            }

            result.Found = true;
            result.VisiblePixels = visible;
            result.MinimumX = minimumX - viewportX;
            result.MinimumY = minimumY;
            result.MaximumX = maximumX - viewportX;
            result.MaximumY = maximumY;
            return result;
        }

        private static int CountVisible(
            BurstFrame frame,
            int startX,
            int width,
            int startY,
            int height)
        {
            if (frame.Colors == null || frame.Colors.Length == 0)
            {
                return 0;
            }

            int clampedStartX = Mathf.Clamp(startX, 0, frame.Width);
            int endX = Mathf.Clamp(startX + width, 0, frame.Width);
            int clampedStartY = Mathf.Clamp(startY, 0, frame.Height);
            int endY = Mathf.Clamp(startY + height, 0, frame.Height);
            int visible = 0;
            for (int y = clampedStartY; y < endY; y++)
            {
                int row = y * frame.Width;
                for (int x = clampedStartX; x < endX; x++)
                {
                    if (frame.Colors[row + x].a > VisibleAlphaThreshold)
                    {
                        visible++;
                    }
                }
            }
            return visible;
        }

        private static void SaveFrames(
            string outputDirectory,
            IList<BurstFrame> frames)
        {
            for (int index = 0; index < frames.Count; index++)
            {
                BurstFrame frame = frames[index];
                if (frame.Colors == null || frame.Colors.Length == 0)
                {
                    continue;
                }

                Texture2D pixels = null;
                try
                {
                    pixels = new Texture2D(
                        frame.Width,
                        frame.Height,
                        TextureFormat.RGBA32,
                        false);
                    pixels.SetPixels32(frame.Colors);
                    pixels.Apply(false, false);
                    File.WriteAllBytes(
                        Path.Combine(outputDirectory, frame.FileName),
                        pixels.EncodeToPNG());
                }
                catch (Exception exception)
                {
                    frame.Issues.Add("PNG write failed: " + exception.Message);
                    Debug.LogException(exception);
                }
                finally
                {
                    if (pixels != null)
                    {
                        UnityEngine.Object.Destroy(pixels);
                    }
                }
            }
        }

        private static void WriteReports(
            string outputDirectory,
            IList<BurstFrame> frames,
            int framesPerPhase,
            int cycles)
        {
            var csv = new StringBuilder();
            csv.AppendLine(
                "file,phase,cycle,phase_index,unity_frame,realtime_seconds," +
                "width,height,expected_width,expected_height,menu_open," +
                "total_visible_pixels,sidecar_visible_pixels,pet_visible_pixels," +
                "pet_min_x,pet_min_y,pet_max_x,pet_max_y,pet_center_x," +
                "pet_center_y,valid,issues");

            int failedFrames = 0;
            for (int index = 0; index < frames.Count; index++)
            {
                BurstFrame frame = frames[index];
                PetMetrics metrics = frame.Metrics;
                bool valid = frame.Issues.Count == 0;
                if (!valid)
                {
                    failedFrames++;
                }

                csv.Append(Csv(frame.FileName)).Append(',')
                    .Append(Csv(frame.Phase)).Append(',')
                    .Append(frame.Cycle.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.PhaseIndex.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.UnityFrame.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.RealtimeSeconds.ToString(
                        "0.000000",
                        CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.Width.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.Height.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.ExpectedWidth.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.ExpectedHeight.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.MenuOpen ? "true" : "false").Append(',')
                    .Append(frame.TotalVisiblePixels.ToString(
                        CultureInfo.InvariantCulture)).Append(',')
                    .Append(frame.SidecarVisiblePixels.ToString(
                        CultureInfo.InvariantCulture)).Append(',')
                    .Append(metrics.VisiblePixels.ToString(
                        CultureInfo.InvariantCulture)).Append(',')
                    .Append(metrics.MinimumX.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(metrics.MinimumY.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(metrics.MaximumX.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(metrics.MaximumY.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(metrics.CenterX.ToString(
                        "0.0",
                        CultureInfo.InvariantCulture)).Append(',')
                    .Append(metrics.CenterY.ToString(
                        "0.0",
                        CultureInfo.InvariantCulture)).Append(',')
                    .Append(valid ? "true" : "false").Append(',')
                    .Append(Csv(string.Join("; ", frame.Issues.ToArray())))
                    .AppendLine();
            }

            File.WriteAllText(
                Path.Combine(outputDirectory, "menu-burst.csv"),
                csv.ToString());

            var summary = new StringBuilder();
            summary.AppendLine(failedFrames == 0 ? "PASS" : "FAIL");
            summary.AppendLine("frames=" + frames.Count);
            summary.AppendLine("frames_per_phase=" + framesPerPhase);
            summary.AppendLine("cycles=" + cycles);
            summary.AppendLine("failed_frames=" + failedFrames);
            for (int index = 0; index < frames.Count; index++)
            {
                BurstFrame frame = frames[index];
                if (frame.Issues.Count > 0)
                {
                    summary.AppendLine(
                        frame.FileName + ": " +
                        string.Join("; ", frame.Issues.ToArray()));
                }
            }
            File.WriteAllText(
                Path.Combine(outputDirectory, "menu-burst-result.txt"),
                summary.ToString());
        }

        private static void WriteUnavailableResult(
            string outputDirectory,
            string reason)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(outputDirectory, "menu-burst-result.txt"),
                    "FAIL" + Environment.NewLine + reason + Environment.NewLine);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static string Csv(string value)
        {
            value = value ?? string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string ReadValue(string name)
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

        private static int ReadInt(
            string name,
            int fallback,
            int minimum,
            int maximum)
        {
            int parsed;
            return int.TryParse(
                ReadValue(name),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed)
                    ? Mathf.Clamp(parsed, minimum, maximum)
                    : fallback;
        }

        private static bool HasArgument(string name)
        {
            return Array.Exists(
                Environment.GetCommandLineArgs(),
                argument => string.Equals(
                    argument,
                    name,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static void QuitIfRequested(int exitCode)
        {
            if (HasArgument("--smoke-quit"))
            {
                Application.Quit(exitCode);
            }
        }

        private sealed class BurstFrame
        {
            public string FileName;
            public string Phase;
            public int Cycle;
            public int PhaseIndex;
            public int UnityFrame;
            public float RealtimeSeconds;
            public int Width;
            public int Height;
            public int ExpectedWidth;
            public int ExpectedHeight;
            public bool MenuOpen;
            public Color32[] Colors;
            public int TotalVisiblePixels;
            public int SidecarVisiblePixels;
            public PetMetrics Metrics;
            public readonly List<string> Issues = new List<string>();
        }

        private struct PetMetrics
        {
            public bool Found;
            public int VisiblePixels;
            public int MinimumX;
            public int MinimumY;
            public int MaximumX;
            public int MaximumY;

            public int Width
            {
                get { return Found ? MaximumX - MinimumX + 1 : 0; }
            }

            public int Height
            {
                get { return Found ? MaximumY - MinimumY + 1 : 0; }
            }

            public float CenterX
            {
                get { return Found ? (MinimumX + MaximumX) * 0.5f : 0.0f; }
            }

            public float CenterY
            {
                get { return Found ? (MinimumY + MaximumY) * 0.5f : 0.0f; }
            }
        }
    }
}
