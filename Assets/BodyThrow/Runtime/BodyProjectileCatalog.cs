using System;
using UnityEngine;

namespace EarthOL.BodyThrow
{
    [CreateAssetMenu(menuName = "Earth OL/Projectile Catalog")]
    public sealed class BodyProjectileCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string key;
            public string displayName;
            public BodyProjectile prefab;
            public Texture2D preview;
        }

        public Entry[] entries = Array.Empty<Entry>();
    }
}
