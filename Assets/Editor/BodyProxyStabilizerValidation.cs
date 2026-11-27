using System;
using System.Reflection;
using EarthOL.BodyThrow;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    public static class BodyProxyStabilizerValidation
    {
        static readonly Vector3 Center = new Vector3(0f, 1.2f, 2f);

        public static void Validate()
        {
            ValidateJitter();
            ValidateMotion();
            ValidateRebaseAndReset();
            ValidateRig();
            Debug.Log("[StabilizerValidation] PASS: shared jitter suppression, translation/rotation response, " +
                "size filtering, frame-rate consistency, anchor rebasing, reacquisition, hidden collisions and attachment scale.");
        }

        static BodyProxyStabilizer Create()
        {
            var filter = new BodyProxyStabilizer();
            Target(filter, Center, Quaternion.identity);
            return filter;
        }

        static void Target(BodyProxyStabilizer filter, Vector3 center, Quaternion rotation,
            float headRadius = 0.11f, float torsoRadius = 0.20f, float torsoHeight = 0.50f)
        {
            filter.SetTarget(true, center + rotation * Vector3.up * 0.5f, headRadius,
                true, center + rotation * Vector3.up * torsoHeight * 0.5f,
                center - rotation * Vector3.up * torsoHeight * 0.5f, torsoRadius);
        }

        static void ValidateJitter()
        {
            var filter = Create();
            Vector3 initialHead = filter.Head;
            float rawSquared = 0f, filteredSquared = 0f;
            for (int sample = 0; sample < 100; sample++)
            {
                float phase = sample * 1.73f;
                var offset = new Vector3(0.004f * Mathf.Sin(phase), 0.003f * Mathf.Cos(phase), 0.003f * Mathf.Sin(phase * 2f));
                var rotation = Quaternion.AngleAxis(0.8f * Mathf.Sin(phase), Vector3.forward);
                Target(filter, Center + offset, rotation, 0.11f + 0.001f * Mathf.Sin(phase),
                    0.20f + 0.001f * Mathf.Cos(phase), 0.50f + 0.002f * Mathf.Sin(phase));
                for (int frame = 0; frame < 10; frame++) filter.Step(1f / 60f);
                Vector3 rawHead = Center + offset + rotation * Vector3.up * 0.5f;
                rawSquared += (rawHead - initialHead).sqrMagnitude;
                filteredSquared += (filter.Head - initialHead).sqrMagnitude;
                Near(Vector3.Distance(filter.Head, (filter.Shoulders + filter.Hips) * 0.5f), 0.5f, 0.0001f,
                    "Common jitter must preserve head/torso relative spacing");
                Near(filter.HeadRadius, 0.11f, 0.0001f, "Head radius noise");
                Near(filter.TorsoRadius, 0.20f, 0.0001f, "Torso radius noise");
            }
            Check(filteredSquared < rawSquared * 0.04f, "Light jitter RMS must be reduced by at least 80%");
            Debug.Log($"[StabilizerValidation] Jitter RMS raw={Mathf.Sqrt(rawSquared / 100f) * 1000f:F2}mm, " +
                $"filtered={Mathf.Sqrt(filteredSquared / 100f) * 1000f:F2}mm.");
        }

        static void ValidateMotion()
        {
            var filter = Create();
            Vector3 target = Center + Vector3.right * 0.3f;
            Target(filter, target, Quaternion.identity);
            filter.Step(1f / 60f);
            Check(filter.Head.x > 0.01f && filter.Head.x < 0.15f, "Real motion must begin smoothly without teleporting");
            for (int i = 1; i < 60; i++) filter.Step(1f / 60f);
            Near(filter.Head.x, 0.3f, 0.012f, "Translation must catch up within one second");

            Target(filter, target, Quaternion.AngleAxis(25f, Vector3.forward), 0.14f, 0.24f, 0.60f);
            filter.Step(1f / 60f);
            Check(filter.HeadRadius > 0.11f && filter.HeadRadius < 0.12f, "Size must change gradually");
            for (int i = 1; i < 90; i++) filter.Step(1f / 60f);
            Near(filter.HeadRadius, 0.14f, 0.002f, "Head size must catch up");
            Near(Vector3.Distance(filter.Shoulders, filter.Hips), 0.6f, 0.007f, "Torso length must catch up");
            Check(Vector3.Angle(filter.Shoulders - filter.Hips,
                Quaternion.AngleAxis(25f, Vector3.forward) * Vector3.up) < 1.6f, "Rotation must catch up");

            var fps30 = Create();
            var fps60 = Create();
            Target(fps30, target, Quaternion.identity);
            Target(fps60, target, Quaternion.identity);
            for (int i = 0; i < 30; i++) fps30.Step(1f / 30f);
            for (int i = 0; i < 60; i++) fps60.Step(1f / 60f);
            Check(Vector3.Distance(fps30.Head, fps60.Head) < 0.002f, "Filtering must be consistent at 30/60 FPS");

            var stalled = Create();
            Target(stalled, Center + Vector3.right, Quaternion.identity);
            stalled.Step(2f);
            Check(stalled.Head.x < 0.8f, "A long frame must not snap directly to the measurement");
        }

        static void ValidateRebaseAndReset()
        {
            var filter = Create();
            Vector3 head = filter.Head;
            Vector3 shoulders = filter.Shoulders;
            filter.SetTarget(true, head + Vector3.right * 0.02f, 0.11f, false, Vector3.zero, Vector3.zero, 0f);
            Check(Vector3.Distance(filter.Head, head) < 0.0001f && Vector3.Distance(filter.Shoulders, shoulders) < 0.0001f,
                "Switching to a head anchor must preserve existing world geometry");
            filter.ForgetTorso();
            filter.Step(1f / 60f);
            Check(!filter.HasTorso, "A hidden torso must stay hidden");
            head = filter.Head;
            Target(filter, Center + Vector3.right * 0.03f, Quaternion.identity);
            Check(Vector3.Distance(filter.Head, head) < 0.0001f, "Returning to a torso anchor must not jump the head");
            filter.Reset();
            Check(!filter.HasHead && !filter.HasTorso, "Reset must discard old body state");
            Target(filter, Center + Vector3.right * 3f, Quaternion.identity);
            Near(filter.Head.x, 3f, 0.0001f, "A newly selected target must not interpolate from the old person");
        }

        static void ValidateRig()
        {
            var root = new GameObject("StabilizerValidationRig");
            try
            {
                var rig = root.AddComponent<BodyProxyRig>();
                rig.BuildDefaultRig();
                rig.HideTrackedParts();
                rig.SetTrackedPose(true, Center + Vector3.up * 0.5f, 0.11f,
                    true, Center + Vector3.up * 0.25f, Center - Vector3.up * 0.25f, 0.20f);
                var head = root.transform.Find("HeadProxy");
                var attached = new GameObject("AttachedProjectile").transform;
                attached.SetParent(head, false);
                attached.localPosition = new Vector3(0.04f, 0.02f, -0.1f);
                Vector3 attachmentPoint = attached.localPosition;
                rig.SetTrackedPose(true, Center + Vector3.up * 0.5f + Vector3.right * 0.3f, 0.12f,
                    true, Center + Vector3.up * 0.25f + Vector3.right * 0.3f,
                    Center - Vector3.up * 0.25f + Vector3.right * 0.3f, 0.21f);
                var filter = (BodyProxyStabilizer)typeof(BodyProxyRig).GetField("m_Stabilizer",
                    BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rig);
                for (int i = 0; i < 60; i++) filter.Step(1f / 60f);
                typeof(BodyProxyRig).GetMethod("ApplyStabilizedPose", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rig, null);
                Check(head.position.x > 0.28f && attached.localPosition == attachmentPoint, "Attached projectile must follow filtered geometry");
                foreach (var part in rig.Parts) Check(part.transform.localScale == Vector3.one, "Attachment roots must keep unit scale");
                Check(attached.lossyScale == Vector3.one, "Stabilization must not stretch attached projectiles");
                rig.HideTorso();
                typeof(BodyProxyRig).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rig, null);
                Check(!root.transform.Find("ChestProxy").gameObject.activeSelf && head.gameObject.activeSelf,
                    "Per-frame smoothing must not reactivate expired collisions");
                rig.HideTrackedParts();
                Check(!rig.HasTrackedPart, "Reset must hide all tracked parts");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void Near(float actual, float expected, float tolerance, string message) =>
            Check(Mathf.Abs(actual - expected) < tolerance, message + $": expected {expected}, got {actual}");

        static void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException("[StabilizerValidation] FAIL: " + message);
        }
    }
}
