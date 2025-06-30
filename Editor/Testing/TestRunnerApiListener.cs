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
			if (!VisualStudioEditor.IsEnabled)
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
			// ExecuteTests format:
			// TestMode:FullName

			var index = command.IndexOf(':');
			if (index < 0)
				return;

			var testMode = (TestMode)Enum.Parse(typeof(TestMode), command.Substring(0, index));
			var filter = command.Substring(index + 1);

			Debug.Log($"Executing tests filter = {filter} in mode {testMode}, command is {command}");

			Filter actualFilter;
			var projectName = Path.GetFileName(Path.GetDirectoryName(Application.dataPath));

			// if the project name is received, we just execute all tests
			if(projectName != null && projectName == filter)
			{
				actualFilter = new Filter { testMode = testMode};
			}
			// if it is an assembly name(by ending with dll), we only execute tests in that assembly
			else if (filter.EndsWith(".dll"))
			{
				actualFilter = new Filter { testMode = testMode, assemblyNames = new[] { filter } };
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
