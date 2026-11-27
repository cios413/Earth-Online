using UnityEngine;
using System.Collections;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace EarthOL.BodyThrow
{
    /// <summary>Static snapshot and anchor; deliberately has no tracking update loop.</summary>
    [DisallowMultipleComponent]
    public sealed class Frozen3DPersonController : MonoBehaviour
    {
        public FrozenPersonData Snapshot { get; private set; }
        public ARAnchor Anchor { get; private set; }
        public int PersonId => Snapshot != null ? Snapshot.PersonId : -1;
        public void Initialize(FrozenPersonData snapshot, ARAnchor anchor)
        {
            Snapshot = snapshot;
            Anchor = anchor;
            if (Application.isPlaying && !Application.isEditor) StartCoroutine(VerifyAnchor());
        }

        IEnumerator VerifyAnchor()
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (Anchor != null && Anchor.trackableId == TrackableId.invalidId && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (Anchor == null || Anchor.trackableId == TrackableId.invalidId)
            {
                Debug.LogError("[FrozenPerson] ARCore未能创建Anchor，已移除未锚定模型。");
                Destroy(gameObject);
            }
            else Debug.Log("[FrozenPerson] Anchor registered: " + Anchor.trackableId + " PersonID=" + PersonId);
        }
    }
}
