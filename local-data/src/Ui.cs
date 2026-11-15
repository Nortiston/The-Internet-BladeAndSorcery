using System;
using System.Collections.Generic;
using System.Linq;
using ThunderRoad;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using PointerEventData = UnityEngine.EventSystems.PointerEventData;
using UnityEngine.UI;

namespace TheInternet
{
    /// <summary>Colours, fonts, procedural sprites and small builders for the mod's UI.</summary>
    public static class Ui
    {
        // Default dark browser palette. TextDark retains its existing API name.
        public static readonly Color TabStrip = Hex(0x15171C);
        public static readonly Color TabActive = Hex(0x292D35);
        public static readonly Color Toolbar = Hex(0x202329);
        public static readonly Color Omnibox = Hex(0x30343D);
        public static readonly Color OmniboxFocus = Hex(0x3B4960);
        public static readonly Color TextDark = Hex(0xF1F3F5);
        public static readonly Color TextDim = Hex(0xADB5C2);
        public static readonly Color IconCol = Hex(0xCDD4DF);
        public static readonly Color IconHover = Hex(0x414752);
        public static readonly Color Accent = Hex(0x8AB4F8);

        // Blade & Sorcery book palette (keyboard)
        public static readonly Color Parchment = new Color(0.93f, 0.85f, 0.67f, 1f);
        public static readonly Color ParchmentDark = new Color(0.84f, 0.72f, 0.52f, 1f);
        public static readonly Color KeyFace = new Color(0.97f, 0.91f, 0.76f, 1f);
        public static readonly Color KeyHover = new Color(1f, 0.96f, 0.84f, 1f);
        public static readonly Color KeyDown = new Color(0.86f, 0.75f, 0.55f, 1f);
        public static readonly Color Ink = new Color(0.27f, 0.17f, 0.08f, 1f);
        public static readonly Color InkSoft = new Color(0.42f, 0.29f, 0.15f, 1f);
        public static readonly Color Leather = new Color(0.36f, 0.22f, 0.11f, 1f);

        public static Color Hex(int rgb) { return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f); }

        // ---------------- fonts ----------------
        public static TMP_FontAsset uiFont;    // clean sans font for the browser chrome
        public static TMP_FontAsset bookFont;  // Blade & Sorcery hand-written font

        public static void FindFonts()
        {
            if (uiFont != null && bookFont != null) return;
            var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Where(f => f != null).ToList();
            if (uiFont == null) uiFont = all.FirstOrDefault(f => f.name == "NotoSans-Regular SDF") ?? all.FirstOrDefault(f => f.name.StartsWith("NotoSans-Regular") && !f.name.Contains("LIT")) ?? TMP_Settings.defaultFontAsset;
            if (bookFont == null) bookFont = all.FirstOrDefault(f => f.name == "Calligraffitti-Regular SDF") ?? all.FirstOrDefault(f => f.name.ToLowerInvariant().Contains("calligraffitti")) ?? uiFont;
            InternetScript.Log("Fonts: ui=" + (uiFont != null ? uiFont.name : "-") + " book=" + (bookFont != null ? bookFont.name : "-"));
        }

