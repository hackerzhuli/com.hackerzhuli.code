[English Version](README.md)

## 描述
这是Unity的Visual Studio Editor包的一个fork。

这个fork与官方包的区别在于，本fork添加了对一些受欢迎的基于Visual Studio Code的代码编辑器和C#开发扩展Dot Rush的支持。

### 支持基于VS Code的代码编辑器
支持基于Visual Studio Code的代码编辑器：
- Visual Studio Code（包括Insiders版本）
- Cursor
- Windsurf（包括Next版本）
- Trae

关于这些编辑器：
这些编辑器都被当作Visual Studio Code，只是有不同的可执行文件名、扩展路径（安装扩展的位置）和其他小的差异。

注意：
- 如果你使用其他基于VS Code的代码编辑器，你需要自己添加支持，非常简单，查看文件`VisualStudioCodeInstallation.cs`并将你的代码编辑器的数据添加到数组`Forks`中。如果你愿意，可以创建一个拉取请求来分享你的更改。
- 我个人只能在Windows上测试这些代码编辑器，如果你在其他平台上遇到问题，你需要自己修复。如果你愿意，可以创建一个拉取请求来分享你的更改。
- 我个人不测试Visual Studio，但由于代码与官方包相同，它应该能正常工作。

### VS Code扩展
官方包支持Visual Studio Tools for Unity扩展。这允许你在Visual Studio Code中调试Unity项目。

### Dot Rush
这个fork添加了对[Dot Rush](https://github.com/JaneySprings/DotRush)的支持。Dot Rush是一个VS Code扩展，支持C#代码高亮、补全、测试管理和调试功能，包括调试Unity。可以将其视为微软的C#扩展和C# Dev Kit扩展的替代品。

我们之所以需要Dot Rush，是因为微软官方的C# Dev Kit扩展（Visual Studio Tools for Unity依赖于它）不支持基于VS Code的代码编辑器（除VS Code本身）中进行调试。

如果你选择的基于VS Code的代码编辑器安装了Dot Rush扩展，此包支持自动修改设置文件(settings.json)和启动文件(launch.json)以方便使用Dot Rush开发项目。

## 代码规则
- 尽量不要修改不相关的文件，并保持代码与官方包同步。

对于拉取请求：
- 尽量保持代码修改最小化。
- 请确保在提交前测试修改（仅在你修改了代码的平台上）

## 安装
卸载官方包，并在Package Manager中安装此fork。

看这个非常牛的截图，Windows上的Unity编辑器检测到的所有流行C#代码编辑器：
![image](Images/Unity%20Editor%20External%20Script%20Editor%20Detection.png)

在你喜欢的基于VS Code代码编辑器中使用Dot Rush调试你的Unity游戏（图中为Trae）：
![image](Images/Debug%20in%20Trae%20With%20Dot%20Rush.png)