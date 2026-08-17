using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace UmaDesktopPet.Standalone.Runtime
{
    internal sealed class FolderPickerRequest
    {
        private volatile bool _isComplete;

        public bool IsComplete { get { return _isComplete; } }
        public string SelectedPath { get; private set; }
        public string Error { get; private set; }

        internal void Complete(string selectedPath, string error)
        {
            SelectedPath = selectedPath;
            Error = error;
            _isComplete = true;
        }
    }

    /// <summary>
    /// Opens the native Windows folder chooser on a dedicated STA thread. The
    /// request is polled from Unity's main thread so no Unity API is called from the
    /// dialog thread.
    /// </summary>
    internal static class WindowsFolderPicker
    {
        public static FolderPickerRequest Begin(string title)
        {
            var request = new FolderPickerRequest();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var thread = new Thread(delegate()
            {
                string path = null;
                string error = null;
                IntPtr displayNameBuffer = IntPtr.Zero;
                int initializeResult = CoInitializeEx(
                    IntPtr.Zero,
                    CoInitApartmentThreaded);
                bool shouldUninitialize = initializeResult >= 0;
                try
                {
                    // BROWSEINFOW expects a caller-owned LPWSTR buffer here.
                    // StringBuilder cannot be marshalled safely as a struct field.
                    displayNameBuffer = Marshal.AllocHGlobal(MaxPath * sizeof(char));
                    Marshal.WriteInt16(displayNameBuffer, 0);
                    var info = new BrowseInfo
                    {
                        Owner = IntPtr.Zero,
                        Root = IntPtr.Zero,
                        DisplayName = displayNameBuffer,
                        Title = string.IsNullOrWhiteSpace(title)
                            ? "Choose your Umamusume installation"
                            : title,
                        Flags = BrowseInfoReturnOnlyFileSystemDirectories |
                            BrowseInfoNewDialogStyle |
                            BrowseInfoEditBox
                    };

                    IntPtr itemList = SHBrowseForFolder(ref info);
                    if (itemList != IntPtr.Zero)
                    {
                        try
                        {
                            var selectedPath = new StringBuilder(MaxPath);
                            if (SHGetPathFromIDList(itemList, selectedPath))
                            {
                                path = selectedPath.ToString();
                            }
                            else
                            {
                                error = "Windows could not read the selected folder.";
                            }
                        }
                        finally
                        {
                            CoTaskMemFree(itemList);
                        }
                    }
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                }
                finally
                {
                    if (displayNameBuffer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(displayNameBuffer);
                    }
                    if (shouldUninitialize)
                    {
                        CoUninitialize();
                    }
                    request.Complete(path, error);
                }
            });
            thread.IsBackground = true;
            thread.Name = "Uma Desktop Pet folder picker";
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
#else
            request.Complete(null, "Folder browsing is available in the Windows build.");
#endif
            return request;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const int MaxPath = 260;
        private const uint CoInitApartmentThreaded = 0x2;
        private const uint BrowseInfoReturnOnlyFileSystemDirectories = 0x1;
        private const uint BrowseInfoEditBox = 0x10;
        private const uint BrowseInfoNewDialogStyle = 0x40;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BrowseInfo
        {
            public IntPtr Owner;
            public IntPtr Root;
            public IntPtr DisplayName;
            [MarshalAs(UnmanagedType.LPWStr)] public string Title;
            public uint Flags;
            public IntPtr Callback;
            public IntPtr Parameter;
            public int Image;
        }

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr memory);

        [DllImport(
            "shell32.dll",
            EntryPoint = "SHBrowseForFolderW",
            CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        private static extern IntPtr SHBrowseForFolder(ref BrowseInfo browseInfo);

        [DllImport(
            "shell32.dll",
            EntryPoint = "SHGetPathFromIDListW",
            CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SHGetPathFromIDList(
            IntPtr itemList,
            StringBuilder path);
#endif
    }
}
