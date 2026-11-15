using System;
using System.Collections.Generic;
using ThunderRoad;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using PointerEventData = UnityEngine.EventSystems.PointerEventData;
using UnityEngine.UI;

namespace TheInternet
{
    /// <summary>
    /// The Chrome-style browser window (tab strip, toolbar, address bar, page). The same window is
    /// shown inside the player book or popped out into the world.
    /// </summary>
    public class BrowserView
    {
        public const float W = 1280f, TabH = 86f, BarH = 52f, PageH = 720f;
        public const float H = TabH + BarH + PageH;

        public RectTransform root;
        public RectTransform tabStrip;
        public RawImage page;
        public BrowserSurface surface;

        private readonly BrowserEngine engine;
        private TMP_Text address, overlay;
        public static readonly List<BrowserView> OpenViews = new List<BrowserView>();
        public BrowserEngine Engine { get { return engine; } }
        public WindowSettings Settings { get { return settings; } }
        public bool IsFloating { get { return floating; } }
        private readonly TMP_Text[] tabTitles = new TMP_Text[10];
        private readonly RectTransform[] tabTitleSlots = new RectTransform[10];
        private readonly Clickable[] tabButtons = new Clickable[10];
        private readonly RectTransform[] tabRects = new RectTransform[10];
        private readonly RectTransform[] tabCloseRects = new RectTransform[10];
        private Clickable newTabButton;
        private Image addressBox, loadBar, muteIcon, reloadIcon;
        private Clickable backBtn, fwdBtn, popBtn, dockBtn, closeBtn, smallerBtn, biggerBtn;
        private string editText;      // address bar text while typing; null = not editing
        private float loadAnim;
        private RectTransform toolbar, pageRect, settingsMenu;
        private GameObject exitFullscreen;
        private readonly WindowSettings settings;
        private TMP_Text followLabel, faceLabel, positionLabel;
        private bool fullscreen;
        private bool settingsInBook;
        private Clickable settingsButton;
        private TMP_Text cursorLabel;
        private TMP_Text adBlockLabel;
        private TMP_Text adBlockNote;
        private TMP_Text screenAudioLabel, screenAudioStatus;
        private Clickable screenAudioButton;
        private bool floating;
        private TMP_Text singleClickLabel;
        private TMP_Text loadingBarLabel;
        private TMP_Text clearDataLabel;
        private TMP_Text storageNote;
        private Clickable restartInternetButton, clearDataButton;
        private float clearDataConfirmUntil;
        private DockedCursor dockedCursor;

        public Action onPopOut, onDock, onClose, onBigger, onSmaller, onToggleKeyboard, onEditAddress, onSettingsClosed, onResetSize, onFocusWindow, onResetInternet, onClearSavedData;

        public bool EditingAddress { get { return editText != null; } }
        public RectTransform SettingsRoot { get { return settingsMenu; } }

        public BrowserView(BrowserEngine engine, Transform parent, WindowSettings settings)
        {
            this.engine = engine;
            this.settings = settings;
            OpenViews.Add(this);
            Ui.FindFonts();
            root = Ui.Rect(parent, "TheInternet Browser", 0.5f, 0.5f, 0.5f, 0.5f);
            root.sizeDelta = new Vector2(W, H);
            var frame = root.gameObject.AddComponent<Image>();
            frame.sprite = Ui.Rounded(20); frame.type = Image.Type.Sliced; frame.pixelsPerUnitMultiplier = 20f / 14f;
            frame.color = Ui.TabStrip;
            frame.raycastTarget = true; // blocks clicks falling through to the book

            BuildTabStrip();
            BuildToolbar();
            BuildPage();
            BuildSettings();
            exitFullscreen = TextButton(root, "Exit full screen", W - 190, 10, 180, () => engine.Key("Escape")).gameObject;
            exitFullscreen.AddComponent<ProximityReveal>().settings = settings;
            exitFullscreen.SetActive(false);
            dockedCursor = root.gameObject.AddComponent<DockedCursor>();
            dockedCursor.settings = settings; dockedCursor.Initialize();
            SetFloating(false);
        }

