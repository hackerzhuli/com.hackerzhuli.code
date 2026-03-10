using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Hackerzhuli.Code.Editor.Code;
using Hackerzhuli.Code.Editor.ProjectGeneration;
using IOPath = System.IO.Path;

namespace Hackerzhuli.Code.Editor.Zed
{
    /// <summary>
    ///     Zed application information for discovery.
    /// </summary>
    internal class ZedAppInfo : IAppInfo
    {
        public string WindowsExeName => "Zed.exe";
        public string MacAppName => "Zed.app";
        public string LinuxExeName => "zed";
        public string WindowsDefaultDirName => "Zed";
    }

    /// <summary>
    ///     Represents a Zed installation on the system.
    ///     Provides functionality for discovering and interacting with Zed.
    /// </summary>
    internal class ZedInstallation : CodeEditorInstallation
    {
        private static readonly IGenerator _generator = GeneratorFactory.GetInstance(GeneratorStyle.SDK);
        private static readonly IAppDiscover _discoverer = AppDiscoverUtils.CreateAppDiscover(new ZedAppInfo());
        private static readonly ZedFilePatcher _filePatcher = new();

        public override bool SupportsAnalyzers => false;

        public override Version LatestLanguageVersionSupported => new Version(11, 0);

        public override IGenerator ProjectGenerator => _generator;

        public override string[] GetAnalyzers()
        {
            return Array.Empty<string>();
        }

        public static bool TryDiscoverInstallation(string exePath, out ICodeEditorInstallation installation)
        {
            installation = null;

            if (string.IsNullOrEmpty(exePath))
                return false;

            if (!_discoverer.IsCandidate(exePath))
                return false;

            var version = GetVersionFromExe(exePath);
            var name = version != null ? $"Zed [{version.ToString(3)}]" : "Zed";

            installation = new ZedInstallation
            {
                Name = name,
                Path = exePath,
                Version = version ?? new Version()
            };

            return true;
        }

        private static Version GetVersionFromExe(string exePath)
        {
            try
            {
                // Note: the code may not work on non windows os, to be tested
                var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                var productVersion = versionInfo.ProductVersion;

                if (!string.IsNullOrEmpty(productVersion))
                {
                    // ProductVersion may contain additional text, try to parse the version part
                    // e.g., "0.123.4" or "0.123.4-preview" -> extract "0.123.4"
                    var versionParts = productVersion.Split('-', ' ', '+')[0];
                    if (Version.TryParse(versionParts, out var version))
                        return version;
                }

                // Fallback to FileVersion if ProductVersion is not available
                var fileVersion = versionInfo.FileVersion;
                if (!string.IsNullOrEmpty(fileVersion))
                {
                    var versionParts = fileVersion.Split('-', ' ', '+')[0];
                    if (Version.TryParse(versionParts, out var version))
                        return version;
                }
            }
            catch (Exception)
            {
                // do not fail if we are not able to retrieve the exact version number
            }

            return null;
        }

        public static IEnumerable<ICodeEditorInstallation> GetInstallations()
        {
            var candidates = _discoverer.GetCandidatePaths();

            foreach (var candidate in candidates)
                if (TryDiscoverInstallation(candidate, out var installation))
                    yield return installation;
        }

        public override void CreateExtraFiles(string projectDirectory)
        {
            _filePatcher.CreateOrPatchFiles(projectDirectory);
        }

        public override bool Open(string path, int line, int column, string solution)
        {
            var cliPath = GetCliPath();

            line = Math.Max(1, line);
            column = Math.Max(0, column);

            var directory = IOPath.GetDirectoryName(solution);

            var filePath = string.IsNullOrEmpty(path) ? string.Empty : FileUtility.GetAbsolutePath(path);

            var arguments = string.IsNullOrEmpty(filePath)
                ? $"\"{directory}\""
                : $"\"{directory}\" \"{filePath}:{line}:{column}\"";

#if HACKERZHULI_CODE_DEBUG
            UnityEngine.Debug.Log($"Opening Zed with arguments: {arguments}");
#endif
            ProcessRunner.Start(ProcessStartInfoFor(cliPath, arguments));

            return true;
        }

        private string GetCliPath()
        {
            var exeDir = IOPath.GetDirectoryName(Path);
            var cliName = GetCliNameForPlatform();
            var cliPath = FileUtility.GetAbsolutePath(IOPath.Combine(exeDir, "bin", cliName));

            // Fallback to original path if CLI tool doesn't exist
            if (!File.Exists(cliPath))
            {
#if HACKERZHULI_CODE_DEBUG
                UnityEngine.Debug.LogWarning($"Zed CLI tool not found at: {cliPath}, falling back to: {Path}");
#endif
                return Path;
            }

            return cliPath;
        }

        private static string GetCliNameForPlatform()
        {
#if UNITY_EDITOR_WIN
            return "zed.exe";
#elif UNITY_EDITOR_OSX
            return "zed";
#elif UNITY_EDITOR_LINUX
            return "zed";
#else
            return "zed";
#endif
        }

        private static ProcessStartInfo ProcessStartInfoFor(string exePath, string arguments)
        {
#if UNITY_EDITOR_OSX
            // wrap with built-in OSX open feature
            arguments = $"-n \"{exePath}\" --args {arguments}";
            var application = "open";
            return ProcessRunner.ProcessStartInfoFor(application, arguments, redirect: false, shell: true);
#else
            return ProcessRunner.ProcessStartInfoFor(exePath, arguments, false);
#endif
        } 

        public static void Initialize()
        {
        }
    }
}
