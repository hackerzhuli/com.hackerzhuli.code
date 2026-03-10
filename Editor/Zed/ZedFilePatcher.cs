using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IOPath = System.IO.Path;
using Debug = UnityEngine.Debug;

namespace Hackerzhuli.Code.Editor.Zed
{
    /// <summary>
    ///     Handles file patching for Zed editor.
    ///     Provides functionality for creating and patching Zed configuration files.
    /// </summary>
    internal class ZedFilePatcher
    {
        /// <summary>
        ///     Initializes a new instance of the ZedFilePatcher class.
        /// </summary>
        public ZedFilePatcher()
        {
        }

        /// <summary>
        ///     Creates or patches configuration files for Zed in the project directory.
        /// </summary>
        /// <param name="projectDirectory">The Unity project directory where the files should be created.</param>
        public void CreateOrPatchFiles(string projectDirectory)
        {
            try
            {
                var zedDirectory = IOPath.Combine(projectDirectory.NormalizePathSeparators(), ".zed");
                Directory.CreateDirectory(zedDirectory);

                var enablePatch = !File.Exists(IOPath.Combine(zedDirectory, ".zedpatchdisable"));

                CreateSettingsFile(zedDirectory, enablePatch);
            }
            catch (IOException)
            {
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
        private static string GenerateSettingsFileContent()
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
                Debug.LogError($"Error generating Zed settings file content: {ex.Message}");
                return CreateEmptySettingsContent();
            }
        }

        /// <summary>
        ///     Creates or patches the settings.json file in the Zed directory.
        /// </summary>
        /// <param name="zedDirectory">The .zed directory path.</param>
        /// <param name="enablePatch">Whether to enable patching of existing files.</param>
        private static void CreateSettingsFile(string zedDirectory, bool enablePatch)
        {
            var settingsFile = IOPath.Combine(zedDirectory, "settings.json");
            if (File.Exists(settingsFile))
            {
                if (enablePatch)
                    PatchSettingsFile(settingsFile);

                return;
            }

            var content = GenerateSettingsFileContent();
            File.WriteAllText(settingsFile, content);
        }

        /// <summary>
        ///     Applies patches to a settings.json file represented as a JSONNode.
        /// </summary>
        /// <param name="settings">The JSON node representing the settings.json content.</param>
        /// <returns>True if any changes were made to the JSON, false otherwise.</returns>
        private static bool PatchSettingsFileImpl(JSONNode settings)
        {
            const string excludesKey = "file_scan_exclusions";

            var patched = false;

            // Get or create file_scan_exclusions array
            var excludes = settings[excludesKey] as JSONArray;
            if (excludes == null)
            {
                excludes = new JSONArray();
                settings[excludesKey] = excludes;
                patched = true;
            }

            // Convert existing exclusions to a HashSet for fast lookup
            var existingExclusions = new HashSet<string>();
            foreach (var item in excludes)
            {
                if (item.Value is JSONString str)
                    existingExclusions.Add(str.Value);
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
            {
                if (!existingExclusions.Contains(pattern))
                {
                    excludes.Add(pattern);
                    patched = true;
                }
            }

            return patched;
        }

        /// <summary>
        ///     Patches an existing settings.json file to update Unity-specific settings.
        /// </summary>
        /// <param name="settingsFile">The path to the settings.json file.</param>
        private static void PatchSettingsFile(string settingsFile)
        {
            try
            {
                var content = File.ReadAllText(settingsFile);
                var settings = JSONNode.Parse(content);

                if (PatchSettingsFileImpl(settings))
                    WriteAllTextFromJObject(settingsFile, settings);
            }
            catch
            {
                // do nothing
            }
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
            sw.Write(node.ToString(4));
        }
    }
}
