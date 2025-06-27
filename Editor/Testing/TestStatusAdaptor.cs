using System;

namespace Microsoft.Unity.VisualStudio.Editor.Testing
{
	/// <summary>
	/// Represents the status of a test execution result.
	/// Corresponds to Unity's TestStatus enumeration.
	/// </summary>
	[Serializable]
	internal enum TestStatusAdaptor
	{
		/// <summary>
		/// The test passed successfully.
		/// </summary>
		Passed,
		
		/// <summary>
		/// The test was skipped and not executed.
		/// </summary>
		Skipped,
		
		/// <summary>
		/// The test result was inconclusive - neither passed nor failed.
		/// </summary>
		Inconclusive,
		
		/// <summary>
		/// The test failed during execution.
		/// </summary>
		Failed,
	}
}
