/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.IO;
using IOPath = System.IO.Path;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    /// Windows-specific implementation for discovering application installations.
    /// </summary>
    internal class WindowsAppDiscover : IAppDiscover
    {
        private readonly IAppInfo _executableInfo;
        
        /// <summary>
        /// Initializes a new instance of the WindowsAppDiscover class.
        /// </summary>
        /// <param name="executableInfo">The executable information to search for.</param>
        public WindowsAppDiscover(IAppInfo executableInfo)
        {
            _executableInfo = executableInfo ?? throw new ArgumentNullException(nameof(executableInfo));
        }
        
        /// <summary>
        /// Gets candidate executable paths for the configured executable.
        /// </summary>
        /// <returns>A list of candidate executable paths.</returns>
        public List<string> GetCandidatePaths()
        {
            var candidates = new List<string>();
            var executableName = _executableInfo.WindowsExeName;
            
            if (string.IsNullOrEmpty(executableName))
                return candidates;
            
            // Check common installation directories
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            
            var candidateDirs = new[]
            {
                programFiles,
                programFilesX86,
                localAppData
            };
            
            foreach (var dir in candidateDirs)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                
                // First, try the specific default directory if specified
                if (!string.IsNullOrEmpty(_executableInfo.WindowsDefaultDirName))
                {
                    var defaultPath = IOPath.Combine(dir, _executableInfo.WindowsDefaultDirName, executableName);
                    if (File.Exists(defaultPath))
                    {
                        candidates.Add(defaultPath);
                        continue; // Skip generic search if we found it in the default location
                    }
                }
                
                // Fallback: Look for the executable in subdirectories
                try
                {
                    foreach (var subDir in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                    {
                        var executablePath = IOPath.Combine(subDir, executableName);
                        if (File.Exists(executablePath))
                        {
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
            
            return candidates;
        }

        /// <summary>
        /// Determines if the given path is a valid candidate executable for Windows.
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