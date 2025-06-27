[中文版](READMEZH.md)

## ⚠️ Important Notice

**Messaging Protocol Changes**: This package uses a modified messaging port and protocol. Advanced integrations with Visual Studio Code forks that have the official Unity extension installed may be affected, if messaging with the Unity extension is needed(which we don't know if it uses the messaging protocol yet), you can install the official Visual Studio Editor package alongside this package. This does not affect debuggers.

**Recommendation**: For the best experience with VS Code forks, we recommend using our [Unity Code extension(source code only, awaiting release for Windows)](https://github.com/hackerzhuli/unity-code.git) which is specifically designed to work with this package.

## Description
This is a fork of Visual Studio Editor package for Unity that focuses on Visual Studio Code and its forks.

The difference between this fork and the official package is that this fork adds support for popular Visual Studio Code forks and alternative C# development extension Dot Rush (that is, alternative to C# Dev Kit) and Unity Code.

### VS Code Forks
Supported Visual Studio Code forks:
- Visual Studio Code (including Insiders)
- Cursor
- Windsurf (including Next)
- Trae

About the forks:
All forks are treated as if they are Visual Studio Code, but with different executable name, extension path(where their extensions are installed) and a few other minor differences.

Notes:
- If you use other forks, you need to add their support yourself, it's very easy, look at the file `VisualStudioCodeInstallation.cs` and add data for your forks to the array `Forks` (and that's it!). If you would like, you can create a pull request to share your changes.
- I can only personally test the VS Code forks on Windows, if you have issues on other platforms, you have to fix it yourself. If you would like, you can create a pull request to share your changes.
- **Visual Studio is not supported** by this package. This package focuses exclusively on Visual Studio Code and its forks.

### VS Code Extensions
The official package supports Visual Studio Tools for Unity extensions. Which allows you to debug Unity projects in Visual Studio Code.

### Dot Rush
This fork adds support for [Dot Rush](https://github.com/JaneySprings/DotRush). Dot Rush is a VS Code extension that supports C# IntelliSense, Test Explorer and Debugging, including with debugging Unity. Think of it as a replacement of the C# extension and C# Dev Kit extension from Microsoft.

The reason we need Dot Rush is because apparently the official C# Dev Kit extension (Which Visual Studio Tools for Unity depends on) from Microsoft doesn't support debugging in VS Code forks other than Visual Studio Code itself. So we need an alternative like Dot Rush in VS Code forks.

This package supports automatially patching the settings file and launch file for working with Dot Rush, if Dot Rush is detected for the VS Code Fork that you selected.

## Code Guidelines
- Try to keep code simple and well documented.

For pull requests:
- Try to keep the code changes minimal.
- Please make sure you test the changes before you submit (only on the platform for which you have changed the code)

## Installation
This package can be installed alongside the official Visual Studio Editor package without conflicts. Simply install this fork through the Package Manager by git url.

Check out this awesome screenshot, every popular C# code editor detected by Unity Editor on Windows:
![image](Images/Unity%20Editor%20External%20Script%20Editor%20Detection.png)

Happy debugging your Unity game with Dot Rush in your favorite VS Code fork:
![image](Images/Debug%20in%20Trae%20With%20Dot%20Rush.png)
