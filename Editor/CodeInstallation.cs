/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Hackerzhuli.Code.Editor.ProjectGeneration;
using UnityEngine;
using IOPath = System.IO.Path;
using Debug = UnityEngine.Debug;
using System.Text.RegularExpressions;
#if UNITY_EDITOR_WIN
using PlatformAppDiscover = Hackerzhuli.Code.Editor.WindowsAppDiscover;
#elif UNITY_EDITOR_OSX
using PlatformAppDiscover = Hackerzhuli.Code.Editor.MacOSAppDiscover;
#else
using PlatformAppDiscover = Hackerzhuli.Code.Editor.LinuxAppDiscover;
#endif

namespace Hackerzhuli.Code.Editor
{
	/// <summary>
	/// Represents the manifest data structure for a Visual Studio Code installation.
	/// </summary>
	[Serializable]
	internal class CodeManifest
	{
		/// <summary>
		/// The name of the VS Code application.
		/// </summary>
		public string name;

		/// <summary>
		/// The version of the VS Code application.
		/// </summary>
		public string version;
	}

	/// <summary>
	/// Represents a Visual Studio Code fork installation.
	/// Provides functionality for discovering, interacting with, and configuring VS Code.
	/// </summary>
	internal class CodeInstallation : CodeEditorInstallation
	{
		/// <summary>
		/// The fork data for this installation.
		/// </summary>
		private CodeFork ForkData { get; set; }

		/// <summary>
		/// The file patcher instance for handling VS Code configuration files.
		/// </summary>
		private CodeFilePatcher FilePatcher { get; set; }

		/// <summary>
		/// The generator instance used for creating project files.
		/// </summary>
		private static readonly IGenerator _generator = GeneratorFactory.GetInstance(GeneratorStyle.SDK);

		/// <summary>
		/// Pre-created platform discoverers for each fork to avoid repeated instantiation.
		/// </summary>
		private static readonly Dictionary<CodeFork, IAppDiscover> _discoverers = InitializeDiscoverers();

		/// <summary>
		/// Initializes the platform discoverers for all supported forks.
		/// </summary>
		/// <returns>A dictionary mapping each fork to its corresponding discoverer.</returns>
		private static Dictionary<CodeFork, IAppDiscover> InitializeDiscoverers()
		{
			var discoverers = new Dictionary<CodeFork, IAppDiscover>();
			foreach (var fork in CodeFork.Forks)
			{
				discoverers[fork] = new PlatformAppDiscover(fork);
			}
			return discoverers;
		}

		/// <summary>
		/// Gets whether this installation supports code analyzers.
		/// </summary>
		/// <returns>Always returns true for VS Code installations.</returns>
		public override bool SupportsAnalyzers => true;

		/// <summary>
		/// Gets the latest C# language version supported by this VS Code installation.
		/// </summary>
		/// <returns>The fork-specific latest supported C# language version, default to 11 if not defined in <see cref="CodeFork"/></returns>
		public override Version LatestLanguageVersionSupported => ForkData?.LatestLanguageVersion ?? new Version(11, 0);

		/// <summary>
		/// Gets the path to the extensions directory for this VS Code installation.
		/// </summary>
		/// <returns>The path to the extensions directory or null if not found.</returns>
		private string GetExtensionsDirectory()
		{
			var extensionsDirName = ForkData.UserDataDirName;

			var extensionsPath = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				extensionsDirName, "extensions");
			
			return Directory.Exists(extensionsPath) ? extensionsPath : null;
		}

		/// <summary>
		/// Gets the array of analyzer assemblies available in the Visual Studio Tools for Unity extension for the VS Code installation.
		/// </summary>
		/// <returns>Array of analyzer assembly paths or an empty array if none found.</returns>
		public override string[] GetAnalyzers()
		{
			// Update extension states to ensure we have the latest information
			FilePatcher?.UpdateExtensionStates();

			if (FilePatcher?.UnityToolsExtensionState?.IsInstalled != true)
				return Array.Empty<string>();

			var extensionPath = IOPath.Combine(FilePatcher.ExtensionsDirectory, FilePatcher.UnityToolsExtensionState.RelativePath);
			return GetAnalyzers(extensionPath);
		}

