# Code Editor Package for Visual Studio

## [1.6.0] - 2026-08-02
Feature:
- Added `EcsVisualSnapshot` (129), a loopback-only Play Mode view of visible Entities Graphics bounds and `Parent` hierarchy using the same camera, pixel and depth semantics as the GameObject visual snapshot
- ECS integration now requires both Entities and Entities Graphics 1.4 or newer, and visual snapshots filter disabled, prefab, hidden, shadows-only and camera-layer-incompatible render Entities before returning at most 200 nearest matches
- Added loopback-only, read-only Play Mode ECS messages for listing Worlds and system trees, inspecting managed/unmanaged systems, querying entities by component/name, and inspecting component values with bounded recursive formatting
- Added an optional Entities integration assembly enabled by `com.unity.entities` 1.4 or newer through `CODE_ENTITIES_ENABLED`; the package keeps no hard Entities dependency and returns `entities_unavailable` when the integration is absent

## [1.5.3] - 2026-08-02
Fix:
- Restored UI inspection `matchedSelectors` on Unity 6.5 by supporting its generic selector matcher and new specificity representation while keeping protocol output stable across Unity versions
- Updated runtime theme coverage for Unity 6.5's built-in theme resource and its renamed `UIDocumentRootElement`

## [1.5.2] - 2026-08-02
Fix:
- Fixed the Unity 6.5 object id conversion to call `EntityId.ToULong` with its required `EntityId` argument

## [1.5.1] - 2026-08-02
Fix:
- Added Unity 6.5 compatibility for scene inspection by using the new object identifier APIs on Unity 6.5 and later while retaining the compatible APIs on older Unity versions
- Object ids in the scene inspection protocol are now opaque hexadecimal strings, so clients no longer depend on Unity's internal identifier type or representation

## [1.5.0] - 2026-08-01
Feature:
- Added an invoke message that calls a public static method of the project by name, in both edit and play mode, so a client can trigger game logic instead of only observing it; arguments are passed as strings and converted to the parameter types the same way a UI field value is, and a return value comes back as a string in the invariant culture
- Overloads are resolved without the client having to spell out parameter types: the candidates taking the right number of arguments are tried in a fixed order and the first one that accepts them is used, so the same request always reaches the same method
- A UI click can now use the right or middle mouse button through a new `button` property, whose values are Unity's own

Change:
- A click is now one implementation for every element and every button. A `Button` used to be activated through a submit event instead of pointer events, which was a second path that behaved differently and could not express a button at all; it now receives the same synthetic move, down and up as anything else, and its `Clickable` fires `clicked` exactly once for a left click
- Contextual menus still do not open on a runtime panel, because Unity only assigns a contextual menu manager to editor panels, so a right click delivers the pointer events and nothing more; this is documented in the protocol rather than worked around

## [1.4.0] - 2026-08-01
Feature:
- Added a visual snapshot message that reports what a camera is actually drawing: the 2D and 3D GameObjects that can be seen, each with the rectangle it covers on screen, as a hierarchy that also carries the ancestors those objects live under, so a client learns both what is on screen and where it sits in the scene
- Coordinates are whole pixels with the origin in the top left corner, matching both the `worldBound` of a UI inspection and the pixels of a Game View screenshot, and depth is expressed in the same unit through a single factor per camera, so an object's distance can be compared to its size
- Only the renderers that draw content are reported by default, since particles, trails, tilemaps and the like are decoration whose bounds say nothing about what the game is showing; asking for `renderers: "all"` brings them back

## [1.3.1] - 2026-08-01
Change:
- The messaging service now runs in every Unity Editor it is installed in, instead of only when this package is the current external script editor; extensions and MCP servers can talk to Unity without the user having to change that preference, while script opening and project file generation stay tied to the editor Unity is actually configured with
- A refresh of the asset database is no longer refused outright in play mode: when Unity's `Script Changes While Playing` preference is set to `Recompile After Finished Playing`, compilation is held off until play mode ends, so the refresh cannot lose the play session and is allowed to run; the other two settings would recompile immediately and are still refused, now with a message that says which setting is in the way

