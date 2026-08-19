using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ParticleSim.UI
{
    /// <summary>
    /// Opens the native Windows "Open File" dialog so the user can pick a file
    /// from their PC. Works both in the Editor (Play Mode) and in Windows
    /// standalone builds, with no external packages.
    /// </summary>
    public static class StandaloneFileDialog
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

        private const int OFN_FILEMUSTEXIST = 0x00001000;
        private const int OFN_PATHMUSTEXIST = 0x00000800;
        private const int OFN_NOCHANGEDIR = 0x00000008;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }
#endif

        /// <summary>
        /// Shows an "Open File" dialog and returns the selected absolute path,
        /// or null if the user cancelled or the platform is unsupported.
        /// </summary>
        public static string OpenFile(string title, string filter = "JSON files\0*.json\0All files\0*.*\0\0")
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var ofn = new OpenFileName();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.lpstrFilter = filter;
            
            // Allocate a buffer for the file path
            const int maxFile = 4096;
            IntPtr buffer = Marshal.AllocHGlobal(maxFile * Marshal.SystemDefaultCharSize);
            for (int i = 0; i < maxFile * Marshal.SystemDefaultCharSize; i++) Marshal.WriteByte(buffer, i, 0);

            ofn.lpstrFile = buffer;
            ofn.nMaxFile = maxFile;
            ofn.lpstrTitle = title;
            ofn.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;

            try
            {
                if (GetOpenFileName(ofn))
                {
                    return Marshal.PtrToStringAuto(buffer);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[StandaloneFileDialog] Native dialog failed: {e.Message}");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return null;
#else
            Debug.LogWarning("[StandaloneFileDialog] Native file dialog is only implemented for Windows.");
            return null;
#endif
        }
    }
}
