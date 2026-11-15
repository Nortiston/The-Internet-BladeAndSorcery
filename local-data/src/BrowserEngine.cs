using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TheInternet
{
    /// <summary>
        /// Runs the bundled browser engine and talks to it over a
    /// local-only socket. This edition saves browser cookies/site data in BrowserData.
    /// </summary>
    public class BrowserEngine
    {
        public const int Width = 1280, Height = 720;
        private readonly BrowserEngine owner;
        private readonly ConcurrentDictionary<int, BrowserEngine> windows = new ConcurrentDictionary<int, BrowserEngine>();
        public int WindowId { get; private set; }
        public int TotalTabCount = 1;
        public bool Empty;
        public event Action<BrowserEngine, Vector3> OnWindowCreated;
        public event Action<int> OnWindowClosed;
        public event Action<int, Vector3> OnMoveWindow;

        public Texture2D Texture { get; private set; }
        public string Url = "", Title = "New Tab";
        public bool CanBack, CanForward, Loading, Muted, Fullscreen;
        public bool AdBlockEnabled = true;
        public bool SpatialAudioEnabled;
        public bool SpatialAudioAllowed { get; private set; }
        private bool floatingStateSent;
        public bool SingleClickMode = true;
        public string[] TabTitles = { "New Tab" };
        public int ActiveTab;
        private volatile int activeTabId;
        private volatile bool audioReceivingEnabled;
        public event Action OnAudioReset;
        public string AudioRoutingStatus = "Normal browser audio.";
        public string AudioCaptureBackend;
        public string PlaybackStatus = "";
        public bool YouTubeCompatibility;
        public bool ClearingSavedData;
        public string DataStatus = "Local Data: saves cookies/logins in BrowserData. Restart keeps them; Clear saved data signs you out.";
        public event Action<float[]> OnAudioSamples;
        public bool Running { get { return owner != null ? owner.Running : process != null && !process.HasExited; } }
        public bool Connected { get { return owner != null ? owner.Connected : client != null && client.Connected && authed; } }
        public string Error;

        public event Action<bool> OnFocusChanged;   // a text field on the page got / lost focus
        public event Action OnStateChanged;

        private string exePath;
        private volatile bool staging, stageDone;
        private string stagedPath, stageError;
        private readonly string modFolder;
        public string Status; // e.g. "Preparing The Internet (first time only)..."
        private Process process;
        private static volatile Process retiringProcess;
        private bool waitingForProfile;
        private static bool PreviousBrowserRunning() { try { return retiringProcess != null && !retiringProcess.HasExited; } catch { return false; } }
        private TcpListener listener;
        private TcpListener audioListener;
        private TcpClient client;
        private TcpClient audioClient;
        private NetworkStream stream;
        private Thread writeThread;
        private volatile bool authed, stopping;
        private string token;
        private readonly BlockingCollection<byte[]> outbox = new BlockingCollection<byte[]>(256);
        private readonly ConcurrentQueue<string> events = new ConcurrentQueue<string>();

        // frame double buffer
        private readonly object frameLock = new object();
        private byte[] frameBack;
        private int frameW, frameH;
        private bool frameNew;
        private byte[] frameFront;

        public BrowserEngine(string modFolder)
        {
            this.modFolder = modFolder;
            windows[0] = this;
        }

        private BrowserEngine(BrowserEngine owner, int id)
        {
            this.owner = owner; modFolder = owner.modFolder; WindowId = id;
            windows = owner.windows;
            Texture = new Texture2D(Width, Height, TextureFormat.BGRA32, false, false);
            Texture.wrapMode = TextureWrapMode.Clamp; Texture.filterMode = FilterMode.Bilinear;
            windows[id] = this;
        }

        private BrowserEngine GetWindow(int id, JObject state)
        {
            BrowserEngine peer;
            if (windows.TryGetValue(id, out peer)) return peer;
            peer = new BrowserEngine(this, id);
            if (OnWindowCreated != null) OnWindowCreated(peer, Position(state["windowPosition"]));
            return peer;
        }
        private static Vector3 Position(JToken value)
        {
            return value == null ? Vector3.zero : new Vector3((float?)value["x"] ?? 0, (float?)value["y"] ?? 0, (float?)value["z"] ?? 0);
        }

        /// <summary>
        /// Native libraries are raw, checksum-listed .dll.payload files in this mod.
        /// Restore their canonical filenames in a program cache on a worker thread.
        /// This avoids both a nested archive and the game's managed DLL loader.
        /// </summary>
        private bool ResolveEngine()
        {
            string source = Path.Combine(modFolder, "BrowserRuntime");
            string manifestPath = Path.Combine(source, "runtime-files.json");
            if (File.Exists(manifestPath))
            {
                if (stageDone) { if (stageError != null) { Error = stageError; Status = null; return false; } exePath = stagedPath; return true; }
                if (!staging)
                {
                    staging = true; Status = "Preparing and checking the bundled browser...";
                    new Thread(() =>
                    {
                        try
                        {
                            string manifest = File.ReadAllText(manifestPath);
                            var descriptor = JObject.Parse(manifest);
                            if ((string)descriptor["edition"] != "local-data") throw new IOException("Wrong browser edition. Install the complete Local Data package without mixing files.");
                            var entries = new System.Collections.Generic.List<RuntimeEntry>();
                            foreach (var item in (JArray)descriptor["files"]) entries.Add(new RuntimeEntry { Source = (string)item["source"], Target = (string)item["target"], Sha256 = (string)item["sha256"] });
                            string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TheInternet-BAS", "runtime");
                            stagedPath = RuntimeFiles.Prepare(source, cache, manifest, entries, () => stopping);
                        }
                        catch (Exception e) { stageError = "Could not prepare the browser: " + e.Message; }
                        stageDone = true;
                    }) { IsBackground = true, Name = "TheInternet runtime" }.Start();
                }
                return false;
            }
            Error = "Local Data browser runtime missing. Extract the complete Local Data 'The Internet' folder into Mods.";
            return false;
        }

        public bool Start()
        {
            if (Running) return true;
            stopping = false;
            Error = null;
            if (PreviousBrowserRunning()) { waitingForProfile = true; Status = "Finishing the previous browser before reopening saved data..."; return false; }
            if (!ResolveEngine()) { if (Error != null) InternetScript.Warn(Error); return false; }
            Status = null;
            try
            {
                Texture = new Texture2D(Width, Height, TextureFormat.BGRA32, false, false);
                Texture.wrapMode = TextureWrapMode.Clamp;
                Texture.filterMode = FilterMode.Bilinear;
                var clear = new Color32[Width * Height];
                for (int i = 0; i < clear.Length; i++) clear[i] = new Color32(255, 255, 255, 255);
                Texture.SetPixels32(clear); Texture.Apply(false);

                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                audioListener = new TcpListener(IPAddress.Loopback, 0);
                audioListener.Start();
                int audioPort = ((IPEndPoint)audioListener.LocalEndpoint).Port;
                token = Guid.NewGuid().ToString("N");
                string dataFolder = Path.Combine(Path.GetFullPath(modFolder), "BrowserData");
                Directory.CreateDirectory(dataFolder);

                var psi = new ProcessStartInfo(exePath)
                {
                    Arguments = "--port=" + port + " --audio-port=" + audioPort + " --token=" + token + " --parent=" + Process.GetCurrentProcess().Id +
                                " --width=" + Width + " --height=" + Height + " --fullscreen-height=" + (int)BrowserView.H + " --fps=30 --data-dir=\"" + dataFolder + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };
                process = Process.Start(psi);
                InternetScript.Log("Browser engine started (pid " + process.Id + ", port " + port + ")");

                var accept = new Thread(AcceptLoop) { IsBackground = true, Name = "TheInternet accept" };
                accept.Start();
                new Thread(AcceptAudioLoop) { IsBackground = true, Name = "TheInternet audio" }.Start();
                return true;
            }
            catch (Exception e)
            {
                Error = "Could not start browser: " + e.Message;
                InternetScript.Warn(Error);
                Stop();
                return false;
            }
        }

        private void AcceptLoop()
        {
            try
            {
                var c = listener.AcceptTcpClient();
                c.NoDelay = true;
                c.ReceiveBufferSize = 4 * 1024 * 1024;
                client = c;
                stream = c.GetStream();
                listener.Stop(); // one connection only
                writeThread = new Thread(WriteLoop) { IsBackground = true, Name = "TheInternet write" };
                writeThread.Start();
                ReadLoop();
            }
            catch (Exception e) { if (!stopping) InternetScript.Warn("Browser connection ended: " + e.Message); }
        }

        private static void ReadExact(Stream s, byte[] buf, int count, int offset = 0)
        {
            int off = offset, end = offset + count;
            while (off < end)
            {
                int n = s.Read(buf, off, end - off);
                if (n <= 0) throw new IOException("closed");
                off += n;
            }
        }

        private void AcceptAudioLoop()
        {
            try
            {
                var connection = audioListener.AcceptTcpClient();
                audioClient = connection; connection.NoDelay = true;
                connection.ReceiveBufferSize = 64 * 1024;
                audioListener.Stop();
                var input = connection.GetStream();
                var header = new byte[5];
                var scratch = new byte[32784];
                ReadExact(input, header, 5);
                int length = BitConverter.ToInt32(header, 1);
                if (header[0] != 1 || length < 1 || length > 1024) throw new IOException("Bad audio handshake");
                ReadExact(input, scratch, length);
                var hello = JObject.Parse(Encoding.UTF8.GetString(scratch, 0, length));
                if ((string)hello["t"] != "hello" || (string)hello["token"] != token) throw new IOException("Unauthorised audio connection");
                while (!stopping)
                {
                    ReadExact(input, header, 5); length = BitConverter.ToInt32(header, 1);
                    if (header[0] != 3 || length < 24 || length > scratch.Length) throw new IOException("Bad audio packet");
                    ReadExact(input, scratch, length);
                    HandleAudioPacket(scratch, length);
                }
            }
            catch (Exception e) { if (!stopping) InternetScript.Warn("Audio connection ended: " + e.Message); }
        }

        private void HandleAudioPacket(byte[] packet, int length)
        {
            BrowserEngine target;
            if (length < 24 || length > 32784 || (length - 16) % 8 != 0 ||
                BitConverter.ToInt32(packet, 0) != 48000 || BitConverter.ToInt32(packet, 4) != 2 ||
                !windows.TryGetValue(BitConverter.ToInt32(packet, 8), out target) || !target.audioReceivingEnabled ||
                BitConverter.ToInt32(packet, 12) != target.activeTabId) return;
            var receive = target.OnAudioSamples;
            if (receive == null) return;
            var samples = new float[(length - 16) / 4];
            Buffer.BlockCopy(packet, 16, samples, 0, length - 16);
            // Subscriber only writes the locked PCM buffer: no Unity APIs here.
            // Audio continues during expensive render/main-thread frames.
            receive(samples);
        }

        private void ReadLoop()
        {
            var head = new byte[5];
            byte[] scratch = new byte[64 * 1024];
            while (!stopping)
            {
                ReadExact(stream, head, 5);
                int type = head[0];
                int len = BitConverter.ToInt32(head, 1);
                if (len < 0 || len > 64 * 1024 * 1024) throw new IOException("bad length");
                if (type == 2)
                {
                    if (len < 12) throw new IOException("Bad image packet");
                    var metadata = new byte[12]; ReadExact(stream, metadata, 12);
                    BrowserEngine target = null;
                    bool known = authed && windows.TryGetValue(BitConverter.ToInt32(metadata, 8), out target);
                    if (!known) { var discard = new byte[len - 12]; ReadExact(stream, discard, discard.Length); continue; }
                    byte[] buf;
                    lock (target.frameLock) { buf = target.frameBack; target.frameBack = null; }
                    if (buf == null || buf.Length != len) buf = new byte[len];
                    Buffer.BlockCopy(metadata, 0, buf, 0, 12); ReadExact(stream, buf, len - 12, 12);
                    int w = BitConverter.ToInt32(buf, 0), h = BitConverter.ToInt32(buf, 4);
                    lock (target.frameLock)
                    {
                        var old = target.frameFront;
                        target.frameFront = buf; target.frameW = w; target.frameH = h; target.frameNew = true;
                        if (old != null) target.frameBack = old;
                    }
                }
                else if (type == 3)
                {
                    if (scratch.Length < len) scratch = new byte[len];
                    ReadExact(stream, scratch, len);
                    if (authed) HandleAudioPacket(scratch, len);
                }
                else
                {
                    if (scratch.Length < len) scratch = new byte[len];
                    ReadExact(stream, scratch, len);
                    string json = Encoding.UTF8.GetString(scratch, 0, len);
                    if (!authed)
                    {
                        // first message must carry our one-time token
                        try
                        {
                            var o = JObject.Parse(json);
                            if ((string)o["t"] == "hello" && (string)o["token"] == token) { authed = true; InternetScript.Log("Browser engine connected"); continue; }
                        }
                        catch { }
                        throw new IOException("unauthorised connection");
                    }
                    events.Enqueue(json);
                }
            }
        }

        private void WriteLoop()
        {
            try
            {
                foreach (var msg in outbox.GetConsumingEnumerable())
                {
                    if (stopping) break;
                    stream.Write(msg, 0, msg.Length);
                }
            }
            catch (Exception e) { if (!stopping) InternetScript.Warn("Browser write failed: " + e.Message); }
        }

        // ---------------- commands ----------------

        public void Send(JObject cmd)
        {
            if (cmd["windowId"] == null) cmd["windowId"] = WindowId;
            if (owner != null) { owner.Send(cmd); return; }
            if (!Connected) return;
            var payload = Encoding.UTF8.GetBytes(cmd.ToString(Newtonsoft.Json.Formatting.None));
            var msg = new byte[5 + payload.Length];
            msg[0] = 1;
            Array.Copy(BitConverter.GetBytes(payload.Length), 0, msg, 1, 4);
            Array.Copy(payload, 0, msg, 5, payload.Length);
            outbox.TryAdd(msg);
        }

        public void Navigate(string text) { Send(new JObject { ["t"] = "nav", ["url"] = text }); }
        public void Back() { Send(new JObject { ["t"] = "back" }); }
        public void Forward() { Send(new JObject { ["t"] = "fwd" }); }
        public void Reload() { Send(new JObject { ["t"] = Loading ? "stop" : "reload" }); }
        public void Home() { Send(new JObject { ["t"] = "home" }); }
        public void ClearSavedData() { Send(new JObject { ["t"] = "clearData" }); }
        public void SetMuted(bool m) { Send(new JObject { ["t"] = "mute", ["v"] = m }); }
        public void SetAdBlockEnabled(bool enabled) { Send(new JObject { ["t"] = "adblock", ["v"] = enabled }); }
        private void ClearScreenAudio()
        {
            SpatialAudioEnabled = false;
            audioReceivingEnabled = false;
            if (OnAudioReset != null) OnAudioReset();
        }
        public void SetSpatialAudioAllowed(bool floating)
        {
            SpatialAudioAllowed = floating;
            if (!floating) ClearScreenAudio();
            floatingStateSent = false;
            if (Connected)
            {
                Send(new JObject { ["t"] = "floating", ["v"] = floating });
                floatingStateSent = true;
            }
        }
        public void SetSpatialAudioEnabled(bool enabled)
        {
            enabled = enabled && SpatialAudioAllowed;
            if (!enabled) ClearScreenAudio();
            Send(new JObject { ["t"] = "spatialAudio", ["v"] = enabled });
        }
        public void NewTab() { Send(new JObject { ["t"] = "newTab" }); }
        public void SelectTab(int index) { Send(new JObject { ["t"] = "selectTab", ["index"] = index }); }
        public void CloseTab(int index) { Send(new JObject { ["t"] = "closeTab", ["index"] = index }); }
        public void DetachTab(int index, Vector3 position) { Send(new JObject { ["t"] = "detachTab", ["index"] = index, ["position"] = new JObject { ["x"] = position.x, ["y"] = position.y, ["z"] = position.z } }); }
        public void MoveTab(int index, int target) { Send(new JObject { ["t"] = "moveTab", ["index"] = index, ["targetWindowId"] = target }); }
        public void MergeWindows(int preferred = 0) { Send(new JObject { ["t"] = "mergeWindows", ["preferredWindowId"] = preferred }); }
        public void Mouse(string kind, Vector2 px, int clicks = 1) { Send(new JObject { ["t"] = "mouse", ["kind"] = kind, ["x"] = px.x, ["y"] = px.y, ["clicks"] = clicks }); }
        public void Wheel(Vector2 px, float dx, float dy) { Send(new JObject { ["t"] = "wheel", ["x"] = px.x, ["y"] = px.y, ["dx"] = dx, ["dy"] = dy }); }
        public void TypeText(string s) { Send(new JObject { ["t"] = "text", ["s"] = s }); }
        public void Key(string key, bool shift = false) { Send(new JObject { ["t"] = "key", ["key"] = key, ["shift"] = shift }); }

        // ---------------- main thread ----------------

        public void Update()
        {
            if (owner != null) return;
            if (waitingForProfile && !PreviousBrowserRunning()) { waitingForProfile = false; Start(); }
            if (staging && stageDone && process == null && !stopping)
            {
                staging = false;
                if (stageError != null) { Error = stageError; Status = null; InternetScript.Warn(Error); }
                else Start();
            }
            // Pop-out can happen while the helper starts. Synchronize the current
            // docking state once its authenticated connection becomes available.
            foreach (var item in windows.Values)
            {
                if (Connected && !item.floatingStateSent)
                {
                    item.Send(new JObject { ["t"] = "floating", ["v"] = item.SpatialAudioAllowed });
                    item.floatingStateSent = true;
                }
            }
            string json;
            while (events.TryDequeue(out json))
            {
                try
                {
                    var o = JObject.Parse(json);
                    switch ((string)o["t"])
                    {
                        case "state":
                            var peer = GetWindow((int?)o["windowId"] ?? 0, o);
                            int total = (int?)o["totalTabs"] ?? 1;
                            foreach (var item in windows.Values) item.TotalTabCount = total;
                            peer.ApplyState(o);
                            break;
                        case "focus":
                            BrowserEngine focus;
                            if (windows.TryGetValue((int?)o["windowId"] ?? 0, out focus) && focus.OnFocusChanged != null) focus.OnFocusChanged((bool?)o["editable"] ?? false);
                            break;
                        case "windowClosed":
                            int closed = (int?)o["windowId"] ?? 0;
                            BrowserEngine removed;
                            if (closed != 0 && windows.TryRemove(closed, out removed))
                            {
                                removed.ClearScreenAudio();
                                if (OnWindowClosed != null) OnWindowClosed(closed);
                                if (removed.Texture != null) UnityEngine.Object.Destroy(removed.Texture);
                            }
                            break;
                        case "moveWindow":
                            if (OnMoveWindow != null) OnMoveWindow((int?)o["windowId"] ?? 0, Position(o["position"]));
                            break;
                    }
                }
                catch (Exception e) { InternetScript.Warn("Bad browser event: " + e.Message); }
            }

            foreach (var item in windows.Values) item.UploadFrame();

            if (process != null && process.HasExited && !stopping)
            {
                int code = SafeExit();
                Error = code == 2 ? "Saved browser data is already in use. Close the other browser/game instance, then restart Internet." : code == 3 ? "Cannot write BrowserData inside the mod folder. Use a writable installation folder." : "The browser stopped. Use Restart Internet in settings.";
                InternetScript.Warn("Browser engine exited (code " + SafeExit() + ")");
                Cleanup();
            }
        }

        private void UploadFrame()
        {
            byte[] buf = null; int w = 0, h = 0;
            lock (frameLock)
            {
                if (frameNew) { buf = frameFront; frameFront = null; w = frameW; h = frameH; frameNew = false; }
            }
            if (buf != null && Texture != null)
            {
                try
                {
                    if (Texture.width != w || Texture.height != h) Texture.Reinitialize(w, h);
                    var data = Texture.GetRawTextureData<byte>();
                    int bytes = w * h * 4;
                    if (data.Length == bytes && buf.Length >= 12 + bytes)
                    {
                        Unity.Collections.NativeArray<byte>.Copy(buf, 12, data, 0, bytes);
                        Texture.Apply(false);
                    }
                }
                catch (Exception e) { InternetScript.Warn("Frame upload failed: " + e.Message); }
                finally { lock (frameLock) { if (frameBack == null) frameBack = buf; } }
            }

        }

        private void ApplyState(JObject o)
        {
                            Empty = (bool?)o["empty"] ?? false;
                            Url = (string)o["url"] ?? ""; Title = (string)o["title"] ?? "";
                            CanBack = (bool?)o["canBack"] ?? false; CanForward = (bool?)o["canFwd"] ?? false;
                            Loading = (bool?)o["loading"] ?? false; Muted = (bool?)o["muted"] ?? false;
                            Fullscreen = (bool?)o["fullscreen"] ?? false;
                            AdBlockEnabled = (bool?)o["adBlockEnabled"] ?? true;
                            PlaybackStatus = (string)o["playbackStatus"] ?? "";
                            YouTubeCompatibility = (bool?)o["youtubeCompatibility"] ?? false;
                            ClearingSavedData = (bool?)o["clearingData"] ?? false;
                            DataStatus = (string)o["dataStatus"] ?? DataStatus;
                            SpatialAudioEnabled = SpatialAudioAllowed && ((bool?)o["spatialAudioEnabled"] ?? false);
                            AudioRoutingStatus = (string)o["audioRoutingStatus"] ?? "";
                            AudioCaptureBackend = (string)o["audioBackend"] ?? "";
                            int tabId = (int?)o["activeTabId"] ?? 0;
                            if (tabId != activeTabId)
                            {
                                audioReceivingEnabled = false;
                                activeTabId = tabId;
                                if (OnAudioReset != null) OnAudioReset();
                            }
                            audioReceivingEnabled = SpatialAudioEnabled;
                            var tabs = o["tabs"] as JArray;
                            if (tabs != null)
                            {
                                TabTitles = new string[Math.Min(10, tabs.Count)];
                                for (int i = 0; i < TabTitles.Length; i++) TabTitles[i] = (string)tabs[i]["title"] ?? "New Tab";
                                ActiveTab = Math.Max(0, Math.Min(TabTitles.Length - 1, (int?)o["activeTab"] ?? 0));
                            }
                            if (OnStateChanged != null) OnStateChanged();
        }

        private int SafeExit() { try { return process.ExitCode; } catch { return -1; } }

        public void Stop()
        {
            if (owner != null) { ClearScreenAudio(); return; }
            stopping = true;
            audioReceivingEnabled = false;
            try { if (Connected) { var q = Encoding.UTF8.GetBytes("{\"t\":\"quit\"}"); var m = new byte[5 + q.Length]; m[0] = 1; Array.Copy(BitConverter.GetBytes(q.Length), 0, m, 1, 4); Array.Copy(q, 0, m, 5, q.Length); stream.Write(m, 0, m.Length); stream.Flush(); } } catch { }
            var p = process;
            if (p != null) retiringProcess = p;
            Cleanup();
            foreach(var item in windows.Values)if(item.Texture!=null){UnityEngine.Object.Destroy(item.Texture);item.Texture=null;}
            if (p != null)
            {
                // give it a moment to wipe its temporary folder, then make sure it is gone
                var t = new Thread(() => { try { if (!p.WaitForExit(3000)) { p.Kill(); p.WaitForExit(2000); } if (p.HasExited && ReferenceEquals(retiringProcess, p)) retiringProcess = null; } catch { } }) { IsBackground = true };
                t.Start();
            }
        }

        private void Cleanup()
        {
            stopping = true;
            foreach (var item in windows.Values) item.audioReceivingEnabled = false;
            try { outbox.CompleteAdding(); } catch { }
            try { if (stream != null) stream.Close(); } catch { }
            try { if (client != null) client.Close(); } catch { }
            try { if (listener != null) listener.Stop(); } catch { }
            try { if (audioClient != null) audioClient.Close(); } catch { }
            try { if (audioListener != null) audioListener.Stop(); } catch { }
            audioClient = null; audioListener = null;
            stream = null; client = null; listener = null; process = null; authed = false;
            floatingStateSent = false;
        }
    }
}
