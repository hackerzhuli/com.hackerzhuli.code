using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hackerzhuli.Code.Editor.Testing
{
    /// <summary>
    ///     Sample test class to validate GetNodeType functionality
    /// </summary>
    //[TestFixture]
    public class SampleTestClassForValidation
    {
        //[Test]
        public void SampleTestMethod1()
        {
            Assert.Pass("Sample test method 1");
        }

        //[Test]
        public void SampleTestMethod2()
        {
            Assert.Pass("Sample test method 2");
        }

        //[TestCase("param1")]
        //[TestCase("param2")]
        public void SampleParameterizedTest(string parameter)
        {
            Assert.IsNotNull(parameter);
        }

        //[Test]
        public void SampleAsyncTest()
        {
            Assert.Pass("Sample async test");
        }

        //[Test]
        public void SampleCoroutineTest()
        {
            Assert.Pass("Sample coroutine test");
        }
    }

    //[TestFixture]
    public class TestAdaptorUtilsTests
    {
        [SetUp]
        public void SetUp()
        {
            _testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            _retrievedTests = new List<ITestAdaptor>();
            _testRetrievalComplete = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (_testRunnerApi != null) Object.DestroyImmediate(_testRunnerApi);
        }

        private TestRunnerApi _testRunnerApi;
        private List<ITestAdaptor> _retrievedTests;
        private bool _testRetrievalComplete;

        /// <summary>
        ///     Retrieves tests from the Unity Test Runner API using async polling
        /// </summary>
        /// <param name="testMode">The test mode to retrieve tests for</param>
        /// <returns>List of test adaptors</returns>
        private async Task<List<ITestAdaptor>> RetrieveTestsFromApiAsync(TestMode testMode)
        {
            _retrievedTests.Clear();
            _testRetrievalComplete = false;

            _testRunnerApi.RetrieveTestList(testMode, testRoot =>
            {
                CollectAllTests(testRoot, _retrievedTests);
                _testRetrievalComplete = true;
            });

            // Poll for completion with timeout
            var timeoutTime = DateTime.Now.AddSeconds(10);
            while (!_testRetrievalComplete &&
                   DateTime.Now < timeoutTime) await Task.Delay(50); // Wait 50ms between checks

            if (!_testRetrievalComplete) Assert.Fail("Test retrieval timed out");

            return new List<ITestAdaptor>(_retrievedTests);
        }

        /// <summary>
        ///     Recursively collects all tests from the test tree
        /// </summary>
        /// <param name="testAdaptor">Root test adaptor</param>
        /// <param name="allTests">List to collect tests into</param>
        private void CollectAllTests(ITestAdaptor testAdaptor, List<ITestAdaptor> allTests)
        {
            if (testAdaptor == null) return;

            allTests.Add(testAdaptor);

            if (testAdaptor.Children != null)
                foreach (var child in testAdaptor.Children)
                    CollectAllTests(child, allTests);
        }

        //[Test]
        public async Task GetNodeType_ConcreteTestNodes_ValidateSpecificExamples()
        {
            // Arrange - Get all tests from Unity Test Runner
            var allTests = await RetrieveTestsFromApiAsync(TestMode.EditMode);
            Assert.IsNotEmpty(allTests, "Should have retrieved some tests to validate");

            // Create a dictionary to find tests by their full names
            // ignore root test, because root test is above assembly level, full name is can conflict
            var testsByFullName = allTests.Where(t => t.Parent != null).ToDictionary(t => t.FullName, t => t);

            // Define expected node types for specific test nodes from our sample test class
            var expectedNodeTypes = new Dictionary<string, TestNodeType>
            {
                // Namespace nodes
                { "Hackerzhuli.Code.Editor.Testing", TestNodeType.Namespace },
                { "Hackerzhuli.Code.Editor", TestNodeType.Namespace },
                { "Hackerzhuli.Code", TestNodeType.Namespace },

                // Class nodes from our sample test classes
                { "Hackerzhuli.Code.Editor.Testing.SampleTestClassForValidation", TestNodeType.Class },

                // Method nodes from our sample test class
                {
                    "Hackerzhuli.Code.Editor.Testing.SampleTestClassForValidation.SampleTestMethod1",
                    TestNodeType.Method
                },
                {
                    "Hackerzhuli.Code.Editor.Testing.SampleTestClassForValidation.SampleTestMethod2",
                    TestNodeType.Method
                },
                { "Hackerzhuli.Code.Editor.Testing.SampleTestClassForValidation.SampleAsyncTest", TestNodeType.Method },
                {
                    "Hackerzhuli.Code.Editor.Testing.SampleTestClassForValidation.SampleCoroutineTest",
                    TestNodeType.Method
                },
                {
                    "Hackerzhuli.Code.Editor.Testing.SampleTestClassForValidation.SampleParameterizedTest",
                    TestNodeType.Method
                },


                // TestCase nodes (parameterized tests)
                {
                    "Hackerzhuli.Code.Editor.Testing.SampleTestClassForValidation.SampleParameterizedTest(\"param1\")",
                    TestNodeType.TestCase
                },
                {
                    "Hackerzhuli.Code.Editor.Testing.SampleTestClassForValidation.SampleParameterizedTest(\"param2\")",
                    TestNodeType.TestCase
                }
            };

            // Check for assembly nodes by testing if FullName ends with .dll
            var assemblyNodes = allTests.Where(t => t.FullName.EndsWith(".dll")).ToList();
            foreach (var assemblyNode in assemblyNodes)
                expectedNodeTypes[assemblyNode.FullName] = TestNodeType.Assembly;

            // Act & Assert - Check each expected node type
            var foundNodes = 0;
            var validatedNodes = new List<string>();

            foreach (var expectedPair in expectedNodeTypes)
            {
                var fullName = expectedPair.Key;
                var expectedNodeType = expectedPair.Value;

                if (testsByFullName.TryGetValue(fullName, out var testAdaptor))
                {
                    foundNodes++;
                    var actualNodeType = testAdaptor.GetNodeType();

                    Assert.AreEqual(expectedNodeType, actualNodeType,
                        $"Test node '{fullName}' should be {expectedNodeType} but was {actualNodeType}. " +
                        $"IsTestAssembly: {testAdaptor.IsTestAssembly}, " +
                        $"HasTypeInfo: {testAdaptor.TypeInfo != null}, " +
                        $"HasMethod: {testAdaptor.Method != null}, " +
                        $"HasArguments: {testAdaptor.Arguments?.Length > 0}, " +
                        $"HasParent: {testAdaptor.Parent != null}");

                    validatedNodes.Add(fullName);
                }
            }

            // Log results
            Debug.Log($"Validated {foundNodes} out of {expectedNodeTypes.Count} expected test nodes:");
            foreach (var nodeName in validatedNodes) Debug.Log($"  ✓ {nodeName}: {expectedNodeTypes[nodeName]}");

            // We should find at least some of the expected nodes
            Assert.IsTrue(foundNodes >= 3,
                $"Expected to find at least 3 concrete test nodes, but only found {foundNodes}. " +
                $"Available test nodes: {string.Join(", ", allTests.Take(10).Select(t => t.FullName))}");
        }
    }
}