using UnityEngine;

/// <summary>
/// Native macOS file picker via osascript. Returns selected path or empty string.
/// </summary>
public static class MacFileDialog
{
    public static string OpenVideoFileDialog()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel(
            "Select Video File", "", "mov,mp4,avi,webm,mkv");
        return path ?? "";
#elif UNITY_STANDALONE_OSX
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = "-e 'POSIX path of (choose file of type {\"mov\",\"mp4\",\"avi\",\"webm\",\"mkv\"} with prompt \"Select Video File\")'",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var proc = System.Diagnostics.Process.Start(psi))
            {
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                return proc.ExitCode == 0 ? output : "";
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MacFileDialog] Failed to open file dialog: {e.Message}");
            return "";
        }
#else
        Debug.LogWarning("[MacFileDialog] File dialog not supported on this platform.");
        return "";
#endif
    }
}