        // ---------------- tab strip ----------------
        private void BuildTabStrip()
        {
            tabStrip = Ui.Px(root, "TabStrip", 0, 0, W, TabH);
            var bg = Ui.Box(tabStrip, new Color(0, 0, 0, 0));
            bg.raycastTarget = true;

            for (int i = 0; i < 10; i++)
            {
                int index = i;
                var tab = Ui.Px(tabStrip, "Tab " + (i + 1), 10 + i * 170, 4, 164, 38);
                tabRects[i] = tab;
                var tabBg = Ui.Box(tab, Ui.TabActive, 20, 0, 20f / 12f); tabBg.raycastTarget = true;
                var click = tab.gameObject.AddComponent<Clickable>();
                click.background = tabBg; click.normal = Ui.TabActive; click.hover = Ui.IconHover; click.pressed = Ui.OmniboxFocus;
                click.onClick = () => { if(onFocusWindow!=null)onFocusWindow(); EndAddressEdit(); engine.SelectTab(index); };
                var drag = tab.gameObject.AddComponent<TabDragHandle>(); drag.view=this; drag.index=index; drag.button=click;
                tabButtons[i] = click;
                tabTitleSlots[i] = Ui.Px(tab, "Title", 12, 0, 114, 38);
                tabTitles[i] = Ui.Text(tabTitleSlots[i], "Text", "New Tab", 17, Ui.TextDark, TextAlignmentOptions.MidlineLeft);
                // Book/world canvases use different clip spaces. Keep these
                // already size-bounded, ellipsized labels out of inherited masks.
                tabTitles[i].maskable = false;
                tabTitles[i].richText = false;
                tabTitles[i].maxVisibleLines = 1;
                tabTitles[i].overflowMode = TextOverflowModes.Ellipsis;
                tabCloseRects[i] = (RectTransform)Ui.IconButton(tab, "Close tab", 130, 5, 28, "close", () => { EndAddressEdit(); engine.CloseTab(index); }).transform;
                tab.gameObject.SetActive(i == 0);
            }
            newTabButton = Ui.IconButton(tabStrip, "New tab", 186, 4, 38, "plus", () => { EndAddressEdit(); engine.NewTab(); });
            settingsButton = TextButton(tabStrip, "Settings", W - 360, 4, 130, () => settingsMenu.gameObject.SetActive(!settingsMenu.gameObject.activeSelf));

            // right-hand window buttons
            popBtn = Ui.IconButton(tabStrip, "PopOut", W - 50, 4, 38, "popout", () => { if (onPopOut != null) onPopOut(); });
            smallerBtn = Ui.IconButton(tabStrip, "Smaller", W - 210, 4, 38, "minus", () => { if (onSmaller != null) onSmaller(); });
            biggerBtn = Ui.IconButton(tabStrip, "Bigger", W - 170, 4, 38, "plus", () => { if (onBigger != null) onBigger(); });
            dockBtn = Ui.IconButton(tabStrip, "Dock", W - 130, 4, 38, "dock", () => { if (onDock != null) onDock(); });
            closeBtn = Ui.IconButton(tabStrip, "Close", W - 50, 4, 38, "close", () => { if (onClose != null) onClose(); });
            closeBtn.hover = new Color(0.91f, 0.27f, 0.22f, 1f);
        }

