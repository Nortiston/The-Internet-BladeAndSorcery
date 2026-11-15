using ThunderRoad;
using UnityEngine;
using UnityEngine.EventSystems;
using PointerEventData=UnityEngine.EventSystems.PointerEventData;

namespace TheInternet
{
    public class TabDragHandle : MonoBehaviour, IPointerDownHandler
    {
        public BrowserView view;
        public int index;
        public Clickable button;
        private bool held,dragging;
        private Side side;
        private float distance;
        private Vector3 start;
        private RectTransform preview;
        public void OnPointerDown(PointerEventData e)
        {
            var p=Pointer.GetActive();if(p==null||view==null||!view.IsFloating)return;
            side=Pointer.activeSide;var ray=new Ray(p.transform.position,p.transform.forward);float d;
            if(!new Plane(view.root.forward,view.root.position).Raycast(ray,out d))return;
            distance=d;start=view.root.InverseTransformPoint(ray.GetPoint(d));held=true;
        }
        private void LateUpdate()
        {
            if(!held)return;if(view==null||!view.IsFloating){Finish(null);return;}var p=Pointer.GetPointer(side);var hand=PlayerControl.GetHand(side);
            if(p==null||hand==null){Finish(null);return;}
            var ray=new Ray(p.transform.position,p.transform.forward);
            var point=ray.GetPoint(distance);
            if(!dragging&&Vector3.Distance(view.root.InverseTransformPoint(point),start)>60f)
            {
                dragging=true;button.interactable=false;
                preview=Ui.Rect(view.root,"Moving tab",.5f,.5f,.5f,.5f);preview.sizeDelta=new Vector2(220,38);
                Ui.Box(preview,Ui.TabActive,12);
                var label=Ui.Text(preview,"Text",index<view.Engine.TabTitles.Length?view.Engine.TabTitles[index]:"Tab",17,Ui.TextDark,TMPro.TextAlignmentOptions.Center);label.maskable=false;
            }
            if(preview!=null)preview.position=point-view.root.forward*0.005f;
            if(!hand.usePressed)Finish(ray);
        }
        private void Finish(Ray? ray)
        {
            held=false;
            if(dragging&&ray.HasValue)
            {
                BrowserView target=null;float nearest=float.MaxValue;
                foreach(var candidate in new System.Collections.Generic.List<BrowserView>(BrowserView.OpenViews))
                {
                    if(!candidate.IsFloating||candidate.root==null||!candidate.root.gameObject.activeInHierarchy)continue;
                    if(Vector3.Dot(ray.Value.direction,candidate.root.forward)<=0)continue;
                    float d;if(!new Plane(candidate.root.forward,candidate.root.position).Raycast(ray.Value,out d))continue;
                    var local=candidate.root.InverseTransformPoint(ray.Value.GetPoint(d));var rect=candidate.root.rect;
                    if(local.x>=rect.xMin-30&&local.x<=rect.xMax+30&&local.y<=rect.yMax+50&&local.y>=rect.yMax-BrowserView.TabH-50&&d<nearest){nearest=d;target=candidate;}
                }
                if(target!=null&&target!=view)view.Engine.MoveTab(index,target.Engine.WindowId);
                else if(target==null)view.Engine.DetachTab(index,ray.Value.GetPoint(Mathf.Max(.4f,distance)));
            }
            dragging=false;if(button!=null)button.interactable=true;
            if(preview!=null)Destroy(preview.gameObject);preview=null;
        }
        private void OnDisable(){if(held)Finish(null);}
    }
}
