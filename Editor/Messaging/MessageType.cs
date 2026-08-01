/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;

namespace Hackerzhuli.Code.Editor.Messaging
{
    internal enum MessageType
    {
        /// <summary>
        ///     Default/unspecified message type. Always uses empty string value.
        /// </summary>
        None = 0,

        /// <summary>
        ///     Heartbeat request message. Clients send this to maintain connection and check Unity's availability.
        ///     Unity responds with Pong. Always uses empty string value.
        /// </summary>
        Ping,

        /// <summary>
        ///     Heartbeat response message. Unity sends this in response to Ping messages.
        ///     Always uses empty string value.
        /// </summary>
        Pong,

        /// <summary>
        ///     Request to start Unity's play mode. Unity will set EditorApplication.isPlaying = true.
        ///     Always uses empty string value.
        /// </summary>
        Play,

        /// <summary>
        ///     Request to stop Unity's play mode. Unity will set EditorApplication.isPlaying = false.
        ///     Always uses empty string value.
        /// </summary>
        Stop,

        /// <summary>
        ///     Request to pause Unity's play mode. Unity will set EditorApplication.isPaused = true.
        ///     Always uses empty string value.
        /// </summary>
        Pause,

        /// <summary>
        ///     Request to unpause Unity's play mode. Unity will set EditorApplication.isPaused = false.
        ///     Always uses empty string value.
        /// </summary>
        Unpause,

        /// <summary>
        ///     Obsolete message type for building projects. No longer supported.
        /// </summary>
        [Obsolete] Build,

        /// <summary>
        ///     Request to refresh Unity's asset database. Unity will call AssetDatabase.Refresh() based on auto-refresh settings.
        ///     Always uses empty string value.
        /// </summary>
        Refresh,

        /// <summary>
        ///     Information message from Unity logs. Sent when Unity logs informational messages.
        ///     Value contains the log message content with optional stack trace.
        /// </summary>
        Info,

        /// <summary>
        ///     Error message from Unity logs. Sent when Unity logs errors, exceptions, or assertions.
        ///     Value contains the error message content with stack trace.
        /// </summary>
        Error,

        /// <summary>
        ///     Warning message from Unity logs. Sent when Unity logs warning messages.
        ///     Value contains the warning message content with optional stack trace.
        /// </summary>
        Warning,

        /// <summary>
        ///     Obsolete message type for opening files/assets. No longer supported.
        /// </summary>
        [Obsolete] Open,

        /// <summary>
        ///     Obsolete message type for file/asset opened confirmation. No longer supported.
        /// </summary>
        [Obsolete] Opened,

        /// <summary>
        ///     Request/response for package version information.
        ///     Request: Empty string. Response: Package version string (e.g., "2.0.17").
        /// </summary>
        Version,

        /// <summary>
        ///     Obsolete message type for updating packages. No longer supported.
        /// </summary>
        [Obsolete] UpdatePackage,

        /// <summary>
        ///     Request/response for Unity project path information.
        ///     Request: Empty string. Response: Full path to Unity project directory.
        /// </summary>
        ProjectPath,

        /// <summary>
        ///     Internal message for TCP fallback coordination when messages exceed 8KB UDP buffer limit.
        ///     Value format: "&lt;port&gt;:&lt;length&gt;" where port is TCP listener port and length is expected message size.
        ///     Not intended to be used directly by clients.
        /// </summary>
        Tcp,

        /// <summary>
        ///     Notification that a test run has started. Sent by Unity's test runner.
        ///     Value format: JSON serialized TestAdaptorContainer with single test adaptor (no children, no source).
        /// </summary>
        TestRunStarted,

        /// <summary>
        ///     Notification that a test run has finished. Sent by Unity's test runner.
        ///     Value format: JSON serialized TestResultAdaptorContainer with single test result (no children).
        /// </summary>
        TestRunFinished,

        /// <summary>
        ///     Notification that a test has started execution. Contains test metadata and hierarchy.
        ///     Value format: JSON serialized TestAdaptorContainer with single test adaptor (no children, no source).
        /// </summary>
        TestStarted,

        /// <summary>
        ///     Notification that a test has finished execution. Contains test results and status.
        ///     Value format: JSON serialized TestResultAdaptorContainer with single test result (no children).
        /// </summary>
        TestFinished,

