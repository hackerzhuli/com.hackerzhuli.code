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
| `CompilationFinished` | 100 | Notification that compilation has finished | Empty string (automatically followed by GetCompileErrors message) |
| `PackageName` | 101 | Request/response for package name | Empty string (request) / Package name string (response) |
| `Online` | 102 | Notifies clients that this package is online and ready to receive messages | Empty string |
| `Offline` | 103 | Notifies clients that this package is offline and can not receive messages | Empty string |
| `IsPlaying` | 104 | Notification of current play mode state | "true" (in play mode) / "false" (in edit mode) |
| `CompilationStarted` | 105 | Notification that compilation has started | Empty string |
| `GetCompileErrors` | 106 | Auto-sent after CompilationFinished, or manual request/response for compile error information | Empty string (request) / JSON serialized LogContainer (response) |
| `UiSnapshot` | 107 | Request a compact snapshot of runtime UI Toolkit documents in the Game View | JSON request / JSON response containing YAML |
| `UiClick` | 108 | Click a button element | JSON request / JSON success or error response |
| `UiHover` | 109 | Move the mouse pointer over an element | JSON request / JSON success or error response |
| `GameViewScreenshot` | 110 | Capture the currently rendered Game View to the project's Temp directory | JSON request / JSON response containing an absolute PNG path |
| `UiHierarchy` | 111 | Request a type/name/USS-class descendant hierarchy for one element | JSON request / JSON response containing YAML |
| `UiInspect` | 112 | Get layout, styles, and properties for an element | JSON request / JSON response containing YAML |
| `UiSetValue` | 113 | Assign a value to a BaseField element such as `Toggle` or `Slider` | JSON request / JSON success or error response |
| `SceneList` | 114 | List the currently open scenes and which one is active | JSON request / JSON response containing YAML |
| `SceneOpen` | 115 | Open a scene asset (Edit Mode only) | JSON request / JSON response containing YAML |
| `LocaleList` | 116 | List the available locales and the selected one (Play Mode only) | JSON request / JSON response containing YAML |
| `LocaleSelect` | 117 | Change the selected locale (Play Mode only) | JSON request / JSON response containing YAML |
| `SceneHierarchy` | 118 | Request the GameObject tree of one open scene | JSON request / JSON response containing YAML |
| `GameObjectHierarchy` | 119 | Request the descendant tree of one GameObject | JSON request / JSON response containing YAML |
| `GameObjectFind` | 120 | Find GameObjects by name or by exact path | JSON request / JSON response containing YAML |
| `GameObjectInspect` | 121 | Get the properties and component members of one GameObject | JSON request / JSON response containing YAML |

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

#### CompilationFinished (Value: 100)
- **Format**: Empty string
- **Description**: Notification sent when Unity's compilation pipeline finishes compiling assemblies. This message is broadcast to all connected clients when the compilation process completes.
- **Automatic Behavior**: 
  - **GetCompileErrors Auto-Send**: Immediately after broadcasting this message, a `GetCompileErrors` message (Value: 106) is automatically sent to all connected clients with the collected compile errors from the compilation session
  - **Error Collection**: Compile errors are collected during the compilation process and automatically provided without requiring a separate request
  - **Manual Requests**: `GetCompileErrors` can still be requested manually
- **Important Notes**:
  - **Compilation Lifecycle**: This message is sent at the end of the compilation process, after all assembly compilation finishes
  - **Relationship to CompilationStarted**: This message pairs with `CompilationStarted` (Value: 105) to provide complete compilation lifecycle notifications
  - **Client Integration**: Clients can expect to receive compile error information automatically after each compilation without needing to explicitly request it

#### GetCompileErrors (Value: 106)
- **Format**: 
  - Request: Empty string
  - Response: JSON serialized LogContainer object
- **Description**: Provides the collected compile errors that occurred during Unity's compilation process. This message is automatically sent to all connected clients immediately after each `CompilationFinished` message, but can also be requested manually.
- **Automatic Behavior**: 
  - **Auto-Send**: Automatically broadcast to all clients after every `CompilationFinished` message
  - **Manual Request**: Can also be requested manually by sending an empty string request

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
- **Usage**: Clients automatically receive structured compile error information after each compilation for IDE integration, error highlighting, and debugging assistance. Manual requests are also supported for on-demand error retrieval.

### Game View Automation (Values: 107-113)

