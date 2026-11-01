using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using EarthOL.ARCore;

namespace EarthOL.BodyThrow
{
    /// <summary>
    /// Tracks the selected person's screen landmarks and fits two collision proxies.
    /// Depth remains an estimate; image coordinates always use the displayed AR view.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BodyPoseTracker : MonoBehaviour
    {
        const int CaptureSize = 640;
        const float ScanInterval = 0.16f;
        const float PoseLostGrace = 0.75f;
        const float DoubleTapInterval = 0.38f;
        const float DoubleTapDistance = 80f;
        const float PartLostGrace = 0.4f;

        Camera m_Camera;
        BodyProxyRig m_Rig;
        BodyPoseDetector m_Detector;
        ARCameraBackground m_Background;
        RenderTexture m_Target;
        Texture2D m_Frame;
        Color32[] m_Pixels;
        readonly Dictionary<int, Vector2> m_Smoothed = new Dictionary<int, Vector2>();
        readonly Dictionary<int, Vector2> m_Rejected = new Dictionary<int, Vector2>();
        float m_Timer;
        float m_LastSeenTime;
        float m_SelectionStartTime;
        float m_HeadSeenTime = float.NegativeInfinity;
        float m_TorsoSeenTime = float.NegativeInfinity;
        float m_Distance;
        int m_ViewWidth;
        int m_ViewHeight;
        ScreenOrientation m_Orientation;
        BodyPoseResult m_Raw;
        Vector2 m_HeadCenter;
        Vector2 m_ShoulderCenter;
        Vector2 m_HipCenter;
        bool m_HeadValid;
        bool m_TorsoValid;
        string m_Status = "双击人物选择目标";
        float m_NextDiagnosticLog;
        BodyPoseResult m_Current;
        bool m_TrackingEnabled;
        bool m_SelectionPending;
        Vector2 m_SelectionScreenPosition;
        bool m_HasPendingTap;
        Vector2 m_PendingTap;
        float m_PendingTapTime;
        bool m_HasProjectileTap;
        Vector2 m_ProjectileTap;
        bool m_PressActive;
        bool m_PressMoved;
        int m_PressId;
        Vector2 m_PressOrigin;
        float m_PressStart;
        float m_PickerPauseStart = -1f;
        const float LongPressDuration = 0.48f;
        const float TapMovement = 40f;

        public bool DeformationMode { get; private set; }
        public bool DeformationGestureActive { get; set; }
        public bool HasPose => m_Current.IsValid;
        public BodyPoseResult Current => m_Current;
        public bool TrackingEnabled => m_TrackingEnabled;
        public bool CanThrow => !DeformationMode && isActiveAndEnabled && m_TrackingEnabled && HasPose &&
            m_Rig != null && m_Rig.HasTrackedPart && Time.realtimeSinceStartup - m_LastSeenTime <= PoseLostGrace;
        public bool ShowDiagnostics { get; set; }

        public void Configure(Camera camera, BodyProxyRig rig)
        {
            m_Camera = camera;
            m_Rig = rig;
            m_Background = camera.GetComponent<ARCameraBackground>();
            m_Rig.HideTrackedParts();
        }

        private void Start()
        {
            m_Detector = new BodyPoseDetector();
            StartCoroutine(m_Detector.Load());
        }

        private void Update()
        {
            if (!enabled || m_Camera == null || m_Rig == null) return;

            PollTapGesture();
            bool interactive = ARCoreModeOverlay.IsProjectilePickerOpen ||
                DeformationMode && DeformationGestureActive && HasPose && !m_SelectionPending;
            if (PausePoseForPicker(interactive, Time.realtimeSinceStartup)) return;
            ExpireParts();

            if (m_TrackingEnabled && Time.realtimeSinceStartup - m_LastSeenTime > PoseLostGrace)
                ResetTracking("人物丢失，请重新双击选择");
            if (m_SelectionPending && Time.realtimeSinceStartup - m_SelectionStartTime > 3f)
                ResetTracking("未找到可靠的人物，请重新双击");

            if (!m_TrackingEnabled && !m_SelectionPending) return;
            if (!m_Rig.gameObject.activeInHierarchy && !m_SelectionPending) return;

            m_Timer += Time.deltaTime;
            if (m_Timer < ScanInterval) return;
            m_Timer = 0f;
            if (m_Detector != null && m_Detector.Ready)
            {
                try { Scan(); }
                catch (System.Exception exception)
                {
                    Debug.LogException(exception);
                    ResetTracking("姿态检测失败，请检查日志后重新选择");
                }
            }
        }

        public bool TryConsumeProjectileTap(out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;
            if (!m_HasProjectileTap) return false;
            screenPosition = m_ProjectileTap;
            m_HasProjectileTap = false;
            return true;
        }

        public void ClearPendingInput()
        {
            m_HasPendingTap = m_HasProjectileTap = false;
        }

        public void SetDeformationMode(bool deform)
        {
            DeformationMode = deform;
            m_PressActive = false;
            ClearPendingInput();
        }

        private bool PausePoseForPicker(bool open, float now)
        {
            if (open)
            {
                if (m_PickerPauseStart < 0f) m_PickerPauseStart = now;
                return true;
            }
            if (m_PickerPauseStart >= 0f)
            {
                float paused = Mathf.Max(0f, now - m_PickerPauseStart);
                m_LastSeenTime += paused;
                m_HeadSeenTime += paused;
                m_TorsoSeenTime += paused;
                m_SelectionStartTime += paused;
                m_PickerPauseStart = -1f;
                m_Timer = 0f;
            }
            return false;
        }

        private void PollTapGesture()
        {
            bool hasPointer = BodyPointerFrame.TryRead(out var frame);
            ProcessWorldPointer(frame, hasPointer, Time.unscaledTime);
        }

        private void ProcessWorldPointer(BodyPointerFrame frame, bool hasPointer, float now)
        {
            if (ARCoreModeOverlay.IsRadialHoldOpen)
            {
                ClearPendingInput();
                if (hasPointer && frame.id == m_PressId)
                {
                    ARCoreModeOverlay.Instance.UpdateRadialSelection(frame.position);
                    if (frame.released || frame.canceled)
                    {
                        ARCoreModeOverlay.Instance.EndRadialSelection(frame.canceled);
                        m_PressActive = false;
                    }
                }
                else
                {
                    ARCoreModeOverlay.Instance.EndRadialSelection(true);
                    m_PressActive = false;
                }
                return;
            }
            if (ARCoreModeOverlay.BlocksWorldInput)
            {
                m_PressActive = false;
                ClearPendingInput();
                return;
            }
            if (hasPointer && frame.down)
            {
                if (ARCoreModeOverlay.IsPointOnOverlay(frame.position))
                {
                    ClearPendingInput();
                    m_PressActive = false;
                    return;
                }
                m_PressActive = true;
                m_PressMoved = false;
                m_PressId = frame.id;
                m_PressOrigin = frame.position;
                m_PressStart = now;
            }
            if (m_PressActive && hasPointer && frame.id == m_PressId)
            {
                m_PressMoved |= Vector2.Distance(frame.position, m_PressOrigin) > TapMovement;
                if (!DeformationMode && frame.held && !m_PressMoved && now - m_PressStart >= LongPressDuration && ARCoreModeOverlay.Instance != null)
                {
                    ClearPendingInput();
                    ARCoreModeOverlay.Instance.BeginRadialSelection(frame.position);
                    return;
                }
                if (frame.released || frame.canceled)
                {
                    m_PressActive = false;
                    if (!frame.canceled && !m_PressMoved && now - m_PressStart < LongPressDuration)
                        HandleTap(frame.position, now);
                }
            }
            else if (m_PressActive && !hasPointer) m_PressActive = false;

            if (!m_PressActive && m_HasPendingTap && now - m_PendingTapTime >= DoubleTapInterval)
            {
                m_HasPendingTap = false;
                if (CanThrow)
                {
                    m_HasProjectileTap = true;
                    m_ProjectileTap = m_PendingTap;
                }
            }
        }

        private void HandleTap(Vector2 screenPosition, float now)
        {
            bool isDoubleTap = m_HasPendingTap &&
                               now - m_PendingTapTime <= DoubleTapInterval &&
                               Vector2.Distance(screenPosition, m_PendingTap) <= DoubleTapDistance;

            if (isDoubleTap)
            {
                m_HasPendingTap = false;
                m_HasProjectileTap = false;
                m_SelectionScreenPosition = screenPosition;
                m_SelectionPending = true;
                m_SelectionStartTime = Time.realtimeSinceStartup;
                m_TrackingEnabled = false;
                m_Current = default;
                m_Smoothed.Clear();
                m_Rejected.Clear();
                m_Distance = 0f;
                m_HeadSeenTime = m_TorsoSeenTime = float.NegativeInfinity;
                m_Raw = default;
                m_HeadValid = m_TorsoValid = false;
                m_Status = "正在选择人物";
                m_Rig.HideTrackedParts();
                Debug.Log("[Pose] 双击收到，开始在点击位置附近选择人物");
                return;
            }

            m_HasPendingTap = true;
            m_PendingTap = screenPosition;
            m_PendingTapTime = now;
        }

        private void Scan()
        {
            if (!TryCapture()) return;
            var candidates = m_Detector.Detect(m_Pixels, m_Frame.width, m_Frame.height, 6);
            if (candidates.Length == 0) return;

            BodyPoseResult selected;
            if (m_SelectionPending)
            {
                selected = SelectCandidateAtScreenPosition(candidates, m_SelectionScreenPosition);
                if (!selected.IsValid) return;

                m_SelectionPending = false;
                m_TrackingEnabled = true;
                if (m_Rig != null) m_Rig.gameObject.SetActive(true);
                Debug.Log("[Pose] 已选择人物，按位置连续匹配；丢失后需重新双击");
            }
            else
            {
                selected = SelectCandidate(candidates);
            }
            if (!selected.IsValid) return;
            m_LastSeenTime = Time.realtimeSinceStartup;
            m_Raw = selected;
            Smooth(selected);
            ApplyToRig(m_Current);
        }

        private bool TryCapture()
        {
            if (m_Background == null || !m_Background.backgroundRenderingEnabled || m_Background.material == null)
            {
                m_Status = "等待 AR 相机画面";
                return false;
            }

            int viewWidth = m_Camera.pixelWidth;
            int viewHeight = m_Camera.pixelHeight;
            if (viewWidth <= 0 || viewHeight <= 0) return false;
            if (m_ViewWidth != viewWidth || m_ViewHeight != viewHeight || m_Orientation != Screen.orientation)
            {
                bool resized = m_Target != null;
                ReleaseCapture();
                m_ViewWidth = viewWidth;
                m_ViewHeight = viewHeight;
                m_Orientation = Screen.orientation;
                float factor = (float)CaptureSize / Mathf.Max(viewWidth, viewHeight);
                int width = Mathf.Max(1, Mathf.RoundToInt(viewWidth * factor));
                int height = Mathf.Max(1, Mathf.RoundToInt(viewHeight * factor));
                m_Target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "BodyPoseDisplayedCamera"
                };
                m_Target.Create();
                m_Frame = new Texture2D(width, height, TextureFormat.RGBA32, false);
                Debug.Log($"[Pose] 画面采样 {viewWidth}x{viewHeight} -> {width}x{height}，使用 AR 显示变换与等比补边");
                if (resized)
                {
                    ResetTracking("画面方向已变化，请重新双击人物");
                    return false;
                }
            }

            var previousActive = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            try
            {
                // Reuse the AR background's display transform (rotation and crop).
                // Do not render the AR camera into a square target or feed virtual objects to YOLO.
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(null, m_Target, m_Background.material);
                RenderTexture.active = m_Target;
                m_Frame.ReadPixels(new Rect(0, 0, m_Target.width, m_Target.height), 0, 0);
                m_Frame.Apply(false);
                m_Pixels = m_Frame.GetPixels32();
                return true;
            }
            finally
            {
                RenderTexture.active = previousActive;
                GL.sRGBWrite = previousSrgbWrite;
            }
        }

