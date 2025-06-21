/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Unity.VisualStudio.Editor.Messaging;
using Microsoft.Unity.VisualStudio.Editor.Testing;
using UnityEditor;
using UnityEngine;
using MessageType = Microsoft.Unity.VisualStudio.Editor.Messaging.MessageType;

namespace Microsoft.Unity.VisualStudio.Editor
{
    /// <summary>
    /// Represents a connected IDE client with endpoint information and connection tracking.
    /// </summary>
    [Serializable]
    public class IdeClient : ISerializationCallbackReceiver
    {
        [SerializeField] private string _address;
        [SerializeField] private int _port;
        [SerializeField] private double _elapsedTime;

        [System.NonSerialized] private IPEndPoint _endPoint;

        /// <summary>
        /// The network endpoint of the connected IDE client.
        /// </summary>
        public IPEndPoint EndPoint 
        { 
            get => _endPoint; 
            set 
            { 
                _endPoint = value;
            } 
        }

        /// <summary>
        /// The elapsed time since the last communication with this client.
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
            {
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
    }

    /// <summary>
    /// Core implementation of Visual Studio integration functionality.
    /// 
    /// This class inherits from ScriptableObject instead of being a static class to leverage Unity's
    /// built-in lifecycle management system. Key advantages:
    /// 
    /// 1. **Unity Lifecycle Integration**: OnEnable/OnDisable are called automatically during
    ///    domain reloads, providing reliable initialization and cleanup,
    ///    without relying on events (like AppDomain.DomainUnload) that can trigger not from the main thread,
    ///    which could make it really complex to get right
    /// 
    /// 2. **State Preservation**: ScriptableObject provides built-in serialization for
    ///    persistent state across domain reloads(after compilation or before enter play mode)
    ///    allowing us to keep the clients and other state preserved.
    /// </summary>
    /// <remarks>
    /// **Behavior During Unity Main Thread Blocking:**
    /// 
    /// When Unity's main thread is blocked for extended periods (compilation, asset data refresh, importing assets, updating packages, etc.),
    /// this system handles messages and client connections as follows:
    /// 
    /// **Message Handling:**
    /// - Incoming messages are queued by the underlying messaging system during blocking periods
    /// - When Update() resumes, all queued messages are processed in sequence via TryDequeueMessage()
    /// - No messages are lost during blocking periods, ensuring reliable communication
    /// 
    /// **Client Connection Management:**
    /// - Client timeout tracking is paused during blocking periods (deltaTime is clamped to 0.1s max)
    /// - Prevents false client disconnections due to long blocking periods
    /// - Client state is preserved across domain reloads through ScriptableObject serialization
    /// 
    /// **Post-Blocking Recovery:**
    /// - All pending messages are processed immediately when Update() resumes
    /// - Client connections remain stable and responsive after blocking periods
    /// - No manual reconnection required from IDE clients
    /// </remarks>
    [CreateAssetMenu(fileName = "VisualStudioIntegrationCore", menuName = "Visual Studio/Integration Core")]
    internal class VisualStudioIntegrationCore : ScriptableObject
    {
        [SerializeField] private double _lastUpdateTime = 0;
        [SerializeField] private bool _refreshRequested = false;
        [SerializeField] private List<IdeClient> _clients = new();

        [System.NonSerialized] private Messager _messager;

        private void OnEnable()
        {
            //Debug.Log("OnEnable");
            CheckLegacyAssemblies();
        }

        private void OnDisable()
        {
            //Debug.Log("OnDisable");
            // make sure we dispose the messager, when domain reload or editor quit
            // this will release the port and allow us to bind to it again on next domain reload
            if (_messager != null)
            {
                Debug.Log("disposing messager and release socket resources");
                _messager.Dispose();
                _messager = null;
            }
        }

        /// <summary>
        /// Updates the integration core by processing incoming messages and managing client timeouts.
        /// This method should be called regularly from Unity's update loop to maintain communication
        /// with connected IDE clients and handle message processing.
        /// </summary>
        public void Update()
        {
            EnsureMessagerInitialized();

            // Process messages from the queue on the main thread
            if (_messager != null)
            {
                while (_messager.TryDequeueMessage(out var message))
                {
                    ProcessIncoming(message);
                }
            }

            var currentTime = EditorApplication.timeSinceStartup;
            double deltaTime = currentTime - _lastUpdateTime;

            if (deltaTime > 1)
            {
                Debug.Log($"long delta time detected, deltaTime: {deltaTime}");
            }

            var clampedDeltaTime = Math.Min(deltaTime, 0.1);
            _lastUpdateTime = currentTime;
            for (int i = _clients.Count - 1; i >= 0; i--)
            {
                var client = _clients[i];
                client.ElapsedTime += clampedDeltaTime;
                if (client.ElapsedTime > 60)
                    _clients.RemoveAt(i);
            }

            // Handle refresh (blocking) at the end of update to allow other messages to be processed first
            if (_refreshRequested)
            {
                _refreshRequested = false;
                RefreshAssetDatabase();
            }
        }