        /// <summary>
        ///     Response containing the hierarchical test structure for the requested test mode.
        ///     Value format: "TestModeName:JsonData" where TestModeName is "EditMode" or "PlayMode" and JsonData is JSON
        ///     serialized TestAdaptorContainer with complete hierarchy.
        /// </summary>
        TestListRetrieved,

        /// <summary>
        ///     Request to retrieve list of available tests for a specific test mode.
        ///     Value format: Test mode string ("EditMode" or "PlayMode").
        /// </summary>
        RetrieveTestList,

        /// <summary>
        ///     Request to execute specific tests identified by full name in the specified test mode.
        ///     Value format: "TestMode:FullTestName" (e.g., "EditMode:MyNamespace.MyTestClass.MyTestMethod").
        /// </summary>
        ExecuteTests,

        /// <summary>
        ///     Request to show usage information. Value format depends on implementation.
        /// </summary>
        ShowUsage,

        /// <summary>
        ///     This is a message sent when the compilation is finished<br />
        /// </summary>
        CompilationFinished = 100,

        /// <summary>
        ///     The name of this package
        /// </summary>
        PackageName = 101,

        /// <summary>
        ///     Notifies clients that we are online and ready to receive messages
        ///     This can be due to after domain reload finished or Unity Editor start
        /// </summary>
        Online = 102,

        /// <summary>
        ///     Notifies clients that we are going offline, and will not be able to receive messages
        ///     This can be due to domain reload or Unity Editor shutdown
        /// </summary>
        Offline = 103,

        /// <summary>
        ///     Notifies clients that the play mode state has changed<br />
        ///     "true" for is playing(or entering play mode), "false" otherwise
        /// </summary>
        IsPlaying = 104,

        /// <summary>
        ///     This is a message sent when the compilation is started<br />
        /// </summary>
        CompilationStarted = 105,

        /// <summary>
        ///     Request for compile error information.
        /// </summary>
        GetCompileErrors = 106,

        /// <summary>
        ///     Request a compact snapshot of runtime UI Toolkit documents in the Game View.
        /// </summary>
        UiSnapshot = 107,

        /// <summary>
        ///     Activate an element returned by UiSnapshot or UiHierarchy.
        /// </summary>
        UiClick = 108,

        /// <summary>
        ///     Move the synthetic mouse pointer over an element.
        /// </summary>
        UiHover = 109,

        /// <summary>
        ///     Capture the currently rendered Game View to the project's Temp directory.
        /// </summary>
        GameViewScreenshot = 110,

        /// <summary>
        ///     Request a type/name/USS-class descendant hierarchy for one UI element ref.
        /// </summary>
        UiHierarchy = 111,

        /// <summary>
        ///     Get layout, styles and properties for an element.
        /// </summary>
        UiInspect = 112,

        /// <summary>
        ///     Assign value to a BaseField element(eg. Toggle, Slider).
        /// </summary>
        UiSetValue = 113,

        /// <summary>
        ///     List the currently open scenes and which one is active. Available in Edit and Play Mode.
        /// </summary>
        SceneList = 114,

        /// <summary>
        ///     Open a scene asset. Only available in Edit Mode.
        /// </summary>
        SceneOpen = 115,

        /// <summary>
        ///     List the available locales and the selected one. Only available in Play Mode.
        /// </summary>
        LocaleList = 116,

        /// <summary>
        ///     Change the selected locale. Only available in Play Mode.
        /// </summary>
        LocaleSelect = 117,

        /// <summary>
        ///     Request a name/instance id GameObject tree for one open scene. Available in Edit and Play Mode.
        /// </summary>
        SceneHierarchy = 118,

        /// <summary>
        ///     Request a name/instance id descendant tree for one GameObject. Available in Edit and Play Mode.
        /// </summary>
        GameObjectHierarchy = 119,

        /// <summary>
        ///     Find GameObjects by name or by exact path. Available in Edit and Play Mode.
        /// </summary>
        GameObjectFind = 120,

        /// <summary>
        ///     Get the main properties and component members of one GameObject. Available in Edit and Play Mode.
        /// </summary>
        GameObjectInspect = 121,

        /// <summary>
        ///     Request the screen space bounds of the GameObjects a camera actually renders, as a hierarchy.
        ///     Only available in Play Mode.
        /// </summary>
        GameObjectVisualSnapshot = 122
    }
}
