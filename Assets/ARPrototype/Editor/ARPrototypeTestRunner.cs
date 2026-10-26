using System.IO;
using System.Globalization;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace ARPrototype.Editor
{
    [InitializeOnLoad]
    public static class ARPrototypeTestRunner
    {
        private static readonly TestRunnerApi Runner;
        private const string RequestPath = "ARPrototype.run-tests";

        static ARPrototypeTestRunner()
        {
            Runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            Runner.RegisterCallbacks(new Results());
            EditorApplication.update += CheckRequest;
        }

        private static void CheckRequest()
        {
            if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(ARPrototypeSetup.ScenePath))
                return;
            File.Delete(RequestPath);
            Run();
        }

        [MenuItem("AR Prototype/Run Physics Acceptance Tests")]
        public static void Run()
        {
            Runner.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.PlayMode,
                assemblyNames = new[] { "ARPrototype.PlayModeTests" }
            }));
        }

        private sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                Directory.CreateDirectory("TestResults");
                var report = new XElement("test-run",
                    new XAttribute("result", result.ResultState),
                    new XAttribute("passed", result.PassCount),
                    new XAttribute("failed", result.FailCount),
                    new XAttribute("skipped", result.SkipCount), Serialize(result));
                new XDocument(report).Save("TestResults/ARPrototype-PlayMode.xml");
                Debug.Log($"AR Prototype tests: {result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped.");
            }

            private static XElement Serialize(ITestResultAdaptor result)
            {
                var node = new XElement(result.HasChildren ? "test-suite" : "test-case",
                    new XAttribute("name", result.Name),
                    new XAttribute("fullname", result.FullName),
                    new XAttribute("result", result.ResultState),
                    new XAttribute("duration", result.Duration.ToString(CultureInfo.InvariantCulture)));
                if (!string.IsNullOrEmpty(result.Message))
                    node.Add(new XElement("failure", new XElement("message", result.Message),
                        new XElement("stack-trace", result.StackTrace ?? string.Empty)));
                if (!string.IsNullOrEmpty(result.Output)) node.Add(new XElement("output", result.Output));
                foreach (ITestResultAdaptor child in result.Children) node.Add(Serialize(child));
                return node;
            }
        }
    }
}
