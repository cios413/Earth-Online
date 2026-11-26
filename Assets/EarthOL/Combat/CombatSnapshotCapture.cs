using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EarthOL.Combat
{
    public enum CombatSnapshotState { Waiting, Capturing, Captured, Failed, Cancelled }

    /// <summary>A single frame owned by one selected person and round, never by a later selection.</summary>
    public sealed class CombatSnapshot
    {
        public int PersonId { get; internal set; }
        public int RoundToken { get; internal set; }
        public int Session { get; internal set; }
        public float SelectedAt { get; internal set; }
        public float CaptureRequestedAt { get; internal set; }
        public CombatSnapshotState State { get; internal set; }
        public bool IsPending => State == CombatSnapshotState.Waiting || State == CombatSnapshotState.Capturing;
        public Texture2D Image { get; internal set; }
        public string SavedPath { get; internal set; }
        public string Error { get; internal set; }
    }

    /// <summary>Captures the defeated person's newly rendered sticker and persists one PNG per round.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatSnapshotCapture : MonoBehaviour
    {
        readonly Dictionary<int, CombatSnapshot> m_Snapshots = new Dictionary<int, CombatSnapshot>();
        readonly WaitForEndOfFrame m_EndOfFrame = new WaitForEndOfFrame();
        string m_SaveDirectory;
        public string SaveDirectory => m_SaveDirectory ?? Path.Combine(Application.persistentDataPath,"CombatSnapshots");

        public CombatSnapshot Schedule(int personId, int token, int session)
        {
            if (m_Snapshots.TryGetValue(token,out var existing)) return existing;
            var snapshot = new CombatSnapshot
            {
                PersonId = personId, RoundToken = token, Session = session,
                SelectedAt = Time.realtimeSinceStartup,
                State = CombatSnapshotState.Waiting
            };
            m_Snapshots.Add(token,snapshot);
            return snapshot;
        }

        bool Owns(CombatSnapshot snapshot) => snapshot != null &&
            m_Snapshots.TryGetValue(snapshot.RoundToken,out var current) && ReferenceEquals(current,snapshot);

        public bool CaptureOnStickerDisplayed(CombatSnapshot snapshot, int personId, int session)
        {
            if (!Owns(snapshot) || snapshot.State != CombatSnapshotState.Waiting ||
                snapshot.PersonId != personId || snapshot.Session != session) return false;
            snapshot.State = CombatSnapshotState.Capturing;
            snapshot.CaptureRequestedAt = Time.realtimeSinceStartup;
            if (Application.isPlaying) StartCoroutine(CaptureFrame(snapshot));
            return true;
        }

        IEnumerator CaptureFrame(CombatSnapshot snapshot)
        {
            yield return m_EndOfFrame;
            if (!Owns(snapshot) || snapshot.State != CombatSnapshotState.Capturing) yield break;
            Texture2D image = null;
            try
            {
                image = ScreenCapture.CaptureScreenshotAsTexture();
                if (image == null) throw new InvalidOperationException("屏幕截图未返回图像。");
            }
            catch (Exception error)
            {
                snapshot.State = CombatSnapshotState.Failed; snapshot.Error = error.Message;
                Debug.LogWarning("[CombatSnapshot] 截图失败："+error.Message);
            }
            if (image != null) CompleteCapture(snapshot,image);
        }

        // Takes ownership even when a stale, cancelled end-of-frame capture arrives.
        void CompleteCapture(CombatSnapshot snapshot, Texture2D image)
        {
            if (!Owns(snapshot) || snapshot.State != CombatSnapshotState.Capturing)
            { DestroyImage(image); return; }
            snapshot.Image = image;
            try
            {
                Directory.CreateDirectory(SaveDirectory);
                string name = $"Combat_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_person{snapshot.PersonId}_round{snapshot.RoundToken}_{Guid.NewGuid():N}.png";
                string path = Path.Combine(SaveDirectory,name);
                File.WriteAllBytes(path,image.EncodeToPNG());
                snapshot.SavedPath = path;
                Debug.Log("[CombatSnapshot] 已保存："+path);
            }
            catch (Exception error)
            {
                snapshot.Error = error.Message;
                Debug.LogWarning("[CombatSnapshot] 图片已截取，但保存失败："+error.Message);
            }
            snapshot.State = CombatSnapshotState.Captured;
        }

        public void CancelPending(CombatSnapshot snapshot)
        {
            if (Owns(snapshot) && snapshot.IsPending) snapshot.State = CombatSnapshotState.Cancelled;
        }

        public void Release(CombatSnapshot snapshot)
        {
            if (!Owns(snapshot)) return;
            CancelPending(snapshot);
            DestroyImage(snapshot.Image); snapshot.Image = null;
            m_Snapshots.Remove(snapshot.RoundToken);
        }

        public void Clear()
        {
            StopAllCoroutines();
            foreach (var snapshot in m_Snapshots.Values)
            {
                if (snapshot.IsPending) snapshot.State = CombatSnapshotState.Cancelled;
                DestroyImage(snapshot.Image); snapshot.Image = null;
            }
            m_Snapshots.Clear();
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            foreach (var snapshot in m_Snapshots.Values) CancelPending(snapshot);
        }
        void OnDisable() => Clear();
        void OnDestroy() => Clear();
        static void DestroyImage(Texture2D image)
        {
            if (image == null) return;
            if (Application.isPlaying) Destroy(image); else DestroyImmediate(image);
        }
    }
}