		/// <summary>
		/// Gets the project generator for this VS Code installation.
		/// </summary>
		public override IGenerator ProjectGenerator => _generator;

		/// <summary>
		/// Identifies the VS Code fork based on the executable path.
		/// </summary>
		/// <param name="exePath">The path to the VS Code executable.</param>
		/// <returns>The VSCodeForkData if a supported fork is identified; otherwise, null.</returns>
		private static CodeFork GetForkDataByPath(string exePath)
		{
			if (string.IsNullOrEmpty(exePath))
				return null;

			// Use the platform discoverers to check if the path is a valid candidate for each fork
			foreach (var kvp in _discoverers)
			{
				var fork = kvp.Key;
				var discoverer = kvp.Value;
				
				if (discoverer.IsCandidate(exePath))
				{
					return fork;
				}
			}

			return null;
		}

		public static bool TryDiscoverInstallation(string exePath, out ICodeEditorInstallation installation)
		{
			//Debug.Log($"trying to get the vs code fork installation at {exePath}");
			installation = null;

			if (string.IsNullOrEmpty(exePath))
				return false;

			var forkData = GetForkDataByPath(exePath);

			if (forkData == null)
				return false;

			Version version = null;

			var isPrerelease = forkData.IsPrerelease;
			string prereleaseKeyword = null;

			try
			{
				var manifestBase = PlatformPathUtility.GetRealPath(exePath);

#if UNITY_EDITOR_WIN
				// on Windows, editorPath is a file, resources as subdirectory
				manifestBase = IOPath.GetDirectoryName(manifestBase);
#elif UNITY_EDITOR_OSX
				// on Mac, editorPath is a directory
				manifestBase = IOPath.Combine(manifestBase, "Contents");
#else
				// on Linux, editorPath is a file, in a bin sub-directory
				var parent = Directory.GetParent(manifestBase);
				// but we can link to [vscode]/code or [vscode]/bin/code
				manifestBase = parent?.Name == "bin" ? parent.Parent?.FullName : parent?.FullName;
#endif

				if (manifestBase == null)
					return false;

				var manifestFullPath = IOPath.Combine(manifestBase, "resources", "app", "package.json");
				if (File.Exists(manifestFullPath))
				{
					var manifest = JsonUtility.FromJson<CodeManifest>(File.ReadAllText(manifestFullPath));
					Version.TryParse(manifest.version.Split('-').First(), out version);

					// If fork is not marked as prerelease, check manifest version for prerelease indicators
					if (!isPrerelease && !string.IsNullOrEmpty(manifest.version))
					{
						var versionLower = manifest.version.ToLowerInvariant();
						string[] prereleaseKeywords =
							{ "alpha", "beta", "rc", "preview", "dev", "nightly", "canary", "pre" };

						foreach (var keyword in prereleaseKeywords)
						{
							if (versionLower.Contains(keyword))
							{
								isPrerelease = true;
								prereleaseKeyword = keyword;
								break;
							}
						}
					}
				}
			}
			catch (Exception)
			{
				// do not fail if we are not able to retrieve the exact version number
			}

			var name = forkData.Name;
			if (isPrerelease != forkData.IsPrerelease && !string.IsNullOrEmpty(prereleaseKeyword))
			{
				name += $" ({prereleaseKeyword})";
			}

			if (version != null)
			{
				name += $" [{version.ToString(3)}]";
			}

			var installation2 = new CodeInstallation()
			{
				ForkData = forkData,
				IsPrerelease = isPrerelease,
				Name = name,
				Path = exePath,
				Version = version ?? new Version()
			};
			installation = installation2;

			// Initialize file patcher
			var extensionsDirectory = installation2.GetExtensionsDirectory();
			if (extensionsDirectory != null)
			{
				installation2.FilePatcher = new CodeFilePatcher(extensionsDirectory);
			}

			//Debug.Log($"discovered vs code installation {name} at {installation.Path}");

			return true;
		}

