/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using SimpleJSON;
using IOPath = System.IO.Path;
using Debug = UnityEngine.Debug;

namespace Microsoft.Unity.VisualStudio.Editor
{
	/// <summary>
	/// Represents a Visual Studio Code installation on the system.
	/// Provides functionality for discovering, interacting with, and configuring VS Code.
	/// </summary>
	 internal class VisualStudioCodeInstallation : VisualStudioInstallation
	{
		/// <summary>
		/// The generator instance used for creating project files.
		/// </summary>
		private static readonly IGenerator _generator = GeneratorFactory.GetInstance(GeneratorStyle.SDK);

		/// <summary>
		/// Gets whether this installation supports code analyzers.
		/// </summary>
		/// <returns>Always returns true for VS Code installations.</returns>
		public override bool SupportsAnalyzers
		{
			get
			{
				return true;
			}
		}

		/// <summary>
		/// Gets the latest C# language version supported by this VS Code installation.
		/// </summary>
		/// <returns>Version object representing C# 13.0.</returns>
		public override Version LatestLanguageVersionSupported
		{
			get
			{
				return new Version(13, 0);
			}
		}

		/// <summary>
		/// Gets the path to the Microsoft Unity extension for VS Code.
		/// </summary>
		/// <returns>The path to the extension directory or null if not found.</returns>
		private string GetExtensionPath()
		{
			var vscode = IsPrerelease ? ".vscode-insiders" : ".vscode";
			var extensionsPath = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), vscode, "extensions");
			if (!Directory.Exists(extensionsPath))
				return null;

			return Directory
				.EnumerateDirectories(extensionsPath, $"{MicrosoftUnityExtensionId}*") // publisherid.extensionid
				.OrderByDescending(n => n)
				.FirstOrDefault();
		}

		/// <summary>
		/// Gets the array of analyzer assemblies available in this VS Code installation.
		/// </summary>
		/// <returns>Array of analyzer assembly paths or an empty array if none found.</returns>
		public override string[] GetAnalyzers()
		{
			var vstuPath = GetExtensionPath();
			if (string.IsNullOrEmpty(vstuPath))
				return Array.Empty<string>();

			return GetAnalyzers(vstuPath); }

		/// <summary>
		/// Gets the project generator for this VS Code installation.
		/// </summary>
		/// <returns>The generator instance for creating project files.</returns>
		public override IGenerator ProjectGenerator
		{
			get
			{
				return _generator;
			}
		}

		/// <summary>
		/// Determines if the specified path is a candidate for VS Code discovery.
		/// </summary>
		/// <param name="path">The path to check.</param>
		/// <returns>True if the path is a potential VS Code installation; otherwise, false.</returns>
		private static bool IsCandidateForDiscovery(string path)
		{
#if UNITY_EDITOR_OSX
			return Directory.Exists(path) && Regex.IsMatch(path, ".*Code.*.app$", RegexOptions.IgnoreCase);
#elif UNITY_EDITOR_WIN
			return File.Exists(path) && Regex.IsMatch(path, ".*Code.*.exe$", RegexOptions.IgnoreCase);
#else
			return File.Exists(path) && path.EndsWith("code", StringComparison.OrdinalIgnoreCase);
#endif
		}

		/// <summary>
		/// Represents the manifest data structure for a Visual Studio Code installation.
		/// </summary>
		[Serializable]
		internal class VisualStudioCodeManifest
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
		/// Attempts to create a Visual Studio Code installation from the specified path
		/// </summary>
		/// <param name="editorPath">The path of the Visual Studio Code executable.</param>
		/// <param name="installation">When this method returns, contains the discovered installation if successful; otherwise, null.</param>
		/// <returns>True if a VS Code installation was found at the specified path; otherwise, false.</returns>
		public static bool TryDiscoverInstallation(string editorPath, out IVisualStudioInstallation installation)
		{
			//Debug.Log($"trying to discover vs code installation at {editorPath}");
			installation = null;

			if (string.IsNullOrEmpty(editorPath))
				return false;

			if (!IsCandidateForDiscovery(editorPath))
				return false;

			Version version = null;
			var isPrerelease = false;

			try
			{
				var manifestBase = GetRealPath(editorPath);

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
					var manifest = JsonUtility.FromJson<VisualStudioCodeManifest>(File.ReadAllText(manifestFullPath));
					Version.TryParse(manifest.version.Split('-').First(), out version);
					isPrerelease = manifest.version.ToLower().Contains("insider");
				}
			}
			catch (Exception)
			{
				// do not fail if we are not able to retrieve the exact version number
			}

			isPrerelease = isPrerelease || editorPath.ToLower().Contains("insider");
			installation = new VisualStudioCodeInstallation()
			{
				IsPrerelease = isPrerelease,
				Name = "Visual Studio Code" + (isPrerelease ? " - Insider" : string.Empty) + (version != null ? $" [{version.ToString(3)}]" : string.Empty),
				Path = editorPath,
				Version = version ?? new Version()
			};

			return true;
		}

		/// <summary>
		/// Gets all Visual Studio Code installations detected on the system.
		/// </summary>
		/// <returns>An enumerable collection of VS Code installations.</returns>
		public static IEnumerable<IVisualStudioInstallation> GetVisualStudioInstallations()
		{
			var candidates = new List<string>();

#if UNITY_EDITOR_WIN
			var localAppPath = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
			var programFiles = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

			foreach (var basePath in new[] {localAppPath, programFiles})
			{
				candidates.Add(IOPath.Combine(basePath, "Microsoft VS Code", "Code.exe"));
				candidates.Add(IOPath.Combine(basePath, "Microsoft VS Code Insiders", "Code - Insiders.exe"));
			}
#elif UNITY_EDITOR_OSX
			var appPath = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
			candidates.AddRange(Directory.EnumerateDirectories(appPath, "Visual Studio Code*.app"));
#elif UNITY_EDITOR_LINUX
			// Well known locations
			candidates.Add("/usr/bin/code");
			candidates.Add("/bin/code");
			candidates.Add("/usr/local/bin/code");

			// Preference ordered base directories relative to which desktop files should be searched
			candidates.AddRange(GetXdgCandidates());
#endif

			foreach (var candidate in candidates.Distinct())
			{
				if (TryDiscoverInstallation(candidate, out var installation))
					yield return installation;
			}
		}

