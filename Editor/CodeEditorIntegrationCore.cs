/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Hackerzhuli.Code.Editor.Messaging;
using Hackerzhuli.Code.Editor.Testing;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Debug = UnityEngine.Debug;
using MessageType = Hackerzhuli.Code.Editor.Messaging.MessageType;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>
    ///     Represents a connected IDE client with endpoint information and connection tracking.
    /// </summary>
    [Serializable]
    public class CodeEditorClient : ISerializationCallbackReceiver
    {
        [SerializeField] private string _address;
        [SerializeField] private int _port;
        [NonSerialized] private double _elapsedTime; // don't serialize, reset when we finish domain reload

        [NonSerialized] private IPEndPoint _endPoint;

        /// <summary>
        ///     The network endpoint of the connected IDE client.
        /// </summary>
        public IPEndPoint EndPoint
        {
            get => _endPoint;
            set => _endPoint = value;
        }

        /// <summary>
        ///     The elapsed time since the last communication with this client.
        /// </summary>
        public double ElapsedTime
        {
            get => _elapsedTime;
            set => _elapsedTime = value;
        }

        public void OnBeforeSerialize()
        {
            // Convert IPEndPoint to serializable fields before serialization
            if (_endPoint != null)
            {
                _address = _endPoint.Address.ToString();
                _port = _endPoint.Port;
            }
        }

        public void OnAfterDeserialize()
        {
            // Reconstruct IPEndPoint from serialized fields after deserialization
            if (!string.IsNullOrEmpty(_address))
                try
                {
                    _endPoint = new IPEndPoint(IPAddress.Parse(_address), _port);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to deserialize client endpoint {_address}:{_port}: {ex.Message}");
                }
        }
    }

    /// <summary>
    ///     Core implementation of Visual Studio integration functionality.
    ///     This class inherits from ScriptableObject instead of being a static class to leverage Unity's
    ///     built-in lifecycle management system. Key advantages:
    ///     1. **Unity Lifecycle Integration**: OnEnable/OnDisable are called automatically during
    ///     domain reloads, providing reliable initialization and cleanup,
    ///     without relying on events (like AppDomain.DomainUnload) that can trigger not from the main thread,
    ///     which could make it really complex to get right
    ///     2. **State Preservation**: ScriptableObject provides built-in serialization for
    ///     persistent state across domain reloads(after compilation or before enter play mode)
    ///     allowing us to keep the clients and other state preserved.
    /// </summary>
    /// <remarks>
    ///     **Behavior During Unity Main Thread Blocking:**
    ///     When Unity's main thread is blocked for extended periods (compilation, asset data refresh, importing assets,
    ///     updating packages, etc.),
    ///     this system handles messages and client connections as follows:
    ///     **Message Handling:**
    ///     - Incoming messages are queued by the underlying messaging system during blocking periods
    ///     - When Update() resumes, all queued messages are processed in sequence via TryDequeueMessage()
    ///     - No messages are lost during blocking periods, ensuring reliable communication
    ///     **Client Connection Management:**
    ///     - Client timeout tracking is paused during blocking periods (deltaTime is clamped to 0.1s max)
    ///     - Prevents false client disconnections due to long blocking periods
    ///     - Client state is preserved across domain reloads through ScriptableObject serialization
    ///     **Post-Blocking Recovery:**
    ///     - All pending messages are processed immediately when Update() resumes
    ///     - Client connections remain stable and responsive after blocking periods
    ///     - No manual reconnection required from IDE clients
    /// </remarks>
    [CreateAssetMenu(fileName = "VisualStudioIntegrationCore", menuName = "Visual Studio/Integration Core")]
    internal class CodeEditorIntegrationCore : ScriptableObject
    {
        [SerializeField] private double _lastUpdateTime;
        [SerializeField] private List<CodeEditorClient> _clients = new();
        
        [NonSerialized] private List<IPEndPoint> _refreshRequesters = new();
        [NonSerialized] private List<Log> _compileErrors = new();
        [NonSerialized] private double _compilationFinishedTime;

        [NonSerialized] private Messenger _messenger;
        [NonSerialized] private bool _needsOnlineNotification;

        private void OnEnable()
        {
            //Debug.Log("OnEnable");
            CheckLegacyAssemblies();

            // Subscribe to Unity events
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            Application.logMessageReceived += OnLogMessageReceived;

            // Flag that we need to notify clients that we're online
            _needsOnlineNotification = true;
        }

        private void OnDisable()
        {
            //Debug.Log("OnDisable");
            // Unsubscribe from Unity events
            EditorApplication.update -= Update;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            Application.logMessageReceived -= OnLogMessageReceived;

            // Notify clients that Unity is going offline before disposing the messenger
            if (_messenger != null)
            {
                // Send offline notification with blocking method
                BroadcastMessageBlocking(MessageType.Offline, "", 500);
                //Debug.Log("disposing messenger and release socket resources");
                _messenger.Dispose();
                _messenger = null;
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    BroadcastMessage(MessageType.IsPlaying, "true");
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    BroadcastMessage(MessageType.IsPlaying, "false");
                    break;
            }
        }

        /// <summary>
        ///     Updates the integration core by processing incoming messages and managing client timeouts.
        ///     This method should be called regularly from Unity's update loop to maintain communication
        ///     with connected IDE clients and handle message processing.
        /// </summary>
        public void Update()
        {
            EnsureMessengerInitialized();

            if (_needsOnlineNotification)
            {
                _needsOnlineNotification = false;
                BroadcastMessage(MessageType.Online, "");
                // Send current play mode state to newly connected clients
                BroadcastMessage(MessageType.IsPlaying, EditorApplication.isPlaying ? "true" : "false");
            }

            // Process messages from the queue on the main thread
            if (_messenger != null)
                while (_messenger.TryDequeueMessage(out var message))
                    ProcessIncoming(message);

            var currentTime = EditorApplication.timeSinceStartup;
            var deltaTime = currentTime - _lastUpdateTime;

            var clampedDeltaTime = Math.Min(deltaTime, 0.1);
            _lastUpdateTime = currentTime;
            for (var i = _clients.Count - 1; i >= 0; i--)
            {
                var client = _clients[i];
                client.ElapsedTime += clampedDeltaTime;
                if (client.ElapsedTime > 4)
                    _clients.RemoveAt(i);
            }

            // Handle refresh (blocking) at the end of update to allow other messages to be processed first
            if (_refreshRequesters.Count > 0)
            {
                // Copy requesters to array and clear the list before refresh to prevent duplicate refreshes
                var requestersToNotify = _refreshRequesters.ToArray();
                _refreshRequesters.Clear();
                
                var refreshResult = RefreshAssetDatabase();
                
                // Notify all clients that requested the refresh with the result
                foreach (var requester in requestersToNotify)
                {
                    Answer(requester, MessageType.Refresh, refreshResult);
                }
            }
        }

        private void OnCompilationStarted(object obj)
        {
            // Clear any existing compile errors when compilation starts
            _compileErrors.Clear();
            BroadcastMessage(MessageType.CompilationStarted, "");
        }

        private void OnCompilationFinished(object obj)
        {
            //Debug.Log("OnAssemblyReload");
            // need to ensure messenger is initialized, because assembly reload event can happen before first Update
            EnsureMessengerInitialized();
            BroadcastMessage(MessageType.CompilationFinished, "");
            _compilationFinishedTime = EditorApplication.timeSinceStartup;
        }

        private string GetPackageVersion()
        {
            var package = PackageInfo.FindForAssembly(typeof(CodeEditorIntegration).Assembly);
            return package.version;
        }

        private string GetPackageName()
        {
            var package = PackageInfo.FindForAssembly(typeof(CodeEditorIntegration).Assembly);
            return package.name;
        }

        /// <summary>
        ///     Gets the collected compile errors as a JSON string.
        /// </summary>
        /// <returns>JSON serialized array of Log objects.</returns>
        private string GetCompileErrorsJson()
        {
            try
            {
                return JsonUtility.ToJson(new LogContainer { Logs = _compileErrors.ToArray() });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to serialize compile errors: {ex.Message}");
                return "{\"Logs\":[]}";
            }
        }

        /// <summary>
        ///     Broadcasts a message of the specified type and value to all connected IDE clients.
        /// </summary>
        /// <param name="type">The type of message to broadcast.</param>
        /// <param name="value">The message content to send to all clients.</param>
        public void BroadcastMessage(MessageType type, string value)
        {
            foreach (var client in _clients) Answer(client.EndPoint, type, value);
        }

        /// <summary>
        ///     Broadcasts a message with blocking UDP send and timeout to all connected IDE clients.
        ///     Used for critical notifications like offline status before messenger disposal.
        /// </summary>
        /// <param name="type">The type of message to broadcast.</param>
        /// <param name="value">The message content to send to all clients.</param>
        /// <param name="timeoutMs">Timeout in milliseconds for the blocking send operation.</param>
        private void BroadcastMessageBlocking(MessageType type, string value, int timeoutMs)
        {
            if (_messenger == null)
                return;

            foreach (var client in _clients) _messenger.SendMessageBlocking(client.EndPoint, type, value, timeoutMs);
        }

        private void CheckLegacyAssemblies()
        {
            var checkList = new HashSet<string>(new[]
                { KnownAssemblies.UnityVS, KnownAssemblies.Messaging, KnownAssemblies.Bridge });

            try
            {
                var assemblies = AppDomain
                    .CurrentDomain
                    .GetAssemblies()
                    .Where(a => checkList.Contains(a.GetName().Name));

                foreach (var assembly in assemblies)
                {
                    // for now we only want to warn against local assemblies, do not check externals.
                    var relativePath = FileUtility.MakeRelativeToProjectPath(assembly.Location);
                    if (relativePath == null)
                        continue;

                    Debug.LogWarning(
                        $"Project contains legacy assembly that could interfere with the Visual Studio Package. You should delete {relativePath}");
                }
            }
            catch (Exception)
            {
                // abandon legacy check
            }
        }

        private int MessagingPort()
        {
            return 58000 + Process.GetCurrentProcess().Id % 1000;
        }

        /// <summary>
        ///     Refresh the asset database.
        /// </summary>
        /// <returns>
        ///     An error message if refresh didn't start, otherwise an empty string.
        /// </returns>
        private string RefreshAssetDatabase()
        {
            // Handle auto-refresh based on kAutoRefreshMode: 0=disabled, 1=enabled, 2=enabled outside play mode
            // var autoRefreshMode = EditorPrefs.GetInt("kAutoRefreshMode", 1);

            // if we are playing, we don't force a refresh
            // We will ignore the setting `autoRefreshMode` because this is not auto refresh, this is refresh requested explicitly
            if (EditorApplication.isPlaying)
                return "Refresh not started: Unity is in play mode";
            
            if (UnityInstallation.IsInSafeMode)
                return "Refresh not started: Unity is in safe mode";
            
            AssetDatabase.Refresh();
            return ""; // Empty string indicates successful refresh
        }

        private void ProcessIncoming(Message message)
        {
            CheckClient(message);

            switch (message.Type)
            {
                case MessageType.Ping:
                    Answer(message, MessageType.Pong);
                    break;
                case MessageType.Play:
                    EditorApplication.isPlaying = true;
                    break;
                case MessageType.Stop:
                    EditorApplication.isPlaying = false;
                    break;
                case MessageType.Pause:
                    EditorApplication.isPaused = true;
                    break;
                case MessageType.Unpause:
                    EditorApplication.isPaused = false;
                    break;
                case MessageType.Refresh:
                    if (!_refreshRequesters.Contains(message.Origin))
                        _refreshRequesters.Add(message.Origin);
                    break;
                case MessageType.Version:
                    Answer(message, MessageType.Version, GetPackageVersion());
                    break;
                case MessageType.ProjectPath:
                    Answer(message, MessageType.ProjectPath,
                        Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
                    break;
                case MessageType.ExecuteTests:
                    TestRunnerApiListener.ExecuteTests(message.Value);
                    Answer(message, MessageType.ExecuteTests);
                    break;
                case MessageType.RetrieveTestList:
                    TestRunnerApiListener.RetrieveTestList(message.Value, (mode, testAdaptor) => 
                    {
                        var value = TestRunnerCallbacks.SerializeTestListRetrievedValue(mode, testAdaptor);
                        Answer(message, MessageType.TestListRetrieved, value);
                    });
                    break;
                case MessageType.ShowUsage:
                    UsageUtility.ShowUsage(message.Value);
                    break;
                case MessageType.PackageName:
                    Answer(message, MessageType.PackageName, GetPackageName());
                    break;
                case MessageType.GetCompileErrors:
                    Answer(message, MessageType.GetCompileErrors, GetCompileErrorsJson());
                    break;
            }
        }

        private void CheckClient(Message message)
        {
            var endPoint = message.Origin;

            var client = _clients.FirstOrDefault(c => c.EndPoint.Equals(endPoint));
            if (client == null)
            {
                client = new CodeEditorClient
                {
                    EndPoint = endPoint,
                    ElapsedTime = 0
                };

                _clients.Add(client);

                // Send current play mode state to new client
                Answer(endPoint, MessageType.IsPlaying, EditorApplication.isPlaying ? "true" : "false");
            }
            else
            {
                client.ElapsedTime = 0;
            }
        }

        private void Answer(Message message, MessageType answerType, string answerValue = "")
        {
            var targetEndPoint = message.Origin;

            Answer(
                targetEndPoint,
                answerType,
                answerValue);
        }

        private void Answer(IPEndPoint targetEndPoint, MessageType answerType, string answerValue)
        {
            _messenger?.SendMessage(targetEndPoint, answerType, answerValue);
        }

        /// <summary>
        ///     Handles Unity log messages and broadcasts them to all connected IDE clients.
        /// </summary>
        /// <param name="logString">The log message content.</param>
        /// <param name="stackTrace">The stack trace associated with the log message.</param>
        /// <param name="type">The type of log message (Log, Warning, Error, etc.).</param>
        private void OnLogMessageReceived(string logString, string stackTrace, LogType type)
        {
            // Collect compile errors during the collection window
            if (EditorApplication.timeSinceStartup - _compilationFinishedTime < 1.0 && type == LogType.Error && logString.Contains("error CS"))
            {
                var unixTimestamp = ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds();
                var compileError = new Log(logString, stackTrace, unixTimestamp);
                _compileErrors.Add(compileError);
            }

            var messageType = type switch
            {
                LogType.Error or LogType.Exception or LogType.Assert => MessageType.Error,
                LogType.Warning => MessageType.Warning,
                _ => MessageType.Info
            };

            // Create a formatted message that includes both the log content and stack trace if available

            var message = string.IsNullOrEmpty(stackTrace) ? logString : $"{logString}\n{stackTrace}";

            BroadcastMessage(messageType, message);
        }

        private void EnsureMessengerInitialized()
        {
            if (_messenger != null || !CodeEditor.IsEnabled)
                return;

            var messagingPort = MessagingPort();
            try
            {
                _messenger = Messenger.BindTo(messagingPort);
            }
            catch (SocketException)
            {
                Debug.LogWarning(
                    $"Unable to use UDP port {messagingPort} for VS/Unity messaging. You should check if another process is already bound to this port or if your firewall settings are compatible.");
            }
        }
    }
}