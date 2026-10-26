using System;
using System.IO;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

namespace ARPrototype.Editor
{
    /// <summary>Creates actual serialized assets using this Editor's APIs, rather than handwritten scene YAML.</summary>
    public static class ARPrototypeSetup
    {
        public const string Root = "Assets/ARPrototype";
        public const string ScenePath = Root + "/Scenes/ARThrowPrototype.unity";
        public const string PrefabPath = Root + "/Prefabs/Projectile.prefab";
        private const string SettingsPath = "Assets/XR/Settings/ARPrototypeXRSettings.asset";

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.update += FirstImport;
        }

        private static void FirstImport()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            EditorApplication.update -= FirstImport;
            if (File.Exists(ScenePath)) return;
            try { CreateMissingAssets(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        [MenuItem("AR Prototype/Create Missing Prototype Assets")]
        public static void CreateMissingAssets()
        {
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Scenes");
            EnsureFolder("Assets/XR/Settings");
            ConfigurePlatforms();
            ConfigureXR();
            int projectileLayer = EnsureLayer("Projectile");
            if (projectileLayer >= 0)
            {
                Physics.IgnoreLayerCollision(projectileLayer, projectileLayer, true);
                EditorUtility.SetDirty(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
            }

            Material projectileMaterial = MaterialAsset("Projectile", new Color(1f, 0.5f, 0.08f));
            Material targetMaterial = MaterialAsset("Target", new Color(0.1f, 0.65f, 0.85f));
            Material obstacleMaterial = MaterialAsset("Obstacle", new Color(0.6f, 0.2f, 0.25f));

            Projectile prefab = AssetDatabase.LoadAssetAtPath<Projectile>(PrefabPath);
            if (prefab == null)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Projectile";
                cube.transform.localScale = Vector3.one * 0.04f;
                if (projectileLayer >= 0) cube.layer = projectileLayer;
                cube.GetComponent<Renderer>().sharedMaterial = projectileMaterial;
                Rigidbody body = cube.AddComponent<Rigidbody>();
                body.mass = 0.02f;
                body.useGravity = false;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                cube.AddComponent<Projectile>();
                prefab = PrefabUtility.SaveAsPrefabAsset(cube, PrefabPath).GetComponent<Projectile>();
                UnityEngine.Object.DestroyImmediate(cube);
            }

            Scene previousScene = SceneManager.GetActiveScene();
            bool openCreatedScene = !previousScene.isDirty &&
                                    (previousScene.path == "Assets/Scenes/SampleScene.unity" ||
                                     string.IsNullOrEmpty(previousScene.path));
            if (!File.Exists(ScenePath))
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                try
                {
                    CreateScene(prefab, targetMaterial, obstacleMaterial);
                    EditorSceneManager.SaveScene(scene, ScenePath);
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                    SceneManager.SetActiveScene(previousScene);
                }
                if (openCreatedScene)
                    EditorSceneManager.OpenScene(ScenePath);
            }

            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(scene => scene.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("AR Prototype: scene, prefab and XR settings are ready. Open " + ScenePath);
        }

        private static void CreateScene(Projectile prefab, Material targetMaterial, Material obstacleMaterial)
        {
            var sessionObject = new GameObject("AR Session");
            ARSession session = sessionObject.AddComponent<ARSession>();
            sessionObject.AddComponent<ARInputManager>();

            var originObject = new GameObject("XR Origin (Mobile AR)");
            XROrigin origin = originObject.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originObject.transform, false);
            var cameraObject = new GameObject("AR Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(offset.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 30f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<ARCameraManager>();
            cameraObject.AddComponent<ARCameraBackground>();
            TrackedPoseDriver driver = cameraObject.AddComponent<TrackedPoseDriver>();
            driver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            origin.Camera = camera;
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            origin.CameraYOffset = 0f;

            // Keep the attachment root at unit scale. Put nonuniform visual dimensions below it.
            var target = new GameObject("TargetCube");
            target.transform.position = new Vector3(0f, 0f, 1.5f);
            Rigidbody targetBody = target.AddComponent<Rigidbody>();
            targetBody.isKinematic = true;
            targetBody.useGravity = false;
            targetBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var targetShape = GameObject.CreatePrimitive(PrimitiveType.Cube);
            targetShape.name = "TargetShape";
            targetShape.transform.SetParent(target.transform, false);
            targetShape.transform.localScale = new Vector3(0.6f, 0.4f, 0.2f);
            targetShape.GetComponent<Renderer>().sharedMaterial = targetMaterial;

            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.name = "OtherCollider (move in front of TargetCube to test rejection)";
            obstacle.transform.position = new Vector3(-0.55f, 0f, 1f);
            obstacle.transform.localScale = new Vector3(0.2f, 0.4f, 0.15f);
            obstacle.GetComponent<Renderer>().sharedMaterial = obstacleMaterial;

            var spawnerObject = new GameObject("Projectile Spawner");
            ProjectileSpawner spawner = spawnerObject.AddComponent<ProjectileSpawner>();
            spawner.ProjectilePrefab = prefab;
            spawner.ARCamera = camera;
            spawner.SelectedTarget = target.transform;
            spawnerObject.AddComponent<EditorARPreview>().Configure(session, camera);

            var lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.gray;
            RenderSettings.skybox = null;
        }

        [MenuItem("AR Prototype/Configure Mobile Platforms and XR")]
        public static void ConfigurePlatformsAndXR()
        {
            EnsureFolder("Assets/XR/Settings");
            ConfigurePlatforms();
            ConfigureXR();
            AssetDatabase.SaveAssets();
        }

        private static void ConfigurePlatforms()
        {
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.prototype.arthrow");
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, "com.prototype.arthrow");
            PlayerSettings.iOS.targetOSVersionString = "13.0";
            PlayerSettings.iOS.cameraUsageDescription = "使用摄像头显示 AR 场景并追踪手机位置。";
        }

        private static void ConfigureXR()
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget settings))
            {
                settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(SettingsPath);
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(settings, SettingsPath);
                }
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            }
            AssignLoader(settings, BuildTargetGroup.Android, "UnityEngine.XR.ARCore.ARCoreLoader");
            AssignLoader(settings, BuildTargetGroup.iOS, "UnityEngine.XR.ARKit.ARKitLoader");
            EditorUtility.SetDirty(settings);
        }

        private static void AssignLoader(XRGeneralSettingsPerBuildTarget settings, BuildTargetGroup group, string type)
        {
            if (!settings.HasSettingsForBuildTarget(group))
                settings.CreateDefaultSettingsForBuildTarget(group);
            if (!settings.HasManagerSettingsForBuildTarget(group))
                settings.CreateDefaultManagerSettingsForBuildTarget(group);
            XRGeneralSettings general = settings.SettingsForBuildTarget(group);
            general.InitManagerOnStart = true;
            if (!XRPackageMetadataStore.AssignLoader(general.Manager, type, group))
                Debug.LogWarning("AR Prototype: enable " + type + " in XR Plug-in Management for " + group);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(general.Manager);
        }

        private static Material MaterialAsset(string name, Color color)
        {
            string path = Root + "/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Standard")) { name = name, color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tags.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                if (!string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) continue;
                layers.GetArrayElementAtIndex(i).stringValue = name;
                tags.ApplyModifiedProperties();
                return i;
            }
            Debug.LogWarning("AR Prototype: no free layer; projectiles will also collide with each other.");
            return -1;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}