        private void UpdateTabs()
        {
            int count = Mathf.Clamp(engine.TabTitles.Length, 0, 10);
            float width = Mathf.Min(260, 850f / Mathf.Clamp(count,1,5));
            for (int i = 0; i < 10; i++)
            {
                tabRects[i].gameObject.SetActive(i < count);
                if (i >= count) continue;
                tabRects[i].anchoredPosition = new Vector2(10 + (i % 5) * width, -4 - (i / 5) * 40);
                tabRects[i].sizeDelta = new Vector2(width - 6, 38);
                // Resize the fixed title slot; the TMP child stretches inside it.
                // Giving the stretch child a sizeDelta added to its parent's width
                // and let long website titles overlap neighboring tabs/buttons.
                tabTitleSlots[i].sizeDelta = new Vector2(width - 58, 38);
                tabTitles[i].text = engine.TabTitles[i];
                tabCloseRects[i].anchoredPosition = new Vector2(width - 40, -5);
                tabButtons[i].normal = i == engine.ActiveTab ? Ui.TabActive : Ui.TabStrip;
                tabButtons[i].Paint();
            }
            int row = count > 5 ? 1 : 0, inRow = count > 5 ? count-5 : count;
            ((RectTransform)newTabButton.transform).anchoredPosition = new Vector2(10 + inRow * width, -4-row*40);
            newTabButton.interactable = engine.TotalTabCount < 10; newTabButton.Paint();
        }

        public void RefreshTabLabels()
        {
            foreach (var title in tabTitles)
            {
                title.maskable = false;
                title.canvasRenderer.DisableRectClipping();
                title.canvasRenderer.cull = false;
                title.SetAllDirty();
            }
        }

        // ---------------- toolbar ----------------
        private void BuildToolbar()
        {
            var bar = Ui.Px(root, "Toolbar", 0, TabH, W, BarH);
            toolbar = bar;
            Ui.Box(bar, Ui.Toolbar).raycastTarget = true;

            backBtn = Ui.IconButton(bar, "Back", 8, 6, 40, "back", () => engine.Back());
            fwdBtn = Ui.IconButton(bar, "Forward", 50, 6, 40, "forward", () => engine.Forward());
            var reload = Ui.IconButton(bar, "Reload", 92, 6, 40, "reload", () => engine.Reload());
            reloadIcon = reload.icon;
            Ui.IconButton(bar, "Home", 134, 6, 40, "home", () => engine.Home());

            var omni = Ui.Px(bar, "Omnibox", 184, 8, W - 184 - 110, 36);
            addressBox = Ui.Box(omni, Ui.Omnibox, 20, 0, 20f / 18f);
            addressBox.raycastTarget = true;
            var omniClick = omni.gameObject.AddComponent<Clickable>();
            omniClick.background = addressBox; omniClick.normal = Ui.Omnibox; omniClick.hover = Ui.IconHover; omniClick.pressed = Ui.OmniboxFocus;
            omniClick.onClick = () => { if (onEditAddress != null) onEditAddress(); };
            var lockRt = Ui.Px(omni, "Lock", 12, 8, 20, 20);
            Ui.IconImage(lockRt, "search", Ui.TextDim, 0f);
            var addrRt = Ui.Px(omni, "Address", 42, 0, W - 184 - 110 - 54, 36);
            address = Ui.Text(addrRt, "Text", "", 17, Ui.TextDark, TextAlignmentOptions.MidlineLeft);

            Ui.IconButton(bar, "Keyboard", W - 96, 6, 40, "keyboard", () => { if (onToggleKeyboard != null) onToggleKeyboard(); });
            var mute = Ui.IconButton(bar, "Mute", W - 50, 6, 40, "speaker", () => engine.SetMuted(!engine.Muted));
            muteIcon = mute.icon;

            var load = Ui.Px(bar, "LoadBar", 0, BarH - 3, W, 3);
            loadBar = Ui.Box(load, Ui.Accent);
        }

        // ---------------- page ----------------
        private void BuildPage()
        {
            var pageRt = Ui.Px(root, "Page", 0, TabH + BarH, W, PageH);
            pageRect = pageRt;
            var bg = Ui.Box(pageRt, Ui.TabStrip);
            bg.raycastTarget = false;
            var imgRt = Ui.Rect(pageRt, "Surface", 0, 0, 1, 1);
            page = imgRt.gameObject.AddComponent<RawImage>();
            page.texture = engine.Texture;
            page.uvRect = new UnityEngine.Rect(0, 1, 1, -1); // engine images are top-down
            page.raycastTarget = true;
            surface = imgRt.gameObject.AddComponent<BrowserSurface>();
            surface.engine = engine;
            surface.onFocusWindow = () => { if(onFocusWindow!=null)onFocusWindow(); };

            var ov = Ui.Rect(pageRt, "Overlay", 0.1f, 0.3f, 0.9f, 0.7f);
            overlay = Ui.Text(ov, "Text", "", 30, Ui.TextDim, TextAlignmentOptions.Center);
            overlay.enableWordWrapping = true;
        }

