using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Hackerzhuli.Code.Editor.Testing
{
    [InitializeOnLoad]
    internal class TestRunnerApiListener
    {
        private const double TestListRetrievalTimeoutSeconds = 10;
        private static readonly TestRunnerApi _testRunnerApi;
        private static readonly TestRunnerCallbacks _testRunnerCallbacks;
        private static readonly Dictionary<TestMode, ITestAdaptor> _testCache = new Dictionary<TestMode, ITestAdaptor>();

        static TestRunnerApiListener()
        {
            if (!UnityInstallation.IsMainUnityEditorProcess)
                return;

            _testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            _testRunnerCallbacks = new TestRunnerCallbacks();

            _testRunnerApi.RegisterCallbacks(_testRunnerCallbacks);
        }

        public static void RetrieveTestList(string mode, Action<TestMode, ITestAdaptor> callback)
        {
            if (!Enum.TryParse(mode, out TestMode testMode))
            {
                TestRunnerCallbacks.ReportRunFailed($"Could not parse test mode '{mode}'.");
                return;
            }

            RetrieveTestList(testMode, callback, TestRunnerCallbacks.ReportRunFailed);
        }

        private static void RetrieveTestList(TestMode mode, Action<TestMode, ITestAdaptor> callback,
            Action<string> failure)
        {
            // If we already have cached test list for this mode, use it directly
            if (_testCache.ContainsKey(mode))
            {
                // Use cached root test adaptor and respond directly to the specific client
                var rootTest = _testCache[mode];
                try
                {
                    callback?.Invoke(mode, rootTest);
                }
                catch (Exception exception)
                {
                    failure?.Invoke($"Failed to process the cached {mode} test list: {exception}");
                }
                //Debug.Log($"Using cached test list for mode {mode}");
                return;
            }

            if (_testRunnerApi == null)
            {
                failure?.Invoke("Unity TestRunnerApi is not initialized.");
                return;
            }

            var completed = false;
            var timeoutAt = EditorApplication.timeSinceStartup + TestListRetrievalTimeoutSeconds;
            EditorApplication.CallbackFunction timeoutCallback = null;

            void Complete(ITestAdaptor testAdaptor)
            {
                if (completed)
                    return;

                completed = true;
                EditorApplication.update -= timeoutCallback;

                if (testAdaptor != null)
                    _testCache[mode] = testAdaptor;

                try
                {
                    callback?.Invoke(mode, testAdaptor);
                }
                catch (Exception exception)
                {
                    failure?.Invoke($"Failed to process the {mode} test list: {exception}");
                }
            }

            timeoutCallback = () =>
            {
                if (completed || EditorApplication.timeSinceStartup < timeoutAt)
                    return;

                completed = true;
                EditorApplication.update -= timeoutCallback;
                failure?.Invoke(
                    $"Timed out retrieving the {mode} test list after {TestListRetrievalTimeoutSeconds} seconds.");
            };

            EditorApplication.update += timeoutCallback;

            try
            {
                _testRunnerApi.RetrieveTestList(mode, Complete);
            }
            catch (Exception exception)
            {
                completed = true;
                EditorApplication.update -= timeoutCallback;
                failure?.Invoke($"Failed to retrieve the {mode} test list: {exception}");
            }
        }

        private static void FindMatches(ITestAdaptor testAdaptor, string searchTerm, List<string> matches)
        {
            if (testAdaptor == null) return;

            if (string.IsNullOrEmpty(searchTerm)) return;

            // if exact match is found we just end it here
            if (testAdaptor.FullName != null && string.Compare(testAdaptor.FullName, searchTerm, StringComparison.OrdinalIgnoreCase) == 0) {
                matches.Add(testAdaptor.FullName);
                return;
            }
            
            // Check if this node matches (any node with FullName can be a match)
            if (testAdaptor.FullName != null && testAdaptor.FullName.EndsWith(searchTerm, StringComparison.OrdinalIgnoreCase))
            {
                // must see the dot right before the search term, otherwise we may match too easy
                if (testAdaptor.FullName.Length > searchTerm.Length && testAdaptor.FullName[testAdaptor.FullName.Length - searchTerm.Length - 1] == '.'){
                    matches.Add(testAdaptor.FullName);
                }
            }
            
            // Recursively traverse children
            if (testAdaptor.Children != null)
            {
                foreach (var child in testAdaptor.Children)
                {
                    FindMatches(child, searchTerm, matches);
                }
            }
        }

        public static void ExecuteTests(string command)
        {
            FileLogger.Log($"Received test execution command: {command}");

            string filter = null;
            var index = command.IndexOf(':');
            // ExecuteTests format:
            // TestMode:Filter or just TestMode
            string mode;
            if (index < 0)
            {
                mode = command;
            }
            else
            {
                mode = command.Substring(0, index);
                filter = command.Substring(index + 1);
            }

            // use try parse instead
            if (!Enum.TryParse(mode, out TestMode testMode))
            {
                TestRunnerCallbacks.ReportRunFailed($"Could not parse test mode '{mode}'.");
                return;
            }

            //Debug.Log($"Executing tests filter = {filter} in mode {testMode}, command is {command}");

            Filter actualFilter = null;

            // if there is no filter, we just execute all tests
            if (string.IsNullOrEmpty(filter))
                actualFilter = new Filter { testMode = testMode };
            // if it is an assembly name(by ending with dll), we only execute tests in that assembly
            else if (filter.EndsWith(".dll"))
                // we need to remove the extension here
                actualFilter = new Filter
                    { testMode = testMode, assemblyNames = new[] { Path.GetFileNameWithoutExtension(filter) } };
            // if filter ends with ?, enable fuzzy matching
            else if (filter.EndsWith("?"))
            {
                var searchTerm = filter[..^1];

                RetrieveTestList(testMode, (_, rootTest) =>
                    {
                        var matchedTests = FindFuzzyMatches(rootTest, searchTerm);

                        ExecuteTests(new Filter
                        {
                            testMode = testMode,
                            testNames = matchedTests.Length > 0 ? matchedTests : new[] { searchTerm }
                        });
                    }, error =>
                    {
                        FileLogger.LogWarning($"{error} Falling back to the original test name.");

                        // Test discovery is only needed for fuzzy expansion. Let Unity try the original
                        // name so a transient discovery failure does not silently discard the run request.
                        ExecuteTests(new Filter { testMode = testMode, testNames = new[] { searchTerm } });
                    });
            }
            // otherwise look for the individual test
            else
                actualFilter = new Filter { testMode = testMode, testNames = new[] { filter } };

            if (actualFilter != null) ExecuteTests(actualFilter);
        }

        private static string[] FindFuzzyMatches(ITestAdaptor rootTest, string searchTerm)
        {
            var matches = new List<string>();

            // Traverse the test tree directly without creating a flat list
            FindMatches(rootTest, searchTerm, matches);
            
            return matches.Distinct().ToArray();
        }

        private static void ExecuteTests(Filter filter)
        {
            if (_testRunnerApi == null)
            {
                TestRunnerCallbacks.ReportRunFailed("Unity TestRunnerApi is not initialized.");
                return;
            }

            try
            {
                var runId = _testRunnerApi.Execute(new ExecutionSettings(filter));
                FileLogger.Log($"Scheduled test run {runId} in mode {filter.testMode}.");
            }
            catch (Exception exception)
            {
                TestRunnerCallbacks.ReportRunFailed($"Failed to schedule the test run: {exception}");
            }
        }
    }
}