		/// <summary>
		/// Gets all Visual Studio Code installations detected on the system.
		/// </summary>
		/// <returns>An enumerable collection of VS Code installations.</returns>
		public static IEnumerable<ICodeEditorInstallation> GetInstallations()
		{
			// Discover installations for each fork using pre-created discoverers
			foreach (var kvp in _discoverers)
			{
				var fork = kvp.Key;
				var discoverer = kvp.Value;
				var candidates = discoverer.GetCandidatePaths();

				foreach (var candidate in candidates)
				{
					// Discoverers already return validated paths, so we can directly attempt discovery
					if (TryDiscoverInstallation(candidate, out var installation))
					{
						yield return installation;
					}
				}
			}
		}

		/// <summary>
		/// Creates additional configuration files for VS Code in the project directory.
		/// </summary>
		/// <param name="projectDirectory">The Unity project directory where the files should be created.</param>
		public override void CreateExtraFiles(string projectDirectory)
		{
			FilePatcher?.CreateOrPatchFiles(projectDirectory);
		}

		/// <summary>
		/// Opens a file in Visual Studio Code at the specified line and column.
		/// </summary>
		/// <param name="path">The path to the file to open, or null to open the solution/workspace.</param>
		/// <param name="line">The line number to navigate to.</param>
		/// <param name="column">The column number to navigate to.</param>
		/// <param name="solution">The path to the solution file.</param>
		/// <returns>True if the operation was successful; otherwise, false.</returns>
		public override bool Open(string path, int line, int column, string solution)
		{
			var exePath = Path;

			line = Math.Max(1, line);
			column = Math.Max(0, column);

			var directory = IOPath.GetDirectoryName(solution);
			var workspace = TryFindWorkspace(directory);

			var target = workspace ?? directory;

			ProcessRunner.Start(string.IsNullOrEmpty(path)
				? ProcessStartInfoFor(exePath, $"\"{target}\"")
				: ProcessStartInfoFor(exePath, $"\"{target}\" -g \"{path}\":{line}:{column}"));

			return true;
		}

		/// <summary>
		/// Attempts to find a VS Code workspace file in the specified directory.
		/// </summary>
		/// <param name="directory">The directory to search in.</param>
		/// <returns>The path to the workspace file if found; otherwise, null.</returns>
		private static string TryFindWorkspace(string directory)
		{
			var files = Directory.GetFiles(directory, "*.code-workspace", SearchOption.TopDirectoryOnly);
			if (files.Length == 0 || files.Length > 1)
				return null;

			return files[0];
		}

		/// <summary>
		/// Creates a ProcessStartInfo object for launching VS Code with the specified arguments.
		/// </summary>
		/// <param name="exePath">The path to the VS Code executable.</param>
		/// <param name="arguments">The command-line arguments to pass to VS Code.</param>
		/// <returns>A configured ProcessStartInfo object.</returns>
		private static ProcessStartInfo ProcessStartInfoFor(string exePath, string arguments)
		{
#if UNITY_EDITOR_OSX
			// wrap with built-in OSX open feature
			arguments = $"-n \"{exePath}\" --args {arguments}";
			var application = "open";
			return ProcessRunner.ProcessStartInfoFor(application, arguments, redirect:false, shell: true);
#else
			return ProcessRunner.ProcessStartInfoFor(exePath, arguments, redirect: false);
#endif
		}

		/// <summary>
		/// Initializes the Visual Studio Code installation.
		/// </summary>
		public static void Initialize()
		{
		}
	}
}
