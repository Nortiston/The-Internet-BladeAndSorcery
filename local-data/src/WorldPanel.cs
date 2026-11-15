using System;
using System.Collections.Generic;
using ThunderRoad;
using UnityEngine;
using UnityEngine.EventSystems;
using PointerEventData = UnityEngine.EventSystems.PointerEventData;
using UnityEngine.UI;

namespace TheInternet
{
    /// <summary>
    /// A floating window in the world (used for the popped-out browser and the keyboard).
    /// It works with the Blade &amp; Sorcery pointer and can be moved by pointing at its title bar,
    /// holding the trigger and moving your hand.
    /// </summary>
    public class WorldPanel
    {
        public GameObject go;
        public Canvas canvas;
        public RectTransform rect;
        public RectTransform content;
        public PanelInteraction interaction;
        public RectTransform moveBar;
        public float metresWide;
        private BoxCollider pointerCollider;

        private static int uiLayer = -1;
        private static bool colliderIsTrigger = true;
        private static readonly List<Type> raycasterTypes = new List<Type>();

        public bool Visible { get { return go != null && go.activeSelf; } }

        public WorldPanel(string name, Vector2 sizeUnits, float metresWide)
        {
            LearnFromBook();
            go = new GameObject(name, typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(go);
            if (uiLayer >= 0) go.layer = uiLayer;
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 0;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            go.AddComponent<GraphicRaycaster>();
            foreach (var t in raycasterTypes) if (go.GetComponent(t) == null) { try { go.AddComponent(t); } catch { } }
            rect = (RectTransform)go.transform;
            rect.sizeDelta = sizeUnits;
            this.metresWide = metresWide;
            ApplyScale();
            content = Ui.Rect(rect, "Front content", 0, 0, 1, 1);
            interaction = go.AddComponent<PanelInteraction>();
            interaction.Initialize(this);

            // Collider so the pointer "sees" the window (the game only points at PointerActive colliders).
            // Like the book: on the "UI" layer, which the pointer sees (so the cursor shows on top of the
            // window) but the click raycaster ignores (so clicks still reach the buttons under it).
            var colGo = new GameObject("PointerCollider");
            int uiPhysicsLayer = LayerMask.NameToLayer("UI");
            colGo.layer = uiPhysicsLayer >= 0 ? uiPhysicsLayer : go.layer;
            colGo.transform.SetParent(go.transform, false);
            try { colGo.tag = "PointerActive"; } catch (Exception e) { InternetScript.Warn("PointerActive tag: " + e.Message); }
            pointerCollider = colGo.AddComponent<BoxCollider>();
            pointerCollider.isTrigger = colliderIsTrigger;
            ResizeCollider();
            BuildMoveBar();
            SetupCamera();
        }

        private void BuildMoveBar()
        {
            moveBar = Ui.Rect(content, "Move bar", 0.5f, 0, 0.5f, 0);
            moveBar.sizeDelta = new Vector2(260, 44);
            moveBar.anchoredPosition = new Vector2(0, -30);
            Ui.Box(moveBar, Color.clear).raycastTarget = true;
            var pill = Ui.Rect(moveBar, "White capsule", 0.08f, 0.34f, 0.92f, 0.66f);
            Ui.Box(pill, Color.white, 20, 0, 20f / 7f);
            var drag = moveBar.gameObject.AddComponent<DragHandle>();
            drag.target = go.transform;
            // The pointer needs a tagged physics surface outside the screen too.
            var colliderGo = new GameObject("Move bar pointer collider");
            colliderGo.transform.SetParent(moveBar, false);
            colliderGo.layer = pointerCollider.gameObject.layer;
            try { colliderGo.tag = "PointerActive"; } catch { }
            var collider = colliderGo.AddComponent<BoxCollider>();
            collider.size = new Vector3(260, 44, 10);
            collider.isTrigger = colliderIsTrigger;
        }

        public bool ContainsPointer(Vector2 point)
        {
            if (rect.rect.Contains(point)) return true;
            if (moveBar == null) return false;
            var barPoint = moveBar.InverseTransformPoint(rect.TransformPoint(new Vector3(point.x, point.y, 0)));
            return moveBar.rect.Contains(new Vector2(barPoint.x, barPoint.y));
        }

        /// <summary>Copy the layer / trigger setting / raycasters the book menu uses, so our windows behave the same.</summary>
        private static void LearnFromBook()
        {
            if (uiLayer >= 0) return;
            try
            {
                var menu = UIPlayerMenu.instance;
                if (menu == null) return;
                foreach (var c in menu.GetComponentsInChildren<Collider>(true))
                {
                    if (!c.CompareTag("PointerActive") || c.gameObject.layer != LayerMask.NameToLayer("UI")) continue;
                    colliderIsTrigger = c.isTrigger;
                    break;
                }
                var canvas = menu.GetComponentInChildren<Canvas>(true);
                if (canvas != null)
                {
                    if (uiLayer < 0) uiLayer = canvas.gameObject.layer;
                    foreach (var rc in canvas.rootCanvas.GetComponents<BaseRaycaster>())
                        if (!(rc is GraphicRaycaster) && !raycasterTypes.Contains(rc.GetType())) raycasterTypes.Add(rc.GetType());
                }
                InternetScript.Log("Window setup: layer " + (uiLayer >= 0 ? LayerMask.LayerToName(uiLayer) : "?") + " trigger=" + colliderIsTrigger);
            }
            catch (Exception e) { InternetScript.Warn("LearnFromBook: " + e.Message); }
        }

        public void SetupCamera()
        {
            try { ThunderRoad.PointerInputModule.SetupCanvasWithUICamera(canvas); } catch { }
        }

        public void ApplyScale()
        {
            float s = metresWide / rect.sizeDelta.x;
            rect.localScale = new Vector3(s, s, s);
        }

        public void ResizeCollider()
        {
            if (pointerCollider == null) return;
            // front face sits ~5 units in front of the UI, so the cursor is drawn on top of the window
            pointerCollider.size = new Vector3(rect.sizeDelta.x, rect.sizeDelta.y, 10f);
            pointerCollider.center = Vector3.zero;
        }

        public void SetVisible(bool v) { if (go != null) go.SetActive(v); }

        /// <summary>Place in front of the player's head.</summary>
        public void PlaceInFront(float distance, float drop, float pitch = 0f)
        {
            Transform head = Head();
            if (head == null) return;
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = PanelInteraction.HorizontalForward(head);
            fwd.Normalize();
            Vector3 pos = head.position + fwd * distance - Vector3.up * drop;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(pitch, 0, 0);
        }

        public static Transform Head()
        {
            try { if (Player.local != null && Player.local.head != null) return Player.local.head.cam != null ? Player.local.head.cam.transform : Player.local.head.transform; } catch { }
            try { if (Player.local != null && Player.local.head != null) return Player.local.head.transform; } catch { }
            return Camera.main != null ? Camera.main.transform : null;
        }

        public void Update()
        {
            if (go == null || !go.activeSelf) return;
            if (canvas.worldCamera == null) SetupCamera();
        }

        public void Destroy()
        {
            if (go != null) UnityEngine.Object.Destroy(go);
            go = null;
        }
    }

