using UnityEngine;

namespace EarthOL.BodyThrow
{
    /// <summary>A confirmed interaction; consumers decide whether it has gameplay consequences.</summary>
    public readonly struct PersonInteractionHit
    {
        static long s_NextId;
        public static long NextId() => System.Threading.Interlocked.Increment(ref s_NextId);
        public readonly int personId;
        public readonly string actionId;
        public readonly Vector2 viewportPoint;
        public readonly long eventId;
        public readonly int context;

        public PersonInteractionHit(int personId, string actionId, Vector2 point, long eventId, int context)
        {
            this.personId = personId;
            this.actionId = actionId;
            viewportPoint = point;
            this.eventId = eventId;
            this.context = context;
        }
    }
}
