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
        private static readonly TestRunnerApi _testRunnerApi;
        private static readonly TestRunnerCallbacks _testRunnerCallbacks;
        private static readonly Dictionary<TestMode, ITestAdaptor> _testCache = new Dictionary<TestMode, ITestAdaptor>();

        static TestRunnerApiListener()
        {
            if (!CodeEditor.IsEnabled)
                return;

            _testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            _testRunnerCallbacks = new TestRunnerCallbacks();

            _testRunnerApi.RegisterCallbacks(_testRunnerCallbacks);
        }

        public static void RetrieveTestList(string mode, Action<TestMode, ITestAdaptor> callback)
        {
            RetrieveTestList((TestMode)Enum.Parse(typeof(TestMode), mode), callback);
        }

        private static void RetrieveTestList(TestMode mode)
        {
            // If we already have cached test list for this mode, use it directly
            if (_testCache.ContainsKey(mode))
            {
                var rootTest = _testCache[mode];
                return;
            }
            
            // No cached data available, retrieve from API
            if(_testRunnerApi != null){
                _testRunnerApi.RetrieveTestList(mode, ta => 
                {
                    // Cache the test list for fuzzy matching
                    _testCache[mode] = ta;
                });
            }
        }

        private static void RetrieveTestList(TestMode mode, System.Action<TestMode, ITestAdaptor> callback)
        {
            // If we already have cached test list for this mode, use it directly
            if (_testCache.ContainsKey(mode))
            {
                // Use cached root test adaptor and respond directly to the specific client
                var rootTest = _testCache[mode];
                callback?.Invoke(mode, rootTest);
                return;
            }
            
            // No cached data available, retrieve from API
            if(_testRunnerApi != null){
                _testRunnerApi.RetrieveTestList(mode, ta => 
                {
                    // Cache the test list for fuzzy matching
                    _testCache[mode] = ta;
                    callback?.Invoke(mode, ta);
                });
            }
        }

        private static void FindMatches(ITestAdaptor testAdaptor, string searchTerm, List<string> matches)
        {
            if (testAdaptor == null) return;

            // if exact match is found we just end it here
            if (testAdaptor.FullName == searchTerm) {
                matches.Add(testAdaptor.FullName);
                return;
            }
            
            // Check if this node matches (any node with FullName can be a match)
            if (testAdaptor.FullName != null && testAdaptor.FullName.EndsWith(searchTerm, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(testAdaptor.FullName);
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
                Debug.LogError($"Could not parse test mode {mode}");
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

                RetrieveTestList(mode, (_, rootTest) =>
                {
                    var matchedTests = FindFuzzyMatches(rootTest, searchTerm);
                    
                    if (matchedTests.Length > 0)
                    {
                        actualFilter = new Filter { testMode = testMode, testNames = matchedTests };
                        ExecuteTests(actualFilter);
                    }
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
            _testRunnerApi?.Execute(new ExecutionSettings(filter));
        }
    }
}