using System;

using UnityEditor.TestTools.TestRunner.Api;

namespace Microsoft.Unity.VisualStudio.Editor.Testing
{
	/// <summary>
	/// Container for serializing an array of <see cref="TestAdaptor"/> objects.
	/// </summary>
	[Serializable]
	internal class TestAdaptorContainer
	{
		/// <summary>
		/// Array of test adaptors for serialization.
		/// </summary>
		public TestAdaptor[] TestAdaptors;
	}

	/// <summary>
	/// Serializable adaptor for Unity's <see cref="ITestAdaptor"/> interface.
	/// Represents a test node in the test tree with metadata and hierarchy information.
	/// </summary>
	[Serializable]
	internal class TestAdaptor
	{
		/// <summary>
		/// The unique identifier of the test.
		/// </summary>
		public string Id;
		
		/// <summary>
		/// The name of the test node.
		/// </summary>
		public string Name;
		
		/// <summary>
		/// The full name of the test including namespace and class.
		/// </summary>
		public string FullName;

		/// <summary>
		/// The full name of the type containing the test method.
		/// </summary>
		public string Type;
		
		/// <summary>
		/// The name of the test method.
		/// </summary>
		public string Method;
		
		/// <summary>
		/// The location of the assembly containing the test.
		/// </summary>
		public string Assembly;

		/// <summary>
		/// Index of parent in TestAdaptors array, -1 for root.
		/// </summary>
		public int Parent;

		/// <summary>
		/// Initializes a new instance of the <see cref="TestAdaptor"/> class from Unity's <see cref="ITestAdaptor"/>.
		/// </summary>
		/// <param name="testAdaptor">The Unity test adaptor to convert from.</param>
		/// <param name="parent">Index of parent in TestAdaptors array, -1 for root.</param>
		public TestAdaptor(ITestAdaptor testAdaptor, int parent)
		{
			Id = testAdaptor.Id;
			Name = testAdaptor.Name;
			FullName = testAdaptor.FullName;

			Type = testAdaptor.TypeInfo?.FullName;
			Method = testAdaptor.Method?.Name;
			Assembly = testAdaptor.TypeInfo?.Assembly?.Location;

			Parent = parent;
		}
	}
}
