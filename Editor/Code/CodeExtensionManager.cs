/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using IOPath = System.IO.Path;
using Debug = UnityEngine.Debug;

namespace Hackerzhuli.Code.Editor.Code
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
	/// Wrapper class for deserializing the extensions.json file.
	/// </summary>
	[Serializable]
	internal class CodeExtensionsWrapper
	{
		public CodeExtensionInfo[] extensions;
	}

	/// <summary>
	/// Represents an extension entry in the extensions.json file.
	/// </summary>
	[Serializable]
	internal class CodeExtensionInfo
	{
		public CodeExtensionIdentifier identifier;
		public string version;
		public string relativeLocation;
	}

	/// <summary>
	/// Represents the identifier of an extension.
	/// </summary>
	[Serializable]
	internal class CodeExtensionIdentifier
	{
		public string id;
	}

	/// <summary>
	/// Manages VS Code extension discovery and state tracking.
	/// Provides functionality for loading and querying VS Code extension information.
	/// </summary>
	internal class CodeExtensionManager
	{
		/// <summary>
		/// The identifier for the Visual Studio Tools for Unity extension for VS Code.< br/>
		/// </summary>
		public const string UnityExtensionId = "visualstudiotoolsforunity.vstuc";

		/// <summary>
		/// The identifier for the DotRush extension for VS Code.
		/// </summary>
		public const string DotRushExtensionId = "nromanov.dotrush";

		/// <summary>
        /// The identifier for the Unity Code extension for VS Code.
        /// </summary>
        public const string UnityCodeExtensionId = "hackerzhuli.unity-code";

		/// <summary>
		/// The path to the extensions directory for this VS Code installation.
		/// </summary>
		public string ExtensionsDirectory { get; }

		/// <summary>
		/// Dictionary of extension states with extension ID as the key.
		/// </summary>
		private Dictionary<string, CodeExtensionState> ExtensionStates { get; set; } = new();

		/// <summary>
		/// Initializes a new instance of the CodeExtensionManager class.
		/// </summary>
		/// <param name="extensionsDirectory">The path to the VS Code extensions directory.</param>
		public CodeExtensionManager(string extensionsDirectory)
		{
			ExtensionsDirectory = extensionsDirectory;
			UpdateExtensionStates();
		}

		/// <summary>
		/// Gets the state of a specific extension. If the extension state doesn't exist in the dictionary,
		/// a new state will be created, added to the dictionary, and returned.
		/// </summary>
		/// <param name="extensionId">The ID of the extension.</param>
		/// <returns>The existing extension state or a newly created state.</returns>
		public CodeExtensionState GetExtensionState(string extensionId)
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
		public CodeExtensionState UnityToolsExtensionState => GetExtensionState(UnityExtensionId);

		/// <summary>
		/// Gets the state of the DotRush extension.
		/// </summary>
		public CodeExtensionState DotRushExtensionState => GetExtensionState(DotRushExtensionId);

        /// <summary>
        /// Gets the state of the Unity Code extension.
        /// </summary>
        public CodeExtensionState UnityCodeExtensionState => GetExtensionState(UnityCodeExtensionId);

        /// <summary>
        /// Updates extension states by reloading from the extensions.json file.
        /// This should be called before any file operations to ensure we have the latest extension information.
        /// </summary>
        public void UpdateExtensionStates()
		{
			LoadExtensionStates(ExtensionsDirectory);
		}

		/// <summary>
		/// Loads extension states from the extensions.json file.
		/// </summary>
		/// <param name="extensionsDirectory">The directory containing the extensions.</param>
		private void LoadExtensionStates(string extensionsDirectory)
		{
			ExtensionStates = new Dictionary<string, CodeExtensionState>();

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

				var wrapper = JsonUtility.FromJson<CodeExtensionsWrapper>(json);
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
		}
	}
}