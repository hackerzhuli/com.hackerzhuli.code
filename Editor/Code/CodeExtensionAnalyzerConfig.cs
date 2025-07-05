/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

namespace Hackerzhuli.Code.Editor.Code
{
	/// <summary>
	///     Configuration for analyzer discovery in VS Code extensions.
	/// </summary>
	internal class CodeExtensionAnalyzerConfig
    {
	    /// <summary>
	    ///     Configuration for extensions that provide analyzers.
	    /// </summary>
	    public static readonly CodeExtensionAnalyzerConfig[] Configs =
        {
            new CodeExtensionAnalyzerConfig
            {
                ExtensionId = CodeExtensionManager.UnityCodeExtensionId,
                AnalyzersRelativePath = "assemblies",
                FilePattern = "*Analyzers.dll"
            },
            new CodeExtensionAnalyzerConfig
            {
                ExtensionId = CodeExtensionManager.UnityExtensionId,
                AnalyzersRelativePath = "Analyzers",
                FilePattern = "*Analyzers.dll"
            }
        };

	    /// <summary>
	    ///     The extension ID to check for installation.
	    /// </summary>
	    public string ExtensionId { get; set; }

	    /// <summary>
	    ///     The relative path within the extension where analyzers are located.
	    /// </summary>
	    public string AnalyzersRelativePath { get; set; }

	    /// <summary>
	    ///     The file pattern to search for analyzer DLL files.
	    /// </summary>
	    public string FilePattern { get; set; }
    }
}