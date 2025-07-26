using System;
using System.Collections.Generic;
using Hackerzhuli.Code.Editor.Messaging;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Hackerzhuli.Code.Editor.Testing
{
    internal class TestRunnerCallbacks : ICallbacks
    {
        public void RunFinished(ITestResultAdaptor testResultAdaptor)
        {
            CodeEditorIntegration.BroadcastMessage(MessageType.TestRunFinished, Serialize(testResultAdaptor));
        }

        public void RunStarted(ITestAdaptor testAdaptor)
        {
            CodeEditorIntegration.BroadcastMessage(MessageType.TestRunStarted,
                SerializeTopLevelOnlyWithNoSource(testAdaptor));
        }

        public void TestFinished(ITestResultAdaptor testResultAdaptor)
        {
            CodeEditorIntegration.BroadcastMessage(MessageType.TestFinished, Serialize(testResultAdaptor));
        }

        public void TestStarted(ITestAdaptor testAdaptor)
        {
            CodeEditorIntegration.BroadcastMessage(MessageType.TestStarted,
                SerializeTopLevelOnlyWithNoSource(testAdaptor));
        }

        private static string Serialize<TContainer, TSource, TAdaptor>(
            TSource source,
            Func<TSource, int, TAdaptor> createAdaptor,
            Func<TSource, IEnumerable<TSource>> children,
            Func<TAdaptor[], TContainer> container)
        {
            var adaptors = new List<TAdaptor>();

            void AddAdaptor(TSource item, int parentIndex)
            {
                var index = adaptors.Count;
                adaptors.Add(createAdaptor(item, parentIndex));
                foreach (var child in children(item))
                    AddAdaptor(child, index);
            }

            AddAdaptor(source, -1);

            return JsonUtility.ToJson(container(adaptors.ToArray()));
        }

        private static string Serialize(ITestAdaptor testAdaptor)
        {
            // Use a single MonoCecilHelper instance for all test adaptors and ensure proper disposal
            using var cecilHelper = new MonoCecilHelper();
            return Serialize(
                testAdaptor,
                (a, parentIndex) => new TestAdaptor(a, parentIndex, cecilHelper),
                a => a.Children,
                r => new TestAdaptorContainer { TestAdaptors = r });
        }

        private string Serialize(ITestResultAdaptor testResultAdaptor)
        {
            // Test results should never include children data (for efficiency, because children is already sent when they finish)
            var summary = new TestResultAdaptor(testResultAdaptor, -1);
            var container = new TestResultAdaptorContainer { TestResultAdaptors = new[] { summary } };
            var result = JsonUtility.ToJson(container);
            //Debug.Log($"Test result is:\n {result}");
            return result;
        }

        private string SerializeTopLevelOnlyWithNoSource(ITestAdaptor testAdaptor)
        {
            var topLevelAdaptor = new TestAdaptor(testAdaptor, -1);
            var container = new TestAdaptorContainer { TestAdaptors = new[] { topLevelAdaptor } };
            return JsonUtility.ToJson(container);
        }

        internal static string SerializeTestListRetrievedValue(TestMode testMode, ITestAdaptor testAdaptor)
        {
            // TestListRetrieved format:
            // TestMode:Json

            return testMode.GetModeString() + ":" + Serialize(testAdaptor);
        }
    }
}