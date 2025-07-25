/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Hackerzhuli.Code.Editor.ProjectGeneration;
using IOPath = System.IO.Path;
using Debug = UnityEngine.Debug;

namespace Hackerzhuli.Code.Editor.Code
{
    /// <summary>
    ///     Handles file patching for VS Code installations.
    ///     Provides functionality for creating and patching VS Code configuration files based on installed extensions.
    /// </summary>
    internal class CodeFilePatcher
    {
        /// <summary>
        ///     The generator instance used for creating project files.
        /// </summary>
        private static readonly IGenerator _generator = GeneratorFactory.GetInstance(GeneratorStyle.SDK);

        /// <summary>
        ///     Initializes a new instance of the CodeFilePatcher class.
        /// </summary>
        /// <param name="extensionManager">The extension manager instance for handling VS Code extension discovery.</param>
        public CodeFilePatcher(CodeExtensionManager extensionManager)
        {
            ExtensionManager = extensionManager ?? throw new ArgumentNullException(nameof(extensionManager));
        }

        /// <summary>
        ///     The extension manager instance for handling VS Code extension discovery.
        /// </summary>
        private CodeExtensionManager ExtensionManager { get; }

        /// <summary>
        ///     Updates extension states by reloading from the extensions.json file.
        ///     This should be called before any file operations to ensure we have the latest extension information.
        /// </summary>
        public void UpdateExtensionStates()
        {
            ExtensionManager.UpdateExtensionStates();
        }

        /// <summary>
        ///     Creates additional configuration files for VS Code in the project directory.
        /// </summary>
        /// <param name="projectDirectory">The Unity project directory where the files should be created.</param>
        public void CreateOrPatchFiles(string projectDirectory)
        {
            try
            {
                // Update extension states to ensure we have the latest information
                UpdateExtensionStates();

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
        ///     Creates a default launch.json content with an empty configurations array.
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
        ///     Adds extension-specific launch configurations to the provided JSON object based on installed extensions.
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

            var patched = false;

            // Iterate through all launch configurations and add them if the extension is installed
            foreach (var launchConfig in CodeLaunchItem.Items)
            {
                // Check if the extension is installed
                var extensionState = ExtensionManager.GetExtensionState(launchConfig.ExtensionId);
                if (!extensionState.IsInstalled)
                    continue;

                // Check if configuration with this type already exists
                if (configurations.Linq.Any(entry => entry.Value[typeKey].Value == launchConfig.Type))
                    continue;

                // Create the launch configuration
                var configObject = new JSONObject();
                configObject.Add(nameKey, launchConfig.Name);
                configObject.Add(typeKey, launchConfig.Type);
                configObject.Add(requestKey, launchConfig.Request);

                // Add any additional properties
                foreach (var additionalProperty in launchConfig.AdditionalProperties)
                    configObject.Add(additionalProperty.Key, additionalProperty.Value.ToString());

                configurations.Add(configObject);
                patched = true;
            }

            return patched;
        }

        /// <summary>
        ///     Generates the launch.json content based on installed extensions.
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
        ///     Creates or patches the launch.json file in the VS Code directory.
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
        ///     Patches an existing launch.json file to include Unity debugging configuration.
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
                    // Only write to file if changes were made
                    WriteAllTextFromJObject(launchFile, launch);
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
        ///     Creates an empty settings.json content with minimal structure.
        /// </summary>
        /// <returns>A JSON string containing the empty settings structure.</returns>
        private static string CreateEmptySettingsContent()
        {
            return @"{
}";
        }

        /// <summary>
        ///     Generates the settings.json content based on project settings.
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

