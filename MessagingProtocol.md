# Visual Studio Code Editor Messaging Protocol

This document describes the UDP-based messaging protocol used by Visual Studio Code Editor package for communication between Unity Editor and Visual Studio Code.

The name of this package is `com.hackerzhuli.code`. The name of the official package this is forked from is `com.unity.ide.visualstudio`. The messaging protocol is modified for better development experience with Visual Studio Code.

## Overview

The protocol uses UDP as the primary transport with automatic fallback to TCP for large messages. The communication is bidirectional, allowing both Unity and Visual Studio Code to send messages to each other.

## Network Configuration

### Port Calculation
- **Messaging Port**: `58000 + (ProcessId % 1000)`
- **Protocol**: UDP (primary), TCP (fallback for large messages)
- **Address**: Binds to `IPAddress.Any` (0.0.0.0)

### Client Timeout Configuration
- **Timeout Period**: Clients are removed after **4 seconds** of inactivity
- **Heartbeat Requirement**: Clients must send messages at least once within the timeout period to stay registered

## Message Format

Messages are serialized in binary format using little-endian encoding:

```
[4 bytes] Message Type (int32)
[4 bytes] String Length (int32)
[N bytes] String Value (UTF-8 encoded)
```

### Message Structure
- **Type**: 32-bit integer representing the MessageType enum value
- **Value**: UTF-8 encoded string with length prefix
- **Origin**: Set by receiver to identify sender's endpoint

### Serialization Details
- **Integer Encoding**: Little-endian 32-bit integers
- **String Encoding**: UTF-8 with 32-bit length prefix
- **Empty Strings**: Represented as length 0 followed by no data
- **Null Strings**: Treated as empty strings

## Message Types

All available message types:

| Type | Value | Description | Value Format |
|------|-------|-------------|-------------|
| `None` | 0 | Default/unspecified message type | Empty string |
| `Ping` | 1 | Heartbeat request | Empty string |
| `Pong` | 2 | Heartbeat response | Empty string |
| `Play` | 3 | Start play mode | Empty string |
| `Stop` | 4 | Stop play mode | Empty string |
| `Pause` | 5 | Pause play mode | Empty string |
| `Unpause` | 6 | Unpause play mode | Empty string |
| ~~`Build`~~ | 7 | ~~Build project~~ (Obsolete) | - |
| `Refresh` | 8 | Refresh asset database | Empty string (request) / Empty string (response) |
| `Info` | 9 | Info message from Unity logs | Log message content with optional stack trace |
| `Error` | 10 | Error message from Unity logs | Error message content with stack trace |
| `Warning` | 11 | Warning message from Unity logs | Warning message content with optional stack trace |
| ~~`Open`~~ | 12 | ~~Open file/asset~~ (Obsolete) | - |
| ~~`Opened`~~ | 13 | ~~File/asset opened confirmation~~ (Obsolete) | - |
| `Version` | 14 | Request/response for package version | Empty string (request) / Version string (response) |
| ~~`UpdatePackage`~~ | 15 | ~~Update package~~ (Obsolete) | - |
| `ProjectPath` | 16 | Request/response for Unity project path | Empty string (request) / Full project path (response) |
| `Tcp` | 17 | Internal message for TCP fallback coordination | `"<port>:<length>"` format |
| `TestRunStarted` | 18 | Test run started | JSON serialized TestAdaptorContainer (top-level adaptors, no children, no Source) |
| `TestRunFinished` | 19 | Test run finished | JSON serialized TestResultAdaptorContainer (top-level results, no children) |
| `TestStarted` | 20 | Notification that a test has started | JSON serialized TestAdaptorContainer (top-level adaptors, no children, no Source) |
| `TestFinished` | 21 | Notification that a test has finished | JSON serialized TestResultAdaptorContainer (top-level results, no children) |
| `TestListRetrieved` | 22 | Notification that test list has been retrieved | JSON serialized TestAdaptorContainer (complete test hierarchy with children) |
| `RetrieveTestList` | 23 | Request to retrieve list of available tests | Test mode string ("EditMode" or "PlayMode") |
| `ExecuteTests` | 24 | Request to execute specific tests | TestMode, TestMode:AssemblyName.dll, or TestMode:FullTestName / Response is empty string|
| `ShowUsage` | 25 | Show usage information | JSON serialized FileUsage object |
| `CompilationFinished` | 100 | Notification that compilation has finished | Empty string |
| `PackageName` | 101 | Request/response for package name | Empty string (request) / Package name string (response) |
| `Online` | 102 | Notifies clients that this package is online and ready to receive messages | Empty string |
| `Offline` | 103 | Notifies clients that this package is offline and can not receive messages | Empty string |
| `IsPlaying` | 104 | Notification of current play mode state | "true" (in play mode) / "false" (in edit mode) |
| `CompilationStarted` | 105 | Notification that compilation has started | Empty string |
| `GetCompileErrors` | 106 | Request/response for compile error information | Empty string (request) / JSON serialized LogContainer (response) |

