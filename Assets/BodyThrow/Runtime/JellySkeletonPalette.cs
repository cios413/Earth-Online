using UnityEngine;

namespace EarthOL.BodyThrow
{
    /// <summary>Two candy colors per selection, retained through tracking loss and rig pooling.</summary>
    public struct JellySkeletonPalette
    {
        public Color body;
        public Color limbs;
        static readonly Color[] Candy =
        {
            new Color(1f, .24f, .51f), new Color(.20f, .85f, .76f),
            new Color(.63f, .34f, 1f), new Color(1f, .53f, .17f),
            new Color(.71f, .92f, .16f), new Color(.26f, .66f, 1f),
            new Color(.98f, .48f, .77f), new Color(1f, .83f, .25f)
        };

        public static JellySkeletonPalette RandomPair()
        {
            int first = Random.Range(0, Candy.Length);
            // Two distinct hues; all limbs share the second color.
            int second = (first + Random.Range(1, Candy.Length)) % Candy.Length;
            return new JellySkeletonPalette { body = Candy[first], limbs = Candy[second] };
        }
    }
}
