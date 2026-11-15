using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TheInternet
{
    // Native DLLs ship as raw .dll.payload files so the game's recursive managed
    // assembly loader leaves them alone. This restores filenames in a program-only
    // cache. Bytes are neither compressed nor encrypted; each file is hash checked.
    public sealed class RuntimeEntry
    {
        public string Source, Target, Sha256;
    }

    public static class RuntimeFiles
    {
        private static readonly object Gate = new object();
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string FileHash(string path) { using (var sha = SHA256.Create()) using (var input = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant(); }
        private static string Child(string root, string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0) throw new IOException("Invalid runtime file path.");
            relative = relative.Replace('\\', '/');
            foreach (var segment in relative.Split('/')) if (segment == "" || segment == "." || segment == "..") throw new IOException("Invalid runtime path segment.");
            string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Runtime path escaped its directory.");
            return full;
        }
        private static void RemoveOwned(string path, string root)
        {
            string full = Path.GetFullPath(path), prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid runtime cleanup target.");
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
        public static string Prepare(string sourceRoot, string cacheRoot, string manifest, IList<RuntimeEntry> files, Func<bool> cancelled = null)
        {
            lock (Gate)
            {
                if (files == null || files.Count < 1 || files.Count > 4096) throw new IOException("Invalid runtime manifest.");
                string key = Hash(Encoding.UTF8.GetBytes(manifest));
                string target = Child(cacheRoot, key), ready = Child(target, "ready.txt");
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                {
                    Child(sourceRoot, file.Source); Child(target, file.Target);
                    if (!names.Add(file.Target.Replace('\\', '/')) || string.Equals(file.Target, "ready.txt", StringComparison.OrdinalIgnoreCase)) throw new IOException("Duplicate runtime file.");
                    if (file.Sha256 == null || file.Sha256.Length != 64 || !System.Text.RegularExpressions.Regex.IsMatch(file.Sha256, "^[a-f0-9]{64}$")) throw new IOException("Invalid runtime checksum.");
                }
                if (!names.Contains("TheInternetBrowser.exe")) throw new IOException("Browser executable missing from runtime manifest.");
                bool valid = File.Exists(ready) && File.ReadAllText(ready) == key;
                if (valid) foreach (var file in files)
                {
                    if (cancelled != null && cancelled()) throw new OperationCanceledException();
                    string path = Child(target, file.Target);
                    if (!File.Exists(path) || FileHash(path) != file.Sha256) { valid = false; break; }
                }
                if (valid) return Child(target, "TheInternetBrowser.exe");
                Directory.CreateDirectory(cacheRoot);
                string staging = Child(cacheRoot, "stage-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                try
                {
                    foreach (var file in files)
                    {
                        if (cancelled != null && cancelled()) throw new OperationCanceledException();
                        string source = Child(sourceRoot, file.Source), destination = Child(staging, file.Target);
                        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("Runtime payload cannot be a link.");
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        File.Copy(source, destination, false);
                        if (FileHash(destination) != file.Sha256) throw new IOException("Runtime checksum mismatch: " + file.Target);
                    }
                    File.WriteAllText(Child(staging, "ready.txt"), key);
                    if (Directory.Exists(target)) RemoveOwned(target, cacheRoot);
                    Directory.Move(staging, target);
                    return Child(target, "TheInternetBrowser.exe");
                }
                finally { if (Directory.Exists(staging)) RemoveOwned(staging, cacheRoot); }
            }
        }
    }
}