#if UNITY_EDITOR_LINUX
		/// <summary>
		/// Regular expression for extracting the executable path from Linux desktop files.
		/// </summary>
		private static readonly Regex DesktopFileExecEntry = new Regex(@"Exec=(\S+)", RegexOptions.Singleline | RegexOptions.Compiled);

		/// <summary>
		/// Gets candidate VS Code paths from XDG data directories on Linux.
		/// </summary>
		/// <returns>An enumerable collection of potential VS Code executable paths.</returns>
		private static IEnumerable<string> GetXdgCandidates()
		{
			var envdirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
			if (string.IsNullOrEmpty(envdirs))
				yield break;

			var dirs = envdirs.Split(':');
			foreach(var dir in dirs)
			{
				Match match = null;

				try
				{
					var desktopFile = IOPath.Combine(dir, "applications/code.desktop");
					if (!File.Exists(desktopFile))
						continue;
				
					var content = File.ReadAllText(desktopFile);
					match = DesktopFileExecEntry.Match(content);
				}
				catch
				{
					// do not fail if we cannot read desktop file
				}

				if (match == null || !match.Success)
					continue;

				yield return match.Groups[1].Value;
				break;
			}
		}

		/// <summary>
		/// Imports the readlink function from libc for resolving symbolic links on Linux.
		/// </summary>
		/// <param name="path">The path to the symbolic link.</param>
		/// <param name="buffer">The buffer to store the target path.</param>
		/// <param name="buflen">The length of the buffer.</param>
		/// <returns>The number of bytes placed in the buffer or -1 if an error occurred.</returns>
		[System.Runtime.InteropServices.DllImport ("libc")]
		private static extern int readlink(string path, byte[] buffer, int buflen);

		/// <summary>
		/// Gets the real path by resolving symbolic links on Linux.
		/// </summary>
		/// <param name="path">The path that might be a symbolic link.</param>
		/// <returns>The resolved path if it's a symbolic link; otherwise, the original path.</returns>
		internal static string GetRealPath(string path)
		{
			byte[] buf = new byte[512];
			int ret = readlink(path, buf, buf.Length);
			if (ret == -1) return path;
			char[] cbuf = new char[512];
			int chars = System.Text.Encoding.Default.GetChars(buf, 0, ret, cbuf, 0);
			return new String(cbuf, 0, chars);
		}