Note:
- Message value greater than or equal to 100 means it does not exist in the official package but was added in this package.

### Value Format Details

Detailed value formats for some of the types:

- **Empty Requests**: `Ping`, `Pong`, `None` always use empty string values
- **Version**: 
  - Request: Empty string
  - Response: Package version string (e.g., "2.0.17")
- **ProjectPath**: 
  - Request: Empty string  
  - Response: Full path to Unity project directory
- **PackageName**: 
  - Request: Empty string
  - Response: Package name string (e.g., "com.hackerzhuli.code")
- **Online**: Empty string - sent when this package comes online after domain reload or editor startup
- **Offline**: Empty string - sent when this package goes offline before domain reload or editor shutdown
- **IsPlaying**: 
  - Value: "true" when Unity is in play mode, "false" when in edit mode
  - Sent automatically when play mode state changes (entering/exiting play mode)
  - Sent to new clients when they connect or when this package comes online
- **Tcp**: Internal format `"<port>:<length>"` (eg. "1234:1024") where port is the TCP listener port and length is the expected message size

- **Test Messages**: Value format depends on Unity's test runner implementation and may contain JSON or structured data

#### Refresh (Value: 8)
- **Format**: 
  - Request: Empty string
  - Response: Empty string for successful refresh, or error message string if refresh was not started
- **Description**: Requests Unity to refresh the asset database. Unity will respond with a Refresh message to the original client when the refresh operation is complete or if it could not be started.
- **Response Values**:
  - **Empty string**: Refresh was successfully started and completed
  - **Error message**: Refresh was not started, with reason (e.g., "Refresh not started: Unity is in play mode", "Refresh not started: Unity is in safe mode")
- **Important Notes**:
  - **Refresh vs Compilation**: A refresh finished notification does NOT mean compilation has finished. These are separate operations:
    - If compilation is needed after refresh, the refresh will finish BEFORE compilation starts
    - If no compilation is needed, the refresh will finish after all asset database operations are complete (including importing assets, etc.)
  - This behavior follows Unity Editor's standard asset refresh workflow
  - For compilation completion notifications, use the `CompilationFinished` message type (Value: 100)
- **Usage**: Clients can use this to trigger asset database refresh and get notified when the refresh operation specifically is complete, allowing them to proceed with operations that depend on the asset database being up-to-date. Clients should check if the response is empty to determine if the refresh was successful.

#### CompilationStarted (Value: 105)
- **Format**: Empty string
- **Description**: Notification sent when Unity's compilation pipeline starts compiling assemblies. This message is broadcast to all connected clients when the compilation process begins.
- **Important Notes**:
  - **Compilation Lifecycle**: This message is sent at the beginning of the compilation process, before any assembly compilation starts
  - **Relationship to CompilationFinished**: This message pairs with `CompilationFinished` (Value: 100) to provide complete compilation lifecycle notifications

#### GetCompileErrors (Value: 106)
- **Format**: 
  - Request: Empty string
  - Response: JSON serialized LogContainer object
- **Description**: Requests the collected compile errors that occurred during Unity's compilation process. Unity collects compile errors within a 1-second window after compilation finishes.

- **C# Structure**:

```csharp
[Serializable]
public class LogContainer
{
    /// <summary>
    /// Array of log entries.
    /// </summary>
    public Log[] Logs { get; set; }
}

[Serializable]
public class Log
{
    /// <summary>
    /// The complete log message as logged by Unity.
    /// </summary>
    public string Message;
    
    /// <summary>
    /// The stack trace associated with the log entry, if available.
    /// </summary>
    public string StackTrace;
    
    /// <summary>
    /// The timestamp when the log entry was captured as Unix timestamp (milliseconds since epoch).
    /// </summary>
    public long Timestamp;
}
```

