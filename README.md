# 拾放 ShelfDock

独立开发的 Windows 拖放暂存工具，与 Yoink 无关联。当前源码版本为 1.18。

## 使用

运行环境：Windows 10 / 11，.NET Framework 4.8。安装器和便携程序通过 Releases 分发。

## 构建

在 Windows PowerShell 中进入 src 文件夹，运行 .\build.ps1。脚本调用 Windows .NET Framework C# 编译器，运行自动检查并生成程序和安装器。无需第三方 NuGet 包。

## 更新记录

各版本变化请见 [CHANGELOG.md](CHANGELOG.md)，每个版本也有独立 GitHub Release。

## 历史版本

保存了 1.0 至 1.17 共 18 个版本的原始程序。1.0 仅保留便携版，1.01 至 1.17 保留安装版和便携版。完整源码快照现存版本为 1.12、1.16、1.17、1.18。未保留其他版本的完整源码和原始 Git 开发记录。历史 Release 的标签用于发布归档，不代表存在对应版本的历史源码提交。

## 数据

个人数据位于 %LOCALAPPDATA%\ShelfDock；文件暂存仅记录原文件路径。迁移原文件需另行处理。

## 1.18 安装与卸载

安装版默认安装到 `C:\Program Files\ShelfDock`，需要管理员权限；目录内提供带界面的 `Uninstall.exe`，也可从开始菜单或 Windows“已安装的应用”进入卸载。

“完全卸载”会删除安装文件、本账户的设置、暂存记录、缓存、快捷方式、开机启动项和应用注册表项。暂存列表引用的外部原文件不会删除。

若旧卸载程序在主程序退出后仍提示安装目录被占用，请先关闭所有旧卸载窗口，再运行 1.18 Release 中卸载修复包里的 `Uninstall.exe`，无需重新安装。