        public void SetFloating(bool floating)
        {
            this.floating = floating;
            engine.SetSpatialAudioAllowed(floating);
            settingsButton.gameObject.SetActive(!floating);
            settingsMenu.gameObject.SetActive(false);
            if (!floating && settingsInBook)
            {
                settingsMenu.SetParent(root, false);
                settingsMenu.anchorMin = settingsMenu.anchorMax = new Vector2(0, 1);
                settingsMenu.pivot = new Vector2(0, 1);
                settingsMenu.anchoredPosition3D = new Vector3(330, -TabH, 0);
                settingsMenu.localScale = Vector3.one;
                settingsInBook = false;
            }
            dockedCursor.enabled = !floating;
            popBtn.gameObject.SetActive(!floating);
            dockBtn.gameObject.SetActive(floating);
            closeBtn.gameObject.SetActive(floating);
            smallerBtn.gameObject.SetActive(floating);
            biggerBtn.gameObject.SetActive(floating);
        }

        public void ShowBookSettings(RectTransform host, bool visible)
        {
            if (host == null) return;
            if (settingsMenu.parent != host)
            {
                settingsMenu.SetParent(host, false);
                settingsMenu.anchorMin = settingsMenu.anchorMax = new Vector2(0.5f, 0.5f);
                settingsMenu.pivot = new Vector2(0.5f, 0.5f);
                settingsMenu.anchoredPosition3D = new Vector3(0, 0, -1);
            }
            settingsInBook = true;
            float scale = Mathf.Min(host.rect.width / settingsMenu.sizeDelta.x, host.rect.height / settingsMenu.sizeDelta.y) * 0.98f;
            settingsMenu.localScale = Vector3.one * Mathf.Max(0.01f, scale);
            settingsMenu.gameObject.SetActive(visible);
        }

        public void Destroy()
        {
            OpenViews.Remove(this);
            // Settings can be reparented into the book while the browser floats.
            if (settingsMenu != null) UnityEngine.Object.Destroy(settingsMenu.gameObject);
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
        }

        private Clickable TextButton(Transform parent, string label, float x, float y, float width, Action action)
        {
            var rt = Ui.Px(parent, label, x, y, width, 38);
            var bg = Ui.Box(rt, Ui.Omnibox, 12); bg.raycastTarget = true;
            Ui.Text(rt, "Label", label, 17, Ui.TextDark, TextAlignmentOptions.Center);
            var c = rt.gameObject.AddComponent<Clickable>();
            c.background = bg; c.normal = Ui.Omnibox; c.hover = Ui.IconHover; c.pressed = Ui.OmniboxFocus; c.onClick = action;
            return c;
        }

        private Clickable BookButton(Transform parent, string label, float x, float y, float width, Action action)
        {
            var c = TextButton(parent, label, x, y, width, action);
            c.normal = Ui.KeyFace; c.hover = Ui.KeyHover; c.pressed = Ui.KeyDown;
            var labelText = c.GetComponentInChildren<TMP_Text>();
            labelText.color = Ui.Ink;
            if (Ui.bookFont != null) { labelText.font = Ui.bookFont; labelText.fontSharedMaterial = Ui.bookFont.material; }
            c.Paint();
            return c;
        }

