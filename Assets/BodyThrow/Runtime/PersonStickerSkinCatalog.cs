using System;
using UnityEngine;

namespace EarthOL.BodyThrow
{
    [Serializable]
    public sealed class PersonStickerSkin
    {
        public string id;
        public string displayName;
        public Sprite headArtwork;
        public bool jellyAntennae;
        public bool Available => headArtwork != null;
    }

    /// <summary>Head artwork and optional antennae share the existing body fitting and jelly shading.</summary>
    [CreateAssetMenu(menuName = "Earth OL/Person Sticker Skins")]
    public sealed class PersonStickerSkinCatalog : ScriptableObject
    {
        public const string DefaultSkinId = "skeleton";
        public PersonStickerSkin[] entries = Array.Empty<PersonStickerSkin>();

        public PersonStickerSkin Find(string id)
        {
            if (entries != null)
                foreach (var entry in entries)
                    if (entry != null && entry.id == id) return entry;
            return null;
        }
    }
}
