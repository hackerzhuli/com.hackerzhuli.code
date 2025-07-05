/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    ///     Static utility class for platform-specific path operations.
    /// </summary>
    internal static class PlatformPathUtility
    {
        /// <summary>
        ///     Gets the real path by resolving symbolic links or shortcuts.
        /// </summary>
        /// <param name="path">The path that might be a symbolic link.</param>
        /// <returns>The resolved path if it's a symbolic link; otherwise, the original path.</returns>
        public static string GetRealPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

#if UNITY_EDITOR_WIN
            return path;
#elif UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            // On Unix-like systems, resolve symbolic links
            try
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Exists && fileInfo.LinkTarget != null)
                {
                    return fileInfo.LinkTarget;
                }
            }
            catch
            {
                // If we can't resolve, return original path
            }
            return path;
#else
            return path;
#endif
        }
    }
}