        private void BuildSettings()
        {
            settingsMenu = Ui.Px(root, "Browser settings", 330, TabH, 620, 738);
            Ui.Box(settingsMenu, Ui.Parchment, 20).raycastTarget = true;
            Ui.Box(Ui.Rect(settingsMenu, "Ink border", 0, 0, 1, 1), Ui.InkSoft, 20, 2);
            Ui.Text(Ui.Px(settingsMenu, "Heading", 20, 10, 570, 32), "Text", "The Internet · Local Data Settings", 26, Ui.Ink, TextAlignmentOptions.MidlineLeft, Ui.bookFont);
            followLabel = BookButton(settingsMenu, "Follow player: Off", 20, 54, 280, () => settings.followPlayer = !settings.followPlayer).GetComponentInChildren<TMP_Text>();
            faceLabel = BookButton(settingsMenu, "Face player: Off", 320, 54, 280, () => settings.facePlayer = !settings.facePlayer).GetComponentInChildren<TMP_Text>();
            positionLabel = Ui.Text(Ui.Px(settingsMenu, "Position", 20, 102, 580, 64), "Text", "", 20, Ui.Ink, TextAlignmentOptions.MidlineLeft, Ui.bookFont);
            positionLabel.enableWordWrapping = true;
            BookButton(settingsMenu, "Closer", 20, 176, 180, () => settings.distance = Mathf.Max(0.4f, settings.distance - 0.1f));
            BookButton(settingsMenu, "Farther", 220, 176, 180, () => settings.distance = Mathf.Min(4f, settings.distance + 0.1f));
            BookButton(settingsMenu, "Reset", 420, 176, 180, () => { settings.distance = 1.1f; settings.height = -0.1f; settings.horizontal = 0; });
            BookButton(settingsMenu, "Left", 20, 224, 130, () => settings.horizontal = Mathf.Max(-2f, settings.horizontal - 0.1f));
            BookButton(settingsMenu, "Right", 170, 224, 130, () => settings.horizontal = Mathf.Min(2f, settings.horizontal + 0.1f));
            BookButton(settingsMenu, "Up", 320, 224, 130, () => settings.height = Mathf.Min(1.5f, settings.height + 0.1f));
            BookButton(settingsMenu, "Down", 470, 224, 130, () => settings.height = Mathf.Max(-1.5f, settings.height - 0.1f));
            var note = Ui.Text(Ui.Px(settingsMenu, "Note", 20, 278, 580, 64), "Text", "Local Data: cookies/site data saved in BrowserData. Restart keeps logins; Clear saved data signs you out.", 19, Ui.InkSoft, TextAlignmentOptions.MidlineLeft, Ui.bookFont);
            note.enableWordWrapping = true;
            storageNote = note;
            cursorLabel = Ui.Text(Ui.Px(settingsMenu, "Cursor settings", 20, 346, 580, 32), "Text", "", 20, Ui.Ink, TextAlignmentOptions.MidlineLeft, Ui.bookFont);
            BookButton(settingsMenu, "Range -", 20, 390, 130, () => settings.clickDistance = Mathf.Max(0.5f, settings.clickDistance - 0.5f));
            BookButton(settingsMenu, "Range +", 170, 390, 130, () => settings.clickDistance = Mathf.Min(10f, settings.clickDistance + 0.5f));
            BookButton(settingsMenu, "Reset reach", 320, 390, 280, () => settings.clickDistance = 4f);
            BookButton(settingsMenu, "Reset screen size", 20, 438, 580, () => { if (onResetSize != null) onResetSize(); });
            adBlockLabel = BookButton(settingsMenu, "Ad blocker: On", 20, 486, 580, () => engine.SetAdBlockEnabled(!engine.AdBlockEnabled)).GetComponentInChildren<TMP_Text>();
            adBlockNote = Ui.Text(Ui.Px(settingsMenu, "Ad blocking note", 20, 528, 580, 28), "Text", "Changing ad blocking reloads all open tabs.", 18, Ui.InkSoft, TextAlignmentOptions.MidlineLeft, Ui.bookFont);
            screenAudioButton = BookButton(settingsMenu, "Audio from screen: Off", 20, 564, 580, () => engine.SetSpatialAudioEnabled(!engine.SpatialAudioEnabled));
            screenAudioLabel = screenAudioButton.GetComponentInChildren<TMP_Text>();
            screenAudioStatus = Ui.Text(Ui.Px(settingsMenu, "Audio routing note", 20, 606, 580, 28), "Text", "", 18, Ui.InkSoft, TextAlignmentOptions.MidlineLeft, Ui.bookFont);
            singleClickLabel = BookButton(settingsMenu, "Single-click mode: On", 20, 644, 280, () => engine.SingleClickMode = !engine.SingleClickMode).GetComponentInChildren<TMP_Text>();
            loadingBarLabel = BookButton(settingsMenu, "Hide loading bar: Off", 320, 644, 280, () => settings.hideLoadingBar = !settings.hideLoadingBar).GetComponentInChildren<TMP_Text>();
            BookButton(settingsMenu, "Done", 420, 692, 180, () => { settingsMenu.gameObject.SetActive(false); if (onSettingsClosed != null) onSettingsClosed(); });
            restartInternetButton = BookButton(settingsMenu, "Restart Internet", 20, 692, 180, () => { if (onResetInternet != null) onResetInternet(); });
            clearDataButton = BookButton(settingsMenu, "Clear saved data", 220, 692, 180, () => {
                if (clearDataConfirmUntil > Time.unscaledTime) { clearDataConfirmUntil = 0; if (onClearSavedData != null) onClearSavedData(); }
                else clearDataConfirmUntil = Time.unscaledTime + 6f;
            });
            clearDataLabel = clearDataButton.GetComponentInChildren<TMP_Text>();
            settingsMenu.gameObject.SetActive(false);
        }

