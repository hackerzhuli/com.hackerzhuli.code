using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using IOPath = System.IO.Path;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    ///     Windows-specific implementation for discovering application installations.
    /// </summary>
    internal class WindowsAppDiscover : IAppDiscover
    {
        private readonly IAppInfo _executableInfo;

        /// <summary>
        ///     Initializes a new instance of the WindowsAppDiscover class.
        /// </summary>
        /// <param name="executableInfo">The executable information to search for.</param>
        public WindowsAppDiscover(IAppInfo executableInfo)
        {
            _executableInfo = executableInfo ?? throw new ArgumentNullException(nameof(executableInfo));
        }

        /// <summary>
        ///     Gets candidate executable paths for the configured executable.
        /// </summary>
        /// <returns>A list of candidate executable paths.</returns>
        public List<string> GetCandidatePaths()
        {
#if HACKERZHULI_CODE_DEBUG
            Debug.Log($"Searching for executable in Windows");
#endif
            var candidates = new List<string>();
            var executableName = _executableInfo.WindowsExeName;

            if (string.IsNullOrEmpty(executableName))
                return candidates;

            // Check common installation directories
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // this is where vs code and other editors typically install their executables
            var localAppDataPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");

            var candidateDirs = new[]
            {
                programFiles,
                programFilesX86,
                localAppData,
                localAppDataPrograms
            };

            // First pass: Check all default locations (more efficient)
            if (!string.IsNullOrEmpty(_executableInfo.WindowsDefaultDirName))
            {
                foreach (var dir in candidateDirs)
                {
                    if (string.IsNullOrEmpty(dir)) continue;

                    var defaultPath = IOPath.Combine(dir, _executableInfo.WindowsDefaultDirName, executableName);
                    if (File.Exists(defaultPath))
                    {
#if HACKERZHULI_CODE_DEBUG
                        Debug.Log($"Found {executableName} in default location: {defaultPath}, dir is {dir}");
#endif
                        candidates.Add(defaultPath);
                    }
                }
            }

            // Only do expensive directory search if no default locations were found
            if (candidates.Count == 0)
            {
                foreach (var dir in candidateDirs)
                {
                    if (string.IsNullOrEmpty(dir)) continue;

                    try
                    {
                        foreach (var subDir in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                        {
                            var executablePath = IOPath.Combine(subDir, executableName);
                            if (File.Exists(executablePath))
                            {
                                //Debug.Log($"Found {executableName} in subdirectory: {executablePath}, dir is {dir}");
                                candidates.Add(executablePath);
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip directories we can't access
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // Skip directories that don't exist
                    }
                }
            }

            return candidates;
        }

        /// <summary>
        ///     Determines if the given path is a valid candidate executable for Windows.
        /// </summary>
        /// <param name="exePath">The path to check.</param>
        /// <returns>True if the path is a valid candidate; otherwise, false.</returns>
        public bool IsCandidate(string exePath)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return false;

            // Check if the path ends with the expected executable name
            return exePath.EndsWith(_executableInfo.WindowsExeName, StringComparison.OrdinalIgnoreCase);
        }
    }
}