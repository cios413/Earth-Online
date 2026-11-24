using EarthOL.BodyThrow;
using UnityEditor;
using UnityEngine;

namespace EarthOL.EditorTools
{
    /// <summary>Live isolated preview of the same mesh animation used by person impacts.</summary>
    public sealed class DreamGrenadePreview : EditorWindow
    {
        PreviewRenderUtility m_Preview;
        GameObject m_Shot;
        DreamGrenadeProjectile m_Dream;
        double m_LastTime;
        float m_Age;
        bool m_Playing = true;

        [MenuItem("Tools/Earth OL/Preview dream grenade splash")]
        public static void Open()
        {
            var window = GetWindow<DreamGrenadePreview>("梦境手雷预览");
            window.minSize = new Vector2(360,480);
            window.Show();
        }

        void OnEnable()
        {
            m_LastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (m_Playing && m_Dream != null)
            {
                m_Age += (float)(now-m_LastTime);
                if (m_Age > m_Dream.TotalImpactSeconds+.4f) m_Age = 0;
                Repaint();
            }
            m_LastTime = now;
        }

        void EnsurePreview()
        {
            if (m_Preview != null) return;
            var prefab = Resources.Load<BodyProjectile>("Throwables/DreamGrenade");
            if (prefab == null) return;
            m_Preview = new PreviewRenderUtility();
            m_Shot = Instantiate(prefab.gameObject);
            m_Shot.GetComponent<BodyProjectile>().enabled = false;
            m_Shot.transform.localScale = Vector3.one*.25f;
            m_Shot.transform.position = new Vector3(0,-.30f,2);
            m_Dream = m_Shot.GetComponent<DreamGrenadeProjectile>();
            m_Preview.AddSingleGO(m_Shot);
            var camera = m_Preview.camera;
            camera.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            camera.orthographic = true; camera.orthographicSize = .49f;
            camera.nearClipPlane = .01f; camera.farClipPlane = 5;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.065f,.055f,.12f,1);
            camera.allowHDR = false;
        }

        void OnGUI()
        {
            EnsurePreview();
            if (m_Dream == null) { EditorGUILayout.HelpBox("请先生成梦境手雷资源。",MessageType.Info); return; }
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button(m_Playing ? "暂停" : "播放",EditorStyles.toolbarButton)) m_Playing = !m_Playing;
            if (GUILayout.Button("重播",EditorStyles.toolbarButton)) { m_Age = 0; m_Playing = true; }
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{m_Age:F2} s",EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUI.BeginChangeCheck();
            float scrubAge = EditorGUILayout.Slider("动画时间",Mathf.Min(m_Age,m_Dream.TotalImpactSeconds),0,m_Dream.TotalImpactSeconds);
            if (EditorGUI.EndChangeCheck()) { m_Age = scrubAge; m_Playing = false; }
            Rect area = GUILayoutUtility.GetRect(100,100,GUILayout.ExpandWidth(true),GUILayout.ExpandHeight(true));
            float side = Mathf.Min(area.width,area.height);
            Rect image = new Rect(area.center.x-side*.5f,area.center.y-side*.5f,side,side);
            if (Event.current.type != EventType.Repaint) return;
            m_Dream.RenderImpact(m_Preview.camera,m_Age);
            m_Preview.BeginPreview(image,GUIStyle.none);
            m_Preview.camera.Render();
            GUI.DrawTexture(image,m_Preview.EndPreview(),ScaleMode.ScaleToFit,false);
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            m_Preview?.Cleanup(); m_Preview = null;
            if (m_Shot != null) DestroyImmediate(m_Shot);
            m_Shot = null; m_Dream = null;
        }
    }
}