        // ---------------- procedural sprites ----------------
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        /// <summary>White rounded rectangle, 9-sliced. outline&gt;0 makes a ring.</summary>
        public static Sprite Rounded(int radius = 20, int outline = 0)
        {
            string key = "rr" + radius + "_" + outline;
            Sprite s;
            if (sprites.TryGetValue(key, out s) && s != null) return s;
            int size = radius * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[size * size];
            float r = radius;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, size - r), cy = Mathf.Clamp(y + 0.5f, r, size - r);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)); // distance from inner rect
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    if (outline > 0) a *= Mathf.Clamp01(d - (r - outline) + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            float b = radius + 1;
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            s.name = "TI_" + key;
            sprites[key] = s;
            return s;
        }

        public static Sprite Icon(string name)
        {
            Sprite s;
            if (sprites.TryGetValue("ic_" + name, out s) && s != null) return s;
            s = Icons.Make(name);
            sprites["ic_" + name] = s;
            return s;
        }

        // ---------------- builders ----------------
        public static RectTransform Rect(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Anchor(rt, x0, y0, x1, y1);
            return rt;
        }

        public static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one; rt.localRotation = Quaternion.identity;
            var p = rt.localPosition; p.z = 0; rt.localPosition = p;
        }

        /// <summary>Rect in pixel units from the parent's top-left (x, y, w, h).</summary>
        public static RectTransform Px(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            rt.localScale = Vector3.one;
            return rt;
        }

        public static Image Box(RectTransform rt, Color c, int radius = 0, int outline = 0, float ppuMul = 1f)
        {
            var img = rt.gameObject.AddComponent<Image>();
            if (radius > 0) { img.sprite = Rounded(radius, outline); img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = ppuMul; }
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align, TMP_FontAsset font = null)
        {
            var rt = Rect(parent, name, 0, 0, 1, 1);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            var f = font ?? uiFont;
            if (f != null) { t.font = f; t.fontSharedMaterial = f.material; }
            t.text = text ?? "";
            t.fontSize = size;
            t.enableAutoSizing = false;
            t.color = color;
            t.alignment = align;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        public static Image IconImage(Transform parent, string icon, Color color, float inset = 0.18f)
        {
            var rt = Rect(parent, "Icon", inset, inset, 1 - inset, 1 - inset);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Icon(icon);
            img.preserveAspect = true;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A round Chrome-style toolbar icon button.</summary>
        public static Clickable IconButton(Transform parent, string name, float x, float y, float size, string icon, Action onClick)
        {
            var rt = Px(parent, name, x, y, size, size);
            var bg = Box(rt, new Color(1, 1, 1, 0), (int)(size / 2) > 0 ? 20 : 0, 0, 40f / size);
            bg.raycastTarget = true;
            var ic = IconImage(rt, icon, IconCol, 0.22f);
            var c = rt.gameObject.AddComponent<Clickable>();
            c.background = bg; c.normal = new Color(1, 1, 1, 0); c.hover = IconHover; c.pressed = Hex(0xDADCE0);
            c.icon = ic;
            c.onClick = onClick;
            return c;
        }
    }

    /// <summary>
    /// Pointer-friendly button. Works with Blade &amp; Sorcery's pointer (enter/exit/down/up), and fires
    /// on release while still hovered; also accepts a normal click event (de-duplicated).
    /// </summary>
    public class Clickable : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public Graphic background;
        public Color normal = Color.white, hover = Color.white, pressed = Color.white;
        public Image icon;
        public TMP_Text label;
        public Action onClick;
        public Action onDown, onUp, onEnter, onExit;
        public bool repeat; // hold to repeat (backspace, arrows)
        public bool interactable = true;

        private bool hovered;
        private readonly TriggerPressGate trigger = new TriggerPressGate();
        private Side pressSide;
        private float lastFire, nextRepeat;

        private void OnEnable() { hovered = false; trigger.Reset(); Paint(); }
        private void OnDisable() { hovered = false; trigger.Reset(); }

        public void OnPointerEnter(PointerEventData e) { hovered = true; Paint(); if (onEnter != null) Safe(onEnter); }
        public void OnPointerExit(PointerEventData e) { hovered = false; trigger.Cancel(); Paint(); if (onExit != null) Safe(onExit); }
        public void OnPointerDown(PointerEventData e)
        {
            if (!interactable || !trigger.Begin()) return;
            pressSide = Pointer.activeSide;
            Paint();
            if (onDown != null) Safe(onDown);
            if (repeat) { Fire(); nextRepeat = Time.unscaledTime + 0.45f; }
        }
        public void OnPointerUp(PointerEventData e)
        {
            // Some game pointer paths emit Up/Click while the physical trigger is
            // still held. Defer activation until its real release in Update.
            var hand = PlayerControl.GetHand(pressSide);
            if (hand != null && hand.usePressed) return;
            CompletePress();
        }

        private void CompletePress()
        {
            bool was = trigger.Complete();
            Paint();
            if (onUp != null) Safe(onUp);
            if (was && hovered && !repeat && interactable) Fire();
        }
        public void OnPointerClick(PointerEventData e)
        {
            // Activation belongs exclusively to the physical release path.
        }

        private void Update()
        {
            if (trigger.Latched)
            {
                var hand = PlayerControl.GetHand(pressSide);
                if (hand == null || !hand.usePressed)
                {
                    if (trigger.Active) CompletePress();
                    trigger.Release();
                }
            }
            if (repeat && trigger.Active && hovered && Time.unscaledTime >= nextRepeat) { Fire(); nextRepeat = Time.unscaledTime + 0.06f; }
        }

        private void Fire()
        {
            lastFire = Time.unscaledTime;
            if (onClick != null) Safe(onClick);
        }

        public void Paint()
        {
            if (background != null) background.color = !interactable ? normal : trigger.Active ? pressed : hovered ? hover : normal;
            if (icon != null) { var c = icon.color; c.a = interactable ? 1f : 0.35f; icon.color = c; }
        }

        private static void Safe(Action a) { try { a(); } catch (Exception ex) { InternetScript.Warn("UI action failed: " + ex); } }
    }

    /// <summary>Tiny anti-aliased line-art icon painter, so the mod needs no image files.</summary>
    public static class Icons
    {
        private const int N = 64;

        public static Sprite Make(string name)
        {
            var segs = new List<Vector4>();
            float w = 5.5f; // stroke width
            switch (name)
            {
                case "back": L(segs, 46, 32, 18, 32); L(segs, 18, 32, 31, 45); L(segs, 18, 32, 31, 19); break;
                case "forward": L(segs, 18, 32, 46, 32); L(segs, 46, 32, 33, 45); L(segs, 46, 32, 33, 19); break;
                case "reload":
                    Arc(segs, 32, 32, 15, 50, 340);
                    { float a = 50 * Mathf.Deg2Rad; float ex = 32 + 15 * Mathf.Cos(a), ey = 32 + 15 * Mathf.Sin(a); L(segs, ex, ey, ex + 1, ey + 11); L(segs, ex, ey, ex + 11, ey + 1); }
                    break;
                case "stop": L(segs, 20, 20, 44, 44); L(segs, 20, 44, 44, 20); break;
                case "home": L(segs, 14, 32, 32, 49); L(segs, 32, 49, 50, 32); L(segs, 20, 36, 20, 15); L(segs, 20, 15, 44, 15); L(segs, 44, 15, 44, 36); L(segs, 29, 15, 29, 26); L(segs, 29, 26, 35, 26); L(segs, 35, 26, 35, 15); break;
                case "close": L(segs, 21, 21, 43, 43); L(segs, 21, 43, 43, 21); break;
                case "plus": L(segs, 32, 16, 32, 48); L(segs, 16, 32, 48, 32); break;
                case "minus": L(segs, 16, 32, 48, 32); break;
                case "keyboard":
                    w = 4.5f; RectOutline(segs, 9, 17, 55, 47);
                    for (int i = 0; i < 4; i++) { L(segs, 16 + i * 10.5f, 38, 17.5f + i * 10.5f, 38); L(segs, 16 + i * 10.5f, 30, 17.5f + i * 10.5f, 30); }
                    L(segs, 22, 23, 42, 23);
                    break;
                case "popout":
                    w = 5f; L(segs, 28, 14, 14, 14); L(segs, 14, 14, 14, 50); L(segs, 14, 50, 50, 50); L(segs, 50, 50, 50, 36);
                    L(segs, 31, 33, 50, 14); L(segs, 50, 14, 37, 14); L(segs, 50, 14, 50, 27);
                    break;
                case "dock":
                    w = 5f; RectOutline(segs, 12, 12, 52, 36);
                    L(segs, 32, 56, 32, 24); L(segs, 32, 24, 22, 34); L(segs, 32, 24, 42, 34);
                    break;
                case "speaker":
                    w = 4.5f; Speaker(segs); Arc(segs, 34, 32, 9, -45, 45); Arc(segs, 34, 32, 17, -45, 45);
                    break;
                case "muted":
                    w = 4.5f; Speaker(segs); L(segs, 42, 24, 56, 40); L(segs, 42, 40, 56, 24);
                    break;
                case "globe":
                    w = 3.5f; Arc(segs, 32, 32, 20, 0, 360); L(segs, 12, 32, 52, 32);
                    Ellipse(segs, 32, 32, 9, 20); L(segs, 15, 42, 49, 42); L(segs, 15, 22, 49, 22);
                    break;
                case "search": w = 5f; Arc(segs, 27, 37, 13, 0, 360); L(segs, 36, 28, 50, 14); break;
                case "lock":
                    w = 4.5f; RectOutline(segs, 18, 12, 46, 34); Arc(segs, 32, 34, 9, 0, 180); L(segs, 23, 34, 23, 34); break;
                case "grip":
                    w = 5f; for (int i = 0; i < 3; i++) { L(segs, 18, 24 + i * 8, 46, 24 + i * 8); }
                    break;
                case "shift": w = 4.5f; L(segs, 32, 50, 14, 30); L(segs, 32, 50, 50, 30); L(segs, 14, 30, 24, 30); L(segs, 50, 30, 40, 30); L(segs, 24, 30, 24, 14); L(segs, 40, 30, 40, 14); L(segs, 24, 14, 40, 14); break;
                case "backspace": w = 4.5f; L(segs, 22, 48, 54, 48); L(segs, 54, 48, 54, 16); L(segs, 54, 16, 22, 16); L(segs, 22, 16, 8, 32); L(segs, 8, 32, 22, 48); L(segs, 30, 25, 44, 39); L(segs, 30, 39, 44, 25); break;
                case "enter": w = 5f; L(segs, 50, 46, 50, 26); L(segs, 50, 26, 14, 26); L(segs, 14, 26, 25, 37); L(segs, 14, 26, 25, 15); break;
                case "left": L(segs, 40, 16, 22, 32); L(segs, 22, 32, 40, 48); break;
                case "right": L(segs, 24, 16, 42, 32); L(segs, 42, 32, 24, 48); break;
                default: Arc(segs, 32, 32, 10, 0, 360); break;
            }
            return Rasterise(segs, w, name);
        }

        private static void L(List<Vector4> s, float x0, float y0, float x1, float y1) { s.Add(new Vector4(x0, y0, x1, y1)); }
        private static void RectOutline(List<Vector4> s, float x0, float y0, float x1, float y1) { L(s, x0, y0, x1, y0); L(s, x1, y0, x1, y1); L(s, x1, y1, x0, y1); L(s, x0, y1, x0, y0); }
        private static void Speaker(List<Vector4> s) { L(s, 10, 25, 18, 25); L(s, 18, 25, 30, 14); L(s, 30, 14, 30, 50); L(s, 30, 50, 18, 39); L(s, 18, 39, 10, 39); L(s, 10, 39, 10, 25); }
        private static void Arc(List<Vector4> s, float cx, float cy, float r, float a0, float a1)
        {
            int steps = Mathf.Max(6, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 10f));
            for (int i = 0; i < steps; i++)
            {
                float t0 = Mathf.Deg2Rad * Mathf.Lerp(a0, a1, i / (float)steps), t1 = Mathf.Deg2Rad * Mathf.Lerp(a0, a1, (i + 1) / (float)steps);
                L(s, cx + r * Mathf.Cos(t0), cy + r * Mathf.Sin(t0), cx + r * Mathf.Cos(t1), cy + r * Mathf.Sin(t1));
            }
        }
        private static void Ellipse(List<Vector4> s, float cx, float cy, float rx, float ry)
        {
            for (int i = 0; i < 36; i++)
            {
                float t0 = i * Mathf.PI * 2 / 36, t1 = (i + 1) * Mathf.PI * 2 / 36;
                L(s, cx + rx * Mathf.Cos(t0), cy + ry * Mathf.Sin(t0), cx + rx * Mathf.Cos(t1), cy + ry * Mathf.Sin(t1));
            }
        }

        private static Sprite Rasterise(List<Vector4> segs, float width, string name)
        {
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[N * N];
            float half = width / 2f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = float.MaxValue;
                    foreach (var s in segs)
                    {
                        var a = new Vector2(s.x, s.y); var b = new Vector2(s.z, s.w);
                        var ab = b - a;
                        float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                        d = Mathf.Min(d, Vector2.Distance(p, a + ab * t));
                    }
                    float alpha = Mathf.Clamp01(half - d + 0.5f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sp = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            sp.name = "TI_icon_" + name;
            return sp;
        }
    }
}
