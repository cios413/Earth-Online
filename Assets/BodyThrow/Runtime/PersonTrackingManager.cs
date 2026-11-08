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
        }

        readonly List<Track> m_Tracks = new List<Track>(8);
        readonly List<Track> m_Suspended = new List<Track>(8);
        readonly List<PersonPoseData> m_Views = new List<PersonPoseData>(8);
        int m_NextId = 1;
        public int TrackingUpdateCount { get; private set; }
        public int FlowUpdateCount { get; private set; }
        public bool NeedsDetectionCorrection { get; private set; }
        public IReadOnlyList<PersonPoseData> Persons => m_Views;
        public int Count => m_Tracks.Count;
        public float LostPersonSeconds { get => lostPersonSeconds; set => lostPersonSeconds = Mathf.Clamp(value, 0.3f, 3f); }

        public void Tick(float timestamp)
        {
            TrackingUpdateCount++;
            for (int i = m_Tracks.Count - 1; i >= 0; i--)
            {
                var track = m_Tracks[i];
                float age = timestamp - track.lastDetection;
                if (age > (track.requiresDetection ? Mathf.Max(3f, lostPersonSeconds) : lostPersonSeconds))
                {
                    m_Tracks.RemoveAt(i);
                    continue;
                }
                if (track.requiresDetection) { track.pose.trackingConfidence = 0f; continue; }
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

            var pairs = new List<(int detection, int track, float score)>(detections.Length * m_Tracks.Count);
            for (int d = 0; d < detections.Length; d++)
            {
                if (!detections[d].IsValid) continue;
                for (int t = 0; t < m_Tracks.Count; t++)
                {
                    float overlap = IoU(detections[d].screenRect, m_Tracks[t].pose.screenRect);
                    float distance = Vector2.Distance(detections[d].screenRect.center, m_Tracks[t].pose.screenRect.center);
                    float scale = Mathf.Max(0.06f, m_Tracks[t].pose.screenRect.size.magnitude * 0.65f);
                    float size = detections[d].screenRect.height / Mathf.Max(0.01f, m_Tracks[t].pose.screenRect.height);
                    if (overlap < minimumAssociationOverlap && distance > scale) continue;
                    if (size < 0.40f || size > 2.5f) continue;
                    float poseSimilarity = LandmarkSimilarity(m_Tracks[t].pose, detections[d]);
                    pairs.Add((d, t, overlap * 0.43f + Mathf.Exp(-distance / scale) * 0.27f +
                        poseSimilarity * 0.18f + Mathf.Clamp01(detections[d].confidence) * 0.12f));
                }
            }
            pairs.Sort((a, b) => b.score.CompareTo(a.score));
            var assignedDetections = new bool[detections.Length];
            var assignedTracks = new bool[m_Tracks.Count];
            foreach (var pair in pairs)
            {
                if (assignedDetections[pair.detection] || assignedTracks[pair.track]) continue;
                Correct(m_Tracks[pair.track], detections[pair.detection], timestamp, latency);
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
            m_Suspended.Clear();
            m_Views.Clear();
            NeedsDetectionCorrection = false;
        }

        public void SuspendPeople()
        {
            m_Suspended.Clear();
            m_Suspended.AddRange(m_Tracks);
            m_Tracks.Clear(); m_Views.Clear();
        }

        public void ResumeCandidates(float now)
        {
            foreach (var track in m_Suspended)
            {
                track.requiresDetection = true;
                track.pose.trackingConfidence = 0f;
                track.lastDetection = track.lastUpdate = now;
                track.boxVelocity = Vector2.zero;
                System.Array.Clear(track.velocity, 0, track.velocity.Length);
                m_Tracks.Add(track);
            }
            m_Suspended.Clear();
            RefreshViews();
        }

        public void ApplyImageFlow(byte[] previous, byte[] current, int width, int height, float deltaTime)
        {
            if (previous == null || current == null || deltaTime <= 0f) return;
            FlowUpdateCount++;
            int attempted = 0, accepted = 0;
            foreach (var track in m_Tracks)
            {
                if (track.requiresDetection) continue;
                Vector2 sum = Vector2.zero; int matches = 0;
                for (int i = 0; i < 17; i++)
                {
                    if (track.pose.keypointConfidence[i] < 0.3f) continue;
                    attempted++;
                    Vector2 predicted = track.velocity[i] * deltaTime;
                    Vector2 previousPoint = track.pose.keypoints[i] - predicted;
                    if (!PersonKeypointFlow.TryTrack(previous, current, width, height, previousPoint, out var movement, out float confidence)) continue;
                    // Tick already predicted this interval. Replace that motion with image evidence.
                    track.pose.keypoints[i] += (movement - predicted) * 0.8f;
                    track.velocity[i] = Vector2.Lerp(track.velocity[i], movement / deltaTime, 0.35f);
                    sum += movement - track.boxVelocity * deltaTime;
                    matches++; accepted++;
                }
                if (matches >= 3)
                {
                    var rect = track.pose.screenRect;
                    rect.center += sum / matches * 0.6f;
                    track.pose.screenRect = rect;
                }
            }
            if (attempted > 0 && accepted < attempted / 3) NeedsDetectionCorrection = true;
        }

        static PersonPoseData trackSnapshot(PersonPoseData source) => new PersonPoseData
        {
            personId = source.personId,
            screenRect = source.screenRect,
            keypoints = (Vector2[])source.keypoints.Clone(),
            keypointConfidence = (float[])source.keypointConfidence.Clone(),
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

        void Correct(Track track, BodyPoseResult detection, float timestamp, float latency)
        {
            float dt = Mathf.Clamp(timestamp - track.lastDetection, 0.01f, 1f);
            latency = Mathf.Clamp(latency, 0f, 0.35f);
            Vector2 boxResidual = detection.screenRect.center + track.boxVelocity * latency - track.pose.screenRect.center;
            Vector2 boxCenter = track.pose.screenRect.center + boxResidual * correctionAlpha;
            track.boxVelocity = Vector2.ClampMagnitude(track.boxVelocity + boxResidual * (correctionBeta / dt), 2f);
            float boxWidth = Mathf.Lerp(track.pose.screenRect.width, detection.screenRect.width, 0.70f);
            float boxHeight = Mathf.Lerp(track.pose.screenRect.height, detection.screenRect.height, 0.70f);
            track.pose.screenRect = new Rect(boxCenter.x - boxWidth * 0.5f, boxCenter.y - boxHeight * 0.5f, boxWidth, boxHeight);

            for (int i = 0; i < 17; i++)
            {
                if (detection.keypointScores[i] >= 0.15f)
                {
                    Vector2 residual = detection.keypoints[i] + track.velocity[i] * latency - track.pose.keypoints[i];
                    track.pose.keypoints[i] += residual * correctionAlpha;
                    track.velocity[i] = Vector2.ClampMagnitude(track.velocity[i] + residual * (correctionBeta / dt), 2f);
                    track.pose.keypointConfidence[i] = detection.keypointScores[i];
                }
                else track.pose.keypointConfidence[i] *= 0.92f;
            }
            track.pose.detectionConfidence = detection.confidence;
            track.requiresDetection = false;
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
            foreach (var track in m_Tracks) m_Views.Add(track.pose);
        }

        static float IoU(Rect a, Rect b)
        {
            float w = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
            float h = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            float intersection = w * h;
            return a.width * a.height + b.width * b.height - intersection <= 0f ? 0f :
                intersection / (a.width * a.height + b.width * b.height - intersection);
        }

        static float LandmarkSimilarity(PersonPoseData track, BodyPoseResult detection)
        {
            float weightedDistance = 0f, totalWeight = 0f;
            float normalizer = Mathf.Max(0.08f, track.screenRect.size.magnitude * 0.55f);
            for (int i = 0; i < 17; i++)
            {
                if (track.keypointConfidence[i] < 0.25f || detection.keypointScores[i] < 0.25f) continue;
                float weight = Mathf.Min(track.keypointConfidence[i], detection.keypointScores[i]);
                weightedDistance += Mathf.Min(1f, Vector2.Distance(track.keypoints[i], detection.keypoints[i]) / normalizer) * weight;
                totalWeight += weight;
            }
            return totalWeight > 0f ? 1f - weightedDistance / totalWeight : 0.5f;
        }
    }
}
