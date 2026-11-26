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
            public bool canHold;
            public Color slimeColor = new Color(.55f, .9f, .15f, 1f);
        }

        public Entry[] entries = Array.Empty<Entry>();
    }
}