#else
		/// <summary>
		/// Gets the real path for non-Linux platforms (no-op implementation).
		/// </summary>
		/// <param name="path">The input path.</param>
		/// <returns>The same path without modification.</returns>
		internal static string GetRealPath(string path)
		{
			return path;
		}
#endif

		/// <summary>
		/// Creates additional configuration files for VS Code in the project directory.
		/// </summary>
		/// <param name="projectDirectory">The Unity project directory where the files should be created.</param>
		public override void CreateExtraFiles(string projectDirectory)
		{
			try
			{
				var vscodeDirectory = IOPath.Combine(projectDirectory.NormalizePathSeparators(), ".vscode");
				Directory.CreateDirectory(vscodeDirectory);

				var enablePatch = !File.Exists(IOPath.Combine(vscodeDirectory, ".vstupatchdisable"));

				CreateRecommendedExtensionsFile(vscodeDirectory, enablePatch);
				CreateSettingsFile(vscodeDirectory, enablePatch);
				CreateLaunchFile(vscodeDirectory, enablePatch);
			}
			catch (IOException)
			{
			}			
		}

		/// <summary>
		/// The default content for the launch.json file.
		/// </summary>
		private const string DefaultLaunchFileContent = @"{
    ""version"": ""0.2.0"",
    ""configurations"": [
        {
            ""name"": ""Attach to Unity"",
            ""type"": ""vstuc"",
            ""request"": ""attach""
        }
     ]
}";

		/// <summary>
		/// Creates or patches the launch.json file in the VS Code directory.
		/// </summary>
		/// <param name="vscodeDirectory">The .vscode directory path.</param>
		/// <param name="enablePatch">Whether to enable patching of existing files.</param>
		private static void CreateLaunchFile(string vscodeDirectory, bool enablePatch)
		{
			var launchFile = IOPath.Combine(vscodeDirectory, "launch.json");
			if (File.Exists(launchFile))
			{
				if (enablePatch)
					PatchLaunchFile(launchFile);

				return;
			}

			File.WriteAllText(launchFile, DefaultLaunchFileContent);
		}

		/// <summary>
		/// Patches an existing launch.json file to include Unity debugging configuration.
		/// </summary>
		/// <param name="launchFile">The path to the launch.json file.</param>
		private static void PatchLaunchFile(string launchFile)
		{
			try
			{
				const string configurationsKey = "configurations";
				const string typeKey = "type";

				var content = File.ReadAllText(launchFile);
				var launch = JSONNode.Parse(content);

				var configurations = launch[configurationsKey] as JSONArray;
				if (configurations == null)
				{
					configurations = new JSONArray();
					launch.Add(configurationsKey, configurations);
				}

				if (configurations.Linq.Any(entry => entry.Value[typeKey].Value == "vstuc"))
					return;

				var defaultContent = JSONNode.Parse(DefaultLaunchFileContent);
				configurations.Add(defaultContent[configurationsKey][0]);

				WriteAllTextFromJObject(launchFile, launch);
			}
			catch (Exception)
			{
				// do not fail if we cannot patch the launch.json file
			}
		}

		/// <summary>
		/// Creates or patches the settings.json file in the VS Code directory.
		/// </summary>
		/// <param name="vscodeDirectory">The .vscode directory path.</param>
		/// <param name="enablePatch">Whether to enable patching of existing files.</param>
		private void CreateSettingsFile(string vscodeDirectory, bool enablePatch)
		{
			var settingsFile = IOPath.Combine(vscodeDirectory, "settings.json");
			if (File.Exists(settingsFile))
			{
				if (enablePatch)
					PatchSettingsFile(settingsFile);

				return;
			}

			const string excludes = @"    ""files.exclude"": {
        ""**/.DS_Store"": true,
        ""**/.git"": true,
        ""**/.vs"": true,
        ""**/.gitmodules"": true,
        ""**/.vsconfig"": true,
        ""**/*.booproj"": true,
        ""**/*.pidb"": true,
        ""**/*.suo"": true,
        ""**/*.user"": true,
        ""**/*.userprefs"": true,
        ""**/*.unityproj"": true,
        ""**/*.dll"": true,
        ""**/*.exe"": true,
        ""**/*.pdf"": true,
        ""**/*.mid"": true,
        ""**/*.midi"": true,
        ""**/*.wav"": true,
        ""**/*.gif"": true,
        ""**/*.ico"": true,
        ""**/*.jpg"": true,
        ""**/*.jpeg"": true,
        ""**/*.png"": true,
        ""**/*.psd"": true,
        ""**/*.tga"": true,
        ""**/*.tif"": true,
        ""**/*.tiff"": true,
        ""**/*.3ds"": true,
        ""**/*.3DS"": true,
        ""**/*.fbx"": true,
        ""**/*.FBX"": true,
        ""**/*.lxo"": true,
        ""**/*.LXO"": true,
        ""**/*.ma"": true,
        ""**/*.MA"": true,
        ""**/*.obj"": true,
        ""**/*.OBJ"": true,
        ""**/*.asset"": true,
        ""**/*.cubemap"": true,
        ""**/*.flare"": true,
        ""**/*.mat"": true,
        ""**/*.meta"": true,
        ""**/*.prefab"": true,
        ""**/*.unity"": true,
        ""build/"": true,
        ""Build/"": true,
        ""Library/"": true,
        ""library/"": true,
        ""obj/"": true,
        ""Obj/"": true,
        ""Logs/"": true,
        ""logs/"": true,
        ""ProjectSettings/"": true,
        ""UserSettings/"": true,
        ""temp/"": true,
        ""Temp/"": true
    }";

			var content = @"{
