# 拾放 ShelfDock

独立开发的 Windows 拖放暂存工具，与 Yoink 无关联。当前源码版本为 1.17。

## 使用

运行环境：Windows 10 / 11，.NET Framework 4.8。安装器和便携程序通过 Releases 分发。

## 构建

在 Windows PowerShell 中进入 src 文件夹，运行 .\build.ps1。脚本调用 Windows .NET Framework C# 编译器，运行自动检查并生成程序和安装器。无需第三方 NuGet 包。

## 更新记录

各版本变化请见 [CHANGELOG.md](CHANGELOG.md)，每个版本也有独立 GitHub Release。

## 历史版本

保存了 1.0 至 1.17 共 18 个版本的原始程序。1.0 仅保留便携版，1.1 至 1.17 保留安装版和便携版。完整源码快照现存版本为 1.12、1.16、1.17。未保留其他版本的完整源码和原始 Git 开发记录。历史 Release 的标签用于发布归档，不代表存在对应版本的历史源码提交。

## 数据

个人数据位于 %LOCALAPPDATA%\ShelfDock；文件暂存仅记录原文件路径。迁移原文件需另行处理。
