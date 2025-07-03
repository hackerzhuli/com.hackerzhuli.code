/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.IO;
using Hackerzhuli.Code.Editor.ProjectGeneration;
using IOPath = System.IO.Path;

namespace Hackerzhuli.Code.Editor.Code
{
	internal interface ICodeEditorInstallation
	{
		string Path { get; }
		bool SupportsAnalyzers { get; }
		Version LatestLanguageVersionSupported { get; }
		string[] GetAnalyzers();
        Unity.CodeEditor.CodeEditor.Installation ToCodeEditorInstallation();
		bool Open(string path, int line, int column, string solutionPath);
		IGenerator ProjectGenerator { get; }
		void CreateExtraFiles(string projectDirectory);
	}

	internal abstract class CodeEditorInstallation : ICodeEditorInstallation
	{
		public string Name { get; set; }
		public string Path { get; set; }
		public Version Version { get; set; }
		public bool IsPrerelease { get; set; }

		public abstract bool SupportsAnalyzers { get; }
		public abstract Version LatestLanguageVersionSupported { get; }
		public abstract string[] GetAnalyzers();
		public abstract IGenerator ProjectGenerator { get; }
		public abstract void CreateExtraFiles(string projectDirectory);
		public abstract bool Open(string path, int line, int column, string solutionPath);

		protected Version GetLatestLanguageVersionSupported(VersionPair[] versions)
		{
			if (versions != null)
			{
				foreach (var entry in versions)
				{
					if (Version >= entry.IdeVersion)
						return entry.LanguageVersion;
				}
			}

			// default to 7.0
			return new Version(7, 0);
		}

		protected static string[] GetAnalyzers(string path)
		{
			var analyzersDirectory = IOPath.GetFullPath(path);

			if (Directory.Exists(analyzersDirectory))
				return Directory.GetFiles(analyzersDirectory, "*Analyzers.dll", SearchOption.AllDirectories);

			return Array.Empty<string>();
		}

		public Unity.CodeEditor.CodeEditor.Installation ToCodeEditorInstallation()
		{
			return new Unity.CodeEditor.CodeEditor.Installation() { Name = Name, Path = Path };
		}
	}
}