## [1.3.0] - 2026-08-01
Feature:
- Added scene and GameObject messages to see what is inside the open scenes: the GameObject tree of a scene, the descendant tree of one GameObject, a search for GameObjects by name or by exact path, and the properties and component members of one GameObject
- GameObjects are reported with an instance id that addresses them exactly in later requests, and a path that matches more than one object now returns the candidate ids instead of guessing which one was meant
- GameObject inspections never read the property getters that quietly instantiate a copy of a material or mesh, so looking at a scene can no longer dirty it

Change:
- UI hierarchies no longer repeat in a comment what the properties on an element already say, and the depth limit is explained once at the top of the document rather than on every element it truncates; `childrenOmitted` is gone, since `omittedChildCount` already says children are missing, and an element the depth limit stopped no longer carries an omission reason at all
- Built-in components report a curated selection of members by default, roughly what the inspector shows, instead of everything reflection can reach; naming the component in `fullDetailComponents` still gives the full picture

Fix:
- Fixed a reference to an object that can be enumerated, such as a `Transform` or a `VisualElement`, being reported as a list of its children instead of as a reference to it, which also affected UI inspections
- Fixed the items of a collection being reported as raw text, so an object reference inside one now carries its type, name and instance id like any other
- Fixed a `LayerMask` being reported as its type name rather than its value, and compiler generated members such as auto property backing fields showing up in inspections

## [1.2.4] - 2026-07-31
Fix:
- Fixed UI hierarchies skipping the built-in structure of composite controls, such as a `ScrollView` jumping straight to its content items and hiding its viewport, content container and scrollers; hierarchies now walk the real visual tree, while snapshots stay compact

## [1.2.3] - 2026-07-31
Fix:
- Fixed the `matchedSelectors` section of UI inspections being unavailable on Unity 6000.2 and later, where the editor helper it relied on no longer exists; it now runs Unity's own selector matcher, which is unchanged across 6000.0 to 6000.3

## [1.2.2] - 2026-07-31
Feature:
- Added a `matchedSelectors` section to UI inspections, listing every USS rule matching the element with its source, line and specificity, and marking which of its declarations survive the cascade and which are overridden, and by what

## [1.2.1] - 2026-07-31
Fix:
- Fixed UI snapshots leaving out content parented to the runtime panel root instead of a `UIDocument` root, such as an open dropdown menu; such elements now appear under a `PanelOverlay` entry

## [1.2.0] - 2026-07-30
Feature:
- Added scene messages to list the open scenes and to open a scene asset in Edit Mode
- Added locale messages to list the available locales and to change the selected one in Play Mode, without taking a dependency on the Localization package

## [1.1.0] - 2026-07-30
Feature:
- Added runtime UI Toolkit and Game View automation for snapshots, hierarchy inspection, pointer interaction, field value assignment, and screenshot capture

## [1.0.14] - 2026-03-10
Fix:
- Fixed zed open the workspace for the first time failed to recognize the file as a workspace file

## [1.0.13] - 2026-03-10
Feature:
- Add Zed editor support and fix Windows app discovery

## [1.0.12] - 2026-03-04
Merged the following changes from upstream:

Internal:
- Fixes for release validation and release process.

Integration:
- Fix Visual Studio Integration to properly wait for the solution to be opened.
- Fix handling of asset-pipeline refresh-mode setting.
- Remove support for `Visual Studio for Mac`. Please use `VS Code` going forward.
- Performance optimizations.

Project generation:
- Disable Workspace-based development feature in `settings.json`.
- Ensure that we only have one `sln` or `slnx` file at a time.
- Properly handle filenames with special characters in `link` tags.
- Add `EnableOnDemandExcludedFolderLoading` capability when generating SDK-Style project.
- Allow customization of `langversion` when using a `rsp` file.
- Move to `slnx` solution generation when using `SDK-Style` projects.
- Both `VS Code` and `Visual Studio 2026` are now using `SDK-Style` projects by default.

## [1.0.11] - 2026-03-04
Feature:
- Added support for Google Antigravity IDE
- Added VSCodium support
- Added configurations for Trae CN and Lingma