    /// <summary>Point at it, hold the trigger and move your hand to move the window.</summary>
    public class DragHandle : MonoBehaviour, IPointerDownHandler
    {
        public Transform target;
        public bool faceHead = true;
        private bool dragging;
        private float grabDist;
        private Vector3 grabOffset;
        private Side side;
        private PanelInteraction interaction;

        public void OnPointerDown(PointerEventData e)
        {
            var p = Pointer.GetActive();
            if (p == null || target == null) return;
            side = Pointer.activeSide;
            var ray = new Ray(p.transform.position, p.transform.forward);
            grabDist = Vector3.Distance(ray.origin, e.pointerCurrentRaycast.worldPosition != Vector3.zero ? e.pointerCurrentRaycast.worldPosition : target.position);
            grabOffset = target.position - ray.GetPoint(grabDist);
            dragging = true;
            interaction = target.GetComponent<PanelInteraction>();
            if (interaction != null) interaction.manipulating = true;
        }

        private void LateUpdate()
        {
            if (!dragging) return;
            var hand = PlayerControl.GetHand(side);
            var p = Pointer.GetPointer(side) ?? Pointer.GetActive();
            if (hand == null || p == null || !hand.usePressed) { EndDrag(); return; }
            var ray = new Ray(p.transform.position, p.transform.forward);
            // thumbstick pushes the window away / pulls it closer while dragging
            float speed = interaction != null && interaction.panel != null ? Mathf.Max(1.2f, interaction.panel.metresWide) : 1.2f;
            grabDist = Mathf.Max(0.3f, grabDist + hand.JoystickAxis.y * speed * Time.unscaledDeltaTime);
            Vector3 pos = ray.GetPoint(grabDist) + grabOffset;
            target.position = pos;
            if (faceHead)
            {
                var head = WorldPanel.Head();
                if (head != null)
                {
                    Vector3 away = pos - head.position;
                    if (away.sqrMagnitude > 0.01f) target.rotation = PanelInteraction.Upright(away);
                }
            }
        }
        private void EndDrag()
        {
            dragging = false;
            if (interaction != null) { interaction.manipulating = false; interaction.CaptureOffset(); }
        }
        private void OnDisable() { if (dragging) EndDrag(); }
    }
}
