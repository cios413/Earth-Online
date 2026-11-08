using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EarthOL.BodyThrow
{
    /// <summary>Per-person screen-space cartoon rigs driven independently of frozen AR anchors.</summary>
    [DisallowMultipleComponent]
    public sealed class Cartoon2DCanvas : MonoBehaviour
    {
        Camera m_Camera;
        PersonTrackingManager m_Tracking;
        Canvas m_Canvas;
        RectTransform m_CanvasRect;
        Sprite m_BodySprite, m_HeadSprite, m_TorsoSprite, m_PairedSprite;
        readonly List<Sprite> m_OwnedSprites = new List<Sprite>();
        readonly Dictionary<int, Cartoon2DRig> m_Rigs = new Dictionary<int, Cartoon2DRig>();
        readonly List<int> m_Remove = new List<int>();
        readonly Stack<Cartoon2DRig> m_Pool = new Stack<Cartoon2DRig>();
        readonly HashSet<int> m_Active = new HashSet<int>();
        readonly HashSet<int> m_Selected = new HashSet<int>();
        readonly Dictionary<int,JellySkeletonPalette> m_Palettes = new Dictionary<int,JellySkeletonPalette>();
        public int RigCount => m_Rigs.Count;
        public bool IsPersonActivated(int id) => m_Selected.Contains(id);
        public void ActivatePerson(int id)
        {
            m_Selected.Add(id);
            var palette=JellySkeletonPalette.RandomPair();
            if(m_Palettes.TryGetValue(id,out var previous))
                while(palette.body==previous.body && palette.limbs==previous.limbs)palette=JellySkeletonPalette.RandomPair();
            m_Palettes[id]=palette;
            if(m_Rigs.TryGetValue(id,out var rig) && rig!=null)rig.SetPalette(palette);
            Refresh();
        }
        public JellySkeletonPalette PaletteFor(int id) => m_Palettes[id];

        public void Configure(Camera camera, PersonTrackingManager tracking)
        {
            m_Camera = camera;
            m_Tracking = tracking;
            var canvasObject = new GameObject("Cartoon2DCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            m_Canvas = canvasObject.GetComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_Canvas.overrideSorting = true;
            m_Canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            m_CanvasRect = (RectTransform)canvasObject.transform;
            m_BodySprite = LoadArtwork("SingleBone");
            m_HeadSprite = LoadArtwork("Skull");
            m_TorsoSprite = LoadArtwork("RibcagePelvis");
            m_PairedSprite = LoadArtwork("PairedBone");
        }

        void LateUpdate() => Refresh();
        public void Refresh()
        {
            if (m_Camera == null || m_Tracking == null) return;
            var persons = m_Tracking.Persons;
            m_Active.Clear();
            float scale = m_Canvas != null ? m_Canvas.scaleFactor : 1f;
            foreach (var person in persons)
            {
                if (person == null || !m_Selected.Contains(person.personId) || !person.IsValid || person.trackingConfidence < 0.20f) continue;
                m_Active.Add(person.personId);
                if (!m_Rigs.TryGetValue(person.personId, out var rig) || rig == null)
                {
                    if (m_Pool.Count > 0)
                    {
                        rig = m_Pool.Pop();
                        rig.ReassignPerson(person.personId);
                    }
                    else
                    {
                        var go = new GameObject("Cartoon Person " + person.personId, typeof(RectTransform));
                        rig = go.AddComponent<Cartoon2DRig>();
                    }
                    rig.Configure(person.personId, m_CanvasRect, m_BodySprite, m_HeadSprite, m_TorsoSprite, m_PairedSprite);
                    rig.SetPalette(m_Palettes[person.personId]);
                    m_Rigs[person.personId] = rig;
                }
                rig.UpdatePose(person, m_Camera, m_CanvasRect, scale);
            }
            m_Remove.Clear();
            foreach (var item in m_Rigs) if (!m_Active.Contains(item.Key)) m_Remove.Add(item.Key);
            foreach (int id in m_Remove)
            {
                var rig = m_Rigs[id];
                m_Rigs.Remove(id);
                if (rig != null)
                {
                    rig.SetVisible(false);
                    rig.gameObject.SetActive(false);
                    rig.transform.SetParent(m_CanvasRect, false);
                    m_Pool.Push(rig);
                }
            }
        }

        void OnGUI()
        {
            if (m_Camera == null || m_Tracking == null) return;
            Color old = GUI.color;
            GUI.color = new Color(.25f, .9f, .6f, .8f);
            foreach (var person in m_Tracking.Persons)
            {
                if (person.trackingConfidence < .2f) continue;
                Vector3 lower = m_Camera.ViewportToScreenPoint(person.screenRect.min);
                Vector3 upper = m_Camera.ViewportToScreenPoint(person.screenRect.max);
                var rect = Rect.MinMaxRect(lower.x, Screen.height-upper.y, upper.x, Screen.height-lower.y);
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.yMax-2f, rect.width, 2f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.y, 2f, rect.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.xMax-2f, rect.y, 2f, rect.height), Texture2D.whiteTexture);
                GUI.Label(new Rect(rect.x, rect.y-25f, 220f,25f), "人物 " + person.personId + (m_Selected.Contains(person.personId) ? " · 已选择" : " · 点击"));
            }
            GUI.color = old;
        }

        public void ClearRigs()
        {
            foreach (var rig in m_Rigs.Values) if (rig != null) DestroyOwned(rig.gameObject);
            m_Rigs.Clear();
            while (m_Pool.Count > 0)
            {
                var rig = m_Pool.Pop();
                if (rig != null) DestroyOwned(rig.gameObject);
            }
        }

        Sprite LoadArtwork(string name)
        {
            var sprite=Resources.Load<Sprite>("CartoonJelly/"+name);
            if(sprite!=null)return sprite;
            // Allow first-run import as a plain Texture2D too; create only one shared sprite per asset.
            var texture=Resources.Load<Texture2D>("CartoonJelly/"+name);
            if(texture==null){Debug.LogError("[CartoonJelly] Missing artwork: "+name);return null;}
            sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),Vector2.one*.5f,100f,0,SpriteMeshType.FullRect);
            m_OwnedSprites.Add(sprite);
            return sprite;
        }

        void OnDestroy()
        {
            ClearRigs();
            foreach(var sprite in m_OwnedSprites)if(sprite!=null)DestroyOwned(sprite);
            if (m_Canvas != null) DestroyOwned(m_Canvas.gameObject);
        }
        static void DestroyOwned(Object obj) { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    }
}
