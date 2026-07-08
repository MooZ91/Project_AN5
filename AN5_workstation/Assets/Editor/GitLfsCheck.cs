using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Detects meshes/textures that are still Git LFS pointer files (i.e. "git lfs pull"
// was never run after cloning), which otherwise show up as a wall of unrelated
// import errors in the Console the first time the project is opened.
[InitializeOnLoad]
public static class GitLfsCheck
{
    private const string SessionKey = "AN5_GitLfsCheckDone";
    private const string PointerHeader = "version https://git-lfs.github.com/spec/v1";

    private static readonly string[] LfsExtensions =
    {
        ".dae", ".DAE", ".stl", ".STL", ".fbx", ".FBX", ".obj",
        ".png", ".jpg", ".jpeg", ".psd", ".tga", ".exr", ".hdr", ".tiff",
        ".wav", ".mp3", ".mp4", ".mov"
    };

    static GitLfsCheck()
    {
        EditorApplication.delayCall += RunCheck;
    }

    [MenuItem("Tools/Git LFS/Check for missing LFS files")]
    private static void ManualCheck()
    {
        SessionState.SetBool(SessionKey, false);
        RunCheck();
        if (!SessionState.GetBool(SessionKey + "_Found", false))
            EditorUtility.DisplayDialog("Git LFS", "No pointer files found — all tracked assets look fully downloaded.", "OK");
    }

    private static void RunCheck()
    {
        if (SessionState.GetBool(SessionKey, false)) return;
        SessionState.SetBool(SessionKey, true);

        var pointerFiles = Directory
            .EnumerateFiles(Application.dataPath, "*.*", SearchOption.AllDirectories)
            .Where(f => LfsExtensions.Contains(Path.GetExtension(f)))
            .Where(IsLfsPointer)
            .ToList();

        SessionState.SetBool(SessionKey + "_Found", pointerFiles.Count > 0);
        if (pointerFiles.Count == 0) return;

        Debug.LogWarning($"[GitLfsCheck] {pointerFiles.Count} asset(s) are still Git LFS pointer files, not real content:\n" +
                          string.Join("\n", pointerFiles.Take(10).Select(f => "  " + f)) +
                          (pointerFiles.Count > 10 ? $"\n  ... and {pointerFiles.Count - 10} more" : ""));

        bool pull = EditorUtility.DisplayDialog(
            "Git LFS files not downloaded",
            $"{pointerFiles.Count} asset(s) (meshes/textures) look like Git LFS pointer text files instead of " +
            "real binary content. This happens when the repo was cloned/pulled without Git LFS installed, or " +
            "before running 'git lfs pull' — Unity will fail to import them and flood the Console with errors.\n\n" +
            "Run 'git lfs pull' now?",
            "Run git lfs pull", "Not now");

        if (pull) PullLfs();
    }

    private static bool IsLfsPointer(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 1024) return false; // real binaries are always bigger
            using var reader = new StreamReader(path);
            return reader.ReadLine()?.StartsWith(PointerHeader) == true;
        }
        catch
        {
            return false;
        }
    }

    private static void PullLfs()
    {
        string repoRoot = FindRepoRoot(Application.dataPath);
        if (repoRoot == null)
        {
            EditorUtility.DisplayDialog("Git LFS", "Could not find a .git directory above the project.", "OK");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "lfs pull",
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            EditorUtility.DisplayProgressBar("Git LFS", "Running 'git lfs pull'...", 0.5f);
            using var process = Process.Start(psi);
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            EditorUtility.ClearProgressBar();

            if (process.ExitCode == 0)
            {
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Git LFS", "'git lfs pull' finished. Reimporting assets...", "OK");
            }
            else
            {
                Debug.LogError($"[GitLfsCheck] git lfs pull failed:\n{stdout}\n{stderr}");
                EditorUtility.DisplayDialog("Git LFS", $"'git lfs pull' failed:\n{stderr}", "OK");
            }
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("Git LFS",
                $"Could not run 'git lfs pull'. Is Git LFS installed and in PATH?\n\n{e.Message}", "OK");
        }
    }

    private static string FindRepoRoot(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