- **Behavior**:
  - **Collection Window**: Compile errors are collected for 1 second after compilation finishes
  - **Error Filtering**: Only log messages containing "error CS" are collected
  - **Automatic Clearing**: Previous compile errors are cleared when compilation starts
  - **Response Format**: Returns JSON with LogContainer containing array of Log objects
- **Usage**: Clients can request this to get structured compile error information for IDE integration, error highlighting, and debugging assistance.

#### RetrieveTestList (Value: 23)
- **Format**: Test mode string ("EditMode" or "PlayMode")
- **Example**: `"EditMode"`
- **Description**: Requests the list of available tests for the specified test mode

#### ExecuteTests (Value: 24)
- **Format**: Supports multiple formats:
  - `TestMode` - Execute all tests in the specified mode
  - `TestMode:AssemblyName.dll` - Execute all tests in the specified assembly
  - `TestMode:FullTestName` - Execute a specific test by its full name
  - `TestMode:PartialTestName?` - Execute tests using fuzzy matching (partial name matching), by ending with `?`
- **Examples**: 
  - `"EditMode"` - Run all edit mode tests
  - `"PlayMode:MyTests.dll"` - Run all tests in MyTests assembly
  - `"EditMode:MyNamespace.MyTestClass"` - Run all tests in MyTestClass
  - `"EditMode:TestMethod?"` - Run all tests whose full name ends with "TestMethod"
  - `"PlayMode:Utils?"` - Run all tests whose full name ends with "Utils"
- **Description**: Executes tests based on the specified filter. The filter can target all tests in a mode, all tests in an assembly, or a specific test by name. When the filter doesn't match any exact test names, fuzzy matching is performed to find tests whose full names end with the specified search term.
- **Fuzzy Matching Behavior**:
  - If filter ends with `?`, the system performs fuzzy matching
  - Fuzzy matching finds all tests (including non-leaf nodes) whose `FullName` ends with the search term
  - Case-insensitive matching is used
  - Both leaf tests and test containers (classes, namespaces) can be matched
  - Multiple matches are supported - all matching tests will be executed

Response:
- A response that is empty is sent to the original client to confirm that the message is received and already processed.

#### TestStarted (Value: 20)
- **Format**: JSON serialized TestAdaptorContainer
- **Important**: Each message contains only top-level test adaptors with no children data. This is an optimization to avoid sending redundant hierarchy information.
- **Note**: The Source field is not populated in this message type for efficiency.
- **C# Structure**:
  
```csharp
public enum TestNodeType{
  Solution,
  Assembly,
  Namespace,
  Class,
  Method,
  /// <summary>
  /// Test case of a parameterized test method
  /// </summary>
  TestCase,
}

[Serializable]
internal class TestAdaptorContainer
{
    public TestAdaptor[] TestAdaptors; // Always contains exactly one element
}

[Serializable]
internal class TestAdaptor
{
  /// <summary>
  /// Unique identifier for the test node, persisted (as much as possible) across compiles, will not conflict accross test modes
  /// </summary>
  public string Id;

  /// <summary>
  /// The name of the test node.
  /// </summary>
  public string Name;
  
  /// <summary>
  /// The full name of the test including namespace and class, for assembly, the path of the assembly
  /// </summary>
  public string FullName;

  /// <summary>
  /// The type of the test node.
  /// </summary>
  public TestNodeType Type;
  
  /// <summary>
  /// Index of parent in TestAdaptors array, -1 for root.
  /// </summary>
  public int Parent;

  /// <summary>
  /// Source location of the test in format "Assets/Path/File.cs:LineNumber".
  /// Only populated for methods, empty for other nodes
  /// </summary>
  public string Source;

  /// <summary>
  /// Number of leaf tests in this test node and its children
  /// </summary>
  public int TestCount;

  /// <summary>
  /// True if this test node has any child test nodes.
  /// </summary>
  public bool HasChildren;
}
```

- **Description**: Sent when a test starts execution. Each message contains only top-level test adaptors without any children data, ensuring efficient and non-redundant messaging. Only the top-level test information is included, and the Source field is not populated.

#### TestFinished (Value: 21)
- **Format**: JSON serialized TestResultAdaptorContainer
- **Important**: Each message contains only top-level test results with no children data. This is an optimization to avoid sending redundant data.

- **C# Structure**:

