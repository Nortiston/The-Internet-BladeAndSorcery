using ThunderRoad;
using UnityEngine;
using UnityEngine.EventSystems;
using PointerEventData = UnityEngine.EventSystems.PointerEventData;
using UnityEngine.UI;

namespace TheInternet
{
    // Session-only preferences: deliberately never serialized or saved.
    public class WindowSettings
    {
        public const float DefaultWidth = 1.3f;
        public bool followPlayer, facePlayer;
        public float distance = 1.1f, height = -0.1f, horizontal;
        public float clickDistance = 4f;
        public bool hideLoadingBar;
        public float cursorScale = 1f;
        public float EffectiveClickDistance { get { return (float)System.Math.Min(float.MaxValue, (double)clickDistance * cursorScale); } }
    }

    public class PanelInteraction : MonoBehaviour
    {
        public WorldPanel panel;
        public WindowSettings settings;
        public WindowSettings pointerSettings;
        public bool manipulating;
        private RectTransform cursor;
        private CanvasGroup front;
        private static Vector3 lastForward = Vector3.forward;

        public static Vector3 HorizontalForward(Transform head)
        {
            var f = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (f.sqrMagnitude > 0.01f) lastForward = f.normalized;
            return lastForward;
        }

        public static Quaternion Upright(Vector3 away)
        {
            var flat = Vector3.ProjectOnPlane(away, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f) flat = lastForward;
            var pitch = -Mathf.Clamp(Mathf.Atan2(away.y, flat.magnitude) * Mathf.Rad2Deg, -80f, 80f);
            return Quaternion.LookRotation(flat.normalized, Vector3.up) * Quaternion.Euler(pitch, 0, 0);
        }

        public void Initialize(WorldPanel owner)
        {
            panel = owner;
            front = panel.content.gameObject.AddComponent<CanvasGroup>();
            var back = Ui.Rect(panel.rect, "Opaque back", 0, 0, 1, 1);
            back.SetAsFirstSibling();
            back.localPosition = new Vector3(0, 0, 12);
            Ui.Box(back, Ui.TabStrip);
            cursor = Ui.Rect(panel.rect, "Pointer overlay", 0.5f, 0.5f, 0.5f, 0.5f);
            cursor.sizeDelta = new Vector2(18, 18);
            var c = cursor.gameObject.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder = 32760;
            Ui.Box(cursor, Color.white, 12);
            var dot = Ui.Rect(cursor, "Dot", 0.25f, 0.25f, 0.75f, 0.75f);
            Ui.Box(dot, Ui.Accent, 12);
            cursor.gameObject.SetActive(false);
        }

        public void CaptureOffset()
        {
            var head = WorldPanel.Head();
            if (settings == null || head == null || !settings.followPlayer) return;
            var f = HorizontalForward(head);
            var delta = transform.position - head.position;
            settings.distance = Mathf.Clamp(Vector3.Dot(delta, f), 0.4f, 4f);
            settings.horizontal = Mathf.Clamp(Vector3.Dot(delta, Vector3.Cross(Vector3.up, f)), -2f, 2f);
            settings.height = Mathf.Clamp(delta.y, -1.5f, 1.5f);
        }

        private void LateUpdate()
        {
            if (panel == null) return;
            var head = WorldPanel.Head();
            if (head == null) { cursor.gameObject.SetActive(false); return; }
            if (!manipulating && settings != null)
            {
                if (settings.followPlayer)
                {
                    var f = HorizontalForward(head);
                    var target = head.position + f * settings.distance + Vector3.Cross(Vector3.up, f) * settings.horizontal + Vector3.up * settings.height;
                    transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
                }
                if (settings.facePlayer || settings.followPlayer)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Upright(transform.position - head.position), 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            }
            bool facingFront = Vector3.Dot(head.position - transform.position, transform.forward) < 0;
            front.alpha = facingFront ? 1 : 0;
            front.blocksRaycasts = facingFront;
            front.interactable = facingFront;
            var p = Pointer.GetActive();
            bool show = false;
            if (facingFront && p != null)
            {
                var ray = new Ray(p.transform.position, p.transform.forward);
                float d;
                if (new Plane(transform.forward, transform.position).Raycast(ray, out d))
                {
                    var local = panel.rect.InverseTransformPoint(ray.GetPoint(d));
                    // Respect nearer physics surfaces, including the other floating panel.
                    RaycastHit hit;
                    bool blocked = Physics.Raycast(ray, out hit, Mathf.Max(0, d - 0.025f)) && !hit.transform.IsChildOf(transform);
                    show = !blocked && d <= (pointerSettings != null ? pointerSettings.EffectiveClickDistance : 4f) && panel.ContainsPointer(new Vector2(local.x, local.y));
                    cursor.anchoredPosition3D = new Vector3(local.x, local.y, -20);
                }
            }
            cursor.gameObject.SetActive(show);
        }
    }

    // Trigger-drag a corner. The opposite corner stays fixed and aspect ratio is preserved.
    public class ResizeHandle : MonoBehaviour, IPointerDownHandler
    {
        public WorldPanel panel;
        public Vector2 corner;
        private bool resizing;
        private Side side;
        private Plane plane;
        private Vector3 anchor, axis;
        private float startProjection, startWidth, aspect;

        public void OnPointerDown(PointerEventData e)
        {
            var p = Pointer.GetActive();
            if (p == null || panel == null) return;
            side = Pointer.activeSide;
            var t = panel.rect;
            plane = new Plane(t.forward, t.position);
            aspect = t.sizeDelta.y / t.sizeDelta.x;
            anchor = t.TransformPoint(new Vector3(-corner.x * t.sizeDelta.x / 2, -corner.y * t.sizeDelta.y / 2, 0));
            axis = (t.right * corner.x + t.up * corner.y * aspect).normalized;
            float d;
            if (!plane.Raycast(new Ray(p.transform.position, p.transform.forward), out d)) return;
            startProjection = Vector3.Dot(p.transform.position + p.transform.forward * d - anchor, axis);
            startWidth = panel.metresWide;
            resizing = true;
            var reveal = GetComponent<ProximityReveal>();
            if (reveal != null) reveal.pinned = true;
            panel.interaction.manipulating = true;
        }

        private void LateUpdate()
        {
            if (!resizing) return;
            var hand = PlayerControl.GetHand(side);
            var p = Pointer.GetPointer(side);
            if (hand == null || p == null || !hand.usePressed) { End(); return; }
            float d;
            var ray = new Ray(p.transform.position, p.transform.forward);
            if (!plane.Raycast(ray, out d)) return;
            float delta = Vector3.Dot(ray.GetPoint(d) - anchor, axis) - startProjection;
            float width = Mathf.Max(0.05f, startWidth + delta / Mathf.Sqrt(1 + aspect * aspect));
            if (float.IsInfinity(width) || float.IsNaN(width)) return;
            panel.metresWide = width;
            panel.ApplyScale();
            panel.rect.position = anchor + (panel.rect.right * corner.x + panel.rect.up * corner.y * aspect) * panel.metresWide / 2;
        }

        private void End()
        {
            resizing = false;
            var reveal = GetComponent<ProximityReveal>();
            if (reveal != null) reveal.pinned = false;
            if (panel != null) { panel.interaction.manipulating = false; panel.interaction.CaptureOffset(); }
        }
        private void OnDisable() { if (resizing) End(); }
    }
}
