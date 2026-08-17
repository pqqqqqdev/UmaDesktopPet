using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UmaDesktopPet.Standalone.Core;

namespace UmaDesktopPet.Standalone.Runtime
{
    [DisallowMultipleComponent]
    public sealed class GameInstallSetupPanel : MonoBehaviour
    {
        private readonly List<string> _candidates = new List<string>();

        private DesktopWindowController _window;
        private GameInstallPreferences _preferences;
        private string _sqliteLibraryPath;
        private string _pathInput = string.Empty;
        private string _message = string.Empty;
        private bool _visible;
        private bool _allowCancel;
        private bool _checking;
        private FolderPickerRequest _folderPicker;
        private Vector2 _candidateScroll;

        public event Action<string, GameRegion> InstallAccepted;
        public event Action Cancelled;

        public bool IsVisible { get { return _visible; } }

        public void Initialize(
            DesktopWindowController window,
            GameInstallPreferences preferences,
            string sqliteLibraryPath)
        {
            _window = window;
            _preferences = preferences ?? throw new ArgumentNullException("preferences");
            _sqliteLibraryPath = sqliteLibraryPath ?? throw new ArgumentNullException(
                "sqliteLibraryPath");

            if (_window != null)
            {
                _window.FilesDropped += HandleFilesDropped;
            }
        }

        public void Show(bool allowCancel, string message, string attemptedPath = null)
        {
            _allowCancel = allowCancel;
            _message = message ?? string.Empty;
            _visible = true;
            _checking = false;
            _folderPicker = null;
            _candidateScroll = Vector2.zero;
            if (!string.IsNullOrWhiteSpace(attemptedPath))
            {
                _pathInput = attemptedPath;
            }
            else
            {
                GameInstallPreferenceSnapshot remembered;
                string ignored;
                if (_preferences.TryLoad(out remembered, out ignored))
                {
                    _pathInput = remembered.GameRoot;
                }
            }
            RefreshCandidates(false);
            SetFileDropEnabled(true);
        }

        public void Hide()
        {
            _visible = false;
            _checking = false;
            _folderPicker = null;
            SetFileDropEnabled(false);
        }

        private void Update()
        {
            if (!_visible || _folderPicker == null || !_folderPicker.IsComplete)
            {
                return;
            }

            FolderPickerRequest completed = _folderPicker;
            _folderPicker = null;
            _checking = false;
            if (!string.IsNullOrWhiteSpace(completed.Error))
            {
                _message = completed.Error;
                return;
            }
            if (!string.IsNullOrWhiteSpace(completed.SelectedPath))
            {
                _pathInput = completed.SelectedPath;
                TryAccept(_pathInput);
                return;
            }
            _message = "Folder selection was cancelled.";
        }

