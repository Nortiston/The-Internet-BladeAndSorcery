using System;
using System.Collections.Generic;
using System.Reflection;
using Pointer = ThunderRoad.Pointer;
using ThunderRoad;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheInternet
{
    /// <summary>
    /// The Internet Local Data edition: retains website cookies/site storage in BrowserData.
    /// Book &gt; Mods &gt; The Internet (Local Data) opens it; Pop Out floats it in the world.
    /// </summary>
    public class InternetScript : ThunderScript
    {
        public const string Tag = "[The Internet]";
        public static InternetScript Instance;
        public static void Log(string m) { Debug.Log(Tag + " " + m); }
        public static void Warn(string m) { Debug.LogWarning(Tag + " " + m); }

        // Keeps The Internet visible in the Mods list when RemoveBlatantModOptions is installed
        // (that mod hides mods without options). Has no effect and is never saved.
        [@ModOption("Book menu compatibility", "Keeps The Internet visible with RemoveBlatantModOptions. This compatibility marker has no effect on the browser; either value is safe.")]
        [ModOptionDontSave]
        public static bool InternetMenuCompatibility;

        private ModManager.ModData mod;
        private BrowserEngine engine;
        private BrowserView view;
        private KeyboardPanel keyboard;
        private BrowserAudioOutput browserAudio;
        private WorldPanel window;          // popped-out browser
        private UIMenuContentArea bookArea;
        private RectTransform bookHolder;   // where the browser sits in the book
        private RectTransform bookNotice;   // "popped out" message in the book
        private RectTransform bookSettingsHost;
        private bool bookSettingsOpen;
        private bool resetInternetPending;
        private readonly SortedDictionary<int,FloatingBrowser> extraWindows=new SortedDictionary<int,FloatingBrowser>();
        private int selectedBookWindow;
        private TMP_Text bookWindowLabel;
        private bool floating;
        private readonly WindowSettings windowSettings = new WindowSettings();
        private bool pointerRangeOverridden;
        private float originalPointerRange, appliedPointerRange;
        private bool keyboardAuto;          // opened because a page text field was focused
        private AddressTarget addressTarget;
        private PageTarget pageTarget;

        public override void ScriptLoaded(ModManager.ModData modData)
        {
            base.ScriptLoaded(modData);
            mod = modData;
            Instance = this;
            Log("PCVR browser mod loaded.");
        }

        public override void ScriptEnable()
        {
            base.ScriptEnable();
            UIModsMenu.OnModMenuOpened += OnMenuOpened;
            Application.quitting += Shutdown;
        }

        public override void ScriptDisable()
        {
            UIModsMenu.OnModMenuOpened -= OnMenuOpened;
            Application.quitting -= Shutdown;
            Shutdown();
            base.ScriptDisable();
        }

        public override void ScriptUnload() { Shutdown(); base.ScriptUnload(); }

        private void Shutdown()
        {
            foreach(var extra in extraWindows.Values)extra.Destroy();extraWindows.Clear();
            RestorePointerRange();
            if (browserAudio != null)
            {
                if (engine != null) engine.OnAudioSamples -= browserAudio.Enqueue;
                if (engine != null) engine.OnAudioReset -= browserAudio.ResetAudio;
                UnityEngine.Object.Destroy(browserAudio.gameObject); browserAudio = null;
            }
            if (engine != null) { engine.Stop(); engine = null; }
            if (view != null) { view.Destroy(); view = null; }
            if (bookHolder != null) UnityEngine.Object.Destroy(bookHolder.gameObject);
            bookHolder = null; bookNotice = null; bookSettingsHost = null; bookArea = null; bookSettingsOpen = false;
            bookWindowLabel=null;selectedBookWindow=0;
            if (window != null) { window.Destroy(); window = null; }
            if (keyboard != null) { keyboard.panel.Destroy(); keyboard = null; }
            floating = false;
        }

        private bool IsMine(UIModsMenu.ModMenu m)
        {
            return m != null && m.modData != null && mod != null && (m.modData == mod || m.modData.Name == mod.Name);
        }

        // ---------------- setup ----------------

        private void EnsureEngine()
        {
            if (engine != null && (engine.Running || engine.Error != null || engine.Status != null)) return;
            if (engine != null)
            {
                foreach(var extra in extraWindows.Values)extra.Destroy();extraWindows.Clear();
                if(browserAudio!=null){engine.OnAudioSamples-=browserAudio.Enqueue;engine.OnAudioReset-=browserAudio.ResetAudio;UnityEngine.Object.Destroy(browserAudio.gameObject);browserAudio=null;}
                engine.Stop();selectedBookWindow=0;
            }
            engine = new BrowserEngine(mod.fullPath);
            engine.OnWindowCreated += CreateFloatingWindow;
            engine.OnWindowClosed += CloseFloatingWindow;
            engine.OnMoveWindow += MoveFloatingWindow;
            engine.OnFocusChanged += OnPageFocus;
            engine.Start();
            if (view != null) Rebind();
        }

        private void Rebind()
        {
            // the view keeps a reference to the engine; rebuild it when the engine is replaced
            var parent = view.root.parent;
            view.Destroy();
            view = null;
            BuildView(parent);
        }

        private void BuildView(Transform parent)
        {
            view = new BrowserView(engine, parent, windowSettings);
            view.root.gameObject.AddComponent<PointerRangeFilter>().settings = windowSettings;
            view.onPopOut = PopOut;
            view.onDock = Dock;
            view.onClose = Dock;
            view.onBigger = () => Resize(1.15f);
            view.onSmaller = () => Resize(1f / 1.15f);
            view.onToggleKeyboard = ToggleKeyboard;
            view.onEditAddress = EditAddress;
            view.onSettingsClosed = () => bookSettingsOpen = false;
            view.onResetSize = ResetScreenSize;
            view.onResetInternet = () => resetInternetPending = true;
            view.onClearSavedData = () => { Dock(); engine.ClearSavedData(); };
            view.onFocusWindow = () => FocusWindow(view);
            addressTarget = new AddressTarget(view, this);
            pageTarget = new PageTarget(engine);
            view.SetFloating(floating);
            if (floating && window != null) { view.root.SetAsFirstSibling(); AttachDrag(); }
        }

        private void OnMenuOpened(UIModsMenu.ModMenu m)
        {
            if (!IsMine(m)) return;
            try
            {
                EnsureEngine();
                if (bookArea != m.contentArea || bookHolder == null) BuildBookPage(m.contentArea);
                HideDefaultContent();
                RefreshBookPage();
            }
            catch (Exception e) { Warn("Opening the book page failed: " + e); }
        }

        private void BuildBookPage(UIMenuContentArea area)
        {
            bookArea = area;
            bookHolder = Ui.Rect(area.transform, "TheInternet Page", 0, 0, 1, 1);
            FitToPaper(area, bookHolder);
            var notice = Ui.Rect(bookHolder, "PoppedOut", 0, 0, 1, 1);
            bookNotice = notice;
            var heading = Ui.Rect(notice, "Heading", 0.08f, 0.83f, 0.92f, 0.97f);
            bookWindowLabel=Ui.Text(heading, "Text", "Window 1", 32, Ui.Ink, TMPro.TextAlignmentOptions.Center, Ui.bookFont);
            var previous=Ui.Rect(notice,"Previous window",.02f,.85f,.14f,.96f);
            var next=Ui.Rect(notice,"Next window",.86f,.85f,.98f,.96f);
            BookWindowButton(previous,"<",()=>CycleWindow(-1));BookWindowButton(next,">",()=>CycleWindow(1));
            var back = Ui.Rect(notice, "Return", 0.08f, 0.72f, 0.48f, 0.81f);
            var bg = Ui.Box(back, Ui.Parchment, 20, 0, 20f / 14f); bg.raycastTarget = true;
            Ui.Box(Ui.Rect(back, "Edge", 0, 0, 1, 1), Ui.Ink, 20, 3, 20f / 14f);
            Ui.Text(back, "Label", "Return to Book", 30, Ui.Ink, TMPro.TextAlignmentOptions.Center, Ui.bookFont);
            var c = back.gameObject.AddComponent<Clickable>();
            c.background = bg; c.normal = Ui.Parchment; c.hover = Ui.KeyHover; c.pressed = Ui.KeyDown;
            c.onClick = Dock;
            var settingsButton = Ui.Rect(notice, "Settings", 0.52f, 0.72f, 0.92f, 0.81f);
            var settingsBg = Ui.Box(settingsButton, Ui.Parchment, 20, 0, 20f / 14f); settingsBg.raycastTarget = true;
            Ui.Box(Ui.Rect(settingsButton, "Edge", 0, 0, 1, 1), Ui.Ink, 20, 3, 20f / 14f);
            Ui.Text(settingsButton, "Label", "Browser Settings", 30, Ui.Ink, TMPro.TextAlignmentOptions.Center, Ui.bookFont);
            var settingsClick = settingsButton.gameObject.AddComponent<Clickable>();
            settingsClick.background = settingsBg; settingsClick.normal = Ui.Parchment; settingsClick.hover = Ui.KeyHover; settingsClick.pressed = Ui.KeyDown;
            settingsClick.onClick = () => { bookSettingsOpen = !bookSettingsOpen; RefreshBookPage(); };
            bookSettingsHost = Ui.Rect(notice, "Browser Settings Host", 0.04f, 0.02f, 0.96f, 0.68f);

            if (view == null) BuildView(bookHolder);
            else if (!floating) Reparent(view.root, bookHolder);
        }

        private void RefreshBookPage()
        {
            if (bookNotice != null) bookNotice.gameObject.SetActive(floating);
            var choices=BookViews();var selected=choices.Find(v=>v.Engine.WindowId==selectedBookWindow);
            if(selected==null&&choices.Count>0){selected=choices[0];selectedBookWindow=selected.Engine.WindowId;}
            if(bookWindowLabel!=null)bookWindowLabel.text="Window "+(selected==null?1:choices.IndexOf(selected)+1)+" of "+Mathf.Max(1,choices.Count);
            if(floating&&bookSettingsHost!=null)
                foreach(var candidate in choices)candidate.ShowBookSettings(bookSettingsHost,bookSettingsOpen&&candidate==selected);
            if (view != null && !floating) { view.root.gameObject.SetActive(true); FitDocked(); }
        }

        private List<BrowserView> BookViews(){var list=new List<BrowserView>();if(view!=null&&!engine.Empty)list.Add(view);foreach(var extra in extraWindows.Values)list.Add(extra.view);return list;}
        private void CycleWindow(int step){var choices=BookViews();if(choices.Count==0)return;int index=choices.FindIndex(v=>v.Engine.WindowId==selectedBookWindow);selectedBookWindow=choices[(index+step+choices.Count)%choices.Count].Engine.WindowId;RefreshBookPage();}
        private void FocusWindow(BrowserView target){selectedBookWindow=target.Engine.WindowId;if(bookSettingsOpen)RefreshBookPage();}
        private static void BookWindowButton(RectTransform rect,string text,Action action)
        {
            var bg=Ui.Box(rect,Ui.Parchment,12);bg.raycastTarget=true;Ui.Text(rect,"Label",text,30,Ui.Ink,TextAlignmentOptions.Center,Ui.bookFont);
            var click=rect.gameObject.AddComponent<Clickable>();click.background=bg;click.normal=Ui.Parchment;click.hover=Ui.KeyHover;click.pressed=Ui.KeyDown;click.onClick=action;
        }
        private void CreateFloatingWindow(BrowserEngine peer,Vector3 position)
        {
            if(!floating&&view!=null)PopOut();
            var extra=new FloatingBrowser(peer,windowSettings,position);extraWindows[peer.WindowId]=extra;
            extra.view.onDock=()=>DockAll(peer.WindowId);extra.view.onClose=()=>DockAll(peer.WindowId);
            extra.view.onResetInternet=()=>resetInternetPending=true;
            extra.view.onClearSavedData=()=>{Dock();engine.ClearSavedData();};
            extra.view.onToggleKeyboard=()=>ToggleKeyboardFor(extra.view,peer);
            extra.view.onEditAddress=()=>EditAddressFor(extra.view);
            extra.view.onSettingsClosed=()=>bookSettingsOpen=false;
            extra.view.onFocusWindow=()=>FocusWindow(extra.view);
            peer.OnFocusChanged+=editable=>OnPageFocusFor(extra.view,peer,editable);
            selectedBookWindow=peer.WindowId;RefreshBookPage();
        }
        private void CloseFloatingWindow(int id){FloatingBrowser extra;if(extraWindows.TryGetValue(id,out extra)){extraWindows.Remove(id);extra.Destroy();}RefreshBookPage();}
        private void MoveFloatingWindow(int id,Vector3 position)
        {
            WorldPanel target;
            if(id==0){if(!floating)PopOut();target=window;}else{FloatingBrowser extra;if(!extraWindows.TryGetValue(id,out extra))return;target=extra.panel;}
            if(target==null)return;target.rect.position=position;var head=WorldPanel.Head();if(head!=null)target.rect.rotation=PanelInteraction.Upright(position-head.position);target.interaction.CaptureOffset();
        }

        private void FitDocked()
        {
            if (view == null || bookHolder == null || floating) return;
            var r = bookHolder.rect;
            if (r.width < 1f || r.height < 1f) return;
            float s = Mathf.Min(r.width / BrowserView.W, r.height / BrowserView.H) * 0.98f;
            view.root.localScale = new Vector3(s, s, s);
            view.root.anchoredPosition3D = new Vector3(0, 0, -1f);
        }

        private static void Reparent(RectTransform rt, Transform parent)
        {
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition3D = Vector3.zero;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            SetLayer(rt, parent.gameObject.layer);
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        // ---------------- pop out / dock ----------------

        private void PopOut()
        {
            if (view == null) return;
            if (window == null)
            {
                window = new WorldPanel("TheInternet Window", new Vector2(BrowserView.W, BrowserView.H), WindowSettings.DefaultWidth);
                window.PlaceInFront(1.1f, 0.1f);
                window.interaction.settings = windowSettings;
                window.interaction.pointerSettings = windowSettings;
                window.content.gameObject.AddComponent<PointerRangeFilter>().settings = windowSettings;
                AddResizeHandles();
            }
            window.SetVisible(true);
            window.PlaceInFront(1.1f, 0.1f);
            Reparent(view.root, window.content);
            view.RefreshTabLabels();
            view.root.SetAsFirstSibling();
            floating = true;
            view.SetFloating(true);
            AttachDrag();
            RefreshBookPage();
            if (keyboard != null && keyboard.Visible) PlaceKeyboard();
            Log("Popped out");
        }

        private void AttachDrag()
        {
            var d = view.tabStrip.GetComponent<DragHandle>() ?? view.tabStrip.gameObject.AddComponent<DragHandle>();
            d.target = window.go.transform;
            d.enabled = true;
        }

        private void AddResizeHandles()
        {
            foreach (float x in new[] { -1f })
                foreach (float y in new[] { 1f })
                {
                    var rt = Ui.Rect(window.content, "Resize corner", (x + 1) / 2, (y + 1) / 2, (x + 1) / 2, (y + 1) / 2);
                    rt.pivot = new Vector2((x + 1) / 2, (y + 1) / 2);
                    rt.sizeDelta = new Vector2(32, 32);
                    Ui.Box(rt, Ui.Accent, 12).raycastTarget = true;
                    var handle = rt.gameObject.AddComponent<ResizeHandle>();
                    handle.panel = window;
                    handle.corner = new Vector2(x, y);
                    rt.gameObject.AddComponent<ProximityReveal>().settings = windowSettings;
                }
        }

        private void Dock()
        { DockAll(0); }
        private void DockAll(int preferredWindow)
        {
            if (view == null) return;
            foreach(var extra in extraWindows.Values)extra.engine.SetSpatialAudioAllowed(false);
            engine.MergeWindows(preferredWindow);
            var d = view.tabStrip.GetComponent<DragHandle>();
            if (d != null) d.enabled = false;
            floating = false;
            bookSettingsOpen = false;
            view.SetFloating(false);
            if (bookHolder != null) Reparent(view.root, bookHolder);
            view.RefreshTabLabels();
            if (window != null) window.SetVisible(false);
            RefreshBookPage();
            if (keyboard != null && keyboard.Visible) keyboard.Hide();
            Log("Docked in book");
        }

        private void Resize(float factor)
        {
            if (window == null) return;
            float width = Mathf.Max(0.05f, window.metresWide * factor);
            if (float.IsInfinity(width) || float.IsNaN(width)) return;
            window.metresWide = width;
            window.ApplyScale();
        }

        private void ResetScreenSize()
        {
            if (window != null) { window.metresWide = WindowSettings.DefaultWidth; window.ApplyScale(); }
            windowSettings.cursorScale = 1f;
            if (!floating) FitDocked();
        }

        // ---------------- keyboard ----------------

        private KeyboardPanel Keyboard()
        {
            if (keyboard == null)
            {
                keyboard = new KeyboardPanel();
                keyboard.panel.interaction.pointerSettings = windowSettings;
                keyboard.panel.content.gameObject.AddComponent<PointerRangeFilter>().settings = windowSettings;
            }
            return keyboard;
        }

        private void ShowKeyboard(IKeyTarget t)
        {
            var kb = Keyboard();
            bool wasHidden = !kb.Visible;
            kb.Show(t);
            if (wasHidden) PlaceKeyboard();
        }

        private void PlaceKeyboard()
        {
            var kb = Keyboard();
            kb.panel.PlaceInFront(0.65f, 0.28f, 20f);
        }

        private void ToggleKeyboard()
        { ToggleKeyboardFor(view,engine); }
        private void ToggleKeyboardFor(BrowserView target,BrowserEngine source)
        {
            var kb = Keyboard();
            if (kb.Visible) { HideKeyboard(); return; }
            keyboardAuto = false;
            pageTarget=new PageTarget(source);addressTarget=new AddressTarget(target,this);
            kb.panel.interaction.pointerSettings=target.Settings;kb.panel.content.GetComponent<PointerRangeFilter>().settings=target.Settings;
            ShowKeyboard(target.EditingAddress ? (IKeyTarget)addressTarget : pageTarget);
        }

        public void HideKeyboard()
        {
            if (keyboard != null) keyboard.Hide();
            if (view != null && view.EditingAddress) view.EndAddressEdit();
            foreach(var extra in extraWindows.Values)if(extra.view.EditingAddress)extra.view.EndAddressEdit();
        }

        private void EditAddress()
        { EditAddressFor(view); }
        private void EditAddressFor(BrowserView target)
        {
            target.BeginAddressEdit();addressTarget=new AddressTarget(target,this);
            Keyboard().panel.interaction.pointerSettings=target.Settings;
            keyboard.panel.content.GetComponent<PointerRangeFilter>().settings=target.Settings;
            keyboardAuto = false;
            ShowKeyboard(addressTarget);
        }

        private void OnPageFocus(bool editable)
        { OnPageFocusFor(view,engine,editable); }
        private void OnPageFocusFor(BrowserView target,BrowserEngine source,bool editable)
        {
            if (target == null) return;
            if (editable)
            {
                FocusWindow(target);if (target.EditingAddress) target.EndAddressEdit();
                pageTarget=new PageTarget(source);
                Keyboard().panel.interaction.pointerSettings=target.Settings;
                keyboard.panel.content.GetComponent<PointerRangeFilter>().settings=target.Settings;
                if (keyboard == null || !keyboard.Visible) keyboardAuto = true;
                ShowKeyboard(pageTarget);
            }
            else if (keyboardAuto && keyboard != null && keyboard.Visible && keyboard.target is PageTarget && ((PageTarget)keyboard.target).Engine==source)
            {
                keyboard.Hide();
                keyboardAuto = false;
            }
        }

        // ---------------- update ----------------

        private bool PointingAt(RectTransform bounds)
        {
            Vector2 point;
            return bounds != null && PointerRangeFilter.TryPoint(bounds, windowSettings.EffectiveClickDistance, out point) && bounds.rect.Contains(point);
        }

        private void UpdatePointerRange()
        {
            if(keyboard!=null&&keyboard.Visible){var settings=keyboard.panel.interaction.pointerSettings??windowSettings;if(PointingAt(keyboard.panel.rect,settings)||PointingAt(keyboard.panel.moveBar,settings)){ApplyPointerRange(settings.EffectiveClickDistance);return;}}
            foreach(var extra in extraWindows.Values)
                if(PointingAt(extra.view.root,extra.settings)||PointingAt(extra.panel.moveBar,extra.settings)) { ApplyPointerRange(extra.settings.EffectiveClickDistance);return; }
            bool targetingBrowser = view != null && PointingAt(view.root);
            bool targetingKeyboard = keyboard != null && keyboard.Visible && PointingAt(keyboard.panel.rect);
            targetingBrowser = targetingBrowser || (floating && window != null && PointingAt(window.moveBar));
            targetingKeyboard = targetingKeyboard || (keyboard != null && keyboard.Visible && PointingAt(keyboard.panel.moveBar));
            if (!targetingBrowser && !targetingKeyboard) { RestorePointerRange(); return; }
            ApplyPointerRange(windowSettings.EffectiveClickDistance);
        }
        private bool PointingAt(RectTransform bounds,WindowSettings settings){Vector2 point;return bounds!=null&&PointerRangeFilter.TryPoint(bounds,settings.EffectiveClickDistance,out point)&&bounds.rect.Contains(point);}
        private void ApplyPointerRange(float distance)
        {
            if (!pointerRangeOverridden || !Mathf.Approximately(Pointer.raycastDistance, appliedPointerRange)) originalPointerRange = Pointer.raycastDistance;
            pointerRangeOverridden = true;
            appliedPointerRange = distance;
            Pointer.raycastDistance = appliedPointerRange;
        }

        private void RestorePointerRange()
        {
            if (pointerRangeOverridden && Mathf.Approximately(Pointer.raycastDistance, appliedPointerRange)) Pointer.raycastDistance = originalPointerRange;
            pointerRangeOverridden = false;
        }

        public override void ScriptUpdate()
        {
            base.ScriptUpdate();
            try
            {
                if (resetInternetPending)
                {
                    resetInternetPending = false;
                    var area = bookArea;
                    Shutdown();
                    EnsureEngine();
                    if (area != null) { BuildBookPage(area); HideDefaultContent(); RefreshBookPage(); }
                    return;
                }
                if (engine != null) engine.Update();
                foreach(var extra in new List<FloatingBrowser>(extraWindows.Values))extra.Update();
                if(floating&&window!=null){window.SetVisible(!engine.Empty);view.root.gameObject.SetActive(!engine.Empty);}
                windowSettings.cursorScale = floating && window != null ? Mathf.Max(1f, window.metresWide / WindowSettings.DefaultWidth) : 1f;
                UpdatePointerRange();
                if (view != null) view.Update();
                if (engine != null && view != null)
                {
                    if (floating && engine.SpatialAudioEnabled && browserAudio == null)
                    {
                        var audioObject = new GameObject("TheInternet Screen Audio");
                        UnityEngine.Object.DontDestroyOnLoad(audioObject);
                        browserAudio = audioObject.AddComponent<BrowserAudioOutput>();
                        engine.OnAudioSamples += browserAudio.Enqueue;
                        engine.OnAudioReset += browserAudio.ResetAudio;
                    }
                    if (browserAudio != null) browserAudio.Apply(view.root.position, floating && engine.SpatialAudioEnabled, engine.Muted, window != null ? window.metresWide : WindowSettings.DefaultWidth);
                }
                if (window != null) window.Update();
                if (keyboard != null)
                {
                    keyboard.Update();
                    if (!keyboard.Visible && view != null && view.EditingAddress) view.EndAddressEdit();
                }
                // safety net: our page is open but the open event was missed
                var open = UIModsMenu.openMenu;
                if (open != null && IsMine(open) && open.contentArea != null && open.contentArea.gameObject.activeInHierarchy)
                {
                    if (bookArea != open.contentArea || bookHolder == null) OnMenuOpened(open);
                    HideDefaultContent();
                    FitDocked();
                    if(floating)RefreshBookPage();
                }
            }
            catch (Exception e) { Warn("Update: " + e.Message); }
        }

        // ---------------- book page helpers ----------------

        private static readonly string[] DefaultParts = { "title", "modInfo", "resetSettingsButton", "optionsContent", "errorsContent", "errorsTitle", "showErrorsButton" };

        private void HideDefaultContent()
        {
            if (bookArea == null || bookHolder == null) return;
            foreach (Transform child in bookArea.transform)
                if (child != bookHolder && child.gameObject.activeSelf) child.gameObject.SetActive(false);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
            foreach (var name in DefaultParts)
            {
                for (var t = bookArea.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    var f = t.GetField(name, flags);
                    if (f == null) continue;
                    var v = f.GetValue(bookArea);
                    var go = v is Component ? ((Component)v).gameObject : v as GameObject;
                    if (go != null && go.activeSelf && !go.transform.IsChildOf(bookHolder) && !bookHolder.IsChildOf(go.transform)) go.SetActive(false);
                    break;
                }
            }
        }

        /// <summary>Keep the page on the empty paper, clear of the mods list on the left.</summary>
        private static void FitToPaper(UIMenuContentArea area, RectTransform holder)
        {
            try
            {
                var ca = (RectTransform)area.transform;
                Rect full = ca.rect, r = full;
                var corners = new Vector3[4];
                var blockers = new List<Transform>();
                if (UIModsMenu.instance != null && UIModsMenu.instance.navigationPane != null) blockers.Add(UIModsMenu.instance.navigationPane);
                if (UIPlayerMenu.instance != null && UIPlayerMenu.instance.navigationPane != null) blockers.Add(UIPlayerMenu.instance.navigationPane);
                foreach (var b in blockers)
                {
                    var brt = b as RectTransform;
                    if (brt == null || brt.IsChildOf(ca)) continue;
                    brt.GetWorldCorners(corners);
                    float bx = float.MinValue;
                    foreach (var w in corners) bx = Mathf.Max(bx, ca.InverseTransformPoint(w).x);
                    if (bx > r.xMin && bx < full.center.x + full.width * 0.2f) r.xMin = bx + full.width * 0.015f;
                }
                r.xMax -= full.width * 0.01f; r.yMin += full.height * 0.02f; r.yMax -= full.height * 0.02f;
                holder.anchorMin = Vector2.zero; holder.anchorMax = Vector2.one;
                holder.offsetMin = new Vector2(r.xMin - full.xMin, r.yMin - full.yMin);
                holder.offsetMax = new Vector2(r.xMax - full.xMax, r.yMax - full.yMax);
            }
            catch (Exception e) { Warn("FitToPaper: " + e.Message); }
        }

        // ---------------- key targets ----------------

        private class AddressTarget : IKeyTarget
        {
            private readonly BrowserView v; private readonly InternetScript s;
            public AddressTarget(BrowserView v, InternetScript s) { this.v = v; this.s = s; }
            public string Describe { get { return "Typing in the address bar"; } }
            public void Type(string t) { if (!v.EditingAddress) v.BeginAddressEdit(); v.AddressType(t); }
            public void Backspace() { v.AddressBackspace(); }
            public void Enter() { v.AddressSubmit(); s.HideKeyboard(); }
            public void Arrow(bool right) { }
        }

        private class PageTarget : IKeyTarget
        {
            public BrowserEngine Engine {get{return e;}}
            private readonly BrowserEngine e;
            public PageTarget(BrowserEngine e) { this.e = e; }
            public string Describe { get { return "Typing on the page"; } }
            public void Type(string t) { e.TypeText(t); }
            public void Backspace() { e.Key("Backspace"); }
            public void Enter() { e.Key("Enter"); }
            public void Arrow(bool right) { e.Key(right ? "Right" : "Left"); }
        }
    }
}
