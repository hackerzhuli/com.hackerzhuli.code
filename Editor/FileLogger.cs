using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    /// A file-based logger that writes to Library/UnityCode directory.<br/>
    /// Provides static methods similar to Unity's Debug.Log<br/>
    /// Mainly for debugging purposes, but users will not see our logs in Unity Console. <br/>
    /// Should only be used in the main thread. <br/>
    /// </summary>
    public class FileLogger : ScriptableObject
    {
        private static FileLogger _instance;
        private string _logDirectory;
        private string _logFilePath;
        
        /// <summary>
        /// Gets the singleton instance of the FileLogger.
        /// </summary>
        private static FileLogger Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = GetOrCreateInstance();
                }
                return _instance;
            }
        }
        
        /// <summary>
        /// Gets or creates the FileLogger instance, handling domain reloads.
        /// </summary>
        private static FileLogger GetOrCreateInstance()
        {
            // Try to find existing instance first (handles domain reload)
            var existingInstance = Resources.FindObjectsOfTypeAll<FileLogger>().FirstOrDefault();
            if (existingInstance != null)
            {
                return existingInstance;
            }
            
            // Create new instance if none exists
            var instance = CreateInstance<FileLogger>();
            instance.hideFlags = HideFlags.HideAndDontSave; // Don't save to scene or show in inspector
            instance.Initialize();
            return instance;
        }
        
        /// <summary>
        /// Initializes the logger and sets up the log directory.
        /// </summary>
        private void Initialize()
        {
            _logDirectory = Path.Combine(Application.dataPath, "..", "Library", "UnityCode");
            
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
            
            _logFilePath = Path.Combine(_logDirectory, "code.log");
            
            // Start with an empty file
            File.WriteAllText(_logFilePath, string.Empty);
        }
        
        /// <summary>
        /// Writes a log entry to the file.
        /// </summary>
        /// <param name="level">The log level (Info, Warning, Error, etc.)</param>
        /// <param name="message">The log message</param>
        /// <param name="context">Optional context object</param>
        private void WriteLog(string level, object message, UnityEngine.Object context = null)
        {
            try
            {
                var instance = Instance; // Ensure initialization
                
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string contextInfo = context != null ? $" [{context.name}]" : "";
                string logEntry = $"[{timestamp}] [{level}]{contextInfo}: {message}\n";
                
                File.AppendAllText(_logFilePath, logEntry);
            }
            catch
            {
                // Silently ignore file logging failures
            }
        }
        
        /// <summary>
        /// Logs an info message to the file.
        /// </summary>
        /// <param name="message">The message to log</param>
        public static void Log(object message)
        {
            if (Instance == null)
            {
                return;
            }
            Instance.WriteLog("INFO", message);
        }
        
        /// <summary>
        /// Logs a warning message to the file.
        /// </summary>
        /// <param name="message">The warning message to log</param>
        public static void LogWarning(object message)
        {
            if (Instance == null)
            {
                return;
            }
            Instance.WriteLog("WARNING", message);
        }
        
        /// <summary>
        /// Logs an error message to the file.
        /// </summary>
        /// <param name="message">The error message to log</param>
        public static void LogError(object message)
        {
            if (Instance == null)
            {
                return;
            }
            Instance.WriteLog("ERROR", message);
        }
    }
}