```csharp
[Serializable]
internal class TestResultAdaptorContainer
{
    public TestResultAdaptor[] TestResultAdaptors; // Always contains exactly one element
}

[Serializable]
internal class TestResultAdaptor
{
  /// <summary>
  /// The unique identifier for the test this result is for.
  /// </summary>
  public string TestId;

  /// <summary>
  /// The number of test cases that passed when running the test and all its children.
  /// </summary>
  public int PassCount;
  
  /// <summary>
  /// The number of test cases that failed when running the test and all its children.
  /// </summary>
  public int FailCount;
  
  /// <summary>
  /// The number of test cases that were inconclusive when running the test and all its children.
  /// </summary>
  public int InconclusiveCount;
  
  /// <summary>
  /// The number of test cases that were skipped when running the test and all its children.
  /// </summary>
  public int SkipCount;

  /// <summary>
  /// Gets the state of the result as a string.
  /// Returns one of these values: Inconclusive, Skipped, Skipped:Ignored, Skipped:Explicit, Passed, Failed, Failed:Error, Failed:Cancelled, Failed:Invalid.
  /// </summary>
  public string ResultState;
  
  /// <summary>
  /// Any stacktrace associated with an error or failure, empty if the test passed (only avaiable for leaf tests)
  /// </summary>
  public string StackTrace;

  /// <summary>
  /// The test status as a simplified enum value.
  /// </summary>
  public TestStatusAdaptor TestStatus;

  /// <summary>
  /// The number of asserts executed when running the test and all its children.
  /// </summary>
  public int AssertCount;
  
  /// <summary>
  /// Gets the elapsed time for running the test in seconds.
  /// </summary>
  public double Duration;
  
  /// <summary>
  /// Gets the time the test started running as Unix timestamp (milliseconds since epoch).
  /// </summary>
  public long StartTime;
  
  /// <summary>
  /// Gets the time the test finished running as Unix timestamp (milliseconds since epoch).
  /// </summary>
  public long EndTime;
  
  /// <summary>
  /// The error message associated with a test failure or with not running the test, empty if the test (and its children) passed
  /// </summary>
  public string Message;
  
  /// <summary>
  /// Gets all logs during the test(only available for leaf tests)(no stack trace for logs is available)
  /// </summary>
  public string Output;
  
  /// <summary>
  /// True if this result has any child results.
  /// </summary>
  public bool HasChildren;

  /// <summary>
  /// Index of parent in TestResultAdaptors array, -1 for root.
  /// </summary>
  public int Parent;
}

[Serializable]
internal enum TestStatusAdaptor
{
    Passed,        // 0
    Skipped,       // 1
    Inconclusive,  // 2
    Failed,        // 3
}
```

- **Description**: Sent when a test finishes execution. Each message contains only top-level test results without any children data, ensuring efficient and non-redundant messaging.

#### TestRunStarted (Value: 18)
- **Format**: JSON serialized TestAdaptorContainer
- **Important**: Each message contains only top-level test adaptors with no children. This is an optimization to avoid sending redundant hierarchy information.
- **Note**: The Source field is not populated in this message type for efficiency.
- **C# Structure**: Uses the same TestAdaptorContainer and TestAdaptor structures as TestStarted (see TestStarted section for complete structure)
- **Description**: Sent when a test run begins execution. Only the top-level tests are included, and the Source field is not populated.
- **Usage**: Clients can use this to prepare UI, show progress indicators, or track which tests are part of the current run.

#### ShowUsage (Value: 25)
- **Format**: JSON serialized FileUsage object
- **C# Structure**:

```csharp
[Serializable]
internal class FileUsage
{
    /// <summary>
    /// The file path to show usage for. Can be absolute or relative to project.
    /// </summary>
    public string Path;
    
    /// <summary>
    /// Optional array of GameObject names representing the hierarchy path within a scene.
    /// Used when showing usage of a specific GameObject in a Unity scene.
    /// Example: ["ParentObject", "ChildObject", "TargetObject"]
    /// </summary>
    public string[] GameObjectPath;
}
```

- **Description**: Requests Unity to show usage/location of a specific file or GameObject. Unity will focus the Project window and select the specified asset, or open a scene and select a GameObject if a hierarchy path is provided.
- **Behavior**:
  - For non-scene files: Selects and pings the asset in the Project window
  - For .unity scene files: Prompts to open the scene (single, additive, or cancel), then optionally navigates to a specific GameObject using the GameObjectPath
  - Handles both absolute and relative file paths, normalizing them to project-relative paths
- **Example**: `{"Path":"Assets/Scenes/MainScene.unity","GameObjectPath":["UI","Canvas","Button"]}`