        private void UpdateFullscreen()
        {
            if (fullscreen == engine.Fullscreen) return;
            fullscreen = engine.Fullscreen;
            tabStrip.gameObject.SetActive(!fullscreen);
            toolbar.gameObject.SetActive(!fullscreen);
            if (!settingsInBook) settingsMenu.gameObject.SetActive(false);
            pageRect.anchoredPosition = new Vector2(0, fullscreen ? 0 : -(TabH + BarH));
            pageRect.sizeDelta = new Vector2(W, fullscreen ? H : PageH);
            engine.Send(new Newtonsoft.Json.Linq.JObject { ["t"] = "resize", ["w"] = (int)W, ["h"] = (int)(fullscreen ? H : PageH), ["fullscreen"] = fullscreen });
            exitFullscreen.SetActive(fullscreen);
        }

        // ---------------- address editing (typed on the in-game keyboard) ----------------
        public void BeginAddressEdit() { editText = ""; Refresh(); }
        public void EndAddressEdit() { editText = null; Refresh(); }
        public void AddressType(string s) { if (editText != null) { editText += s; Refresh(); } }
        public void AddressBackspace() { if (!string.IsNullOrEmpty(editText)) { editText = editText.Substring(0, editText.Length - 1); Refresh(); } }
        public void AddressSubmit()
        {
            if (editText == null) return;
            string t = editText.Trim();
            editText = null;
            if (t.Length > 0) engine.Navigate(t);
            Refresh();
        }

