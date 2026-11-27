using System.Collections.Generic;
using UnityEngine;

namespace EarthOL.BodyThrow
{
    /// <summary>Multi-person alpha-beta keypoint tracking between low-rate YOLO corrections.</summary>
    [DisallowMultipleComponent]
    public sealed class PersonTrackingManager : MonoBehaviour
    {
        [SerializeField, Range(0.3f, 3f)] private float lostPersonSeconds = 1.0f;
        [SerializeField, Range(0.05f, 0.6f)] private float minimumAssociationOverlap = 0.06f;
        [SerializeField, Range(0.1f, 0.95f)] private float correctionAlpha = 0.72f;
        [SerializeField, Range(0.01f, 0.6f)] private float correctionBeta = 0.10f;
        [SerializeField, Range(2f, 120f)] private float reidentificationSeconds = 60f;

        sealed class Track
        {
            public PersonPoseData pose;
            public readonly Vector2[] velocity = new Vector2[17];
            public Vector2 boxVelocity;
            public float lastDetection;
            public float lastUpdate;
            public PersonFacing candidateFacing;
            public int facingVotes;
            public bool requiresDetection;
            public bool motionReliable = true;
        }

        readonly List<Track> m_Tracks = new List<Track>(8);
        readonly List<PersonPoseData> m_Views = new List<PersonPoseData>(8);
        readonly Vector2[] m_FlowMovements = new Vector2[17];
        readonly bool[] m_FlowMatches = new bool[17];
        int m_NextId = 1;
        float m_PausedAt = -1f;
        float m_HeldAt = -1f;
        public int TrackingUpdateCount { get; private set; }
        public int FlowUpdateCount { get; private set; }
        public int FlowRejectionCount { get; private set; }
        public int FlowTrackingCount { get; private set; }
        public int FlowMatchCount { get; private set; }
        public int RecentSingleRecoveryCount { get; private set; }
        public bool NeedsDetectionCorrection { get; private set; }
        public IReadOnlyList<PersonPoseData> Persons => m_Views;
        public int Count => m_Views.Count;
        public float LostPersonSeconds { get => lostPersonSeconds; set => lostPersonSeconds = Mathf.Clamp(value, 0.3f, 3f); }
        public float ReidentificationSeconds
        {
            get => reidentificationSeconds;
            set => reidentificationSeconds = Mathf.Clamp(value, 2f, 120f);
        }

        public void PauseTracks(float timestamp)
        {
            if (m_PausedAt >= 0f) return;
            m_PausedAt = timestamp;
            foreach (var track in m_Tracks)
            {
                track.requiresDetection = true;
                track.pose.trackingConfidence = 0f;
            }
            RefreshViews();
        }

        public void ResumeTracks(float timestamp)
        {
            if (m_PausedAt < 0f) return;
            float pausedDuration = Mathf.Max(0f, timestamp - m_PausedAt);
            foreach (var track in m_Tracks)
            {
                track.lastDetection += pausedDuration;
                track.lastUpdate += pausedDuration;
                track.pose.timestamp += pausedDuration;
            }
            m_PausedAt = -1f;
            RefreshViews();
        }

        public void HoldTracks(float timestamp)
        {
            if (m_HeldAt >= 0f) return;
            m_HeldAt = timestamp;
        }

        public void ResumeHeldTracks(float timestamp)
        {
            if (m_HeldAt < 0f) return;
            float heldDuration = Mathf.Max(0f, timestamp - m_HeldAt);
            foreach (var track in m_Tracks)
            {
                track.lastDetection += heldDuration;
                track.lastUpdate += heldDuration;
                track.pose.timestamp += heldDuration;
            }
            m_HeldAt = -1f;
            RefreshViews();
        }

        public void Tick(float timestamp)
        {
            TrackingUpdateCount++;
            if (m_HeldAt >= 0f) return;
            for (int i = m_Tracks.Count - 1; i >= 0; i--)
            {
                var track = m_Tracks[i];
                float age = timestamp - track.lastDetection;
                if (age > reidentificationSeconds)
                {
                    m_Tracks.RemoveAt(i);
                    continue;
                }
                if (track.requiresDetection || age > lostPersonSeconds)
                {
                    track.requiresDetection = true;
                    track.pose.trackingConfidence = 0f;
                    continue;
                }
                Predict(track, timestamp);
                track.pose.trackingConfidence = track.pose.detectionConfidence * Mathf.Exp(-age / Mathf.Max(0.05f, lostPersonSeconds));
            }
            RefreshViews();
        }

