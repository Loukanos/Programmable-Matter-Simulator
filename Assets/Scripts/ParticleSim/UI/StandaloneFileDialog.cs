using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ParticleSim.UI
{
    public static class StandaloneFileDialog
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetSaveFileName([In, Out] OpenFileName ofn);

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

        public static string OpenFile(string title, string filter = "JSON files\0*.json\0All files\0*.*\0\0") =>
            ShowDialog(title, filter, initialFileName: null, defaultExtension: null, save: false);

        public static string SaveFile(string title, string suggestedFileName, string filter, string defaultExtension) =>
            ShowDialog(title, filter, suggestedFileName, defaultExtension, save: true);

        private static string ShowDialog(string title, string filter, string initialFileName, string defaultExtension, bool save)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var ofn = new OpenFileName();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.lpstrFilter = filter;

            const int maxFile = 4096;
            IntPtr buffer = Marshal.AllocHGlobal(maxFile * Marshal.SystemDefaultCharSize);
            for (int i = 0; i < maxFile * Marshal.SystemDefaultCharSize; i++) Marshal.WriteByte(buffer, i, 0);
            if (!string.IsNullOrEmpty(initialFileName))
            {
                char[] name = initialFileName.ToCharArray();
                Marshal.Copy(name, 0, buffer, Math.Min(name.Length, maxFile - 1));
            }

            ofn.lpstrFile = buffer;
            ofn.nMaxFile = maxFile;
            ofn.lpstrTitle = title;
            ofn.lpstrDefExt = defaultExtension;
            // No OFN_OVERWRITEPROMPT when saving, on purpose: picking an existing file means appending to it.
            ofn.Flags = save
                ? OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR
                : OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;

            try
            {
                if (save ? GetSaveFileName(ofn) : GetOpenFileName(ofn))
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
