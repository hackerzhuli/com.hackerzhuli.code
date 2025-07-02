using System;
using System.IO;
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

		static TestRunnerApiListener()
		{
			if (!VisualStudioCodeEditor.IsEnabled)
				return;

			_testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
			_testRunnerCallbacks = new TestRunnerCallbacks();

			_testRunnerApi.RegisterCallbacks(_testRunnerCallbacks);
		}

		public static void RetrieveTestList(string mode)
		{
			RetrieveTestList((TestMode) Enum.Parse(typeof(TestMode), mode));
		}

		private static void RetrieveTestList(TestMode mode)
		{
			_testRunnerApi?.RetrieveTestList(mode, ta => _testRunnerCallbacks.TestListRetrieved(mode, ta));
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

			Filter actualFilter;

			// if there is no filter, we just execute all tests
			if(string.IsNullOrEmpty(filter))
			{
				actualFilter = new Filter { testMode = testMode};
			}
			// if it is an assembly name(by ending with dll), we only execute tests in that assembly
			else if (filter.EndsWith(".dll"))
			{
				// we need to remove the extension here
				actualFilter = new Filter { testMode = testMode, assemblyNames = new[] { Path.GetFileNameWithoutExtension(filter) } };
			}
			// otherwise look for the individual test
			else
			{
				actualFilter = new Filter { testMode = testMode, testNames = new[] { filter } };
			}

			if(actualFilter != null)
			{
				ExecuteTests(actualFilter);
			}
		}

		private static void ExecuteTests(Filter filter)
		{
			_testRunnerApi?.Execute(new ExecutionSettings(filter));
		}
	}
}
