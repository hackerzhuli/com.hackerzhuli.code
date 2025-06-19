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
	/// Represents the state of a VS Code extension.
	/// </summary>
	public record CodeExtensionState
	{
		/// <summary>
		/// The identifier of the extension.
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// The version of the extension if installed, otherwise null.
		/// </summary>
		public string Version { get; set; }

		/// <summary>
		/// The relative path to the extension if installed, otherwise null.
		/// </summary>
		public string RelativePath { get; set; }

		/// <summary>
		/// Whether the extension is installed.
		/// </summary>
		public bool IsInstalled => !string.IsNullOrEmpty(RelativePath);
	}

	/// <summary>
	/// Data for a Visual Studio Code fork.
	/// </summary>
	public record CodeForkData
	{
		/// <summary>
		/// The name of the fork (that is displayed to the user).
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// The default folder name for a fork used on Windows (typically in Program Files or Local AppData).
		/// </summary>
		public string WindowsDefaultDirName { get; set; }

		/// <summary>
		/// The executable name on Windows (without .exe extension).
		/// </summary>
		public string WindowsExeName { get; set; }

		/// <summary>
		/// The app name on macOS (without .app extension).
		/// </summary>
		public string MacAppName { get; set; }

		/// <summary>
		/// The executable name on Linux.
		/// </summary>
		public string LinuxExeName { get; set; }

		/// <summary>
		/// The user data directory name in the user profile (including the leading dot)(it's the same across different platforms).
		/// </summary>
		public string UserDataDirName { get; set; }

		/// <summary>
		/// The latest C# language version supported by this fork.
		/// </summary>
		public Version LatestLanguageVersion { get; set; }

		/// <summary>
		/// True if this fork is always a pre-release version, otherwise false(then is pre-release version will be checked dynamically)
		/// </summary>
		public bool IsPrerelease { get; set; }

		/// <summary>
		/// True if this fork is from Microsoft, otherwise false. This affects how launch files are patched.
		/// </summary>
		public bool IsMicrosoft { get; set; }
	}

	/// <summary>
	/// Represents a Visual Studio Code fork installation.
	/// Provides functionality for discovering, interacting with, and configuring VS Code.
	/// </summary>
	internal class VisualStudioCodeInstallation : VisualStudioInstallation
	{
		/// <summary>
		/// The fork data for this installation.
		/// </summary>
		private CodeForkData ForkData { get; set; }

		/// <summary>
		/// Dictionary of extension states with extension ID as the key.
		/// </summary>
		private Dictionary<string, CodeExtensionState> ExtensionStates { get; set; } = new();

		/// <summary>
		/// Gets the state of a specific extension. If the extension state doesn't exist in the dictionary,
		/// a new state will be created, added to the dictionary, and returned.
		/// </summary>
		/// <param name="extensionId">The ID of the extension.</param>
		/// <returns>The existing extension state or a newly created state.</returns>
		private CodeExtensionState GetExtensionState(string extensionId)
		{
			if (ExtensionStates.TryGetValue(extensionId, out var state))
				return state;
			
			// Create a new extension state, add it to the dictionary, and return it
			var newState = new CodeExtensionState { Id = extensionId };
			ExtensionStates[extensionId] = newState;
			return newState;
		}

		/// <summary>
		/// Gets the state of the Visual Studio Tools for Unity extension.
		/// </summary>
		private CodeExtensionState UnityToolsExtensionState => GetExtensionState(MicrosoftUnityExtensionId);

		/// <summary>
		/// Gets the state of the C# Dev Kit extension.
		/// </summary>
		private CodeExtensionState CSharpDevKitExtensionState => GetExtensionState(CSharpDevKitExtensionId);

		/// <summary>
		/// Gets the state of the DotRush extension.
		/// </summary>
		private CodeExtensionState DotRushExtensionState => GetExtensionState(DotRushExtensionId);

		/// <summary>
		/// Static array of supported Visual Studio Code forks.<br/>
		/// VS Code Insiders is treated as a fork because it have a different executable name than the stable version<br/>
		/// If for a fork, a prerelease version and the stable version have same executable name, then it should be treated as the same fork
		/// </summary>
		private static readonly CodeForkData[] Forks = new[]
		{
			new CodeForkData
			{
				Name = "Visual Studio Code",
				WindowsDefaultDirName = "Microsoft VS Code",
				WindowsExeName = "Code",
				MacAppName = "Visual Studio Code",
				LinuxExeName = "code",
				UserDataDirName = ".vscode",
				LatestLanguageVersion = new Version(13, 0),
				IsPrerelease = false,
				IsMicrosoft = true
			},
			new CodeForkData
			{
				Name = "Visual Studio Code Insiders",
				WindowsDefaultDirName = "Microsoft VS Code Insiders",
				WindowsExeName = "Code - Insiders",
				MacAppName = "Visual Studio Code - Insiders",
				LinuxExeName = "code-insiders",
				UserDataDirName = ".vscode-insiders",
				LatestLanguageVersion = new Version(13, 0),
				IsPrerelease = true,
				IsMicrosoft = true
			},
			new CodeForkData
			{
				Name = "Cursor",
				WindowsDefaultDirName = "Cursor",
				WindowsExeName = "Cursor",
				MacAppName = "Cursor",
				LinuxExeName = "cursor",
				UserDataDirName = ".cursor",
				IsPrerelease = false
			},
			new CodeForkData
			{
				Name = "Windsurf",
				WindowsDefaultDirName = "Windsurf",
				WindowsExeName = "Windsurf",
				MacAppName = "Windsurf",
				LinuxExeName = "windsurf",
				UserDataDirName = ".windsurf",
				IsPrerelease = false,
				IsMicrosoft = false
			},
			new CodeForkData
			{
				Name = "Windsurf Next",
				WindowsDefaultDirName = "Windsurf Next",
				WindowsExeName = "Windsurf - Next",
				MacAppName = "Windsurf - Next",
				LinuxExeName = "windsurf-next",
				UserDataDirName = ".windsurf-next",
				IsPrerelease = true,
				IsMicrosoft = false
			},
			new CodeForkData
			{
				Name = "Trae",
				WindowsDefaultDirName = "Trae",
				WindowsExeName = "Trae",
				MacAppName = "Trae",
				LinuxExeName = "trae",
				UserDataDirName = ".trae",
				IsPrerelease = false,
				IsMicrosoft = false
			}
		};

		/// <summary>
		/// The generator instance used for creating project files.
		/// </summary>
		private static readonly IGenerator _generator = GeneratorFactory.GetInstance(GeneratorStyle.SDK);

		/// <summary>
		/// Gets whether this installation supports code analyzers.
		/// </summary>
		/// <returns>Always returns true for VS Code installations.</returns>
		public override bool SupportsAnalyzers => true;

		/// <summary>
		/// Gets the latest C# language version supported by this VS Code installation.
		/// </summary>
		/// <returns>The fork-specific latest supported C# language version, default to 11 if not defined in <see cref="CodeForkData"/></returns>
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
		/// Wrapper class for deserializing the extensions.json file.
		/// </summary>
		[Serializable]
		private class ExtensionsWrapper
		{
			public ExtensionInfo[] extensions;
		}

		/// <summary>
		/// Represents an extension entry in the extensions.json file.
		/// </summary>
		[Serializable]
		private class ExtensionInfo
		{
			public ExtensionIdentifier identifier;
			public string version;
			public string relativeLocation;
		}

		/// <summary>
		/// Represents the identifier of an extension.
		/// </summary>
		[Serializable]
		private class ExtensionIdentifier
		{
			public string id;
		}

		/// <summary>
		/// Loads extension states from the extensions.json file.
		/// </summary>
		/// <param name="extensionsDirectory">The directory containing the extensions.</param>
		private void LoadExtensionStates(string extensionsDirectory)
		{
			// Debug.Log($"Loading extension states from {extensionsDirectory}");
			// Initialize dictionary with default empty states for known extensions
			// These will be updated if found in extensions.json, and any other extensions will also be added
			ExtensionStates = new Dictionary<string, CodeExtensionState>
			{
				[MicrosoftUnityExtensionId] = new() { Id = MicrosoftUnityExtensionId },
				[CSharpDevKitExtensionId] = new() { Id = CSharpDevKitExtensionId },
				[DotRushExtensionId] = new() { Id = DotRushExtensionId }
			};

			if (string.IsNullOrEmpty(extensionsDirectory))
			{
				Debug.LogError($"Extensions directory is null or empty");
				return;
			}

			try
			{
				var extensionsJsonPath = IOPath.Combine(extensionsDirectory, "extensions.json");
				if (!File.Exists(extensionsJsonPath))
					return;

				var json = File.ReadAllText(extensionsJsonPath);
				// Wrap the JSON array in an object for JsonUtility
				json = $"{{\"extensions\":{json}}}";

				var wrapper = JsonUtility.FromJson<ExtensionsWrapper>(json);
				if (wrapper?.extensions == null)
				{
					Debug.LogError($"Extensions wrapper is null");
					return;
				}

				foreach (var extension in wrapper.extensions)
				{
					if (extension?.identifier?.id == null || extension?.relativeLocation == null)
						continue;

					var extensionId = extension.identifier.id;

					if (!ExtensionStates.ContainsKey(extensionId))
					{
						continue;
					}

					var state = new CodeExtensionState
					{
						Id = extensionId,
						RelativePath = extension.relativeLocation,
						Version = extension.version
					};

					ExtensionStates[extensionId] = state;
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"Error loading extensions.json: {ex.Message}");
			}

			Debug.Log($"Loaded {ExtensionStates.Count} extension states, they are :{string.Join(", ", ExtensionStates.Select(kv => kv.Value))}");
		}

		/// <summary>
		/// Gets the array of analyzer assemblies available in the Visual Studio Tools for Unity extension for the VS Code installation.
		/// </summary>
		/// <returns>Array of analyzer assembly paths or an empty array if none found.</returns>
		public override string[] GetAnalyzers()
		{
			if (!UnityToolsExtensionState.IsInstalled)
				return Array.Empty<string>();

			return GetAnalyzers(IOPath.Combine(GetExtensionsDirectory(), UnityToolsExtensionState.RelativePath));
		}

		/// <summary>
		/// Gets the project generator for this VS Code installation.
		/// </summary>
		public override IGenerator ProjectGenerator => _generator;

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
		/// Identifies the VS Code fork based on the executable path.
		/// </summary>
		/// <param name="exePath">The path to the VS Code executable.</param>
		/// <returns>The VSCodeForkData if a supported fork is identified; otherwise, null.</returns>
		private static CodeForkData GetForkDataByPath(string exePath)
		{
			if (string.IsNullOrEmpty(exePath))
				return null;

#if UNITY_EDITOR_OSX
			if (!Directory.Exists(exePath))
				return null;

			// Check if the path ends with any of the supported fork app names
			foreach (var fork in SupportedForks)
			{
				if (exePath.EndsWith($"{fork.MacAppName}.app", StringComparison.OrdinalIgnoreCase))
				{
					return fork;
				}
			}

#elif UNITY_EDITOR_WIN
			if (!File.Exists(exePath))
				return null;

			// Check if the path ends with any of the supported fork executable names
			foreach (var fork in Forks)
			{
				if (exePath.EndsWith($"{fork.WindowsExeName}.exe", StringComparison.OrdinalIgnoreCase))
				{
					return fork;
				}
			}
#else
			if (!File.Exists(exePath))
				return null;

			// Check if the path ends with any of the supported fork executable names
			foreach (var fork in SupportedForks)
			{
				if (exePath.EndsWith(fork.LinuxExeName, StringComparison.OrdinalIgnoreCase) ||
				    exePath.EndsWith($"{fork.LinuxExeName}.desktop"))
				{
					return fork;
				}
			}
#endif

			return null;
		}

		public static bool TryDiscoverInstallation(string exePath, out IVisualStudioInstallation installation)
		{
			Debug.Log($"trying to get the vs code fork installation at {exePath}");
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
				var manifestBase = GetRealPath(exePath);

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

			var installation2 = new VisualStudioCodeInstallation()
			{
				ForkData = forkData,
				IsPrerelease = isPrerelease,
				Name = name,
				Path = exePath,
				Version = version ?? new Version()
			};
			installation = installation2;

			// Load extension states
			var extensionsDirectory = installation2.GetExtensionsDirectory();
			installation2.LoadExtensionStates(extensionsDirectory);

			Debug.Log($"discovered vs code installation {name} at {installation.Path}");

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
			var localAppPath = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"Programs");
			var programFiles = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

			foreach (var basePath in new[] { localAppPath, programFiles })
			{
				foreach (var fork in Forks)
				{
					candidates.Add(
						IOPath.Combine(basePath, fork.WindowsDefaultDirName, $"{fork.WindowsExeName}.exe"));
				}
			}
#elif UNITY_EDITOR_OSX
			var appPath = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
			
			// Add specific fork app patterns
			foreach (var fork in SupportedForks)
			{
				candidates.AddRange(Directory.EnumerateDirectories(appPath, $"{fork.MacAppName}*.app"));
			}
#elif UNITY_EDITOR_LINUX
			// Well known locations for all forks
			foreach (var fork in SupportedForks)
			{
				candidates.Add($"/usr/bin/{fork.LinuxExeName}");
				candidates.Add($"/bin/{fork.LinuxExeName}");
				candidates.Add($"/usr/local/bin/{fork.LinuxExeName}");
			}

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
		private static readonly Regex DesktopFileExecEntry =
 new Regex(@"Exec=(\S+)", RegexOptions.Singleline | RegexOptions.Compiled);

		/// <summary>
		/// Gets candidate VS Code fork paths from XDG data directories on Linux.
		/// </summary>
		/// <returns>An enumerable collection of potential VS Code fork executable paths.</returns>
		private static IEnumerable<string> GetXdgCandidates()
		{
			var envdirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
			if (string.IsNullOrEmpty(envdirs))
				yield break;

			var dirs = envdirs.Split(':');
			foreach(var dir in dirs)
			{
				foreach (var fork in SupportedForks)
				{
					Match match = null;
					var desktopFileName = $"{fork.LinuxExeName}.desktop";

					try
					{
						var desktopFile = IOPath.Combine(dir, $"applications/{desktopFileName}");
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
				}
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
		/// Creates a default launch.json content with an empty configurations array.
		/// </summary>
		/// <returns>A JSON string containing the empty launch configuration.</returns>
		private static string CreateEmptyLaunchContent()
		{
			return @"{
    ""version"": ""0.2.0"",
    ""configurations"": []
}";
		}

		/// <summary>
		/// Adds extension-specific launch configurations to the provided JSON object based on installed extensions.
		/// </summary>
		/// <param name="launchJson">The JSON object representing the launch.json file.</param>
		/// <returns>True if any changes were made to the JSON object; otherwise, false.</returns>
		private bool PatchLaunchFileImpl(JSONNode launchJson)
		{
			const string configurationsKey = "configurations";
			const string typeKey = "type";
			const string nameKey = "name";
			const string requestKey = "request";

			var configurations = launchJson[configurationsKey] as JSONArray;
			if (configurations == null)
			{
				configurations = new JSONArray();
				launchJson.Add(configurationsKey, configurations);
			}

			bool patched = false;

			// Add Unity Tools configuration if installed, not already present, and using a Microsoft fork
			if (ForkData.IsMicrosoft && UnityToolsExtensionState.IsInstalled && 
			    !configurations.Linq.Any(entry => entry.Value[typeKey].Value == "vstuc"))
			{
				var unityToolsConfig = new JSONObject();
				unityToolsConfig.Add(nameKey, "Attach to Unity");
				unityToolsConfig.Add(typeKey, "vstuc");
				unityToolsConfig.Add(requestKey, "attach");
				configurations.Add(unityToolsConfig);
				patched = true;
			}

			// Add DotRush configuration if installed and not already present
			if (DotRushExtensionState.IsInstalled && 
			    !configurations.Linq.Any(entry => entry.Value[typeKey].Value == "unity"))
			{
				var dotRushConfig = new JSONObject();
				dotRushConfig.Add(nameKey, "Attach to Unity with DotRush");
				dotRushConfig.Add(typeKey, "unity");
				dotRushConfig.Add(requestKey, "attach");
				configurations.Add(dotRushConfig);
				patched = true;
			}

			return patched;
		}

		/// <summary>
		/// Generates the launch.json content based on installed extensions.
		/// </summary>
		/// <returns>A JSON string containing the appropriate launch configurations.</returns>
		private string GenerateLaunchFileContent()
		{
			try
			{
				var emptyContent = CreateEmptyLaunchContent();
				var launchJson = JSONNode.Parse(emptyContent);

				PatchLaunchFileImpl(launchJson);

				return launchJson.ToString();
			}
			catch (Exception ex)
			{
				// This should not happen with our controlled input, but handle it just in case
				Debug.LogError($"Error generating launch file content: {ex.Message}");
				return CreateEmptyLaunchContent(); // Fallback to empty content
			}
		}

		/// <summary>
		/// Creates or patches the launch.json file in the VS Code directory.
		/// </summary>
		/// <param name="vscodeDirectory">The .vscode directory path.</param>
		/// <param name="enablePatch">Whether to enable patching of existing files.</param>
		private void CreateLaunchFile(string vscodeDirectory, bool enablePatch)
		{
			var launchFile = IOPath.Combine(vscodeDirectory, "launch.json");
			if (File.Exists(launchFile))
			{
				if (enablePatch)
					PatchLaunchFile(launchFile);

				return;
			}

			// Generate content based on installed extensions
			var content = GenerateLaunchFileContent();
			File.WriteAllText(launchFile, content);
		}

		/// <summary>
		/// Patches an existing launch.json file to include Unity debugging configuration.
		/// </summary>
		/// <param name="launchFile">The path to the launch.json file.</param>
		private void PatchLaunchFile(string launchFile)
		{
			try
			{
				var content = File.ReadAllText(launchFile);
				var launch = JSONNode.Parse(content);

				// Apply patches using the common implementation
				if (PatchLaunchFileImpl(launch))
				{
					// Only write to file if changes were made
					WriteAllTextFromJObject(launchFile, launch);
				}
			}
			catch (Exception ex)
			{
				// Handle parsing errors for malformed launch.json files
				Debug.LogError($"Error patching launch file at {launchFile}: {ex.Message}");
				
				// Create a new launch file with default content as fallback
				File.WriteAllText(launchFile, GenerateLaunchFileContent());
			}
		}

		/// <summary>
		/// Creates an empty settings.json content with minimal structure.
		/// </summary>
		/// <returns>A JSON string containing the empty settings structure.</returns>
		private static string CreateEmptySettingsContent()
		{
			return @"{
}";
		}

		/// <summary>
		/// Generates the settings.json content based on project settings.
		/// </summary>
		/// <returns>A JSON string containing the appropriate settings.</returns>
		private string GenerateSettingsFileContent()
		{
			try
			{
				var emptyContent = CreateEmptySettingsContent();
				var settingsJson = JSONNode.Parse(emptyContent);

				// Apply patches to the empty settings
				PatchSettingsFileImpl(settingsJson);

				return settingsJson.ToString(aIndent: 4);
			}
			catch (Exception ex)
			{
				// This should not happen with our controlled input, but handle it just in case
				Debug.LogError($"Error generating settings file content: {ex.Message}");
				return CreateEmptySettingsContent(); // Fallback to empty content
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

			// Generate content based on project settings
			var content = GenerateSettingsFileContent();
			File.WriteAllText(settingsFile, content);
		}

		/// <summary>
		/// Applies patches to a settings.json file represented as a JSONNode.
		/// </summary>
		/// <param name="settings">The JSON node representing the settings.json content.</param>
		/// <returns>True if any changes were made to the JSON, false otherwise.</returns>
		private bool PatchSettingsFileImpl(JSONNode settings)
		{
			const string excludesKey = "files.exclude";
			const string associationsKey = "files.associations";
			const string nestingEnabledKey = "explorer.fileNesting.enabled";
			const string nestingPatternsKey = "explorer.fileNesting.patterns";
			const string solutionKey = "dotnet.defaultSolution";

			var patched = false;

			// Add default files.exclude settings
			var excludes = settings[excludesKey] as JSONObject;
			if (excludes == null)
			{
				excludes = new JSONObject();
				settings[excludesKey] = excludes;
				patched = true;
			}

			// Add default exclude patterns if they don't exist
			string[] defaultExcludePatterns = new string[]
			{
				"**/.DS_Store", "**/.git", "**/.vs", "**/.gitmodules", "**/.vsconfig",
				"**/*.booproj", "**/*.pidb", "**/*.suo", "**/*.user", "**/*.userprefs", "**/*.unityproj",
				"**/*.dll", "**/*.exe", "**/*.pdf", "**/*.mid", "**/*.midi", "**/*.wav",
				"**/*.gif", "**/*.ico", "**/*.jpg", "**/*.jpeg", "**/*.png", "**/*.psd", "**/*.tga", "**/*.tif",
				"**/*.tiff",
				"**/*.3ds", "**/*.3DS", "**/*.fbx", "**/*.FBX", "**/*.lxo", "**/*.LXO", "**/*.ma", "**/*.MA",
				"**/*.obj", "**/*.OBJ",
				"**/*.asset", "**/*.cubemap", "**/*.flare", "**/*.mat", "**/*.meta", "**/*.prefab", "**/*.unity",
				"build/", "Build/", "Library/", "library/", "obj/", "Obj/", "Logs/", "logs/", "ProjectSettings/",
				"UserSettings/", "temp/", "Temp/"
			};

			foreach (var pattern in defaultExcludePatterns)
			{
				if (!excludes.HasKey(pattern))
				{
					excludes[pattern] = true;
					patched = true;
				}
			}

			// Add default files.associations settings
			var associations = settings[associationsKey] as JSONObject;
			if (associations == null)
			{
				associations = new JSONObject();
				settings[associationsKey] = associations;
				patched = true;
			}

			// Add default file associations if they don't exist
			var defaultAssociations = new Dictionary<string, string>
			{
				{ "*.asset", "yaml" },
				{ "*.meta", "yaml" },
				{ "*.prefab", "yaml" },
				{ "*.unity", "yaml" }
			};

			// Handle .uxml and .uss associations based on installed extensions
			// If Unity extension is not installed, add .uxml to xml and .uss to css associations
			// If Unity extension is installed but DotRush is not, remove those associations
			// If both Unity and DotRush are installed, keep the associations as they are
			if (!UnityToolsExtensionState.IsInstalled)
			{
				// Unity extension not installed, add associations
				defaultAssociations["*.uxml"] = "xml";
				defaultAssociations["*.uss"] = "css";
			}
			else if (UnityToolsExtensionState.IsInstalled && !DotRushExtensionState.IsInstalled)
			{
				// Unity extension installed but DotRush is not, remove associations if they exist
				if (associations.HasKey("*.uxml") && associations["*.uxml"] == "xml")
				{
					associations.Remove("*.uxml");
					patched = true;
				}

				if (associations.HasKey("*.uss") && associations["*.uss"] == "css")
				{
					associations.Remove("*.uss");
					patched = true;
				}
			}
			// If both Unity and DotRush are installed, we don't modify these associations

			foreach (var association in defaultAssociations)
			{
				if (!associations.HasKey(association.Key) || associations[association.Key] != association.Value)
				{
					associations[association.Key] = association.Value;
					patched = true;
				}
			}

			// Add explorer.fileNesting.enabled setting
			if (!settings.HasKey(nestingEnabledKey) || settings[nestingEnabledKey].AsBool != true)
			{
				settings[nestingEnabledKey] = true;
				patched = true;
			}

			// Add explorer.fileNesting.patterns settings
			var nestingPatterns = settings[nestingPatternsKey] as JSONObject;
			if (nestingPatterns == null)
			{
				nestingPatterns = new JSONObject();
				settings[nestingPatternsKey] = nestingPatterns;
				patched = true;
			}

			// Add default nesting pattern if it doesn't exist
			if (!nestingPatterns.HasKey("*.sln") || nestingPatterns["*.sln"] != "*.csproj")
			{
				nestingPatterns["*.sln"] = "*.csproj";
				patched = true;
			}

			// Find and collect solution+project files patterns to remove
			// We need to collect them first to avoid modifying the collection during iteration
			var keysToRemove = new List<string>();
			foreach (var exclude in excludes)
			{
				if (!bool.TryParse(exclude.Value, out var exc) || !exc)
					continue;

				var key = exclude.Key;

				if (!key.EndsWith(".sln") && !key.EndsWith(".csproj"))
					continue;

				if (!Regex.IsMatch(key, "^(\\*\\*[\\\\\\/])?\\*\\.(sln|csproj)$"))
					continue;

				keysToRemove.Add(key);
				patched = true;
			}

			// Remove the collected keys
			foreach (var key in keysToRemove)
				excludes.Remove(key);

			// Check default solution
			var defaultSolution = settings[solutionKey];
			var solutionFile = IOPath.GetFileName(ProjectGenerator.SolutionFile());
			if (defaultSolution == null || defaultSolution.Value != solutionFile)
			{
				settings[solutionKey] = solutionFile;
				patched = true;
			}

			// Check dotrush.roslyn.projectOrSolutionFiles setting
			if (DotRushExtensionState.IsInstalled)
			{
				const string dotRushSolutionKey = "dotrush.roslyn.projectOrSolutionFiles";
				var dotRushSolutionSetting = settings[dotRushSolutionKey];
				var absoluteSolutionPath = ProjectGenerator.SolutionFile();
				
				if (dotRushSolutionSetting is JSONArray { Count: 1 } arr && arr[0] is JSONString str &&
				    str.Value == absoluteSolutionPath)
				{
					// the same, do nothing
				}
				else
				{
					var solutionPathArray = new JSONArray();
					solutionPathArray.Add(absoluteSolutionPath);
					settings[dotRushSolutionKey] = solutionPathArray;
					patched = true;
				}
			}

			return patched;
		}

		/// <summary>
		/// Patches an existing settings.json file to update Unity-specific settings.
		/// </summary>
		/// <param name="settingsFile">The path to the settings.json file.</param>
		private void PatchSettingsFile(string settingsFile)
		{
			try
			{
				var content = File.ReadAllText(settingsFile);
				var settings = JSONNode.Parse(content);

				// Apply patches using the common implementation
				if (PatchSettingsFileImpl(settings))
				{
					// Only write to file if changes were made
					WriteAllTextFromJObject(settingsFile, settings);
				}
			}
			catch
			{
				// do nothing
			}
		}

		/// <summary>
		/// The identifier for the Visual Studio Tools for Unity extension for VS Code.
		/// </summary>
		private const string MicrosoftUnityExtensionId = "visualstudiotoolsforunity.vstuc";

		/// <summary>
		/// The identifier for the C# Dev Kit extension for VS Code.
		/// </summary>
		private const string CSharpDevKitExtensionId = "ms-dotnettools.csdevkit";

		/// <summary>
		/// The identifier for the DotRush extension for VS Code.
		/// </summary>
		private const string DotRushExtensionId = "nromanov.dotrush";

		/// <summary>
		/// Creates an empty extensions.json content with minimal structure.
		/// </summary>
		/// <returns>A JSON string containing the empty extensions structure.</returns>
		private static string CreateEmptyExtensionsContent()
		{
			return @"{
}";
		}

		/// <summary>
		/// Generates the extensions.json content based on installed extensions.
		/// </summary>
		/// <returns>A JSON string containing the appropriate extension recommendations.</returns>
		private static string GenerateRecommendedExtensionsContent()
		{
			try
			{
				var emptyContent = CreateEmptyExtensionsContent();
				var extensionsJson = JSONNode.Parse(emptyContent);

				PatchRecommendedExtensionsFileImpl(extensionsJson);

				return extensionsJson.ToString(aIndent: 4);
			}
			catch (Exception ex)
			{
				// This should not happen with our controlled input, but handle it just in case
				Debug.LogError($"Error generating extensions file content: {ex.Message}");
				return CreateEmptyExtensionsContent(); // Fallback to empty content
			}
		}

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

			// Create a new extensions file with generated content
			File.WriteAllText(extensionFile, GenerateRecommendedExtensionsContent());
		}

		/// <summary>
		/// Patches an existing extensions.json file to include the Visual Studio Tools for Unity extension.
		/// </summary>
		/// <param name="extensionFile">The path to the extensions.json file.</param>
		private static void PatchRecommendedExtensionsFile(string extensionFile)
		{
			try
			{
				var content = File.ReadAllText(extensionFile);
				var extensions = JSONNode.Parse(content);

				// Apply patches using the common implementation
				if (PatchRecommendedExtensionsFileImpl(extensions))
				{
					// Only write to file if changes were made
					WriteAllTextFromJObject(extensionFile, extensions);
				}
			}
			catch (Exception ex)
			{
				// Handle parsing errors for malformed extensions.json files
				Debug.LogError($"Error patching extensions file at {extensionFile}: {ex.Message}");
				
				// Create a new extensions file with generated content as fallback
				File.WriteAllText(extensionFile, GenerateRecommendedExtensionsContent());
			}
		}

		/// <summary>
		/// Applies patches to an extensions.json file represented as a JSONNode.
		/// </summary>
		/// <param name="extensions">The JSON node representing the extensions.json content.</param>
		/// <returns>True if any changes were made to the JSON, false otherwise.</returns>
		private static bool PatchRecommendedExtensionsFileImpl(JSONNode extensions)
		{
			const string recommendationsKey = "recommendations";
			
			var patched = false;

			// Ensure recommendations array exists
			var recommendations = extensions[recommendationsKey] as JSONArray;
			if (recommendations == null)
			{
				recommendations = new JSONArray();
				extensions.Add(recommendationsKey, recommendations);
				patched = true;
			}

			// Add Microsoft Unity extension if not already present
			if (!recommendations.Linq.Any(entry => entry.Value.Value == MicrosoftUnityExtensionId))
			{
				recommendations.Add(MicrosoftUnityExtensionId);
				patched = true;
			}

			return patched;
		}

		/// <summary>
		/// Writes a JSON node to a file with proper formatting.
		/// </summary>
		/// <param name="file">The path to the file to write.</param>
		/// <param name="node">The JSON node to write.</param>
		private static void WriteAllTextFromJObject(string file, JSONNode node)
		{
			using var fs = File.Open(file, FileMode.Create);
			using var sw = new StreamWriter(fs);
			// Keep formatting/indent in sync with default contents
			sw.Write(node.ToString(aIndent: 4));
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
			arguments = $"-n \"{application}\" --args {arguments}";
			application = "open";
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