Fix:
- Replaced `FileInfo.LinkTarget` with libc `realpath()` for .NET Standard 2.1 compatibility

## [1.0.10] - 2025-08-07
Feature:
- Improved compile error handling and messaging, using compilation pipeline API instead of log message events to get compile errors, which is more reliable, and compile errors are sent immediately after compilation finishes
- Automatically removes USS-CSS association in `settings.json` when `Unity Code Pro` extension is installed, since we have native USS language server support
- Added file-based logger for debugging

## [1.0.9] - 2025-7-27
Feature:
- Added fuzzy test name matching support - append '?' to test filters to enable fuzzy matching using "ends with" comparison

Improved:
- Implemented test list caching per TestMode for improved performance and reduced API calls
- Added callback-based test list retrieval to send responses only to requesting clients instead of broadcasting to all connected clients

## [1.0.8] - 2025-7-25
Removed:
- Removed UXML schema catalog generation and XML validation features due to compatibility issues with the Red Hat XML extension for UXML files
- Removed CreateUxmlSchemaCatalog method and related XML catalog configuration
- Removed Red Hat XML extension recommendation from VS Code extensions

## [1.0.7] - 2025-7-25
Feature:
- Added CompileErrors message type (106) for collecting and retrieving Unity compilation errors
- Added Log class for JSON serialization of compile error information with Unix timestamp support
- Implemented automatic compile error collection within 1-second window after compilation finishes
- Added filtering for "error CS" messages to capture C# compilation errors specifically

## [1.0.6] - 2025-07-22
Feature:
- Added HasChildren property to TestAdaptor for better test hierarchy information
- Added compilation started notification (CompilationStarted message type) to provide complete compilation lifecycle visibility

Improved:
- Enhanced refresh protocol with client notification system - clients now receive confirmation when refresh operations complete
- Simplified asset refresh logic by removing unnecessary autoRefreshMode check for explicit refresh requests
- Better error handling in messaging protocol for refresh operations

## [1.0.5] - 2025-07-21
Feature:
- Added UXML validation and auto-completion support for Red Hat XML extension. Automatically generates XML catalog files and configures VS Code settings when the Red Hat XML extension is installed and UIElementsSchema directory exists.

Changed:
- USS files are no longer automatically associated with CSS to allow for our native USS language server support. 

## [1.0.4] - 2025-07-08
Fix:
- Changed `com.unity.test-framework` package to version `1.4.6` because some people may be using older versions of test framework, the new version `1.5.1` may not appear existing for some people, that can be a Unity version problem.

## [1.0.2] - 2025-07-05
Fix:
- Changed extesion id to `hackerzhuli.unity-code-pro` to match the new id on the marketplace.

## [1.0.1] - 2025-07-05

Documentation:
- Updated package description for clarity

Build:
- Updated dependencies to latest versions

Code Improvement:
- Removed unneeded copyright headers from files written from scratch
- Improved analyzer discovery from extensions in CodeInstallation
- Restructured GetAnalyzers method to support multiple extensions and avoid duplicate analyzer DLLs

## [1.0.0] - 2025-7-3

**Note:** This version represents a restart of the package versioning as this is now released as a new package `com.hackerzhuli.code` (previously `com.unity.ide.visualstudio`).

Integration:

- Added support for popular VS Code forks including Cursor Windsurf and Trae.
- Added support for Dot Rush extesion for VS Code, automatically add needed setting and launch options
- Added support for Unity Code extension for VS Code, automatically add launch options

Messaging Protocol:
- Improved existing messages (eg. testing related messages) to for better performance, and integration with external IDE
- Added new messages (eg. CompilationFinished, IsPlaying) for better development experience in external IDE
- Added MessagingProtocol documentation for easier development of external IDE extensions.

Code Improvement:
- Improve some code with better structure and documentation
- Changed VisualStudioIntegration core logic into a ScriptableObject CodeEditorIntegrationCore, to make code less error prone and make use of Unity lifecycle events and automatic state preservation through serialization and deserialization
- Improve code quality for some classes(eg. CodeEditorIntegrationCore) by making it single threaded to avoid problems.
  
Removed:
- Removed support for Visual Studio
