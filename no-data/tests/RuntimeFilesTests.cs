using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheInternet;

static class RuntimeFilesTests
{
    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    static void Check(bool condition) { if (!condition) throw new Exception("Runtime staging regression"); }
    static void Main(string[] args)
    {
        if (args.Length == 2)
        {
            string manifest = File.ReadAllText(Path.Combine(args[0], "runtime-files.json"));
            using var json = JsonDocument.Parse(manifest);
            var items = new List<RuntimeEntry>();
            foreach (var item in json.RootElement.GetProperty("files").EnumerateArray()) items.Add(new RuntimeEntry { Source = item.GetProperty("source").GetString(), Target = item.GetProperty("target").GetString(), Sha256 = item.GetProperty("sha256").GetString() });
            Console.WriteLine(RuntimeFiles.Prepare(args[0], args[1], manifest, items)); return;
        }
        string root = Path.Combine(Path.GetTempPath(), "TheInternet-stage-test-" + Guid.NewGuid().ToString("N"));
        string payload = Path.Combine(root, "payload"), cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(payload);
        try
        {
            File.WriteAllText(Path.Combine(payload,"TheInternetBrowser.exe"),"test program");
            File.WriteAllText(Path.Combine(payload,"ffmpeg.dll.payload"),"test native library");
            var entries = new List<RuntimeEntry> { new RuntimeEntry { Source="TheInternetBrowser.exe",Target="TheInternetBrowser.exe",Sha256=Hash("test program") },new RuntimeEntry {Source="ffmpeg.dll.payload",Target="ffmpeg.dll",Sha256=Hash("test native library") } };
            string exe=RuntimeFiles.Prepare(payload,cache,"version-one",entries);
            Check(File.ReadAllText(exe)=="test program");
            Check(File.ReadAllText(Path.Combine(Path.GetDirectoryName(exe),"ffmpeg.dll"))=="test native library");
            Check(!File.Exists(Path.Combine(payload,"ffmpeg.dll")));
            Check(RuntimeFiles.Prepare(payload,cache,"version-one",entries)==exe);
            File.WriteAllText(exe,"corrupted cache");
            Check(File.ReadAllText(RuntimeFiles.Prepare(payload,cache,"version-one",entries))=="test program");
            var bad=new List<RuntimeEntry>(entries) {new RuntimeEntry {Source="../escape",Target="escape",Sha256=Hash("x")}};
            bool rejected=false;try{RuntimeFiles.Prepare(payload,cache,"bad-path",bad);}catch(IOException){rejected=true;}Check(rejected);
            bad=new List<RuntimeEntry>(entries) {new RuntimeEntry {Source="TheInternetBrowser.exe",Target="../escape",Sha256=Hash("test program")}};
            rejected=false;try{RuntimeFiles.Prepare(payload,cache,"bad-target",bad);}catch(IOException){rejected=true;}Check(rejected);
            rejected=false;try{RuntimeFiles.Prepare(payload,cache,"cancelled",entries,()=>true);}catch(OperationCanceledException){rejected=true;}Check(rejected);
            Check(Directory.GetDirectories(cache,"stage-*").Length==0);
            File.WriteAllText(Path.Combine(payload,"ffmpeg.dll.payload"),"bad payload");
            rejected=false;try{RuntimeFiles.Prepare(payload,cache,"version-two",entries);}catch(IOException){rejected=true;}Check(rejected);
            Check(Directory.GetDirectories(cache,"stage-*").Length==0);
            Console.WriteLine("PASS: raw DLL staging, checksums, reuse, corrupt-cache repair, traversal rejection, cancellation and failure cleanup.");
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("TheInternet-stage-test-")) throw new Exception("Invalid test cleanup path");
            Directory.Delete(root,true);
        }
    }
}
