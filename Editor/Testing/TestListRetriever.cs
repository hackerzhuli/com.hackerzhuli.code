using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Hackerzhuli.Code.Editor.Testing
{
	/// <summary>
	/// Test class for retrieving and converting Unity test lists to TestAdaptorContainer for local testing.
	/// Includes performance timing to measure conversion overhead.
	/// </summary>
	internal class TestListRetrieverTests
	{
		private TestRunnerApi _testRunnerApi;
		private TestAdaptorContainer _lastRetrievedContainer;
		private long _lastConversionTimeMs;
		private int _lastTestCount;

		//[SetUp]
		public void SetUp()
		{
			_testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
		}
   
		//[TearDown]
		public void TearDown()
		{
			if (_testRunnerApi)
			{
				Object.DestroyImmediate(_testRunnerApi);
				_testRunnerApi = null;
			}
		}

		/// <summary>
		/// Retrieves tests for the specified mode and converts them to TestAdaptorContainer asynchronously.
		/// </summary>
		/// <param name="testMode">The test mode to retrieve tests for.</param>
		/// <returns>Task containing the container, conversion time, and test count.</returns>
		private async System.Threading.Tasks.Task<(TestAdaptorContainer container, long conversionTimeMs, int testCount)> RetrieveTestsAsync(TestMode testMode)
		{
			if (!_testRunnerApi)
			{
				Debug.LogError("TestRunnerApi is not initialized.");
				return (null, 0, 0);
			}

			Debug.Log($"Starting test retrieval for {testMode} mode...");
			var overallStopwatch = Stopwatch.StartNew();

			var tcs = new System.Threading.Tasks.TaskCompletionSource<ITestAdaptor>();

			_testRunnerApi.RetrieveTestList(testMode, testAdaptor =>
			{
				tcs.SetResult(testAdaptor);
			});

			try
			{
				var testAdaptor = await tcs.Task;

				// Start timing the conversion process
				var conversionStopwatch = Stopwatch.StartNew();

				// Convert Unity test adaptor to our container
				var container = ConvertToTestAdaptorContainer(testAdaptor);

				conversionStopwatch.Stop();
				overallStopwatch.Stop();

				// Store results
				_lastRetrievedContainer = container;
				_lastConversionTimeMs = conversionStopwatch.ElapsedMilliseconds;
				_lastTestCount = container?.TestAdaptors?.Length ?? 0;

				// Log performance metrics
				Debug.Log($"Test retrieval completed for {testMode} mode:\n" +
				         $"- Total time: {overallStopwatch.ElapsedMilliseconds}ms\n" +
				         $"- Conversion time: {_lastConversionTimeMs}ms\n" +
				         $"- Test count: {_lastTestCount}\n" +
				         $"- Assembly loading overhead: {overallStopwatch.ElapsedMilliseconds - _lastConversionTimeMs}ms");

				return (container, _lastConversionTimeMs, _lastTestCount);
			}
			catch (Exception ex)
			{
				overallStopwatch.Stop();
				Debug.LogError($"Error during test retrieval and conversion: {ex.Message}\n{ex.StackTrace}");
				return (null, 0, 0);
			}
		}

		/// <summary>
		/// Converts Unity's ITestAdaptor to our TestAdaptorContainer using the same logic as TestRunnerCallbacks.
		/// </summary>
		/// <param name="testAdaptor">The Unity test adaptor to convert.</param>
		/// <returns>TestAdaptorContainer with all tests converted.</returns>
		private TestAdaptorContainer ConvertToTestAdaptorContainer(ITestAdaptor testAdaptor)
		{
			if (testAdaptor == null)
			{
				return new TestAdaptorContainer { TestAdaptors = Array.Empty<TestAdaptor>() };
			}
			using var cecilHelper = new MonoCecilHelper();

			var adaptors = new List<TestAdaptor>();

			// Recursive function to add test adaptor and its children
			void AddAdaptor(ITestAdaptor item, int parentIndex)
			{
				var index = adaptors.Count;
				adaptors.Add(new TestAdaptor(item, parentIndex, cecilHelper));
				
				// Add all children recursively
				foreach (var child in item.Children)
				{
					AddAdaptor(child, index);
				}
			}

			// Start conversion from root with no parent (-1)
			AddAdaptor(testAdaptor, -1);

			return new TestAdaptorContainer { TestAdaptors = adaptors.ToArray() };
		}

		/// <summary>
		/// Test that retrieves all editor mode tests and measures conversion performance.
		/// </summary>
		//[Test]
		public async System.Threading.Tasks.Task TestRetrieveEditorModeTests()
		{
			var result = await RetrieveTestsAsync(TestMode.EditMode);
			
			Assert.IsNotNull(result.container, "Failed to retrieve editor mode tests");
			Assert.IsNotNull(result.container.TestAdaptors, "TestAdaptors array should not be null");
			Assert.Greater(result.conversionTimeMs, 0, "Conversion time should be measured");
			
			Debug.Log($"Editor Mode Tests - Count: {result.testCount}, Conversion Time: {result.conversionTimeMs}ms");
			LogTestSummary(result.container, "Editor Mode");
		}

		/// <summary>
		/// Test that validates source location information is properly populated for both methods and types.
		/// </summary>
		//[Test]
		public async System.Threading.Tasks.Task TestSourceLocationPopulation()
		{
			var result = await RetrieveTestsAsync(TestMode.EditMode);
			
			Assert.IsNotNull(result.container, "Failed to retrieve tests");
			
			var testsWithSourceLocation = 0;
			var totalTestMethods = 0;
			var typesWithSourceLocation = 0;
			var totalTestTypes = 0;
			
			foreach (var test in result.container.TestAdaptors)
			{
				if (!string.IsNullOrEmpty(test.Method))
				{
					// This is a test method
					totalTestMethods++;
					if (!string.IsNullOrEmpty(test.SourceLocation))
					{
						testsWithSourceLocation++;
						Assert.IsTrue(test.SourceLocation.Contains(":"), "Source location should contain line number");
					}
				}
				else if (string.IsNullOrEmpty(test.Method) && !string.IsNullOrEmpty(test.Type))
				{
					// This is a test type/class
					totalTestTypes++;
					if (!string.IsNullOrEmpty(test.SourceLocation))
					{
						typesWithSourceLocation++;
						Assert.IsTrue(test.SourceLocation.Contains(":"), "Type source location should contain line number");
					}
				}
			}
			
			var methodCoverage = totalTestMethods > 0 ? (float)testsWithSourceLocation / totalTestMethods * 100 : 0;
			var typeCoverage = totalTestTypes > 0 ? (float)typesWithSourceLocation / totalTestTypes * 100 : 0;
			
			Debug.Log($"Method Source Location Coverage: {testsWithSourceLocation}/{totalTestMethods} ({methodCoverage:F1}%)");
			Debug.Log($"Type Source Location Coverage: {typesWithSourceLocation}/{totalTestTypes} ({typeCoverage:F1}%)");
			
			// Log some examples of method source locations
			var methodExamplesLogged = 0;
			Debug.Log("=== Method Source Location Examples ===");
			foreach (var test in result.container.TestAdaptors)
			{
				if (!string.IsNullOrEmpty(test.Method) && !string.IsNullOrEmpty(test.SourceLocation) && methodExamplesLogged < 5)
				{
					Debug.Log($"Method: {test.Name} -> {test.SourceLocation}");
					methodExamplesLogged++;
				}
			}
			
			// Log some examples of type source locations
			var typeExamplesLogged = 0;
			Debug.Log("=== Type Source Location Examples ===");
			foreach (var test in result.container.TestAdaptors)
			{
				if (string.IsNullOrEmpty(test.Method) && !string.IsNullOrEmpty(test.Type) && !string.IsNullOrEmpty(test.SourceLocation) && typeExamplesLogged < 5)
				{
					Debug.Log($"Type: {test.Name} ({test.Type}) -> {test.SourceLocation}");
					typeExamplesLogged++;
				}
			}
			
			// Log types without source location for debugging
			var typesWithoutSourceLogged = 0;
			Debug.Log("=== Types WITHOUT Source Location (for debugging) ===");
			foreach (var test in result.container.TestAdaptors)
			{
				if (string.IsNullOrEmpty(test.Method) && !string.IsNullOrEmpty(test.Type) && string.IsNullOrEmpty(test.SourceLocation) && typesWithoutSourceLogged < 5)
				{
					Debug.Log($"Type without source: {test.Name} ({test.Type})");
					typesWithoutSourceLogged++;
				}
			}
		}

		/// <summary>
		/// Performance test that benchmarks MonoCecilHelper source location retrieval speed by calling it 1000 times.
		/// </summary>
		//[Test]
		public async System.Threading.Tasks.Task TestMonoCecilHelperPerformance()
		{
			var result = await RetrieveTestsAsync(TestMode.EditMode);
			
			Assert.IsNotNull(result.container, "Failed to retrieve tests");
			
			// Find test methods with reflection info for benchmarking
			var testMethodsWithReflection = new List<(string assembly, string type, string method)>();
			
			foreach (var test in result.container.TestAdaptors)
			{
				if (!string.IsNullOrEmpty(test.Method) && !string.IsNullOrEmpty(test.Assembly) && !string.IsNullOrEmpty(test.Type))
				{
					testMethodsWithReflection.Add((test.Assembly, test.Type, test.Method));
					if (testMethodsWithReflection.Count >= 10) // Limit to first 10 for performance testing
						break;
				}
			}
			
			Assert.Greater(testMethodsWithReflection.Count, 0, "No test methods found for performance testing");
			
			Debug.Log($"Starting MonoCecilHelper performance test with {testMethodsWithReflection.Count} test methods, 1000 iterations each...");
			
			var totalStopwatch = Stopwatch.StartNew();
			var successfulCalls = 0;
			var failedCalls = 0;
			var validSourceLocationCalls = 0;
			var emptySourceLocationCalls = 0;
			
			// Perform 1000 iterations of source location retrieval using disposable pattern
			using var helper = new MonoCecilHelper();
			for (var iteration = 0; iteration < 1000; iteration++)
			{
				foreach (var (assembly, type, method) in testMethodsWithReflection)
				{
					try
					{
						// Load assembly and get method info
						var assemblyObj = System.Reflection.Assembly.LoadFrom(assembly);
						var typeObj = assemblyObj.GetType(type);
						var methodInfo = typeObj?.GetMethod(method);

						if (methodInfo != null)
						{
							// Call MonoCecilHelper to get source location (with instance-level caching)
							var sourceLocation = helper.TryGetCecilFileOpenInfo(methodInfo.DeclaringType, methodInfo);
							successfulCalls++;

							// Check if we got meaningful source location data
							if (sourceLocation != null && !string.IsNullOrEmpty(sourceLocation.FilePath) &&
							    sourceLocation.LineNumber > 0)
							{
								validSourceLocationCalls++;
							}
							else
							{
								emptySourceLocationCalls++;
							}
						}
						else
						{
							failedCalls++;
						}
					}
					catch (Exception)
					{
						failedCalls++;
					}
				}
			}

			totalStopwatch.Stop();
			
			var totalCalls = successfulCalls + failedCalls;
			var avgTimePerCall = totalCalls > 0 ? (double)totalStopwatch.ElapsedMilliseconds / totalCalls : 0;
			var successRate = totalCalls > 0 ? (double)successfulCalls / totalCalls * 100 : 0;
			var validSourceLocationRate = successfulCalls > 0 ? (double)validSourceLocationCalls / successfulCalls * 100 : 0;
			
			Debug.Log($"MonoCecilHelper Performance Results (WITH DISPOSABLE PATTERN):");
			Debug.Log($"- Total time: {totalStopwatch.ElapsedMilliseconds}ms");
			Debug.Log($"- Total calls: {totalCalls:N0}");
			Debug.Log($"- Successful calls: {successfulCalls:N0}");
			Debug.Log($"- Failed calls: {failedCalls:N0}");
			Debug.Log($"- Success rate: {successRate:F1}%");
			Debug.Log($"- Valid source locations: {validSourceLocationCalls:N0}");
			Debug.Log($"- Empty source locations: {emptySourceLocationCalls:N0}");
			Debug.Log($"- Source location success rate: {validSourceLocationRate:F1}%");
			Debug.Log($"- Average time per call: {avgTimePerCall:F3}ms (with instance-level caching)");
			Debug.Log($"- Calls per second: {(totalCalls / (totalStopwatch.ElapsedMilliseconds / 1000.0)):F0}");
			Debug.Log($"- MonoCecilHelper properly disposed to release file handles");
			
			// Performance assertions
			Assert.Greater(successfulCalls, 0, "At least some calls should succeed");
			Assert.Less(avgTimePerCall, 5.0, "Average time per call should be reasonable with instance-level caching");
			Assert.Greater(successRate, 50.0, "Success rate should be greater than 50%");
			Assert.Greater(validSourceLocationCalls, 0, "At least some calls should return valid source location data");
			Assert.Greater(validSourceLocationRate, 30.0, "At least 30% of successful calls should return meaningful source location data");
		}
		    
		/// <summary>
		/// Logs a summary of the test container for debugging purposes.
		/// </summary>
		/// <param name="container">The test container to summarize.</param>
		/// <param name="mode">The test mode name for logging.</param>
		private void LogTestSummary(TestAdaptorContainer container, string mode)
		{
			if (container?.TestAdaptors == null)
			{
				Debug.Log($"{mode} - No tests found");
				return;
			}
			
			var suites = 0;
			var testMethods = 0;
			
			foreach (var test in container.TestAdaptors)
			{
				if (string.IsNullOrEmpty(test.Method))
					suites++;
				else
					testMethods++;
			}
			
			Debug.Log($"{mode} Summary - Total: {container.TestAdaptors.Length}, Suites: {suites}, Test Methods: {testMethods}");
		}
	}
}