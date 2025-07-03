/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Collections.Generic;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    /// Represents application information used to discover it on the system
    /// </summary>
    public interface IAppInfo
    {
        /// <summary>
        /// The executable name on Windows (with .exe extension).
        /// </summary>
        string WindowsExeName { get; }

        /// <summary>
        /// The app name on macOS (with .app extension).
        /// </summary>
        string MacAppName { get; }

        /// <summary>
        /// The executable name on Linux.
        /// </summary>
        string LinuxExeName { get; }
        
        /// <summary>
        /// The default directory name on Windows for installation detection.
        /// </summary>
        string WindowsDefaultDirName { get; }
        
        /// <summary>
        /// Gets the actual executable name for the current platform.
        /// </summary>
        /// <returns>The executable name for the current platform.</returns>
        string GetActualExeName()
        {
#if UNITY_EDITOR_WIN
            return WindowsExeName;
#elif UNITY_EDITOR_OSX
            return MacAppName;
#elif UNITY_EDITOR_LINUX
            return LinuxExeName;
#else
            return WindowsExeName; // Default fallback
#endif
        }
    }

    /// <summary>
    /// Interface for application discovery.
    /// </summary>
    internal interface IAppDiscover
    {
        /// <summary>
        /// Gets candidate executable paths for the configured executable.
        /// </summary>
        /// <returns>A list of candidate executable paths.</returns>
        List<string> GetCandidatePaths();

        /// <summary>
        /// Determines if the given path is a valid candidate executable.
        /// </summary>
        /// <param name="exePath">The path to check.</param>
        /// <returns>True if the path is a valid candidate; otherwise, false.</returns>
        bool IsCandidate(string exePath);
    }

    internal static class AppDiscoverUtils{
        /// <summary>
        /// Creates an appropriate IAppDiscover instance based on the current platform.
        /// </summary>
        /// <returns>An IAppDiscover instance for the current platform.</returns>
        public static IAppDiscover CreateAppDiscover(IAppInfo appInfo)
		{
			#if UNITY_EDITOR_WIN
			return new WindowsAppDiscover(appInfo);
			#elif UNITY_EDITOR_OSX
			return new MacOSAppDiscover(appInfo);
			#else
			return new LinuxAppDiscover(appInfo);
			#endif
		}
    }
}