        public void UpdateDetections(BodyPoseResult[] detections, float timestamp, float latency = 0f)
        {
            NeedsDetectionCorrection = false;
            Tick(timestamp);
            if (detections == null || detections.Length == 0) return;

            var pairs = new List<(int detection, int track, float score, bool dormant)>(detections.Length * m_Tracks.Count);
            Track recentSingle = null;
            if (detections.Length == 1)
            {
                foreach (var track in m_Tracks)
                {
                    if (timestamp - track.lastDetection > 3f) continue;
                    if (recentSingle != null) { recentSingle = null; break; }
                    recentSingle = track;
                }
            }
            for (int d = 0; d < detections.Length; d++)
            {
                if (!detections[d].IsValid) continue;
                for (int t = 0; t < m_Tracks.Count; t++)
                {
                    float overlap = IoU(detections[d].screenRect, m_Tracks[t].pose.screenRect);
                    float distance = Vector2.Distance(detections[d].screenRect.center, m_Tracks[t].pose.screenRect.center);
                    float scale = Mathf.Max(0.06f, m_Tracks[t].pose.screenRect.size.magnitude * 0.65f);
                    float size = detections[d].screenRect.height / Mathf.Max(0.01f, m_Tracks[t].pose.screenRect.height);
                    if (size < 0.40f || size > 2.5f) continue;
                    var track = m_Tracks[t];
                    float poseSimilarity = LandmarkSimilarity(track.pose, detections[d]);
                    float appearanceSimilarity = AppearanceSimilarity(track.pose.appearance, detections[d].appearance);
                    bool activeGeometryMatch = overlap >= minimumAssociationOverlap || distance <= scale;
                    bool dormantGeometryMatch = timestamp - track.lastDetection <= 3f &&
                        overlap >= 0.25f && poseSimilarity >= 0.65f &&
                        (track.pose.appearance == null || detections[d].appearance == null || appearanceSimilarity >= 0.5f);
                    bool dormantAppearanceMatch = appearanceSimilarity >= 0.72f &&
                        size >= 0.55f && size <= 1.8f;
                    bool geometryMatch = track.requiresDetection ? dormantGeometryMatch : activeGeometryMatch;
                    bool singleRecovery = track == recentSingle && size >= .67f && size <= 1.5f &&
                        (track.pose.appearance == null || detections[d].appearance == null || appearanceSimilarity >= .44f) &&
                        LandmarkSimilarity(track.pose, detections[d], alignBoxes: true) >= .78f;
                    if (!geometryMatch && !dormantAppearanceMatch && !singleRecovery) continue;
                    float activePriority = track.requiresDetection ? 0f : 1f;
                    pairs.Add((d, t, activePriority + overlap * 0.30f + Mathf.Exp(-distance / scale) * 0.18f +
                        poseSimilarity * 0.16f + appearanceSimilarity * 0.24f +
                        Mathf.Clamp01(detections[d].confidence) * 0.12f, track.requiresDetection));
                }
            }
            pairs.Sort((a, b) => b.score.CompareTo(a.score));
            var assignedDetections = new bool[detections.Length];
            var assignedTracks = new bool[m_Tracks.Count];
            foreach (var pair in pairs)
            {
                if (assignedDetections[pair.detection] || assignedTracks[pair.track]) continue;
                if (pair.dormant && pairs.Exists(other => other.dormant &&
                    !assignedDetections[other.detection] && !assignedTracks[other.track] &&
                    (other.detection == pair.detection) != (other.track == pair.track) &&
                    Mathf.Abs(other.score - pair.score) < 0.08f)) continue;
                var track = m_Tracks[pair.track];
                var detection = detections[pair.detection];
                bool displaced = IoU(detection.screenRect, track.pose.screenRect) < minimumAssociationOverlap &&
                    Vector2.Distance(detection.screenRect.center, track.pose.screenRect.center) >
                    Mathf.Max(.06f, track.pose.screenRect.size.magnitude * .65f);
                if (track == recentSingle && (track.requiresDetection || displaced)) RecentSingleRecoveryCount++;
                Correct(track, detection, timestamp, latency, displaced);
                assignedDetections[pair.detection] = assignedTracks[pair.track] = true;
            }
            for (int d = 0; d < detections.Length; d++)
                if (!assignedDetections[d]) AddTrack(detections[d], timestamp);
            RefreshViews();
        }

        public PersonPoseData Find(int personId)
        {
            foreach (var track in m_Tracks) if (track.pose.personId == personId) return track.pose;
            return null;
        }

