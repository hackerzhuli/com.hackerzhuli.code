using System;
using System.IO;
using System.Reflection;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine.TestTools;

namespace Hackerzhuli.Code.Editor.Testing
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
		/// The name of the test node.
		/// </summary>a
		public string Name;
		
		/// <summary>
		/// The full name of the test including namespace and class, for assembly, the path of the assembly
		/// </summary>
		public string FullName;

		/// <summary>
		/// The full name of the type containing the test method.
		/// </summary>
		public string Type;
		
		/// <summary>
		/// The name of the test method, if it is not a method, empty
		/// </summary>
		public string Method;

		/// <summary>
		/// The name of the assembly containing the test(eg. MyNamespace.MyAssembly), if it is an assembly or not in an assembly, empty string
		/// </summary>
		public string Assembly;

		/// <summary>
		/// The mode of the test("PlayMode" or "EditMode")
		/// </summary>
		public string Mode;
		
		/// <summary>
		/// Index of parent in TestAdaptors array, -1 for root.
		/// </summary>
		public int Parent;

		/// <summary>
		/// Source location of the test in format "Assets/Path/File.cs:LineNumber".
		/// Only populated for methods and types, null for namespaces or assemblies or other things
		/// </summary>
		public string SourceLocation;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestAdaptor"/> class from Unity's <see cref="ITestAdaptor"/>.
        /// </summary>
        /// <param name="testAdaptor">The Unity test adaptor to convert from.</param>
        /// <param name="parent">Index of parent in TestAdaptors array, -1 for root.</param>
        /// <param name="cecilHelper">Shared MonoCecilHelper instance for source location retrieval.</param>
        public TestAdaptor(ITestAdaptor testAdaptor, int parent, MonoCecilHelper cecilHelper = null)
		{
			Name = testAdaptor.Name;
			FullName = testAdaptor.FullName;
			Type = testAdaptor.TypeInfo?.FullName;
			Method = testAdaptor.Method?.Name;
			Assembly = testAdaptor.GetAssemblyName();
			Mode = testAdaptor.GetMode().ToString();
			Parent = parent;

			// Populate source location for leaf tests (actual test methods) or test types
			if (testAdaptor.TypeInfo != null)
			{
				if (!testAdaptor.IsSuite && testAdaptor.Method != null)
				{
					// For test methods, get method source location
					SourceLocation = GetMethodSourceLocation(testAdaptor, cecilHelper);
				}
				else if (testAdaptor.IsSuite && testAdaptor.Method == null && !string.IsNullOrEmpty(testAdaptor.TypeInfo.FullName))
				{
					// For test types (suites without methods), get type source location
					SourceLocation = GetTypeSourceLocation(testAdaptor, cecilHelper);
				}
			}
		}

        /// <summary>
        /// Gets the source location for a test method using MonoCecil debug information.
        /// </summary>
        /// <param name="testAdaptor">The test adaptor containing method information.</param>
        /// <param name="cecilHelper">Shared MonoCecilHelper instance for source location retrieval.</param>
        /// <returns>Source location in format "Assets/Path/File.cs:LineNumber" or null if not found.</returns>
        private static string GetMethodSourceLocation(ITestAdaptor testAdaptor, MonoCecilHelper cecilHelper)
		{
			// If no cecil helper provided, skip source location detection
			if (cecilHelper == null) return null;

			try
			{
				// Get the actual System.Type from the type info
				var type = testAdaptor.TypeInfo.Type;
				if (type == null) return null;

				// Get the MethodInfo from reflection
				var methodInfo = type.GetMethod(testAdaptor.Method.Name, 
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
				if (methodInfo == null) return null;

				// Use shared MonoCecilHelper to get file location
				var fileOpenInfo = cecilHelper.TryGetCecilFileOpenInfo(type, methodInfo);
				
				if (fileOpenInfo is { FilePath: not null, LineNumber: > 0 })
				{
					// Convert absolute path to relative path from project root
					var relativePath = GetRelativePathFromProject(fileOpenInfo.FilePath);
					if (relativePath != null)
					{
						return $"{relativePath}:{fileOpenInfo.LineNumber}";
					}
				}
			}
			catch
			{
				// Silently ignore errors in source location detection
			}

			return null;
		}

		/// <summary>
		/// Gets the source location for a test type using MonoCecil debug information.
		/// </summary>
		/// <param name="testAdaptor">The test adaptor containing type information.</param>
		/// <param name="cecilHelper">Shared MonoCecilHelper instance for source location retrieval.</param>
		/// <returns>Source location in format "Assets/Path/File.cs:LineNumber" or null if not found.</returns>
		private static string GetTypeSourceLocation(ITestAdaptor testAdaptor, MonoCecilHelper cecilHelper)
		{
			// If no cecil helper provided, skip source location detection
			if (cecilHelper == null) return null;

			try
			{
				// Get the actual System.Type from the type info
				var type = testAdaptor.TypeInfo.Type;
				if (type == null) return null;

				// Use shared MonoCecilHelper to get type source location
				var fileOpenInfo = cecilHelper.TryGetCecilTypeSourceLocation(type);
				
				if (fileOpenInfo is { FilePath: not null, LineNumber: > 0 })
				{
					// Convert absolute path to relative path from project root
					var relativePath = GetRelativePathFromProject(fileOpenInfo.FilePath);
					if (relativePath != null)
					{
						return $"{relativePath}:{fileOpenInfo.LineNumber}";
					}
				}
			}
			catch
			{
				// Silently ignore errors in source location detection
			}

			return null;
		}

		/// <summary>
		/// Converts an absolute file path to a relative path from the Unity project root.
		/// </summary>
		/// <param name="absolutePath">The absolute file path.</param>
		/// <returns>Relative path starting with "Assets/" or null if not within project.</returns>
		private static string GetRelativePathFromProject(string absolutePath)
		{
			if (string.IsNullOrEmpty(absolutePath)) return null;

			try
			{
				// Get the Unity project root (parent of Assets folder)
				var projectRoot = UnityEngine.Application.dataPath; // Points to Assets folder
				projectRoot = Directory.GetParent(projectRoot)?.FullName; // Go up to project root
				
				if (projectRoot == null) return null;

				// Normalize paths for comparison
				var normalizedProjectRoot = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				var normalizedAbsolutePath = Path.GetFullPath(absolutePath);

				// Check if the file is within the project
				if (normalizedAbsolutePath.StartsWith(normalizedProjectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				{
					// Get relative path and convert to forward slashes
					var relativePath = Path.GetRelativePath(normalizedProjectRoot, normalizedAbsolutePath);
					return relativePath.Replace(Path.DirectorySeparatorChar, '/');
				}
			}
			catch
			{
				// Silently ignore path conversion errors
			}

			return null;
		}
	}
}