" + excludes + @",
    ""files.associations"": {
        ""*.asset"": ""yaml"",
        ""*.meta"": ""yaml"",
        ""*.prefab"": ""yaml"",
        ""*.unity"": ""yaml"",
    },
    ""explorer.fileNesting.enabled"": true,
    ""explorer.fileNesting.patterns"": {
        ""*.sln"": ""*.csproj"",
    },
    ""dotnet.defaultSolution"": """ + IOPath.GetFileName(ProjectGenerator.SolutionFile()) + @"""
}";

			File.WriteAllText(settingsFile, content);
		}

		/// <summary>
		/// Patches an existing settings.json file to update Unity-specific settings.
		/// </summary>
		/// <param name="settingsFile">The path to the settings.json file.</param>
		private void PatchSettingsFile(string settingsFile)
		{
			try
			{
				const string excludesKey = "files.exclude";
				const string solutionKey = "dotnet.defaultSolution";

				var content = File.ReadAllText(settingsFile);
				var settings = JSONNode.Parse(content);

				var excludes = settings[excludesKey] as JSONObject;
				if (excludes == null)
					return;

				var patchList = new List<string>();
				var patched = false;

				// Remove files.exclude for solution+project files in the project root
				foreach (var exclude in excludes)
				{
					if (!bool.TryParse(exclude.Value, out var exc) || !exc)
						continue;

					var key = exclude.Key;

					if (!key.EndsWith(".sln") && !key.EndsWith(".csproj"))
						continue;

					if (!Regex.IsMatch(key, "^(\\*\\*[\\\\\\/])?\\*\\.(sln|csproj)$"))
						continue;

					patchList.Add(key);
					patched = true;
				}

				// Check default solution
				var defaultSolution = settings[solutionKey];
				var solutionFile = IOPath.GetFileName(ProjectGenerator.SolutionFile());
				if (defaultSolution == null || defaultSolution.Value != solutionFile)
				{
					settings[solutionKey] = solutionFile;
					patched = true;
				}

				if (!patched)
					return;

				foreach (var patch in patchList)
					excludes.Remove(patch);

				WriteAllTextFromJObject(settingsFile, settings);
			}
			catch (Exception)
			{
				// do not fail if we cannot patch the settings.json file
			}
		}

		/// <summary>
		/// The identifier for the Microsoft Visual Studio Tools for Unity extension for VS Code.
		/// </summary>
		private const string MicrosoftUnityExtensionId = "visualstudiotoolsforunity.vstuc";
		/// <summary>
		/// The default content for the extensions.json file.
		/// </summary>
		private const string DefaultRecommendedExtensionsContent = @"{
    ""recommendations"": [
      """+ MicrosoftUnityExtensionId + @"""
    ]
}
";

		/// <summary>
		/// Creates or patches the extensions.json file in the VS Code directory.
		/// </summary>
		/// <param name="vscodeDirectory">The .vscode directory path.</param>
		/// <param name="enablePatch">Whether to enable patching of existing files.</param>
		private static void CreateRecommendedExtensionsFile(string vscodeDirectory, bool enablePatch)
		{
			// see https://tattoocoder.com/recommending-vscode-extensions-within-your-open-source-projects/
			var extensionFile = IOPath.Combine(vscodeDirectory, "extensions.json");
			if (File.Exists(extensionFile))
			{
				if (enablePatch)
					PatchRecommendedExtensionsFile(extensionFile);

				return;
			}

			File.WriteAllText(extensionFile, DefaultRecommendedExtensionsContent);
		}

		/// <summary>
		/// Patches an existing extensions.json file to include the Microsoft Unity extension.
		/// </summary>
		/// <param name="extensionFile">The path to the extensions.json file.</param>
		private static void PatchRecommendedExtensionsFile(string extensionFile)
		{
			try
			{
				const string recommendationsKey = "recommendations";

				var content = File.ReadAllText(extensionFile);
				var extensions = JSONNode.Parse(content);

				var recommendations = extensions[recommendationsKey] as JSONArray;
				if (recommendations == null)
				{
					recommendations = new JSONArray();
					extensions.Add(recommendationsKey, recommendations);
				}

				if (recommendations.Linq.Any(entry => entry.Value.Value == MicrosoftUnityExtensionId))
					return;

				recommendations.Add(MicrosoftUnityExtensionId);
				WriteAllTextFromJObject(extensionFile, extensions);
			}
			catch (Exception)
			{
				// do not fail if we cannot patch the extensions.json file
			}
		}

		/// <summary>
		/// Writes a JSON node to a file with proper formatting.
		/// </summary>
		/// <param name="file">The path to the file to write.</param>
		/// <param name="node">The JSON node to write.</param>
		private static void WriteAllTextFromJObject(string file, JSONNode node)
		{
			using (var fs = File.Open(file, FileMode.Create))
			using (var sw = new StreamWriter(fs))
			{
				// Keep formatting/indent in sync with default contents
				sw.Write(node.ToString(aIndent: 4));
			}
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
			var application = Path;

			line = Math.Max(1, line);
			column = Math.Max(0, column);

			var directory = IOPath.GetDirectoryName(solution);
			var workspace = TryFindWorkspace(directory);

			var target = workspace ?? directory;

			ProcessRunner.Start(string.IsNullOrEmpty(path)
				? ProcessStartInfoFor(application, $"\"{target}\"")
				: ProcessStartInfoFor(application, $"\"{target}\" -g \"{path}\":{line}:{column}"));

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
		/// <param name="application">The path to the VS Code executable.</param>
		/// <param name="arguments">The command-line arguments to pass to VS Code.</param>
		/// <returns>A configured ProcessStartInfo object.</returns>
		private static ProcessStartInfo ProcessStartInfoFor(string application, string arguments)
		{
#if UNITY_EDITOR_OSX
			// wrap with built-in OSX open feature
			arguments = $"-n \"{application}\" --args {arguments}";
			application = "open";
			return ProcessRunner.ProcessStartInfoFor(application, arguments, redirect:false, shell: true);
#else
			return ProcessRunner.ProcessStartInfoFor(application, arguments, redirect: false);
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