        private void OnGUI()
        {
            if (!_visible)
            {
                return;
            }

            float width = Mathf.Min(
                DesktopWindowController.SidePanelWidth - 16.0f,
                Screen.width - 20.0f);
            float height = Mathf.Min(456.0f, Screen.height - 20.0f);
            float x = 8.0f;
            float y = (Screen.height - height) * 0.5f;
            Rect panel = new Rect(x, y, width, height);

            Color oldColor = GUI.color;
            GUI.color = new Color(0.94f, 0.97f, 0.99f, 0.985f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = oldColor;
            GUI.Box(panel, GUIContent.none);

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            titleStyle.normal.textColor = new Color(0.22f, 0.16f, 0.14f);
            GUI.Label(
                new Rect(x + 18.0f, y + 14.0f, width - 36.0f, 32.0f),
                "Connect Umamusume",
                titleStyle);

            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true
            };
            bodyStyle.normal.textColor = new Color(0.26f, 0.22f, 0.20f);
            GUI.Label(
                new Rect(x + 18.0f, y + 50.0f, width - 36.0f, 48.0f),
                "The desktop pet reads your installed game files directly. Nothing is " +
                "modified, copied, or exported. JP and Global are detected " +
                "automatically.",
                bodyStyle);

            float cursorY = y + 102.0f;
            if (_candidates.Count > 0)
            {
                GUI.Label(
                    new Rect(x + 18.0f, cursorY, width - 36.0f, 20.0f),
                    _candidates.Count == 1
                        ? "Installation found"
                        : "Choose an installation",
                    GUI.skin.label);
                cursorY += 22.0f;

                Rect scrollArea = new Rect(
                    x + 18.0f,
                    cursorY,
                    width - 36.0f,
                    Mathf.Min(94.0f, _candidates.Count * 46.0f));
                Rect scrollContent = new Rect(
                    0.0f,
                    0.0f,
                    scrollArea.width - 18.0f,
                    _candidates.Count * 46.0f);
                _candidateScroll = GUI.BeginScrollView(
                    scrollArea,
                    _candidateScroll,
                    scrollContent);
                var candidateStyle = new GUIStyle(GUI.skin.button)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip
                };
                for (int index = 0; index < _candidates.Count; index++)
                {
                    string candidate = _candidates[index];
                    Rect button = new Rect(
                        0.0f,
                        index * 46.0f,
                        scrollContent.width,
                        40.0f);
                    var content = new GUIContent(
                        ShortenPath(candidate, button.width - 4.0f, candidateStyle),
                        candidate);
                    if (GUI.Button(button, content, candidateStyle))
                    {
                        _pathInput = candidate;
                        TryAccept(candidate);
                    }
                }
                GUI.EndScrollView();
                cursorY += scrollArea.height + 8.0f;
            }

            GUI.Label(
                new Rect(x + 18.0f, cursorY, width - 36.0f, 20.0f),
                "Game folder",
                GUI.skin.label);
            cursorY += 21.0f;
            GUI.enabled = !_checking;
            _pathInput = GUI.TextField(
                new Rect(x + 18.0f, cursorY, width - 36.0f, 28.0f),
                _pathInput ?? string.Empty);
            cursorY += 34.0f;

            float buttonGap = 6.0f;
            float buttonWidth = (width - 36.0f - buttonGap * 2.0f) / 3.0f;
            if (GUI.Button(
                new Rect(x + 18.0f, cursorY, buttonWidth, 30.0f),
                "Browse..."))
            {
                BeginFolderBrowse();
            }
            if (GUI.Button(
                new Rect(
                    x + 18.0f + buttonWidth + buttonGap,
                    cursorY,
                    buttonWidth,
                    30.0f),
                "Use folder"))
            {
                TryAccept(_pathInput);
            }
            if (GUI.Button(
                new Rect(
                    x + 18.0f + (buttonWidth + buttonGap) * 2.0f,
                    cursorY,
                    buttonWidth,
                    30.0f),
                "Scan again"))
            {
                RefreshCandidates(true);
            }
            GUI.enabled = true;
            cursorY += 38.0f;

            var messageStyle = new GUIStyle(bodyStyle)
            {
                fontStyle = FontStyle.Bold
            };
            messageStyle.normal.textColor = _checking
                ? new Color(0.20f, 0.46f, 0.72f)
                : new Color(0.65f, 0.24f, 0.18f);
            GUI.Label(
                new Rect(x + 18.0f, cursorY, width - 36.0f, 50.0f),
                _message,
                messageStyle);

            string footer =
                "Global: use Download All in the game's settings if files are missing.";
            GUI.Label(
                new Rect(x + 18.0f, y + height - 72.0f, width - 36.0f, 34.0f),
                footer,
                bodyStyle);