#### TestListRetrieved (Value: 22)
- **Format**: `TestMode:JsonData`
- **Structure**: `TestModeName + ":" + JSON serialized TestAdaptorContainer`
- **TestModeName**: "EditMode" or "PlayMode"
- **JsonData**: JSON serialized TestAdaptorContainer with complete test hierarchy (unlike other test messages which only contain top level test adaptors)
- **Important**: This is the only test message that contains the complete hierarchical structure with all tests and their relationships
- **Description**: Response containing the complete hierarchical test structure as JSON for the requested test mode

## Protocol Flow

### Client Registration
1. Client sends any message to this package's messaging port
2. This package registers the client's endpoint
3. This package responds appropriately based on message type
4. Client must send messages within 4 seconds to stay registered 

### Heartbeat Mechanism
- Send `Ping` message to this package
- This package responds with `Pong` message
- Clients are automatically removed after 4 seconds of inactivity 

### Online/Offline and Domain Reload
When domain reload starts(typically along with compilation), this package is disabled due to Unity's mechanism. Socket will be disposed and this package can't receive messages for a while(domain reload can take up to a minute depending on the project). The Offline message is sent right before this package's socket is disposed.

Once domain reload finishes, this package will be enabled and socket will be recreated. And previous clients will be preserved. But messages that are not handled or not received at all(when this package is offline) will not be processed. The Online message is sent right after this package's socket is recreated.

### Large Message Handling (TCP Fallback)

When a message exceeds the 8KB UDP buffer limit, the protocol automatically switches to TCP for reliable delivery of large messages.

#### Fallback Trigger
- **Condition**: Serialized message size ≥ 8192 bytes (`UdpSocket.BufferSize`)
- **Detection**: Sender checks buffer length before UDP transmission
- **Scope**: Applies to individual messages, not the entire connection

#### Detailed Process

**1. Sender (This package or Client)**:
   - Detects message size exceeds UDP buffer limit
   - Creates a temporary TCP listener on an available port (system-assigned)
   - Replaces original message with `Tcp` control message
   - Sends UDP message with `MessageType.Tcp` and value format: `"<port>:<length>"`
   - Waits for incoming TCP connection on the listener port
   - Sends the actual large message over TCP connection
   - Closes TCP connection and listener after transmission

**2. Receiver (This package or Client)**:
   - Receives `Tcp` control message via UDP
   - Validates message type is `MessageType.Tcp` (value: 17)
   - Parses message value to extract: `port` and `length`
   - Initiates TCP connection to sender's IP address on specified port
   - Allocates buffer of exact `length` for receiving data
   - Reads complete message from TCP stream (must read exactly `length` bytes)
   - Deserializes received buffer using standard message format
   - Closes TCP connection

#### Critical Implementation Notes

- **Timeout Handling**: TCP operations have 5-second timeout (`ConnectOrReadTimeoutMilliseconds`)
- **Exact Read Required**: Must read exactly `length` bytes from TCP stream
- **Connection Cleanup**: Always close TCP connections and listeners after use
- **Error Recovery**: Failed TCP operations should not crash the UDP messaging loop
- **Thread Safety**: TCP operations run on background threads, ensure proper synchronization
- **Port Availability**: TCP listener uses system-assigned ports (port 0), not fixed ports

## Implementation Notes

- Clients can be implemented in any language that supports UDP sockets and binary serialization
- The protocol is designed for localhost communication between this package and external tools
- Message serialization uses little-endian encoding for cross-platform compatibility

## Error Handling

- **Socket Exceptions**: This package will attempt to rebind on domain reload
- **Port Conflicts**: This package uses `ReuseAddress` but conflicts may still occur
- **Message Size**: Messages larger than 8KB automatically use TCP fallback
- **Client Timeout**: Clients are removed after 4 seconds of inactivity 

## Security Considerations

- **Local Communication Only**: Protocol is designed for localhost communication
- **No Authentication**: No built-in authentication mechanism
- **Process ID Based Ports**: Ports are calculated based on Unity process ID

## Limitations

- **UDP Reliability**: No guaranteed delivery (inherent UDP limitation)
- **Message Ordering**: No guaranteed order (inherent UDP limitation)
- **Buffer Size**: 8KB limit for UDP messages (larger messages use TCP)
- **Client Management**: Automatic cleanup after 4 seconds of inactivity 

## Troubleshooting

1. **Connection Issues**: Verify Unity process ID and calculated port
2. **Port Conflicts**: Another application might be using the calculated port
3. **Client Timeout**: Send heartbeat messages regularly within 4 seconds