using System;
using System.Collections.Generic;
using EarthOL.ARCore;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EarthOL.EditorTools
{
    /// <summary>
    /// 一键打 Android APK（给真机试用）。
    /// 用的是 Unity 自带的 SDK / NDK / OpenJDK，签名用 debug keystore，可以直接侧载安装。
    /// </summary>
    public static class AndroidBuild
    {
        const string kScenePath = "Assets/Scenes/ARCoreMainScene.unity";
        const string kOutputDir = "Builds";
        const string kApkName = "EarthOL-PokeDeform.apk";

        [MenuItem("Tools/Earth OL/⑤ 打 Android APK（真机试用）", false, 20)]
        public static void MenuBuild() => Build(development: false);

        public static void BuildApk() => Build(development: false);

        /// <summary>带脚本调试信息的开发版，出问题时方便 adb logcat / 调试器</summary>
        public static void BuildDevelopmentApk() => Build(development: true);

        static void Build(bool development)
        {
            var root = System.IO.Path.GetDirectoryName(Application.dataPath) ?? ".";
            var outputDir = System.IO.Path.Combine(root, kOutputDir);
            System.IO.Directory.CreateDirectory(outputDir);

            var apkPath = System.IO.Path.Combine(outputDir, kApkName);

            // APK 而不是 AAB，方便直接安装
            EditorUserBuildSettings.buildAppBundle = false;

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            // 构建前强制重新导入场景，排除"导入缓存过期"导致打进旧场景的可能，并记录编辑器实际看到的组件数
            AssetDatabase.ImportAsset(kScenePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            ARCoreReferenceLibrarySetup.EnsureLibrary();

            int sceneBootstrapCount = Object.FindObjectsOfType<ARCoreMainSceneBootstrap>(true).Length;
            Debug.Log($"[Earth OL] 构建前场景检查: ARCoreMainSceneBootstrap={sceneBootstrapCount}");

            if (sceneBootstrapCount == 0)
                throw new Exception(
                    "[Earth OL] ARCore 场景缺少 ARCoreMainSceneBootstrap，拒绝构建。");

            var buildFlags = development
                ? BuildOptions.Development | BuildOptions.AllowDebugging
                : BuildOptions.None;

            // 强制不用增量缓存：之前 Unity 的增量构建一直复用旧的场景产物，
            // 无论场景怎么改，打出来的包里都是旧数据（白色硬边方块、戳不动）。
            buildFlags |= BuildOptions.CleanBuildCache;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { kScenePath },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = buildFlags
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            var lines = new List<string>
            {
                "=== Android 构建结果 ===",
                "类型: " + (development ? "Development" : "Release(debug 签名)"),
                "结果: " + summary.result,                "输出: " + summary.outputPath,
                "大小: " + (summary.totalSize / 1048576.0).ToString("F1") + " MB",
                "错误数: " + summary.totalErrors,
                "耗时: " + summary.totalTime
            };

            if (summary.result == BuildResult.Succeeded)
            {
                lines.Add("构建前场景: ARCoreMainSceneBootstrap=" + sceneBootstrapCount);
                lines.Add("包名: " + PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android));
                lines.Add("架构: ARM64=" + ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) != 0)
                          + " ARMv7=" + ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARMv7) != 0));
                lines.Add("最低 Android 版本(API): " + (int)PlayerSettings.Android.minSdkVersion);
            }

            WriteReport(lines);

            if (summary.result != BuildResult.Succeeded)
                throw new Exception("[Earth OL] Android 构建失败：" + summary.result);

            Debug.Log("[Earth OL] APK 已生成：" + apkPath);
        }

        static void WriteReport(List<string> lines)
        {
            try
            {
                var root = System.IO.Path.GetDirectoryName(Application.dataPath);
                var path = System.IO.Path.Combine(root ?? ".", "Logs", "build_report.txt");
                System.IO.File.WriteAllText(path, string.Join("\n", lines.ToArray()), System.Text.Encoding.UTF8);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Earth OL] 写构建报告失败：" + exception.Message);
            }
        }
    }
}