            string exitLabel = _allowCancel ? "Cancel" : "Quit";
            bool exitEnabled = GUI.enabled;
            GUI.enabled = exitEnabled && _folderPicker == null;
            bool exitClicked = GUI.Button(
                new Rect(x + width - 106.0f, y + height - 36.0f, 88.0f, 26.0f),
                exitLabel);
            GUI.enabled = exitEnabled;
            if (exitClicked)
            {
                if (_allowCancel)
                {
                    Hide();
                    Action cancelled = Cancelled;
                    if (cancelled != null)
                    {
                        cancelled();
                    }
                }
                else
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                }
            }
        }

        internal void BeginFolderBrowse()
        {
            if (!_visible || _checking)
            {
                return;
            }

            _checking = true;
            _message = "Waiting for a folder...";
            _folderPicker = WindowsFolderPicker.Begin(
                "Choose Umamusume or its Persistent folder");
        }

        private void TryAccept(string input)
        {
            if (_checking)
            {
                return;
            }

            string root;
            if (!GameInstallLocator.TryNormalizeRoot(input, out root))
            {
                _message =
                    "Choose the game, *_Data, Persistent, or game executable folder.";
                return;
            }

            _checking = true;
            _message = "Checking the installed game files...";
            StartCoroutine(CheckAndAccept(root));
        }

        private IEnumerator CheckAndAccept(string root)
        {
            // Let IMGUI finish and repaint the checking message before reading a
            // large catalog. This also keeps scene changes out of an OnGUI call.
            yield return null;
            if (!_visible)
            {
                _checking = false;
                yield break;
            }

            GameCompatibilityReport report = GameCompatibilityProbe.Probe(
                root,
                _sqliteLibraryPath);
            _checking = false;
            if (!report.IsCompatible || !report.Region.HasValue)
            {
                _message = report.Message;
                Debug.LogWarning(
                    "Game compatibility check failed: " + report.Status + "\n" +
                    report.Details);
                yield break;
            }

            string saveError;
            if (!_preferences.TrySave(root, out saveError))
            {
                Debug.LogWarning(
                    saveError + " The compatible game folder will still be " +
                    "used for this session, but it may need to be selected " +
                    "again next time.");
            }

            Hide();
            Action<string, GameRegion> accepted = InstallAccepted;
            if (accepted != null)
            {
                accepted(root, report.Region.Value);
            }
        }

        private void RefreshCandidates(bool showResult)
        {
            _candidates.Clear();
            _candidates.AddRange(GameInstallLocator.FindCandidates());
            if (showResult)
            {
                _message = _candidates.Count == 0
                    ? "No installation was found automatically. Browse or drag its folder here."
                    : _candidates.Count == 1
                        ? "One installation was found."
                        : _candidates.Count + " installations were found.";
            }
        }

        private void HandleFilesDropped(string[] paths)
        {
            if (!_visible || paths == null)
            {
                return;
            }
            foreach (string path in paths)
            {
                string root;
                if (GameInstallLocator.TryNormalizeRoot(path, out root))
                {
                    _pathInput = path;
                    TryAccept(path);
                    return;
                }
            }
            _message = "The dropped item is not an Umamusume installation.";
        }

        private void SetFileDropEnabled(bool enabled)
        {
            if (_window != null)
            {
                _window.SetFileDropEnabled(enabled);
            }
        }

        private static string ShortenPath(
            string path,
            float maxWidth,
            GUIStyle style)
        {
            if (string.IsNullOrEmpty(path) ||
                style.CalcSize(new GUIContent(path)).x <= maxWidth)
            {
                return path;
            }

            const string Ellipsis = "...";
            string best = Ellipsis;
            int low = 0;
            int high = path.Length;
            while (low <= high)
            {
                int keptCharacters = (low + high) / 2;
                int prefixLength = (keptCharacters + 1) / 2;
                int suffixLength = keptCharacters / 2;
                string candidate = path.Substring(0, prefixLength) +
                    Ellipsis +
                    path.Substring(path.Length - suffixLength, suffixLength);
                if (style.CalcSize(new GUIContent(candidate)).x <= maxWidth)
                {
                    best = candidate;
                    low = keptCharacters + 1;
                }
                else
                {
                    high = keptCharacters - 1;
                }
            }
            return best;
        }

        private void OnDestroy()
        {
            if (_window != null)
            {
                _window.FilesDropped -= HandleFilesDropped;
                _window.SetFileDropEnabled(false);
            }
        }
    }
}