        public PersonPoseData Pick(Vector2 viewportPoint, float horizontalPadding = 0f, float verticalPadding = 0f)
        {
            Track best = null;
            float bestCost = float.MaxValue;
            foreach (var track in m_Tracks)
            {
                if (track.pose.trackingConfidence < 0.2f) continue;
                Rect bounds = track.pose.screenRect;
                bounds = Rect.MinMaxRect(bounds.xMin - bounds.width * horizontalPadding,
                    bounds.yMin - bounds.height * verticalPadding, bounds.xMax + bounds.width * horizontalPadding,
                    bounds.yMax + bounds.height * verticalPadding);
                if (!bounds.Contains(viewportPoint)) continue;
                float cost = Vector2.Distance(track.pose.screenRect.center, viewportPoint) - track.pose.detectionConfidence * 0.02f;
                if (cost < bestCost) { best = track; bestCost = cost; }
            }
            return best != null ? trackSnapshot(best.pose) : null;
        }

        public void ClearPeople()
        {
            m_Tracks.Clear();
            m_Views.Clear();
            m_PausedAt = -1f;
            m_HeldAt = -1f;
            NeedsDetectionCorrection = false;
        }

        public void ApplyImageFlow(byte[] previous, byte[] current, int width, int height, float deltaTime,
            Matrix4x4? previousView = null, Matrix4x4? currentView = null)
        {
            if (previous == null || current == null || deltaTime <= 0f) return;
            FlowUpdateCount++;
            foreach (var track in m_Tracks)
            {
                if (track.requiresDetection) continue;
                int attempted = 0, matches = 0;
                for (int i = 0; i < 17; i++)
                {
                    m_FlowMatches[i] = false;
                    if (track.pose.keypointConfidence[i] < 0.3f) continue;
                    attempted++;
                    Vector2 predicted = track.velocity[i] * deltaTime;
                    Vector2 previousPoint = track.pose.keypoints[i] - predicted;
                    Vector2 source = previousView.HasValue ? ReprojectPoint(previousView.Value, previousPoint) : previousPoint;
                    Vector2 destination = currentView.HasValue ? ReprojectPoint(currentView.Value, previousPoint) : previousPoint;
                    Vector2 cameraMotion = destination - source;
                    if (!PersonKeypointFlow.TryTrack(previous, current, width, height, source, out var movement, out float confidence,
                            cameraMotion + predicted) ||
                        confidence < .55f) continue;
                    m_FlowMovements[i] = movement - cameraMotion;
                    m_FlowMatches[i] = true;
                    matches++;
                }
                Vector2 torsoMotion = Vector2.zero;
                int torsoMatches = 0;
                for (int i = 5; i <= 12; i++)
                {
                    if ((i != 5 && i != 6 && i != 11 && i != 12) || !m_FlowMatches[i]) continue;
                    torsoMotion += m_FlowMovements[i];
                    torsoMatches++;
                }
                bool hasTorsoMotion = torsoMatches >= 2;
                if (hasTorsoMotion)
                {
                    torsoMotion /= torsoMatches;
                    float tolerance = 2.5f / Mathf.Min(width - 1, height - 1);
                    for (int i = 5; i <= 12; i++)
                    {
                        if ((i != 5 && i != 6 && i != 11 && i != 12) || !m_FlowMatches[i]) continue;
                        if (Vector2.Distance(m_FlowMovements[i], torsoMotion) > tolerance) hasTorsoMotion = false;
                    }
                }
                if (!hasTorsoMotion && matches < Mathf.Max(3, (attempted + 1) / 2))
                {
                    RejectFlow(track);
                    continue;
                }
                Vector2 sum = Vector2.zero;
                FlowTrackingCount++;
                FlowMatchCount += matches;
                for (int i = 0; i < 17; i++)
                {
                    if (!m_FlowMatches[i] && (!hasTorsoMotion || track.pose.keypointConfidence[i] < .15f)) continue;
                    Vector2 predicted = track.velocity[i] * deltaTime;
                    Vector2 movement = m_FlowMatches[i] ? m_FlowMovements[i] : torsoMotion;
                    // Tick already predicted this interval. Replace that motion with image evidence.
                    track.pose.keypoints[i] += (movement - predicted) * 0.8f;
                    track.velocity[i] = Vector2.ClampMagnitude(Vector2.Lerp(track.velocity[i], movement / deltaTime, 0.35f), .8f);
                    if (m_FlowMatches[i]) sum += movement;
                }
                var rect = track.pose.screenRect;
                Vector2 boxResidual = (hasTorsoMotion ? torsoMotion : sum / matches) - track.boxVelocity * deltaTime;
                rect.center += boxResidual * .8f;
                track.pose.screenRect = rect;
                track.boxVelocity = Vector2.ClampMagnitude(track.boxVelocity + boxResidual / deltaTime * .35f, .8f);
            }
        }

