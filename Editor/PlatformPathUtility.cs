using System.IO;
using System.Runtime.InteropServices;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    ///     Static utility class for platform-specific path operations.
    /// </summary>
    internal static class PlatformPathUtility
    {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
        [DllImport("libc", EntryPoint = "realpath", CharSet = CharSet.Ansi)]
        static extern System.IntPtr unix_realpath(string path, System.IntPtr resolved);
#endif
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
            // On Unix-like systems, resolve symbolic links via libc realpath().
            // FileInfo.LinkTarget is .NET 6+ only; Unity uses .NET Standard 2.1.
            try
            {
                var ptr = unix_realpath(path, System.IntPtr.Zero);
                if (ptr != System.IntPtr.Zero)
                {
                    var resolved = Marshal.PtrToStringAnsi(ptr);
                    Marshal.FreeHGlobal(ptr);
                    if (!string.IsNullOrEmpty(resolved))
                        return resolved;
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