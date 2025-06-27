using System;
using UnityEditor.TestTools.TestRunner.Api;

namespace Hackerzhuli.Code.Editor.Testing
{
	/// <summary>
	/// Container for serializing an array of <see cref="TestResultAdaptor"/> objects.
	/// </summary>
	[Serializable]
	internal class TestResultAdaptorContainer
	{
		/// <summary>
		/// Array of test result adaptors for serialization.
		/// </summary>
		public TestResultAdaptor[] TestResultAdaptors;
	}

	/// <summary>
	/// Serializable adaptor for Unity's <see cref="ITestResultAdaptor"/> interface.
	/// Represents the test results for a node in the test tree.
	/// </summary>
	[Serializable]
	internal class TestResultAdaptor
	{
		/// <summary>
		/// The name of the test node.
		/// </summary>
		public string Name;
		
		/// <summary>
		/// Gets the full name of the test result.
		/// </summary>
		public string FullName;

		/// <summary>
		/// The number of test cases that passed when running the test and all its children.
		/// </summary>
		public int PassCount;
		
		/// <summary>
		/// The number of test cases that failed when running the test and all its children.
		/// </summary>
		public int FailCount;
		
		/// <summary>
		/// The number of test cases that were inconclusive when running the test and all its children.
		/// </summary>
		public int InconclusiveCount;
		
		/// <summary>
		/// The number of test cases that were skipped when running the test and all its children.
		/// </summary>
		public int SkipCount;

		/// <summary>
		/// Gets the state of the result as a string.
		/// Returns one of these values: Inconclusive, Skipped, Skipped:Ignored, Skipped:Explicit, Passed, Failed, Failed:Error, Failed:Cancelled, Failed:Invalid.
		/// </summary>
		public string ResultState;
		
		/// <summary>
		/// Any stacktrace associated with an error or failure, empty if the test passed (only avaiable for leaf tests)
		/// </summary>
		public string StackTrace;

		/// <summary>
		/// The test status as a simplified enum value.
		/// </summary>
		public TestStatusAdaptor TestStatus;

		/// <summary>
		/// The number of asserts executed when running the test and all its children.
		/// </summary>
		public int AssertCount;
		
		/// <summary>
		/// Gets the elapsed time for running the test in seconds.
		/// </summary>
		public double Duration;
		
		/// <summary>
		/// Gets the time the test started running as Unix timestamp (milliseconds since epoch).
		/// </summary>
		public long StartTime;
		
		/// <summary>
		/// Gets the time the test finished running as Unix timestamp (milliseconds since epoch).
		/// </summary>
		public long EndTime;
		
		/// <summary>
		/// The error message associated with a test failure or with not running the test, empty if the test (and its children) passed
		/// </summary>
		public string Message;
		
		/// <summary>
		/// Gets all logs during the test(only available for leaf tests)(no stack trace for logs is available)
		/// </summary>
		public string Output;
		
		/// <summary>
		/// True if this result has any child results.
		/// </summary>
		public bool HasChildren;

		/// <summary>
		/// Index of parent in TestResultAdaptors array, -1 for root.
		/// </summary>
		public int Parent;

		/// <summary>
		/// Initializes a new instance of the <see cref="TestResultAdaptor"/> class from Unity's <see cref="ITestResultAdaptor"/>.
		/// </summary>
		/// <param name="testResultAdaptor">The Unity test result adaptor to convert from.</param>
		/// <param name="parent">Index of parent in TestResultAdaptors array, -1 for root.</param>
		public TestResultAdaptor(ITestResultAdaptor testResultAdaptor, int parent)
		{
			Name = testResultAdaptor.Name;
			FullName = testResultAdaptor.FullName;

			PassCount = testResultAdaptor.PassCount;
			FailCount = testResultAdaptor.FailCount;
			InconclusiveCount = testResultAdaptor.InconclusiveCount;
			SkipCount = testResultAdaptor.SkipCount;

			ResultState = testResultAdaptor.ResultState;
			StackTrace = testResultAdaptor.StackTrace;
			
			AssertCount = testResultAdaptor.AssertCount;
			Duration = testResultAdaptor.Duration;
			StartTime = ((DateTimeOffset)testResultAdaptor.StartTime).ToUnixTimeMilliseconds();
			EndTime = ((DateTimeOffset)testResultAdaptor.EndTime).ToUnixTimeMilliseconds();
			Message = testResultAdaptor.Message;
			Output = testResultAdaptor.Output;
			HasChildren = testResultAdaptor.HasChildren;

			switch (testResultAdaptor.TestStatus)
			{
				case UnityEditor.TestTools.TestRunner.Api.TestStatus.Passed:
					TestStatus = TestStatusAdaptor.Passed;
					break;
				case UnityEditor.TestTools.TestRunner.Api.TestStatus.Skipped:
					TestStatus = TestStatusAdaptor.Skipped;
					break;
				case UnityEditor.TestTools.TestRunner.Api.TestStatus.Inconclusive:
					TestStatus = TestStatusAdaptor.Inconclusive;
					break;
				case UnityEditor.TestTools.TestRunner.Api.TestStatus.Failed:
					TestStatus = TestStatusAdaptor.Failed;
					break;
			}

			Parent = parent;
		}
	}
}