        internal void Reproject(Matrix4x4 projection)
        {
            foreach (var track in m_Tracks)
            {
                if (track.requiresDetection) continue;
                var rect = track.pose.screenRect;
                rect.center = ReprojectPoint(projection, rect.center);
                track.pose.screenRect = rect;
                for (int i = 0; i < 17; i++)
                    track.pose.keypoints[i] = ReprojectPoint(projection, track.pose.keypoints[i]);
            }
        }

        public static Vector2 ReprojectPoint(Matrix4x4 projection, Vector2 point)
        {
            var clip = projection * new Vector4(point.x * 2f - 1f, point.y * 2f - 1f, 0f, 1f);
            if (clip.w < .1f) return point;
            return new Vector2(clip.x, clip.y) * (.5f / clip.w) + Vector2.one * .5f;
        }

        internal static Rect ReprojectRect(Matrix4x4 projection, Rect rect)
        {
            Vector2 a = ReprojectPoint(projection, rect.min);
            Vector2 b = ReprojectPoint(projection, new Vector2(rect.xMin, rect.yMax));
            Vector2 c = ReprojectPoint(projection, new Vector2(rect.xMax, rect.yMin));
            Vector2 d = ReprojectPoint(projection, rect.max);
            Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            Vector2 max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        internal void RejectImageFlow()
        {
            foreach (var track in m_Tracks)
                if (!track.requiresDetection) RejectFlow(track);
        }

        void RejectFlow(Track track)
        {
            NeedsDetectionCorrection = true;
            FlowRejectionCount++;
            track.motionReliable = false;
            track.boxVelocity = Vector2.zero;
            System.Array.Clear(track.velocity, 0, track.velocity.Length);
        }

        static PersonPoseData trackSnapshot(PersonPoseData source) => new PersonPoseData
        {
            personId = source.personId,
            screenRect = source.screenRect,
            keypoints = (Vector2[])source.keypoints.Clone(),
            keypointConfidence = (float[])source.keypointConfidence.Clone(),
            appearance = source.appearance != null ? (float[])source.appearance.Clone() : null,
            detectionConfidence = source.detectionConfidence,
            trackingConfidence = source.trackingConfidence,
            timestamp = source.timestamp,
            facing = source.facing
        };

        void AddTrack(BodyPoseResult detection, float timestamp)
        {
            var pose = PersonPoseData.FromDetection(detection, timestamp);
            if (pose == null) return;
            pose.personId = m_NextId++;
            pose.facing = PersonFacingDetector.Estimate(pose);
            m_Tracks.Add(new Track { pose = pose, lastDetection = timestamp, lastUpdate = timestamp,
                candidateFacing = pose.facing, facingVotes = pose.facing == PersonFacing.Unknown ? 0 : 2 });
        }

        void Correct(Track track, BodyPoseResult detection, float timestamp, float latency, bool displaced)
        {
            if (track.requiresDetection || displaced)
            {
                var pose = PersonPoseData.FromDetection(detection, timestamp);
                pose.personId = track.pose.personId;
                pose.facing = track.pose.facing;
                pose.appearance = track.pose.appearance ?? pose.appearance;
                track.pose = pose;
                track.boxVelocity = Vector2.zero;
                System.Array.Clear(track.velocity, 0, track.velocity.Length);
                track.requiresDetection = false;
                track.motionReliable = true;
                track.lastDetection = track.lastUpdate = timestamp;
                return;
            }
            float dt = Mathf.Clamp(timestamp - track.lastDetection, 0.01f, 1f);
            latency = track.motionReliable ? Mathf.Clamp(latency, 0f, 0.16f) : 0f;
            float alpha = track.motionReliable ? correctionAlpha : 1f;
            float beta = track.motionReliable ? correctionBeta : 0f;
            Vector2 boxResidual = detection.screenRect.center + track.boxVelocity * latency - track.pose.screenRect.center;
            Vector2 boxCenter = track.pose.screenRect.center + boxResidual * alpha;
            track.boxVelocity = Vector2.ClampMagnitude(track.boxVelocity + boxResidual * (beta / dt), .8f);
            float boxWidth = Mathf.Lerp(track.pose.screenRect.width, detection.screenRect.width, 0.70f);
            float boxHeight = Mathf.Lerp(track.pose.screenRect.height, detection.screenRect.height, 0.70f);
            track.pose.screenRect = new Rect(boxCenter.x - boxWidth * 0.5f, boxCenter.y - boxHeight * 0.5f, boxWidth, boxHeight);

            for (int i = 0; i < 17; i++)
            {
                if (detection.keypointScores[i] >= 0.15f)
                {
                    Vector2 residual = detection.keypoints[i] + track.velocity[i] * latency - track.pose.keypoints[i];
                    track.pose.keypoints[i] += residual * alpha;
                    track.velocity[i] = Vector2.ClampMagnitude(track.velocity[i] + residual * (beta / dt), .8f);
                    track.pose.keypointConfidence[i] = detection.keypointScores[i];
                }
                else track.pose.keypointConfidence[i] *= 0.92f;
            }
            track.pose.detectionConfidence = detection.confidence;
            UpdateAppearance(track, detection.appearance);
            track.requiresDetection = false;
            track.motionReliable = true;
            track.pose.trackingConfidence = Mathf.Lerp(track.pose.trackingConfidence, detection.confidence, 0.6f);
            track.pose.timestamp = timestamp;
            track.lastDetection = track.lastUpdate = timestamp;

            var facing = PersonFacingDetector.Estimate(track.pose);
            if (facing == track.candidateFacing) track.facingVotes = Mathf.Min(track.facingVotes + 1, 5);
            else { track.candidateFacing = facing; track.facingVotes = 1; }
            if (track.facingVotes >= 3 && facing != PersonFacing.Unknown) track.pose.facing = facing;
        }

        static void Predict(Track track, float timestamp)
        {
            float dt = Mathf.Clamp(timestamp - track.lastUpdate, 0f, 0.1f);
            if (dt <= 0f) return;
            Vector2 center = track.pose.screenRect.center + track.boxVelocity * dt;
            var rect = track.pose.screenRect;
            rect.center = center;
            track.pose.screenRect = rect;
            for (int i = 0; i < 17; i++)
                if (track.pose.keypointConfidence[i] > 0.08f)
                    track.pose.keypoints[i] += track.velocity[i] * dt;
            track.pose.timestamp = timestamp;
            track.lastUpdate = timestamp;
        }

        void RefreshViews()
        {
            m_Views.Clear();
            foreach (var track in m_Tracks)
                if (!track.requiresDetection) m_Views.Add(track.pose);
        }

        static float IoU(Rect a, Rect b)
        {
            float w = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
            float h = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            float intersection = w * h;
            return a.width * a.height + b.width * b.height - intersection <= 0f ? 0f :
                intersection / (a.width * a.height + b.width * b.height - intersection);
        }

        static float LandmarkSimilarity(PersonPoseData track, BodyPoseResult detection, bool alignBoxes = false)
        {
            float weightedDistance = 0f, totalWeight = 0f;
            int sharedPoints = 0;
            float normalizer = Mathf.Max(0.08f, track.screenRect.size.magnitude * 0.55f);
            float heightScale = track.screenRect.height / Mathf.Max(.01f, detection.screenRect.height);
            for (int i = 0; i < 17; i++)
            {
                if (track.keypointConfidence[i] < 0.25f || detection.keypointScores[i] < 0.25f) continue;
                sharedPoints++;
                float weight = Mathf.Min(track.keypointConfidence[i], detection.keypointScores[i]);
                Vector2 point = detection.keypoints[i];
                if (alignBoxes) point = track.screenRect.center + (point - detection.screenRect.center) * heightScale;
                weightedDistance += Mathf.Min(1f, Vector2.Distance(track.keypoints[i], point) / normalizer) * weight;
                totalWeight += weight;
            }
            return totalWeight > 0f && (!alignBoxes || sharedPoints >= 5) ? 1f - weightedDistance / totalWeight : 0.5f;
        }

        static float AppearanceSimilarity(float[] first, float[] second)
        {
            if (first == null || second == null || first.Length != second.Length) return 0f;
            float intersection = 0f;
            for (int i = 0; i < first.Length; i++)
                intersection += Mathf.Min(first[i], second[i]);
            return intersection;
        }

        static void UpdateAppearance(Track track, float[] appearance)
        {
            if (appearance == null) return;
            if (track.pose.appearance == null || track.pose.appearance.Length != appearance.Length)
            {
                track.pose.appearance = (float[])appearance.Clone();
                return;
            }
            for (int i = 0; i < appearance.Length; i++)
                track.pose.appearance[i] = Mathf.Lerp(track.pose.appearance[i], appearance[i], 0.12f);
        }
    }
}
