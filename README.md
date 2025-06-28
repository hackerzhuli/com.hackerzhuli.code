[中文版](READMEZH.md)

## ⚠️ Important Notice

This package is desinged for integration with my [Unity Code extension](https://github.com/hackerzhuli/unity-code.git) for VS Code. If you want to use it with the official Unity extension for VS Code(or a fork), it can work, but if there are any issues, you can try installing the offical package along with this package. Also my Unity Code extension is Windows only, but you can build from source for other platforms.

## Features
The purpose of this package is enable VS Code forks to have great Unity development experience, rivaling Visual Studio and Rider, working with my Unity Code extension for VS Code.

### Supported VS Code Forks
- Visual Studio Code (including Insiders)
- Cursor
- Windsurf (including Next)
- Trae

About the forks:
All forks are treated as if they are Visual Studio Code, but with different executable name, extension path(where their extensions are installed) and a few other minor differences.

Notes:
- If you use other forks, you need to add their support yourself, it's very easy, look at the file `VisualStudioCodeInstallation.cs` and add data for your forks to the array `Forks` (and that's it!). If you would like, you can create a pull request to share your changes.

### VS Code Extensions
This package is designed to work with Unity Code and Dot Rush extension for VS Code. But you can still use it with C# Dev Kit and Unity extension, it can work, but I don't officially support it.

#### Dot Rush
[Dot Rush](https://github.com/JaneySprings/DotRush) is a VS Code extension that supports C# IntelliSense, Test Explorer(not including Unity), Debugging(including with debugging Unity) and other features. Think of it as a alternative of the C# extension and C# Dev Kit extension.

#### Unity Code
[Unity Code](https://github.com/hackerzhuli/unity-code.git) is a VS Code extension that supports Unity Tests, debugging with Unity Editor, a Unity Console to see Unity's logs, and other useful things for Unity projects. Think of it as a alternative to the Unity extension. It is Windows only, but you can build from source for other platforms.

## Platform Support
- I only test on Windows, if you have issues on other platforms, you have to fix it yourself. If you would like, you can create a pull request to share your changes.

## Code Guidelines
- Try to keep code simple and well documented.

For pull requests:
- Try to keep the code changes minimal.
- Please make sure you test the changes before you submit (only on the platform for which you have changed the code)

## Installation
This package can be installed alongside the official Visual Studio Editor package without conflicts. Simply install this package through the Package Manager by git url.

## Screenshots
Check out this awesome screenshot, every popular C# code editor detected by Unity Editor on Windows:
![image](Images/Unity%20Editor%20External%20Script%20Editor%20Detection.png)

Happy debugging your Unity game with Dot Rush in your favorite VS Code fork:
![image](Images/Debug%20in%20Trae%20With%20Dot%20Rush.png)
