using System;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    /// A file-based logger that writes to Library/UnityCode directory.<br/>
    /// Provides static methods similar to Unity's Debug.Log<br/>
    /// Mainly for debugging purposes, but users will not see our logs in Unity Console. <br/>
    /// Thread-safe and can be used from any thread. <br/>
    /// It is low performance, so it is disabled when it's installed by a user.
    /// </summary>
    [InitializeOnLoad]
    public class FileLogger : ScriptableObject
    {
        private static FileLogger _instance;
        private static readonly object _instanceLock = new object();
        private readonly object _fileLock = new object();
        private string _logDirectory;
        private string _logFilePath;
            
        /// <summary>
        /// Gets the singleton instance of the FileLogger.
        /// Must be explicitly initialized from the main thread first.
        /// </summary>
        private static FileLogger Instance
        {
            get
            {
                return _instance;
            }
        }

        /// <summary>
        /// Initializes the FileLogger instance. Must be called from the main thread.
        /// This method handles domain reloads and should be called during Unity initialization.
        /// </summary>
        public static void Initialize()
        {
            if (_instance != null)
            {
                return; // Already initialized
            }
            
            lock (_instanceLock)
            {
                if (_instance != null)
                {
                    return; // Double-check after acquiring lock
                }
                
                // Try to find existing instance first (handles domain reload)
                var existingInstance = Resources.FindObjectsOfTypeAll<FileLogger>().FirstOrDefault();
                if (existingInstance != null)
                {
                    _instance = existingInstance;
                    return;
                }
                
                // Create new instance if none exists
                _instance = CreateInstance<FileLogger>();
                _instance.hideFlags = HideFlags.HideAndDontSave; // Don't save to scene or show in inspector
                _instance.InitializeInternal();
            }
        }
        
        /// <summary>
        /// Initializes the logger and sets up the log directory.
        /// </summary>
        private void InitializeInternal()
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
        /// Writes a log entry to the file in a thread-safe manner.
        /// </summary>
        /// <param name="level">The log level (Info, Warning, Error, etc.)</param>
        /// <param name="message">The log message</param>
        /// <param name="context">Optional context object</param>
        private void WriteLog(string level, object message, UnityEngine.Object context = null)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string contextInfo = context != null ? $" [{context.name}]" : "";
                string threadInfo = $" [Thread:{Thread.CurrentThread.ManagedThreadId}]";
                string logEntry = $"[{timestamp}] [{level}]{contextInfo}{threadInfo}: {message}\n";
                
                lock (_fileLock)
                {
                    File.AppendAllText(_logFilePath, logEntry);
                }
            }
            catch
            {
                // Silently ignore file logging failures
                // note that our log methods can be called from a Unity log callback
                // so we can't log to Unity console here, that can be recursive
            }
        }
        
        /// <summary>
        /// Logs an info message to the file.
        /// </summary>
        /// <param name="message">The message to log</param>
        [Conditional("HACKERZHULI_CODE_DEBUG")]
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
        [Conditional("HACKERZHULI_CODE_DEBUG")]
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
        [Conditional("HACKERZHULI_CODE_DEBUG")]
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