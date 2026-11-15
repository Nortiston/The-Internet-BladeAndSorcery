using ThunderRoad;
using UnityEngine;
using UnityEngine.UI;

namespace TheInternet
{
    public class PointerRangeFilter : MonoBehaviour, ICanvasRaycastFilter
    {
        public WindowSettings settings;
        public static bool TryPoint(RectTransform rect, float range, out Vector2 local)
        {
            local = Vector2.zero;
            var pointer = Pointer.GetActive();
            if (pointer == null || !rect.gameObject.activeInHierarchy) return false;
            var ray = new Ray(pointer.transform.position, pointer.transform.forward);
            float d;
            if (Vector3.Dot(ray.direction, rect.forward) <= 0 || !new Plane(rect.forward, rect.position).Raycast(ray, out d) || d > range) return false;
            var point = rect.InverseTransformPoint(ray.GetPoint(d));
            local = new Vector2(point.x, point.y);
            return true;
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            Vector2 point;
            return settings == null || TryPoint((RectTransform)transform, settings.EffectiveClickDistance, out point);
        }
    }

    // Keep the target alive for proximity detection, but let hidden controls pass
    // clicks through to the page. No invisible button can catch an unseen click.
    public class ProximityReveal : MonoBehaviour
    {
        public WindowSettings settings;
        public float margin = 48f;
        public bool pinned;
        private CanvasGroup group;
        private float lastNear = -10f;
        private void Awake()
        {
            group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0; group.blocksRaycasts = false; group.interactable = false;
        }
        private void Update()
        {
            Vector2 point;
            var rt = (RectTransform)transform;
            var r = rt.rect;
            r.xMin -= margin; r.xMax += margin; r.yMin -= margin; r.yMax += margin;
            if (PointerRangeFilter.TryPoint(rt, settings != null ? settings.EffectiveClickDistance : 4f, out point) && r.Contains(point)) lastNear = Time.unscaledTime;
            bool visible = pinned || Time.unscaledTime - lastNear < 0.25f;
            group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1 : 0, Time.unscaledDeltaTime * 12);
            group.blocksRaycasts = visible;
            group.interactable = visible;
        }
        private void OnDisable()
        {
            lastNear = -10; pinned = false;
            if (group != null) { group.alpha = 0; group.blocksRaycasts = false; group.interactable = false; }
        }
    }

    public class DockedCursor : MonoBehaviour
    {
        public WindowSettings settings;
        private RectTransform cursor;
        public void Initialize()
        {
            cursor = Ui.Rect(transform, "Docked pointer", 0.5f, 0.5f, 0.5f, 0.5f);
            cursor.sizeDelta = new Vector2(18, 18);
            var c = cursor.gameObject.AddComponent<Canvas>(); c.overrideSorting = true; c.sortingOrder = 32760;
            Ui.Box(cursor, Color.white, 12);
            Ui.Box(Ui.Rect(cursor, "Dot", 0.25f, 0.25f, 0.75f, 0.75f), Ui.Accent, 12);
            cursor.gameObject.SetActive(false);
        }
        private void LateUpdate()
        {
            Vector2 point;
            var bounds = (RectTransform)transform;
            bool visible = PointerRangeFilter.TryPoint(bounds, settings.EffectiveClickDistance, out point) && bounds.rect.Contains(point);
            cursor.gameObject.SetActive(visible);
            if (visible) { cursor.anchoredPosition3D = new Vector3(point.x, point.y, -20); }
        }
        private void OnDisable() { if (cursor != null) cursor.gameObject.SetActive(false); }
    }
}
