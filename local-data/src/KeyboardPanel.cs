using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheInternet
{
    /// <summary>Who receives the keys: the address bar or the web page.</summary>
    public interface IKeyTarget
    {
        void Type(string s);
        void Backspace();
        void Enter();
        void Arrow(bool right);
        string Describe { get; }
    }

    /// <summary>Floating keyboard themed like the Blade &amp; Sorcery book (parchment, brown ink, leather).</summary>
    public class KeyboardPanel
    {
        public const float W = 1060f, H = 430f;
        public WorldPanel panel;
        public IKeyTarget target;
        private RectTransform keysArea;
        private TMP_Text targetLabel;
        private bool shift, caps, symbols;
        private float lastShiftTap;

        public bool Visible { get { return panel != null && panel.Visible; } }

        private struct KeyDef
        {
            public string label, value, icon; public float width; public Action action; public bool repeat, highlight;
        }

        public KeyboardPanel()
        {
            Ui.FindFonts();
            panel = new WorldPanel("TheInternet Keyboard", new Vector2(W, H), 0.78f);
            var root = panel.content;

            // leather frame + parchment page
            var frame = Ui.Rect(root, "Frame", 0, 0, 1, 1);
            Ui.Box(frame, Ui.Leather, 20, 0, 20f / 22f).raycastTarget = true;
            var paper = Ui.Rect(root, "Paper", 0, 0, 1, 1);
            paper.offsetMin = new Vector2(10, 10); paper.offsetMax = new Vector2(-10, -10);
            Ui.Box(paper, Ui.Parchment, 20, 0, 20f / 16f);
            var rule = Ui.Px(paper, "Rule", 20, 52, W - 60, 2);
            Ui.Box(rule, new Color(Ui.Ink.r, Ui.Ink.g, Ui.Ink.b, 0.35f));

            // title bar = drag handle
            var title = Ui.Px(paper, "TitleBar", 0, 0, W - 20, 52);
            var tbg = Ui.Box(title, new Color(0, 0, 0, 0)); tbg.raycastTarget = true;
            var drag = title.gameObject.AddComponent<DragHandle>(); drag.target = panel.go.transform;
            var gripRt = Ui.Px(title, "Grip", 14, 10, 32, 32); Ui.IconImage(gripRt, "grip", Ui.InkSoft, 0f);
            var nameRt = Ui.Px(title, "Name", 54, 0, 300, 52);
            Ui.Text(nameRt, "Text", "The Internet", 30, Ui.Ink, TextAlignmentOptions.MidlineLeft, Ui.bookFont);
            var tgtRt = Ui.Px(title, "Target", 340, 0, W - 20 - 340 - 70, 52);
            targetLabel = Ui.Text(tgtRt, "Text", "", 20, Ui.InkSoft, TextAlignmentOptions.MidlineRight);
            var close = Ui.IconButton(title, "Close", W - 20 - 56, 8, 38, "close", Hide);
            close.icon.color = Ui.Ink; close.hover = Ui.KeyHover; close.pressed = Ui.KeyDown;

            keysArea = Ui.Px(paper, "Keys", 14, 62, W - 48, H - 20 - 70);
            panel.SetVisible(false);
            Build();
        }

        public void Show(IKeyTarget t)
        {
            target = t;
            if (!panel.Visible) panel.SetVisible(true);
            panel.SetupCamera();
            UpdateLabel();
        }

        public void Hide() { panel.SetVisible(false); }

        public void UpdateLabel() { if (targetLabel != null) targetLabel.text = target != null ? target.Describe : ""; }

        public void Update() { panel.Update(); }

        // ---------------- layout ----------------
        private List<List<KeyDef>> Layout()
        {
            var rows = new List<List<KeyDef>>();
            Func<string, KeyDef> ch = s => new KeyDef { label = s, value = s, width = 1 };
            if (!symbols)
            {
                rows.Add(Row("1234567890", ch, new KeyDef { icon = "backspace", width = 1.6f, action = () => { if (target != null) target.Backspace(); }, repeat = true }));
                rows.Add(Row("qwertyuiop", ch));
                rows.Add(Row("asdfghjkl", ch, new KeyDef { icon = "enter", width = 1.8f, action = () => { if (target != null) target.Enter(); }, highlight = true }));
                var r3 = new List<KeyDef> { new KeyDef { icon = "shift", width = 1.6f, action = ToggleShift, highlight = shift || caps } };
                r3.AddRange(Row("zxcvbnm,.", ch));
                r3.Add(new KeyDef { label = "-", value = "-", width = 1 });
                rows.Add(r3);
            }
            else
            {
                rows.Add(Row("1234567890", ch, new KeyDef { icon = "backspace", width = 1.6f, action = () => { if (target != null) target.Backspace(); }, repeat = true }));
                rows.Add(Row("!@#$%^&*()", ch));
                rows.Add(Row("-_=+[]{};", ch, new KeyDef { icon = "enter", width = 1.8f, action = () => { if (target != null) target.Enter(); }, highlight = true }));
                rows.Add(Row(":'\"/?\\|<>~", ch));
            }
            rows.Add(new List<KeyDef>
            {
                new KeyDef { label = symbols ? "ABC" : "?123", width = 1.6f, action = () => { symbols = !symbols; Build(); } },
                new KeyDef { label = "@", value = "@", width = 1 },
                new KeyDef { label = "Space", value = " ", width = 4.6f },
                new KeyDef { label = ".com", value = ".com", width = 1.5f },
                new KeyDef { label = "/", value = "/", width = 1 },
                new KeyDef { icon = "left", width = 1, action = () => { if (target != null) target.Arrow(false); }, repeat = true },
                new KeyDef { icon = "right", width = 1, action = () => { if (target != null) target.Arrow(true); }, repeat = true },
            });
            return rows;
        }

        private static List<KeyDef> Row(string chars, Func<string, KeyDef> make, params KeyDef[] extra)
        {
            var r = new List<KeyDef>();
            foreach (char c in chars) r.Add(make(c.ToString()));
            r.AddRange(extra);
            return r;
        }

        private void ToggleShift()
        {
            // double tap = caps lock
            if (Time.unscaledTime - lastShiftTap < 0.35f) { caps = !caps; shift = false; }
            else shift = !shift;
            lastShiftTap = Time.unscaledTime;
            Build();
        }

        private void Build()
        {
            for (int i = keysArea.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(keysArea.GetChild(i).gameObject);
            var rows = Layout();
            float areaW = keysArea.sizeDelta.x, areaH = keysArea.sizeDelta.y;
            float gap = 8f;
            float rowH = (areaH - gap * (rows.Count - 1)) / rows.Count;
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                float units = 0; foreach (var k in row) units += k.width;
                float unitW = (areaW - gap * (row.Count - 1)) / Mathf.Max(units, 11f);
                float rowW = units * unitW + gap * (row.Count - 1);
                float x = (areaW - rowW) / 2f, y = r * (rowH + gap);
                foreach (var k in row)
                {
                    float w = k.width * unitW;
                    MakeKey(k, x, y, w, rowH);
                    x += w + gap;
                }
            }
        }

        private void MakeKey(KeyDef k, float x, float y, float w, float h)
        {
            var rt = Ui.Px(keysArea, "Key_" + (k.label ?? k.icon), x, y, w, h);
            var face = Ui.Box(rt, k.highlight ? Ui.ParchmentDark : Ui.KeyFace, 20, 0, 20f / 10f);
            face.raycastTarget = true;
            var edge = Ui.Rect(rt, "Edge", 0, 0, 1, 1);
            Ui.Box(edge, new Color(Ui.Ink.r, Ui.Ink.g, Ui.Ink.b, 0.55f), 20, 3, 20f / 10f);
            var c = rt.gameObject.AddComponent<Clickable>();
            c.background = face;
            c.normal = face.color; c.hover = Ui.KeyHover; c.pressed = Ui.KeyDown;
            c.repeat = k.repeat;
            if (k.icon != null)
            {
                var icRt = Ui.Rect(rt, "Icon", 0.5f, 0.5f, 0.5f, 0.5f);
                icRt.sizeDelta = new Vector2(h * 0.62f, h * 0.62f);
                var img = icRt.gameObject.AddComponent<Image>();
                img.sprite = Ui.Icon(k.icon); img.color = Ui.Ink; img.preserveAspect = true; img.raycastTarget = false;
            }
            else
            {
                string label = k.label;
                bool upper = (shift || caps) && !symbols && label.Length == 1 && char.IsLetter(label[0]);
                if (upper) label = label.ToUpperInvariant();
                var t = Ui.Text(rt, "Label", label, label.Length > 1 ? 24 : 32, Ui.Ink, TextAlignmentOptions.Center);
                c.label = t;
            }
            if (k.action != null) c.onClick = k.action;
            else
            {
                string v = k.value;
                c.onClick = () =>
                {
                    if (target == null) return;
                    string s = v;
                    if ((shift || caps) && !symbols && s.Length == 1 && char.IsLetter(s[0])) s = s.ToUpperInvariant();
                    target.Type(s);
                    if (shift) { shift = false; Build(); }
                };
            }
        }
    }
}