These messages automate runtime UI Toolkit content rendered in the Unity Game View. They do not
inspect Editor UI, do not use the Unity Accessibility API, and are available only while the Editor
is in Play Mode. Paused Play Mode is supported.

All Game View automation requests, and the Editor state requests documented in
[Editor State (Values: 114-117)](#editor-state-values-114-117), share the same envelope:

- Must originate from a loopback address. Non-loopback requests receive `forbidden`.
- Use a JSON object as the message value.
- Require an opaque `requestId`. A JSON string or number is accepted and returned unchanged.
- Receive their response using the same message type as the request.
- May use TCP fallback automatically when a YAML snapshot or inspection exceeds the UDP limit.

Minimal request:

```json
{"requestId":"request-42"}
```

Success without a result value:

```json
{"requestId":"request-42","ok":true}
```

Success with a result value:

```json
{
  "requestId":"request-42",
  "ok":true,
  "snapshot":"- UIDocument:\n  - Button [text=\"Play\"] [ref=e1]"
}
```

All failures use the same shape:

```json
{
  "requestId":"request-42",
  "ok":false,
  "error":{
    "code":"unknown_ref",
    "message":"Unknown UI element ref 'e1'."
  }
}
```

#### Element Refs

`[ref=eN]` is automation metadata, not a Unity UI Toolkit property. Snapshot and hierarchy nodes
receive refs so later requests can address the exact `VisualElement`.

- Refs are stable while their mapping and element remain valid.
- A detached element returns `stale_ref`.
- An unrecognized or pruned mapping returns `unknown_ref`.
- Domain reload recreates the service and invalidates all refs.
- Clients should acquire fresh refs after domain reload and should prefer refs from the most recent
  snapshot or hierarchy response.

#### UiSnapshot (Value: 107)

Request:

```json
{"requestId":"snapshot-1"}
```

Success:

```json
{
  "requestId":"snapshot-1",
  "ok":true,
  "snapshot":"- UIDocument [name=\"Runtime HUD\"]:\n  - Button [name=\"play\"] [text=\"Play\"] [ref=e1]"
}
```

The snapshot starts at every active `UIDocument` attached to a runtime panel. Documents are ordered
by panel settings, document sorting order, and visual-tree order. Editor panels are not included.

Elements parented directly to a runtime panel root instead of a `UIDocument` root, such as the menu
of an open dropdown, follow the documents of that same panel under a `PanelOverlay` entry, which
carries the panel root name:

```yaml
- PanelOverlay [panel="PanelSettings"]:
  - VisualElement [ref=e7]:
    - ScrollView [ref=e8]:
```

The compact YAML tree:

- Uses `element.GetType().Name`, including the actual type name of custom derived controls.
- Uses UI Toolkit property names rather than ARIA roles or accessibility states.
- Includes standard controls, text elements, informative generic elements, and custom elements.
- Collapses implementation children of common built-in controls derived from `BaseField<T>` and
  other controls whose internal hierarchy is normally noise.
- Retains an element whose resolved `display` is `None` or `visibility` is `Hidden`, but silently
  omits its descendants. Opacity zero does not trigger this rule.
- When one parent has at least 20 direct children and every child has the same runtime type, emits
  only the first 10. Compact snapshots do not add omission metadata.

The native property whitelist includes, where applicable:

- Common: `name`, `tooltip`, and `enabledSelf=false`.
- Visibility: `display=None` and `visibility=Hidden`.
- Text controls: `text`.
- `Toggle` and `RadioButton`: `label`, `value`.
- Text and numeric fields: `label`, `value`, and relevant `isReadOnly`/`multiline` state.
- Sliders: `label`, `value`, and their range properties.
- Popup fields: `label`, `value`, `index`.
- `Foldout`, `ProgressBar`, collection views, and `ScrollView`: their compact native state.

Strings use deterministic YAML escaping and numbers use invariant culture.

#### UiClick (Value: 108)

Request:

```json
{"requestId":"click-1","ref":"e1"}
```

Before activation, Unity validates that the element is attached, visible, enabled, and has a point
inside its clipped bounds that is not covered by another element.

Activation depends on the target:

- `Button` and custom `Button` subclasses receive `NavigationSubmitEvent`. Unity's own Button
  handler invokes its `Clickable`, fires `Button.clicked` once, and manages the temporary
  `:active` state. Synthetic PointerDown/PointerUp events are not sent for this path.
- Other elements receive synthetic move, primary-button down, and primary-button up events through
  the runtime panel. This preserves the normal UI Toolkit event pipeline for controls such as
  `Toggle`.

The request does not call user callbacks through reflection.

#### UiHover (Value: 109)

Request:

```json
{"requestId":"hover-1","ref":"e1"}
```

Unity finds and validates a visible hittable point, then sends pointer and mouse move events through
the runtime panel. Normal hit testing, PointerEnter/PointerLeave callbacks, and `:hover` state apply.
Another hover request or real pointer movement naturally replaces the current hover target.

#### GameViewScreenshot (Value: 110)

Request:

```json
{"requestId":"screenshot-1"}
```

Success:

```json
{
  "requestId":"screenshot-1",
  "ok":true,
  "path":"F:\\projects\\MyGame\\Temp\\GameViewScreenshots\\game-view-20260730-153012-123-a1b2c3.png"
}
```

The screenshot captures the complete Game View using `ScreenCapture.CaptureScreenshot` with
`superSize = 1`; it is not limited to UI Toolkit content.

- The destination is always `<project>/Temp/GameViewScreenshots`.
- Unity generates a unique `game-view-<UTC timestamp>-<random id>.png` filename.
- Requests cannot provide `path`, `fileName`, or `filename`.
- Requests are queued and processed one at a time.
- Unity responds only after the file size is stable across two checks and the PNG signature and
  terminal IEND chunk are valid.
- The default timeout is 10 seconds.
- PNG files are not automatically deleted.

#### UiHierarchy (Value: 111)

Request:

```json
{"requestId":"hierarchy-1","ref":"e1","depth":3}
```

`depth` is optional and must be a non-negative integer:

- `0` returns only the selected element.
- `1` returns the selected element and all of its direct children.
- Higher values request additional descendant levels.
- The effective depth may be reduced automatically to keep output at or below 200 elements.
- Direct children are always retained by the dynamic limit.

Success:

```json
{
  "requestId":"hierarchy-1",
  "ok":true,
  "hierarchy":"- VisualElement [name=\"toolbar\"] [ussClasses=[\"toolbar\"]] [ref=e1]:\n  - Button [name=\"play\"] [ussClasses=[\"unity-button\",\"primary\"]] [ref=e2]"
}
```

Unlike `UiSnapshot`, hierarchy expands built-in implementation nodes and invisible descendants. It
walks the real visual tree rather than the content-container view, so the internal structure of
composite controls is included: a `ScrollView` shows its viewport, content container and scrollers,
not just its content items. Each normal node contains only:

- Its actual runtime type.
- Non-empty `name`.
- Non-empty USS class list as `ussClasses`.
- Its automation `ref`.

Protocol omission metadata may additionally appear on a parent:

```yaml
- VisualElement [name="items"] [omittedChildCount=12] [omissionReason="similar_children"] [ref=e1]:
  - Label [ussClasses=["unity-label"]] [ref=e2]
```

The same-type sibling rule applies when a parent has at least 20 direct children of exactly the same
runtime type: only the first 10 are emitted. `omissionReason` is one of `similar_children`, `hidden`
or `built_in_implementation`, and is **absent when the depth limit is what stopped the element**,
because that reason is the same for every element at that depth and is already stated in the header.

When the depth limit actually cuts something off, the document starts with a single comment line
naming the effective depth and where it came from, and nothing repeats it further down:

```yaml
# maxDepth 3 (dynamic, 200 element budget); elements cut off by it carry omittedChildCount without a reason
```

A requested limit reads `# maxDepth 3 (requested); ...` instead. No other comments are emitted:
everything else an element omits is stated by the properties on that element's own line.

The runtime panel root and a `UIDocument` root cannot be used as the hierarchy target because their
trees may be unbounded. Such requests return `forbidden`; select one of their descendants instead.

#### UiInspect (Value: 112)

Gets the layout, styles, and properties of one referenced element. Descendants are not included.

Request:

```json
{"requestId":"inspect-1","ref":"e1"}
```

Success returns a YAML document for exactly one element, without descendants:

```yaml
type: VisualElement
ref: e1
layout:
  margin: {top: 0, right: 0, bottom: 0, left: 0}
  border: {top: 0, right: 0, bottom: 0, left: 0}
  padding: {top: 8, right: 8, bottom: 8, left: 8}
  contentSize: [370,115]
geometry:
  worldBound: [464,273,386,131]
  worldClip: [0,0,1920,1080]
  boundingBox: [0,0,386,131]
  layout: [14,175,386,131]
  lastLayout: [14,175,386,131]
element:
  name: "event-log"
  debugId: 2042
  pickingMode: Position
  pseudoStates: 0
  focusable: false
  display: Flex
  visibility: Visible
  opacity: 1
  enabledSelf: true
  enabledInHierarchy: true
uss:
  classes: ["unity-scroll-view","event-log"]
  styleSheets:
    - "GameViewAutomationDemo (owner=TemplateContainer, name=Runtime HUD-container)"
  matchedSelectors:
    - selector: ".demo-column"
      source: "Packages/com.hackerzhuli.code/Tests/Runtime/Resources/GameViewAutomationDemo.uss:40"
      specificity: 11
      appliedDeclarations:
        - {property: "width", value: "50%", line: 42}
      overriddenDeclarations:
        - {property: "padding", value: "14px", line: 44, overriddenBy: "Packages/com.hackerzhuli.code/Tests/Runtime/Resources/GameViewAutomationDemo.uss:64"}
    - selector: ".event-log"
      source: "Packages/com.hackerzhuli.code/Tests/Runtime/Resources/GameViewAutomationDemo.uss:64"
      specificity: 11
      appliedDeclarations:
        - {property: "padding", value: "8px", line: 66}
        - {property: "background-color", value: "#11161D", line: 67}
  inlineStyles: {}
  resolvedStyles:
    background-color: "#181D26"
    flex-direction: Column
properties:
  childCount: 2
```

The sections are modeled after the useful parts of UI Toolkit Debugger:

- `layout`: box model and content size.
- `geometry`: world, clip, bounding, current layout, and last layout rectangles.
- `element`: identity, data source, picking, pseudo state, focus, visibility, and enabled state.
- `uss`: classes, inherited stylesheet sources, matched selectors, explicitly set inline styles, and
  resolved styles.
- `properties`: remaining public properties marked with Unity's `[CreateProperty]`.

The `matchedSelectors` section answers where a style actually comes from. It lists every USS rule
whose selector matches the element, like the "Matching Selectors" section of the UI Toolkit Debugger,
ordered the way Unity applies them: from lowest to highest priority, so a later rule overrides an
earlier one. Each rule reports its `source` as `path:line`, its `specificity`, and splits its
declarations into the ones that survive the cascade and the ones that lose:

- `appliedDeclarations`: the declarations this rule actually contributes.
- `overriddenDeclarations`: the ones that lose, each with `overriddenBy` naming the source that wins,
  or `"inline"` when an inline style beats every rule.

Declarations are compared by the property name as written, so a shorthand such as `margin` and a
longhand such as `margin-left` can both be applied, each winning for the part it writes. Style sheets
without a project asset path, such as the built in runtime theme, are identified by their name
instead of a path. Unity does not expose selector matching publicly, so this section runs Unity's own
matcher through reflection; when a future Unity version moves it, the section reports
`matchedSelectors: "<unavailable: ...>"` and the rest of the inspection is unaffected.

The `properties` section filters common properties declared by `VisualElement` or its base types when
they are duplicated by another section, expose large implementation objects, or contain arbitrary
user state. `[CreateProperty]` members declared by derived controls and custom elements are retained.

Resolved style output is intentionally diagnostic rather than exhaustive. It omits only obvious
no-effect values or fields made meaningless by another value, including:

- Background image placement, repeat, size, tint, and slice fields when there is no background image.
- A side's border color when that side's border width is zero.
- Zero border radius and transparent background color.
- Zero offsets for the default relative positioning mode.
- Transition fields when all transition durations are zero.
- Transform fields when the transform is identity.
- Text outline color when outline width is zero.
- Box-model values already present in the `layout` section.
- `display`, `visibility`, and `opacity`, which already appear in the `element` section.

Colors use quoted USS-compatible hex strings: `"#RRGGBB"` when opaque and `"#RRGGBBAA"` when
alpha is present.

#### UiSetValue (Value: 113)

Assigns a value to a value-bearing runtime field, such as `Toggle`, `Slider`, or `TextField`.
The current protocol implementation supports elements derived from `BaseField<T>`.

Request:

```json
{"requestId":"set-1","ref":"e1","value":"25%"}
```

`value` may be a JSON string, primitive, array, or object. Non-string JSON values are converted to
their compact JSON representation before parsing. The target must derive from `BaseField<T>`.
Unity assigns the converted value through the public `value` property, so normal value-change
notifications and `ChangeEvent<T>` behavior apply.

Successful assignment:

```json
{"requestId":"set-1","ok":true}
```

Supported input forms include:

| Target type | Accepted examples |
|-------------|-------------------|
| `string` | Any JSON string, including empty strings and escaped newlines |
| `bool` | `true`, `false`, `1`, `0`, `yes`, `no`, `on`, `off`, `checked`, `unchecked`, `enabled`, `disabled` |
| Integer types | `42`, `1_000`, `0x2A`, `0b101010` |
| `float`, `double`, `decimal` | `0.25`, `1.5e2`, `25%`, optional `f`/`d`/`m` suffix |
| Enum and flags | Case-insensitive names, numeric values, and names separated by `,`, `|`, or `+`; spaces, hyphens, and underscores in names are ignored |
| `Color`, `Color32` | `#RGB`, `#RGBA`, `#RRGGBB`, `#RRGGBBAA`, `rgb(...)`, `rgba(...)`, numeric component lists, and common color names |
| `Vector2/3/4`, `Vector2Int/3Int` | `[1,2]`, `(1, 2, 3)`, `x=1; y=2; z=3` |
| `Rect`, `RectInt` | `[x,y,width,height]` or a labeled four-component form |
| `Bounds`, `BoundsInt` | Six components: center/position followed by size |
| `Quaternion` | Four numeric components |
| `LayerMask` | Any supported integer form |
| `Guid` | Standard GUID strings |
| Other types | A public static `Parse(string, IFormatProvider)`, `Parse(string)`, or public string constructor, when available |

Null is accepted only for reference types and nullable value types. Unity object references are not
resolved from strings.

`UiSetValue` requires an attached and enabled element but does not require geometric visibility or a
hittable screen point. A public `isReadOnly=true` property causes `read_only`.

#### Game View Automation Error Codes

| Code | Meaning |
|------|---------|
| `invalid_request` | Malformed JSON, missing/invalid fields, invalid `depth`, forbidden screenshot path input, or a non-`BaseField<T>` set-value target |
| `not_playing` | The Editor is not in Play Mode, or Play Mode ended before queued capture |
| `unknown_ref` | The ref is not present in the current automation mapping |
| `stale_ref` | The mapped element is detached from its runtime panel |
| `not_visible` | A click or hover target is hidden or has no visible clipped area |
| `disabled` | The target is disabled in its hierarchy |
| `read_only` | `UiSetValue` targeted a field whose public `isReadOnly` property is true |
| `not_hittable` | No sampled point can hit the target or one of its descendants |
| `panel_missing` | No active runtime `UIDocument` is attached to a panel |
| `forbidden` | Non-loopback caller, or a forbidden hierarchy root |
| `invalid_value` | The supplied value cannot be converted or assigned to the field value type |
| `unsupported_value_type` | No safe string conversion is available for the field value type |
| `capture_failed` | Unity could not begin Game View capture |
| `capture_timeout` | The PNG did not become complete within 10 seconds |
| `write_failed` | The screenshot directory or file could not be written |
| `internal_error` | An unexpected automation exception occurred |

### Editor State (Values: 114-117)

These messages report and change Editor state that is not part of the Game View: the open scenes and
the selected locale. They use the same request and response envelope as Game View automation
(loopback only, opaque `requestId`, response reuses the request message type, automatic TCP fallback),
and the same error shape.

Unlike Game View automation, each message has its own mode requirement:

| Message | Value | Edit Mode | Play Mode |
|---------|-------|-----------|-----------|
| `SceneList` | 114 | Yes | Yes |
| `SceneOpen` | 115 | Yes | No, returns `is_playing` |
| `LocaleList` | 116 | No, returns `not_playing` | Yes |
| `LocaleSelect` | 117 | No, returns `not_playing` | Yes |

#### SceneList (Value: 114)

Request:

```json
{"requestId":"scene-1"}
```

Success returns the `scenes` property, a YAML document describing every scene that is currently open
in the Editor, in hierarchy order:

```yaml
mode: Edit
activeScene: "Assets/Scenes/Main.unity"
scenes:
  - name: "Main"
    path: "Assets/Scenes/Main.unity"
    isActive: true
    isLoaded: true
    isDirty: false
    isSubScene: false
    buildIndex: 0
    rootCount: 12
```

- `mode` is `Edit` or `Play`.
- `activeScene` is the path of the active scene, or `null` when there is none.
- `path` is the project relative asset path, and is empty for a scene that was never saved.
- `buildIndex` is `-1` when the scene is not in the build settings.
- `rootCount` is omitted for a scene that is open but not loaded, because it cannot be read.
- When no scene is open, `scenes: []` is emitted.

#### SceneOpen (Value: 115)

Only available in Edit Mode. Request:

```json
{
  "requestId":"scene-2",
  "path":"Assets/Scenes/Main.unity",
  "mode":"Single",
  "unsavedChanges":"refuse"
}
```

- `path` is required. Absolute paths are accepted and normalized; a path outside the project or one
  that does not end in `.unity` returns `invalid_request`, and a path with no scene asset returns
  `not_found`.
- `mode` is optional and defaults to `Single`. It accepts `Single`, `Additive` and
  `AdditiveWithoutLoading`, matched case insensitively, and maps to Unity's `OpenSceneMode`.
- `unsavedChanges` is optional and defaults to `refuse`. It only applies to `Single`, the only mode
  that closes the open scenes and can therefore discard unsaved work:
  - `refuse` returns `unsaved_changes` and names the modified scenes.
  - `save` saves every modified open scene first, and returns `save_failed` if Unity could not.
  - `discard` opens the scene and loses the unsaved changes.

Unity never shows a save dialog for this request, because a modal dialog would block automation.

Success returns the same `scenes` YAML as `SceneList`, describing the state after the scene was
opened.

#### LocaleList (Value: 116)

Only available in Play Mode, because the runtime locale list is only populated while playing.
Request:

```json
{"requestId":"locale-1"}
```

Success returns the `locales` property:

```yaml
selectedLocale: "zh-Hans"
locales:
  - code: "en"
    name: "English"
    sortOrder: 0
  - code: "zh-Hans"
    name: "Chinese (Simplified)"
    sortOrder: 10
```

- `selectedLocale` is the identifier code of the selected locale, or `null` when none is selected.
- When there are no locales, `locales: []` is emitted.

This package has no dependency on the Localization package. It reads
`UnityEngine.Localization.Settings.LocalizationSettings` through reflection, so a project without
com.unity.localization keeps working and only receives `localization_unavailable` for this message.

#### LocaleSelect (Value: 117)

Only available in Play Mode. Request:

```json
{"requestId":"locale-2","code":"zh-Hans"}
```

`code` is required and is matched case insensitively against the locale identifier code first, and
against the locale name second. Unity assigns `LocalizationSettings.SelectedLocale`, so the normal
selected locale changed notifications apply.

Success returns the same `locales` YAML as `LocaleList`, so the new selection is visible immediately.

#### Editor State Error Codes

In addition to the shared `invalid_request`, `forbidden` and `internal_error` codes:

| Code | Meaning |
|------|---------|
| `is_playing` | `SceneOpen` was requested while the Editor is in Play Mode |
| `busy` | Unity is compiling scripts or importing assets and cannot open a scene |
| `not_found` | No scene asset exists at the requested path |
| `unsaved_changes` | Opening the scene in `Single` mode would discard unsaved changes, and `unsavedChanges` is `refuse` |
| `save_failed` | Unity could not save the modified scenes, so the scene was not opened |
| `not_playing` | A locale message was requested outside of Play Mode |
| `localization_unavailable` | com.unity.localization is not installed, or the project has no Localization Settings asset |
| `unknown_locale` | No available locale matches the requested code or name |

### Scene and GameObject Inspection (Values: 118-121)

These messages report what is inside the open scenes. They use the same request and response envelope
as Game View automation (loopback only, opaque `requestId`, response reuses the request message type,
automatic TCP fallback), and the same error shape. All four work in both Edit Mode and Play Mode,
because reading the hierarchy never changes it.

**Addressing a GameObject.** Every GameObject is reported with its instance `id`, and every message
that targets one accepts either `id` or `path`:

- `id` is a signed integer that is stable for as long as the object lives in the current session, and
  is the only unambiguous way to name an object. It does **not** survive a domain reload (script
  recompilation) or entering and leaving Play Mode; after either, look the object up again. The
  instance id of a component is accepted too and resolves to the GameObject it is attached to.
- `path` is the slash separated chain of names from the scene root down, for example
  `Canvas/Panel/Button`. A leading slash is tolerated. **A path is always matched in full and case
  sensitively** - there is no partial or fuzzy form of it. A GameObject whose name contains a slash
  cannot be addressed by path at all, which is one reason ids exist.
- When a path matches more than one object the request fails with `ambiguous_path`, and the error
  message lists the candidates with their ids so the next call can be exact:

  ```
  3 GameObjects match path 'Canvas/Panel/Button'. Retry with one of these ids:
    id=-4312 scene="Assets/Scenes/Main.unity" path="Canvas/Panel/Button"
    id=-5108 scene="Assets/Scenes/Main.unity" path="Canvas/Panel/Button"
    id=-6002 scene="Assets/Scenes/UI.unity" path="Canvas/Panel/Button"
  ```

  At most 20 candidates are named.
- An optional `scene` narrows the search to one scene, matched case sensitively against the scene's
  asset path or its name.

**What is searched.** Every open and loaded scene, plus, in Play Mode, the `DontDestroyOnLoad` scene,
which holds the objects that survive scene loads and which `SceneList` never reports. Objects flagged
`HideFlags.HideInHierarchy` are editor internals and are skipped everywhere.

**Response size.** Hierarchies are capped on three axes: at most 20 children are listed per object, at
most 50 root objects per scene, and at most 200 objects in total. The total budget is enforced by
lowering the depth *before* anything is written, so the answer is a shallower complete tree rather
than a truncated deep one.

#### SceneHierarchy (Value: 118)

Request:

```json
{"requestId":"go-1","scene":"Assets/Scenes/Main.unity","depth":3}
```

- `scene` is optional and defaults to the active scene.
- `depth` is optional and must be a non-negative integer. `0` returns only the root objects, `1` adds
  their direct children, and so on. The effective depth is the lower of the requested one and the one
  the 200 object budget allows.

Success returns the `hierarchy` property:

```yaml
scene: "Assets/Scenes/Main.unity"
mode: Edit
maxDepth: 3
depthLimit: "dynamic"
rootCount: 87
rootsOmitted: 37
gameObjects:
  - "Main Camera" [id=-4312]
  - "Canvas" [id=-4320]:
    - "Panel" [id=-4330] [active=false] [omittedChildCount=63]:
      - "Button 01" [id=-4340]
    - "Overlay" [id=-4331] [omittedChildCount=8]
```

- `scene` is the asset path, falling back to the name for a scene that was never saved and for
  `DontDestroyOnLoad`.
- `maxDepth` is the depth that was actually written.
- `depthLimit` is `dynamic` or `requested`, and **appears only when the depth actually cut something
  off**. It is the single place the depth limit is explained; individual objects only carry
  `omissionReason="depth_limit"`.
- `rootsOmitted` appears only when the scene has more than 50 root objects.
- Each object carries its name and `id`. `active` appears only when the object is inactive, and
  reports its own state rather than the resolved one.
- `omittedChildCount` is the only thing an object says about its missing children, and appears only
  when some are missing. It needs no accompanying reason: an object stopped by the depth limit lists
  no children at all, while one stopped by the child limit lists the first 20 and counts the rest,
  and the depth itself is stated once in the header. No comments are ever emitted.
- A scene with no visible roots emits `gameObjects: []`.

#### GameObjectHierarchy (Value: 119)

Request:

```json
{"requestId":"go-2","id":-4320,"depth":2}
```

Exactly one of `id` or `path` is required; `scene` and `depth` behave as above.

Success returns the same `hierarchy` YAML, with the requested object as the single root, `path` added
after `scene`, and `rootCount` and `rootsOmitted` omitted:

```yaml
scene: "Assets/Scenes/Main.unity"
path: "Canvas/Panel"
mode: Edit
maxDepth: 2
gameObjects:
  - "Panel" [id=-4330]:
    - "Button" [id=-4331]
```

#### GameObjectFind (Value: 120)

Request:

```json
{"requestId":"go-3","name":"Button","match":"contains","scene":"Assets/Scenes/Main.unity"}
```

- Exactly one of `name` or `path` is required.
- `match` is optional and defaults to `exact`. `contains` matches a substring and **applies to `name`
  only**; combining it with `path` returns `invalid_request`, because a path that is not matched in
  full is not a path. Both modes are case sensitive.
- `scene` is optional and narrows the search to one scene.

Success returns the `gameObjects` property:

```yaml
query: "Button"
queryKind: "name"
match: "contains"
count: 3
gameObjects:
  - name: "Button"
    id: -4312
    scene: "Assets/Scenes/Main.unity"
    path: "Canvas/Panel/Button"
    active: true
    activeInHierarchy: true
    componentCount: 4
```

- `count` is the real number of matches. At most 100 are listed, and `matchesOmitted` appears when
  some were left out.
- No match is not an error: `count: 0` and `gameObjects: []` are returned.

#### GameObjectInspect (Value: 121)

Request:

```json
{"requestId":"go-4","path":"Level/Player","fullDetailComponents":["Camera"]}
```

Exactly one of `id` or `path` is required, and `scene` narrows a path lookup.

`fullDetailComponents` is an optional array of component type names, matched case sensitively against
either the short type name (`Camera`) or the full one (`UnityEngine.Camera`). Those components report
every instance member they have, public and non-public, instead of the default selection described
below.

Success returns the `gameObject` property:

```yaml
name: "Player"
id: -4312
scene: "Assets/Scenes/Main.unity"
path: "Level/Player"
mode: Edit
active: true
activeInHierarchy: true
tag: "Player"
layer: 8
layerName: "Player"
isStatic: false
parentId: -4300
childCount: 3
prefab:
  status: Connected
  assetPath: "Assets/Prefabs/Player.prefab"
components:
  - type: "Transform"
    id: -4314
    detail: "common"
    members:
      eulerAngles: [0,0,0]
      localEulerAngles: [0,0,0]
      localPosition: [0,1,0]
      localScale: [1,1,1]
      lossyScale: [1,1,1]
      position: [0,1,0]
  - type: "MeshRenderer"
    id: -4315
    detail: "common"
    members:
      receiveShadows: true
      sharedMaterial: "Material(name=Rock,instanceId=-8801)"
  - type: "PlayerController"
    id: -4316
    enabled: true
    members:
      speed: 5
```

- `parentId` is omitted for a root object.
- `prefab` is omitted entirely unless the object is part of a prefab instance.
- The transform is a component like any other and is listed first, as Unity always returns it first.
- Each component reports `type`, its own instance `id`, `enabled` when it is a `Behaviour`, `members`,
  and sometimes `detail`.
- `members` is sorted by name and holds instance fields and readable properties. Members declared by
  `Component`, `Behaviour`, `MonoBehaviour` and `UnityEngine.Object` are always left out, because they
  are identical noise on every component. Which of the rest are reported depends on `detail`:

  | `detail` | Meaning |
  |----------|---------|
  | absent | Every public instance member. This is what user scripts and less common components get. |
  | `common` | A curated selection for a well known built-in component, roughly what the inspector shows. Reflecting over `Camera`, for example, yields around sixty members including several matrices, which buries the dozen values that describe how the camera is set up. Ask for the component in `fullDetailComponents` to see the rest. |
  | `full` | Every instance member, public and non-public, because `fullDetailComponents` named this component. |

  Curated lists exist for the transforms, renderers, colliders, rigidbodies, `Camera`, `Light`,
  `MeshFilter`, `Animator`, `AudioSource`, `Canvas`, `CanvasGroup`, `UIDocument` and the common uGUI
  components, and a type inherits the lists of its base types.
- Members that Unity marks obsolete are skipped, and so are the getters that create an object as a
  side effect (`material`, `materials`, `mesh`); their `shared` counterparts carry the same
  information without dirtying the scene.
- A value that references another Unity object is written as
  `"Type(name=X,instanceId=N)"`, so an id from an inspection can be fed straight back into a request.
  Collections list at most 20 items and then summarize the rest.
- A member whose getter throws is reported as `"<error: ExceptionType: message>"` rather than failing
  the request, and a component whose script is missing appears as `type: "<missing script>"`.

#### Scene and GameObject Inspection Error Codes

In addition to the shared `invalid_request`, `forbidden` and `internal_error` codes:

| Code | Meaning |
|------|---------|
| `not_found` | No open scene, GameObject or instance id matches the request |
| `ambiguous_path` | The path matches more than one GameObject; the message lists the candidate ids |

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