        /// <summary>
        /// Handles assembly reload events by notifying connected IDE clients that compilation has finished.
        /// </summary>
        public void OnAssemblyReload()
        {
            Debug.Log("OnAssemblyReload");
            // need to ensure messager is initialized, because assembly reload event can happen before first Update
            EnsureMessagerInitialized();
            BroadcastMessage(MessageType.CompilationFinished, "");
        }

        private string GetPackageVersion()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VisualStudioIntegration).Assembly);
            return package.version;
        }

        private string GetPackageName()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VisualStudioIntegration).Assembly);
            return package.name;
        }

        /// <summary>
        /// Broadcasts a message of the specified type and value to all connected IDE clients.
        /// </summary>
        /// <param name="type">The type of message to broadcast.</param>
        /// <param name="value">The message content to send to all clients.</param>
        public void BroadcastMessage(MessageType type, string value)
        {
            foreach (var client in _clients)
            {
                Answer(client.EndPoint, type, value);
            }
        }

        private void CheckLegacyAssemblies()
        {
            var checkList = new HashSet<string>(new[] { KnownAssemblies.UnityVS, KnownAssemblies.Messaging, KnownAssemblies.Bridge });

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

                    Debug.LogWarning($"Project contains legacy assembly that could interfere with the Visual Studio Package. You should delete {relativePath}");
                }
            }
            catch (Exception)
            {
                // abandon legacy check
            }
        }

        private int DebuggingPort()
        {
            return 56000 + (System.Diagnostics.Process.GetCurrentProcess().Id % 1000);
        }

        private int MessagingPort()
        {
            return DebuggingPort() + 2;
        }

        private void RefreshAssetDatabase()
        {
            // Handle auto-refresh based on kAutoRefreshMode: 0=disabled, 1=enabled, 2=enabled outside play mode
            var autoRefreshMode = EditorPrefs.GetInt("kAutoRefreshMode", 1);

            // If auto-refresh is disabled (0), do not try to force refresh the Asset database
            if (autoRefreshMode != 0)
            {
                // If auto-refresh is set to "enabled outside play mode" (2) and we're in play mode, skip refresh
                if (!(autoRefreshMode == 2 && EditorApplication.isPlaying))
                {
                    if (!UnityInstallation.IsInSafeMode)
                    {
                        AssetDatabase.Refresh();
                    }
                }
            }
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
                    _refreshRequested = true;
                    break;
                case MessageType.Version:
                    Answer(message, MessageType.Version, GetPackageVersion());
                    break;
                case MessageType.ProjectPath:
                    Answer(message, MessageType.ProjectPath, Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
                    break;
                case MessageType.ExecuteTests:
                    TestRunnerApiListener.ExecuteTests(message.Value);
                    break;
                case MessageType.RetrieveTestList:
                    TestRunnerApiListener.RetrieveTestList(message.Value);
                    break;
                case MessageType.ShowUsage:
                    UsageUtility.ShowUsage(message.Value);
                    break;
                case MessageType.PackageName:
                    Answer(message, MessageType.PackageName, GetPackageName());
                    break;
            }
        }

        private void CheckClient(Message message)
        {
            var endPoint = message.Origin;

            var client = _clients.FirstOrDefault(c => c.EndPoint.Equals(endPoint));
            if (client == null)
            {
                client = new IdeClient
                {
                    EndPoint = endPoint,
                    ElapsedTime = 0
                };

                _clients.Add(client);
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
            _messager?.SendMessage(targetEndPoint, answerType, answerValue);
        }

        private void EnsureMessagerInitialized()
        {
            if (_messager != null || !VisualStudioEditor.IsEnabled)
                return;

            var messagingPort = MessagingPort();
            try
            {
                _messager = Messager.BindTo(messagingPort);
            }
            catch (SocketException)
            {
                Debug.LogWarning($"Unable to use UDP port {messagingPort} for VS/Unity messaging. You should check if another process is already bound to this port or if your firewall settings are compatible.");
            }
        }
    }
}