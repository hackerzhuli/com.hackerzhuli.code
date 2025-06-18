## Description
This is a fork of Unity's Visual Studio Editor package for Unity Editor.

The difference between this fork and the official package is that this fork adds support for popular Visual Studio Code forks.

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
- I don't personally test Visual Studio, but since the code is same as the official package, it should work as expected.

## Guidelines
- Try not to change unrelated files and keep the code in sync with the official package.

For pull requests:
- Try to keep the code changes minimal.
- Please make sure you test the changes before you submit (only on the platform for which you have changed the code)

## Installation
Uninstall the official package, and install this fork in Package Manager.

Check out this awesome screenshot, every popular C# code editor detected by Unity Editor on Windows:
![image](Images/Unity%20Editor%20External%20Script%20Editor%20Detection.png)