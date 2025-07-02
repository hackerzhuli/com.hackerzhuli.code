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
    /// macOS-specific implementation for discovering application installations.
    /// </summary>
    internal class MacOSAppDiscover : IAppDiscover
    {
        private readonly IAppInfo _executableInfo;
        
        /// <summary>
        /// Initializes a new instance of the MacOSAppDiscover class.
        /// </summary>
        /// <param name="executableInfo">The executable information to search for.</param>
        public MacOSAppDiscover(IAppInfo executableInfo)
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
            var appName = _executableInfo.MacAppName;
            
            if (string.IsNullOrEmpty(appName))
                return candidates;
            
            // Extract the executable name without .app extension for use inside the bundle
            var executableName = appName.EndsWith(".app") ? appName[..^4] : appName;
            
            // Common macOS application directories
            var candidateDirs = new[]
            {
                "/Applications",
                "/System/Applications",
                "/usr/local/bin",
                "/opt/homebrew/bin",
                IOPath.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Applications")
            };
            
            foreach (var dir in candidateDirs)
            {
                if (!Directory.Exists(dir)) continue;
                
                try
                {
                    // Look for the specific .app bundle
                    var appBundlePath = IOPath.Combine(dir, appName);
                    if (Directory.Exists(appBundlePath))
                    {
                        var contentsDir = IOPath.Combine(appBundlePath, "Contents", "MacOS");
                        if (Directory.Exists(contentsDir))
                        {
                            // Check if there's any executable file in the MacOS directory
                            // Don't assume the executable name matches the app name
                            var files = Directory.GetFiles(contentsDir);
                            bool hasExecutable = false;
                            foreach (var file in files)
                            {
                                // Check if the file is likely an executable (not a library or other file)
                                var fileName = IOPath.GetFileName(file);
                                if (!fileName.StartsWith(".") && !fileName.EndsWith(".dylib") && !fileName.EndsWith(".so"))
                                {
                                    hasExecutable = true;
                                    break;
                                }
                            }
                            if (hasExecutable)
                            {
                                candidates.Add(appBundlePath); // Return the .app bundle path, not the internal executable
                            }
                        }
                    }
                    
                    // Also check for direct executables in bin directories
                    if (dir.EndsWith("bin"))
                    {
                        var executablePath = IOPath.Combine(dir, executableName);
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
        /// Determines if the given path is a valid candidate executable for macOS.
        /// </summary>
        /// <param name="exePath">The path to check.</param>
        /// <returns>True if the path is a valid candidate; otherwise, false.</returns>
        public bool IsCandidate(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
                return false;

            // For macOS, check if it's a .app bundle that exists
            if (exePath.EndsWith(_executableInfo.MacAppName, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(exePath))
                {
                    // Verify it's a valid .app bundle with executable files in Contents/MacOS
                     var contentsDir = IOPath.Combine(exePath, "Contents", "MacOS");
                     if (Directory.Exists(contentsDir))
                     {
                         var files = Directory.GetFiles(contentsDir);
                         foreach (var file in files)
                         {
                             // Check if the file is likely an executable (not a library or other file)
                             var fileName = IOPath.GetFileName(file);
                             if (!fileName.StartsWith(".") && !fileName.EndsWith(".dylib") && !fileName.EndsWith(".so"))
                             {
                                 return true;
                             }
                         }
                     }
                }
                return false;
            }

            // Also check for direct executables (for bin directories)
            var executableName = _executableInfo.MacAppName.EndsWith(".app") 
                ? _executableInfo.MacAppName[..^4] 
                : _executableInfo.MacAppName;
            
            return File.Exists(exePath) && exePath.EndsWith(executableName, StringComparison.OrdinalIgnoreCase);
        }
    }
}