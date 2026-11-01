using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using UnityEditor.XR.ARSubsystems;

namespace EarthOL.EditorTools
{
    public static class ARCoreReferenceLibrarySetup
    {
        const string LibraryPath = "Assets/Resources/ARCoreReferenceImageLibrary.asset";
        const string Image3Path = "Assets/Editor/Vuforia/ImageTargetTextures/cios/3_scaled.jpg";
        const string Image6Path = "Assets/Editor/Vuforia/ImageTargetTextures/cios/6_scaled.jpg";

        [MenuItem("Tools/Earth OL/ARCore 生成参考图像库")]
        public static void MenuEnsureLibrary()
        {
            EnsureLibrary();
            Debug.Log("[ARCore] 参考图像库已生成：" + LibraryPath);
        }

        public static XRReferenceImageLibrary EnsureLibrary()
        {
            var image3 = AssetDatabase.LoadAssetAtPath<Texture2D>(Image3Path);
            var image6 = AssetDatabase.LoadAssetAtPath<Texture2D>(Image6Path);
            if (image3 == null || image6 == null)
                throw new Exception("找不到 3_scaled.jpg 或 6_scaled.jpg，无法生成 ARCore 参考图像库。");

            var library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            while (library.count > 0) library.RemoveAt(library.count - 1);

            Add(library, image3, "3_scaled", new Vector2(0.1125f, 0.2f));
            Add(library, image6, "6_scaled", new Vector2(0.1125f, 0.2f));
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return library;
        }

        static void Add(XRReferenceImageLibrary library, Texture2D texture, string name, Vector2 size)
        {
            library.Add();
            int index = library.count - 1;
            library.SetTexture(index, texture, true);
            library.SetSpecifySize(index, true);
            library.SetSize(index, size);
            library.SetName(index, name);
        }
    }
}
