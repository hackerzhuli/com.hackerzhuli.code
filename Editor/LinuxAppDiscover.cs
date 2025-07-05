using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using IOPath = System.IO.Path;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    ///     Linux-specific implementation for discovering application installations.
    /// </summary>
    internal class LinuxAppDiscover : IAppDiscover
    {
        /// <summary>
        ///     Regular expression for extracting the executable path from Linux desktop files.
        /// </summary>
        private static readonly Regex DesktopFileExecEntry =
            new(@"Exec=(\S+)", RegexOptions.Singleline | RegexOptions.Compiled);

        private readonly IAppInfo _executableInfo;

        /// <summary>
        ///     Initializes a new instance of the LinuxAppDiscover class.
        /// </summary>
        /// <param name="executableInfo">The executable information to search for.</param>
        public LinuxAppDiscover(IAppInfo executableInfo)
        {
            _executableInfo = executableInfo ?? throw new ArgumentNullException(nameof(executableInfo));
        }

        /// <summary>
        ///     Gets candidate executable paths for the configured executable.
        /// </summary>
        /// <returns>A list of potential executable paths.</returns>
        public List<string> GetCandidatePaths()
        {
            var candidates = new List<string>();
            var executableName = _executableInfo.LinuxExeName;

            if (string.IsNullOrEmpty(executableName))
                return candidates;

            // Check common Linux executable directories
            var executableDirs = new[]
            {
                "/usr/bin",
                "/usr/local/bin",
                "/opt",
                "/snap/bin",
                "/bin",
                IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin")
            };

            foreach (var dir in executableDirs)
            {
                if (!Directory.Exists(dir)) continue;

                try
                {
                    var executablePath = IOPath.Combine(dir, executableName);
                    if (File.Exists(executablePath)) candidates.Add(executablePath);

                    // For /opt, also check subdirectories
                    if (dir == "/opt")
                        foreach (var subDir in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                        {
                            var subDirExecutablePath = IOPath.Combine(subDir, "bin", executableName);
                            if (File.Exists(subDirExecutablePath)) candidates.Add(subDirExecutablePath);
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

            // Also check XDG desktop entries
            candidates.AddRange(GetXdgCandidates(executableName));

            return candidates;
        }

        /// <summary>
        ///     Determines if the given path is a valid candidate executable for Linux.
        /// </summary>
        /// <param name="exePath">The path to check.</param>
        /// <returns>True if the path is a valid candidate; otherwise, false.</returns>
        public bool IsCandidate(string exePath)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return false;

            // Check if the path ends with the expected executable name or .desktop file
            return exePath.EndsWith(_executableInfo.LinuxExeName) ||
                   exePath.EndsWith($"{_executableInfo.LinuxExeName}.desktop");
        }

        /// <summary>
        ///     Gets candidate executable paths from XDG data directories on Linux.
        /// </summary>
        /// <param name="executableName">The name of the executable to search for.</param>
        /// <returns>A list of potential executable paths.</returns>
        private List<string> GetXdgCandidates(string executableName)
        {
            var candidates = new List<string>();
            var envdirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
            if (string.IsNullOrEmpty(envdirs))
                return candidates;

            var dirs = envdirs.Split(':');
            foreach (var dir in dirs)
            {
                if (string.IsNullOrEmpty(executableName))
                    continue;

                Match match = null;
                var desktopFileName = $"{executableName}.desktop";

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

                if (match != null && match.Success) candidates.Add(match.Groups[1].Value);
            }

            return candidates;
        }
    }
}