                return settingsJson.ToString(4);
            }
            catch (Exception ex)
            {
                // This should not happen with our controlled input, but handle it just in case
                Debug.LogError($"Error generating settings file content: {ex.Message}");
                return CreateEmptySettingsContent(); // Fallback to empty content
            }
        }

        /// <summary>
        ///     Creates or patches the settings.json file in the VS Code directory.
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
        ///     Applies patches to a settings.json file represented as a JSONNode.
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
            var defaultExcludePatterns = new[]
            {
                "**/.DS_Store", "**/.git", "**/.vs", "**/.vsconfig",
                "**/*.booproj", "**/*.pidb", "**/*.suo", "**/*.user", "**/*.userprefs", "**/*.unityproj",
                "**/*.dll", "**/*.exe", "**/*.pdf", "**/*.mid", "**/*.midi", "**/*.wav",
                "**/*.ico", "**/*.psd", "**/*.tga", "**/*.tif", "**/*.tiff",
                "**/*.3ds", "**/*.3DS", "**/*.fbx", "**/*.FBX", "**/*.lxo", "**/*.LXO", "**/*.ma", "**/*.MA",
                "**/*.obj", "**/*.OBJ",
                "**/*.cubemap", "**/*.flare", "**/*.mat", "**/*.meta", "**/*.prefab", "**/*.unity",
                "build/", "Build/", "Library/", "library/", "obj/", "Obj/", "Logs/", "logs/", "ProjectSettings/",
                "UserSettings/", "temp/", "Temp/"
            };

            foreach (var pattern in defaultExcludePatterns)
                if (!excludes.HasKey(pattern))
                {
                    excludes[pattern] = true;
                    patched = true;
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

            // Handle .uxml associations based on installed extensions
            // If Unity extension is not installed, add .uxml to xml association
            // USS files are no longer associated with CSS to allow for custom language server
            // If Unity extension is installed but DotRush is not, remove those associations
            // If both Unity and DotRush are installed, keep the associations as they are
            if (!ExtensionManager.UnityToolsExtensionState.IsInstalled)
            {
                // Unity extension not installed, add associations
                defaultAssociations["*.uxml"] = "xml";
                // USS files are no longer associated with CSS - removed for custom language server support
            }
            else if (ExtensionManager.UnityToolsExtensionState.IsInstalled &&
                     !ExtensionManager.DotRushExtensionState.IsInstalled)
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
                if (!associations.HasKey(association.Key) || associations[association.Key] != association.Value)
                {
                    associations[association.Key] = association.Value;
                    patched = true;
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
            var solutionFile = IOPath.GetFileName(_generator.SolutionFile());
            if (defaultSolution == null || defaultSolution.Value != solutionFile)
            {
                settings[solutionKey] = solutionFile;
                patched = true;
            }

            // Check dotrush.roslyn.projectOrSolutionFiles setting
            if (ExtensionManager.DotRushExtensionState.IsInstalled)
            {
                const string dotRushSolutionKey = "dotrush.roslyn.projectOrSolutionFiles";
                var dotRushSolutionSetting = settings[dotRushSolutionKey];
                var absoluteSolutionPath = _generator.SolutionFile();

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
        ///     Patches an existing settings.json file to update Unity-specific settings.
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
                    // Only write to file if changes were made
                    WriteAllTextFromJObject(settingsFile, settings);
            }
            catch
            {
                // do nothing
            }
        }

        /// <summary>
        ///     Creates an empty extensions.json content with minimal structure.
        /// </summary>
        /// <returns>A JSON string containing the empty extensions structure.</returns>
        private static string CreateEmptyExtensionsContent()
        {
            return @"{
}";
        }

        /// <summary>
        ///     Generates the extensions.json content based on installed extensions.
        /// </summary>
        /// <returns>A JSON string containing the appropriate extension recommendations.</returns>
        private static string GenerateRecommendedExtensionsContent()
        {
            try
            {
                var emptyContent = CreateEmptyExtensionsContent();
                var extensionsJson = JSONNode.Parse(emptyContent);

                PatchRecommendedExtensionsFileImpl(extensionsJson);

                return extensionsJson.ToString(4);
            }
            catch (Exception ex)
            {
                // This should not happen with our controlled input, but handle it just in case
                Debug.LogError($"Error generating extensions file content: {ex.Message}");
                return CreateEmptyExtensionsContent(); // Fallback to empty content
            }
        }

        /// <summary>
        ///     Creates or patches the extensions.json file in the VS Code directory.
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
        ///     Patches an existing extensions.json file to include the Visual Studio Tools for Unity extension.
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
                    // Only write to file if changes were made
                    WriteAllTextFromJObject(extensionFile, extensions);
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
        ///     Applies patches to an extensions.json file represented as a JSONNode.
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

            // Add Unity Code extension if not already present
            if (!recommendations.Linq.Any(entry => entry.Value.Value == CodeExtensionManager.UnityCodeExtensionId))
            {
                recommendations.Add(CodeExtensionManager.UnityCodeExtensionId);
                patched = true;
            }

            return patched;
        }

        /// <summary>
        ///     Writes a JSON node to a file with proper formatting.
        /// </summary>
        /// <param name="file">The path to the file to write.</param>
        /// <param name="node">The JSON node to write.</param>
        private static void WriteAllTextFromJObject(string file, JSONNode node)
        {
            using var fs = File.Open(file, FileMode.Create);
            using var sw = new StreamWriter(fs);
            // Keep formatting/indent in sync with default contents
            sw.Write(node.ToString(4));
        }
    }
}