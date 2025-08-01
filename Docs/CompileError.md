

``` csharp
namespace UnityEditor.Compilation;

/// <summary>
///   <para>Compiler message type.</para>
/// </summary>
public enum CompilerMessageType
{
	/// <summary>
	///   <para>Error message.</para>
	/// </summary>
	Error,
	/// <summary>
	///   <para>Warning message.</para>
	/// </summary>
	Warning,
	/// <summary>
	///   <para>Info message.</para>
	/// </summary>
	Info
}

/// <summary>
///   <para>Compiler Message.</para>
/// </summary>
public struct CompilerMessage
{
	/// <summary>
	///   <para>Compiler message.</para>
	/// </summary>
	public string message;

	/// <summary>
	///   <para>File for the message.</para>
	/// </summary>
	public string file;

	/// <summary>
	///   <para>File line for the message.</para>
	/// </summary>
	public int line;

	/// <summary>
	///   <para>Line column for the message.</para>
	/// </summary>
	public int column;

	/// <summary>
	///   <para>Message type.</para>
	/// </summary>
	public CompilerMessageType type;
}
```