        public void Update()
        {
            UpdateFullscreen();
            UpdateTabs();
            singleClickLabel.text = "Single-click mode: " + (engine.SingleClickMode ? "On" : "Off");
            loadingBarLabel.text = "Hide loading bar: " + (settings.hideLoadingBar ? "On" : "Off");
            adBlockLabel.text = "Ad blocker: " + (engine.AdBlockEnabled ? "On" : "Off") + "  (select to toggle)";
            adBlockNote.text = string.IsNullOrEmpty(engine.PlaybackStatus) ? (engine.AdBlockEnabled ? "YouTube protection on. Changing this reloads tabs." : "Ad blocking off. Changing this reloads tabs.") : engine.PlaybackStatus;
            storageNote.text = engine.DataStatus;
            restartInternetButton.interactable = !engine.ClearingSavedData; restartInternetButton.Paint();
            clearDataButton.interactable = engine.Connected && !engine.ClearingSavedData; clearDataButton.Paint();
            clearDataLabel.text = engine.ClearingSavedData ? "Clearing..." : clearDataConfirmUntil > Time.unscaledTime ? "Confirm clear" : "Clear saved data";
            screenAudioButton.interactable = floating; screenAudioButton.Paint();
            screenAudioLabel.text = floating ? "Audio from screen: " + (engine.SpatialAudioEnabled ? "On" : "Off") : "Audio from screen: Off (docked)";
            screenAudioStatus.text = floating ? engine.AudioRoutingStatus : "Normal audio in book; float the screen to enable.";
            cursorLabel.text = string.Format("Cursor reach: {0:0.0} m base / {1:0.0} m scaled", settings.clickDistance, settings.EffectiveClickDistance);
            followLabel.text = "Follow player: " + (settings.followPlayer ? "On" : "Off");
            faceLabel.text = "Face player: " + (settings.facePlayer ? "On" : "Off");
            positionLabel.text = string.Format("Distance: {0:0.0} m    Height: {1:+0.0;-0.0;0.0} m    Side: {2:+0.0;-0.0;0.0} m", settings.distance, settings.height, settings.horizontal);
            if (page.texture != engine.Texture) page.texture = engine.Texture;
            backBtn.interactable = engine.CanBack; backBtn.Paint();
            fwdBtn.interactable = engine.CanForward; fwdBtn.Paint();
            if (engine.Loading && !settings.hideLoadingBar)
            {
                loadAnim = (loadAnim + Time.unscaledDeltaTime * 0.6f) % 1f;
                loadBar.enabled = true;
                Ui.Anchor(loadBar.rectTransform, 0, 0, Mathf.Lerp(0.1f, 0.95f, loadAnim), 1);
            }
            else loadBar.enabled = false;
            Refresh();
        }

        private void Refresh()
        {
            if (editText != null)
            {
                address.text = editText + ((Time.unscaledTime % 1f) < 0.5f ? "|" : " ");
                address.color = Ui.TextDark;
                addressBox.color = Ui.OmniboxFocus;
            }
            else
            {
                string u = engine.Url ?? "";
                if (u.StartsWith("https://")) u = u.Substring(8);
                if (u.StartsWith("www.")) u = u.Substring(4);
                address.text = string.IsNullOrEmpty(u) || u.StartsWith("file:") ? "Search Google or type a URL" : u;
                address.color = string.IsNullOrEmpty(u) ? Ui.TextDim : Ui.TextDark;
            }
            if (muteIcon != null) muteIcon.sprite = Ui.Icon(engine.Muted ? "muted" : "speaker");
            if (reloadIcon != null) reloadIcon.sprite = Ui.Icon(engine.Loading ? "stop" : "reload");
            overlay.text = engine.Empty ? "Tabs are in other windows." : engine.Error ?? engine.Status ?? (!engine.Connected ? "Starting The Internet..." : "");
        }
    }

    /// <summary>
    /// The web page surface: turns the Blade &amp; Sorcery pointer into mouse input for the page,
    /// and the thumbstick into scrolling.
    /// </summary>
    public class BrowserSurface : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public BrowserEngine engine;
        public Action onFocusWindow;
        public bool hovered;
        private bool down;
        private bool pendingClick;
        private readonly TriggerPressGate trigger = new TriggerPressGate();
        private Side pressSide;
        private Vector2 lastPx = new Vector2(-1, -1);
        private float lastMoveSent, lastClickTime;
        private Vector2 lastClickPx;
        private Vector2 wheelAccum;

