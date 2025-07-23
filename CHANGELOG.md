# Code Editor Package for Visual Studio

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
