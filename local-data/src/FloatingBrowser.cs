using System;
using UnityEngine;

namespace TheInternet
{
    public sealed class FloatingBrowser
    {
        public readonly BrowserEngine engine;
        public readonly BrowserView view;
        public readonly WorldPanel panel;
        public readonly WindowSettings settings;
        private BrowserAudioOutput audio;
        public FloatingBrowser(BrowserEngine engine,WindowSettings template,Vector3 position)
        {
            this.engine=engine;
            settings=new WindowSettings {clickDistance=template.clickDistance,hideLoadingBar=template.hideLoadingBar,facePlayer=template.facePlayer,followPlayer=template.followPlayer};
            panel=new WorldPanel("TheInternet Window "+engine.WindowId,new Vector2(BrowserView.W,BrowserView.H),WindowSettings.DefaultWidth);
            panel.PlaceInFront(1.2f,.1f);
            if(position!=Vector3.zero)panel.rect.position=position;
            var head=WorldPanel.Head();if(head!=null)panel.rect.rotation=PanelInteraction.Upright(panel.rect.position-head.position);
            panel.interaction.settings=settings;panel.interaction.pointerSettings=settings;panel.interaction.CaptureOffset();
            panel.content.gameObject.AddComponent<PointerRangeFilter>().settings=settings;
            view=new BrowserView(engine,panel.content,settings);view.root.SetAsFirstSibling();view.SetFloating(true);
            var drag=view.tabStrip.gameObject.AddComponent<DragHandle>();drag.target=panel.go.transform;
            var corner=Ui.Rect(panel.content,"Resize corner",0,1,0,1);corner.pivot=new Vector2(0,1);corner.sizeDelta=new Vector2(32,32);
            Ui.Box(corner,Ui.Accent,12).raycastTarget=true;
            var resize=corner.gameObject.AddComponent<ResizeHandle>();resize.panel=panel;resize.corner=new Vector2(-1,1);
            corner.gameObject.AddComponent<ProximityReveal>().settings=settings;
            view.onBigger=()=>Resize(1.15f);view.onSmaller=()=>Resize(1f/1.15f);view.onResetSize=ResetSize;
        }
        private void Resize(float factor){float width=Mathf.Max(.05f,panel.metresWide*factor);if(float.IsInfinity(width)||float.IsNaN(width))return;panel.metresWide=width;panel.ApplyScale();}
        public void ResetSize(){panel.metresWide=WindowSettings.DefaultWidth;panel.ApplyScale();settings.cursorScale=1;}
        public void Update()
        {
            settings.cursorScale=Mathf.Max(1,panel.metresWide/WindowSettings.DefaultWidth);
            view.Update();panel.Update();
            if(engine.SpatialAudioEnabled&&audio==null){var go=new GameObject("TheInternet Audio "+engine.WindowId);UnityEngine.Object.DontDestroyOnLoad(go);audio=go.AddComponent<BrowserAudioOutput>();engine.OnAudioSamples+=audio.Enqueue;engine.OnAudioReset+=audio.ResetAudio;}
            if(audio!=null)audio.Apply(view.root.position,engine.SpatialAudioEnabled,engine.Muted,panel.metresWide);
        }
        public void Destroy()
        {
            if(audio!=null){engine.OnAudioSamples-=audio.Enqueue;engine.OnAudioReset-=audio.ResetAudio;UnityEngine.Object.Destroy(audio.gameObject);}
            engine.Stop();view.Destroy();panel.Destroy();
        }
    }
}