        public void OnPointerEnter(PointerEventData e) { hovered = true; }
        public void OnPointerExit(PointerEventData e)
        {
            hovered = false;
            pendingClick = false; trigger.Cancel();
            if (down) { down = false; engine.Mouse("mouseUp", lastPx); }
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (trigger.Latched || down) return;
            Vector2 px;
            if (!PointerPixel(out px)) return;
            if (!trigger.Begin()) return;
            if(onFocusWindow!=null)onFocusWindow();
            hovered = true;
            pressSide = Pointer.activeSide;
            if (engine.SingleClickMode)
            {
                pendingClick = true; lastPx = px;
                engine.Mouse("mouseMove", px);
                return;
            }
            int clicks = (Time.unscaledTime - lastClickTime < 0.4f && Vector2.Distance(px, lastClickPx) < 12f) ? 2 : 1;
            lastClickTime = Time.unscaledTime; lastClickPx = px;
            engine.Mouse("mouseMove", px);
            engine.Mouse("mouseDown", px, clicks);
            down = true; lastPx = px;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!down && !pendingClick) return;
            var hand = PlayerControl.GetHand(pressSide);
            if (hand != null && hand.usePressed) return;
            ReleasePress();
        }

        private void ReleasePress()
        {
            Vector2 px;
            if (pendingClick && trigger.Active && PointerPixel(out px))
            {
                engine.Mouse("mouseMove", px);
                engine.Mouse("mouseDown", px, 1);
                engine.Mouse("mouseUp", px, 1);
            }
            pendingClick = false;
            if (down)
            {
                down = false;
                if (!PointerPixel(out px)) px = lastPx;
                engine.Mouse("mouseUp", px);
            }
            trigger.Complete();
        }

        private void OnDisable()
        {
            if (down && engine != null) engine.Mouse("mouseUp", lastPx);
            down = false; pendingClick = false; hovered = false; trigger.Reset();
        }

        private void Update()
        {
            if (engine == null || !engine.Connected) return;
            var pressedHand = PlayerControl.GetHand(pressSide);
            if (trigger.Latched && (pressedHand == null || !pressedHand.usePressed))
            {
                ReleasePress(); trigger.Release();
            }
            if (!hovered && !down && !pendingClick) return;
            Vector2 px;
            if (PointerPixel(out px))
            {
                if (Vector2.Distance(px, lastPx) > 1.5f && Time.unscaledTime - lastMoveSent > 1f / 60f)
                {
                    engine.Mouse("mouseMove", px);
                    lastPx = px; lastMoveSent = Time.unscaledTime;
                }
            }
            // thumbstick scrolling while pointing at the page
            if (hovered)
            {
                var hand = PlayerControl.GetHand(Pointer.activeSide);
                if (hand != null)
                {
                    Vector2 axis = hand.JoystickAxis;
                    if (Mathf.Abs(axis.y) < 0.2f) axis.y = 0;
                    if (Mathf.Abs(axis.x) < 0.35f) axis.x = 0;
                    wheelAccum += new Vector2(axis.x, axis.y) * 1600f * Time.unscaledDeltaTime;
                    if (Mathf.Abs(wheelAccum.y) >= 20f || Mathf.Abs(wheelAccum.x) >= 20f)
                    {
                        engine.Wheel(lastPx.x < 0 ? new Vector2(BrowserEngine.Width / 2f, BrowserEngine.Height / 2f) : lastPx, -wheelAccum.x, wheelAccum.y);
                        wheelAccum = Vector2.zero;
                    }
                }
            }
            else wheelAccum = Vector2.zero;
        }

        /// <summary>Where the active pointer ray hits this surface, in page pixels.</summary>
        private bool PointerPixel(out Vector2 px)
        {
            px = Vector2.zero;
            var p = Pointer.GetActive();
            if (p == null) return false;
            var rt = (RectTransform)transform;
            if (Vector3.Dot(p.transform.forward, rt.forward) <= 0) return false;
            var plane = new Plane(rt.forward, rt.position);
            var ray = new Ray(p.transform.position, p.transform.forward);
            float d;
            if (!plane.Raycast(ray, out d)) return false;
            var range = GetComponentInParent<PointerRangeFilter>();
            if (range != null && range.settings != null && d > range.settings.EffectiveClickDistance) return false;
            Vector3 local = rt.InverseTransformPoint(ray.GetPoint(d));
            var r = rt.rect;
            float u = (local.x - r.xMin) / r.width, v = (local.y - r.yMin) / r.height;
            if (u < -0.02f || u > 1.02f || v < -0.02f || v > 1.02f) return false;
            px = new Vector2(Mathf.Clamp01(u) * (engine.Texture != null ? engine.Texture.width : BrowserEngine.Width), (1f - Mathf.Clamp01(v)) * (engine.Texture != null ? engine.Texture.height : BrowserEngine.Height));
            return true;
        }
    }
}