        private BodyPoseResult SelectCandidate(BodyPoseResult[] candidates)
        {
            if (!m_Current.IsValid) return default;

            BodyPoseResult best = default;
            float bestScore = -1f;
            foreach (var candidate in candidates)
            {
                float overlap = IoU(candidate.screenRect, m_Current.screenRect);
                float centerDistance = Vector2.Distance(candidate.screenRect.center, m_Current.screenRect.center);
                float maxDistance = Mathf.Max(0.06f, m_Current.screenRect.size.magnitude * 0.45f);
                float sizeRatio = candidate.screenRect.height / Mathf.Max(0.01f, m_Current.screenRect.height);
                if (overlap < 0.1f || centerDistance > maxDistance || sizeRatio < 0.45f || sizeRatio > 2.2f) continue;
                float score = overlap * 0.7f + candidate.confidence * 0.3f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            return best;
        }

        private BodyPoseResult SelectCandidateAtScreenPosition(BodyPoseResult[] candidates, Vector2 screenPosition)
        {
            Vector3 viewport = m_Camera.ScreenToViewportPoint(screenPosition);
            Vector2 normalized = new Vector2(viewport.x, viewport.y);

            BodyPoseResult best = default;
            float bestDistance = float.MaxValue;
            foreach (var candidate in candidates)
            {
                float aspect = (float)m_Camera.pixelWidth / m_Camera.pixelHeight;
                if (!BodyPoseGeometry.TryFitHead(candidate, aspect, out _, out _) &&
                    !BodyPoseGeometry.TryFitTorso(candidate, aspect, out _, out _, out _)) continue;
                Vector2 center = candidate.screenRect.center;
                bool inside = candidate.screenRect.Contains(normalized);
                float distance = Vector2.Distance(center, normalized);
                if (inside) distance *= 0.1f;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return bestDistance <= 0.35f ? best : default;
        }

        private void Smooth(BodyPoseResult pose)
        {
            var filtered = new Vector2[pose.keypoints.Length];
            var scores = new float[pose.keypoints.Length];
            float jumpLimit = Mathf.Max(0.045f, pose.screenRect.size.magnitude * 0.18f);
            for (int i = 0; i < pose.keypoints.Length; i++)
            {
                bool valid = BodyPoseGeometry.HasPoint(pose, i);
                bool hasPrevious = m_Smoothed.TryGetValue(i, out var previous);
                if (valid && hasPrevious && Vector2.Distance(previous, pose.keypoints[i]) > jumpLimit)
                {
                    // A large move must appear in two consecutive detections.
                    bool confirmed = m_Rejected.TryGetValue(i, out var rejected) &&
                        Vector2.Distance(rejected, pose.keypoints[i]) < jumpLimit * 0.5f;
                    m_Rejected[i] = pose.keypoints[i];
                    valid = confirmed;
                }
                else
                    m_Rejected.Remove(i);

                if (valid)
                {
                    filtered[i] = hasPrevious ? Vector2.Lerp(previous, pose.keypoints[i], 0.55f) : pose.keypoints[i];
                    m_Smoothed[i] = filtered[i];
                    scores[i] = pose.keypointScores[i];
                    m_Rejected.Remove(i);
                }
                else
                {
                    filtered[i] = hasPrevious ? previous : pose.keypoints[i];
                    scores[i] = 0f;
                }
            }

            m_Current = pose;
            m_Current.keypoints = filtered;
            m_Current.keypointScores = scores;
        }

        private void ApplyToRig(BodyPoseResult pose)
        {
            if (!pose.IsValid) return;

            float aspect = (float)m_Camera.pixelWidth / m_Camera.pixelHeight;
            m_HeadValid = BodyPoseGeometry.TryFitHead(pose, aspect, out m_HeadCenter, out float headRadius);
            m_TorsoValid = BodyPoseGeometry.TryFitTorso(pose, aspect, out m_ShoulderCenter, out m_HipCenter, out float torsoRadius);

            // Prefer a shoulder-to-hip measurement: a half-body detection must not
            // suddenly become a 1.70 m full-body box. Keep the last depth if obscured.
            float estimate = 0f;
            if (m_TorsoValid)
            {
                float projectedLength = Vector3.Distance(ScreenToWorld(m_ShoulderCenter, 1f), ScreenToWorld(m_HipCenter, 1f));
                estimate = 0.5f / Mathf.Max(0.01f, projectedLength);
            }
            else if (m_Distance <= 0f && m_HeadValid)
            {
                float projectedRadius = WorldRadius(m_HeadCenter, headRadius, 1f);
                estimate = 0.105f / Mathf.Max(0.005f, projectedRadius);
            }
            if (estimate > 0f)
            {
                estimate = Mathf.Clamp(estimate, 0.45f, 6f);
                m_Distance = m_Distance <= 0f ? estimate : Mathf.Lerp(m_Distance,
                    Mathf.Clamp(estimate, m_Distance * 0.8f, m_Distance * 1.2f), 0.25f);
            }

            if (m_Distance > 0f)
            {
                m_Rig.SetTrackedPose(m_HeadValid, ScreenToWorld(m_HeadCenter, m_Distance),
                    WorldRadius(m_HeadCenter, headRadius, m_Distance),
                    m_TorsoValid, ScreenToWorld(m_ShoulderCenter, m_Distance), ScreenToWorld(m_HipCenter, m_Distance),
                    WorldRadius((m_ShoulderCenter + m_HipCenter) * 0.5f, torsoRadius, m_Distance));
                if (m_HeadValid) m_HeadSeenTime = Time.realtimeSinceStartup;
                if (m_TorsoValid) m_TorsoSeenTime = Time.realtimeSinceStartup;
            }
            ExpireParts();
            m_Status = $"已选择人物  头部：{(m_HeadValid ? "可靠" : "待识别")}  躯干：{(m_TorsoValid ? "可靠" : "待识别")}";
            if (Time.realtimeSinceStartup >= m_NextDiagnosticLog)
            {
                Debug.Log($"[Pose] 拟合 head={m_HeadValid} torso={m_TorsoValid}，估计距离={m_Distance:F2}m");
                m_NextDiagnosticLog = Time.realtimeSinceStartup + 3f;
            }
        }

        private Vector3 ScreenToWorld(Vector2 normalized, float distance)
        {
            return m_Camera.ViewportToWorldPoint(new Vector3(normalized.x, normalized.y, distance));
        }

        private float WorldRadius(Vector2 center, float radius, float distance) =>
            Vector3.Distance(ScreenToWorld(center, distance), ScreenToWorld(center + Vector2.up * radius, distance));

        private void ExpireParts()
        {
            if (m_Rig == null) return;
            if (Time.realtimeSinceStartup - m_HeadSeenTime > PartLostGrace)
            {
                m_Rig.HideHead();
                m_HeadValid = false;
            }
            if (Time.realtimeSinceStartup - m_TorsoSeenTime > PartLostGrace)
            {
                m_Rig.HideTorso();
                m_TorsoValid = false;
            }
        }

        private void ResetTracking(string status)
        {
            m_PickerPauseStart = -1f;
            m_PressActive = false;
            m_TrackingEnabled = m_SelectionPending = m_HasPendingTap = m_HasProjectileTap = false;
            m_Current = m_Raw = default;
            m_Smoothed.Clear();
            m_Rejected.Clear();
            m_Distance = 0f;
            m_HeadValid = m_TorsoValid = false;
            m_HeadSeenTime = m_TorsoSeenTime = float.NegativeInfinity;
            m_Status = status;
            if (m_Rig != null) m_Rig.HideTrackedParts();
        }

        private void OnDisable() => ResetTracking("双击人物选择目标");

        private void OnGUI()
        {
            if (!ShowDiagnostics || !isActiveAndEnabled || m_Camera == null) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Clamp(Screen.height / 65, 16, 30) };
            float previewWidth = Mathf.Min(Screen.width * 0.25f, 240f);
            float previewHeight = m_Frame != null ? previewWidth * m_Frame.height / m_Frame.width : 0f;
            if (m_Frame != null)
                GUI.DrawTexture(new Rect(Screen.width - previewWidth - 12f, 12f, previewWidth, previewHeight), m_Frame);
            GUI.Label(new Rect(12f, Screen.height - 115f, Screen.width - 24f, 100f),
                m_Status + "\n黄点：原始关键点  绿框：人物范围  青线：代理中心", style);
            if (!m_Raw.IsValid) return;
            Vector2 lower = GuiPoint(m_Raw.screenRect.min);
            Vector2 upper = GuiPoint(m_Raw.screenRect.max);
            var rect = Rect.MinMaxRect(lower.x, upper.y, upper.x, lower.y);
            DrawLine(new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), Color.green);
            DrawLine(new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMax, rect.yMax), Color.green);
            DrawLine(new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin, rect.yMax), Color.green);
            DrawLine(new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), Color.green);
            for (int i = 0; i <= 12; i++)
            {
                if (i > 6 && i < 11 || !BodyPoseGeometry.HasPoint(m_Raw, i)) continue;
                Vector2 p = GuiPoint(m_Raw.keypoints[i]);
                var previous = GUI.color;
                GUI.color = Color.yellow;
                GUI.DrawTexture(new Rect(p.x - 4f, p.y - 4f, 8f, 8f), Texture2D.whiteTexture);
                GUI.color = previous;
                GUI.Label(new Rect(p.x + 5f, p.y - 15f, 100f, 35f), $"{i}:{m_Raw.keypointScores[i]:F2}", style);
            }
            if (m_HeadValid)
            {
                Vector2 p = GuiPoint(m_HeadCenter);
                DrawLine(p - Vector2.right * 12f, p + Vector2.right * 12f, Color.cyan);
                DrawLine(p - Vector2.up * 12f, p + Vector2.up * 12f, Color.cyan);
            }
            if (m_TorsoValid) DrawLine(GuiPoint(m_ShoulderCenter), GuiPoint(m_HipCenter), Color.cyan);
        }

        private Vector2 GuiPoint(Vector2 viewport)
        {
            Vector3 point = m_Camera.ViewportToScreenPoint(viewport);
            return new Vector2(point.x, Screen.height - point.y);
        }

        private static void DrawLine(Vector2 start, Vector2 end, Color color)
        {
            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg, start);
            GUI.DrawTexture(new Rect(start.x, start.y, Vector2.Distance(start, end), 2f), Texture2D.whiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private static float IoU(Rect a, Rect b)
        {
            float width = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
            float height = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            float intersection = width * height;
            float union = a.width * a.height + b.width * b.height - intersection;
            return union <= 0f ? 0f : intersection / union;
        }

        private void OnDestroy()
        {
            ReleaseCapture();
            m_Detector?.Dispose();
        }

        private void ReleaseCapture()
        {
            if (m_Target != null)
            {
                m_Target.Release();
                Destroy(m_Target);
                m_Target = null;
            }
            if (m_Frame != null) Destroy(m_Frame);
            m_Frame = null;
        }
    }
}
