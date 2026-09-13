# WinMac

Windows 11 上复刻 macOS 桌面的一站式方案，替代 **MyDockFinder + MacType + Rainmeter**。

基于 **C# / .NET 8 / WinUI 3（Windows App SDK）**，单进程多窗口、低内存、事件驱动。开发环境为 macOS，**所有编译与打包由 GitHub Actions 在 Windows 上完成**，本仓库不本地部署。

## 方案文档

架构与里程碑见 `.trae/documents/winmac-macos-ui-replication.md`。

## 工程结构

```
src/WinMac.Core/   配置、Win32 互操作、单实例、自启等服务（纯 .NET）
src/WinMac.Text/   macOS 风格字渲染参数模型（灰度 AA + gamma/contrast 曲线）
src/WinMac.Shell/  WinUI 3 单工程入口（非打包自包含 exe）
.github/workflows/ GitHub Actions：Windows 编译 + 打包 + Release
```

## 打包（GitHub Actions）

1. 把本目录推送到你的 GitHub 仓库；
2. Actions → **build**：push/PR 触发，`windows-latest` 编译并打出 `WinMac-x64.zip`（非打包自包含，可直接解压运行）；
3. 推送 **tag** `v*`（如 `v0.1.0`）→ 自动生成 **Release** 并附加 zip。

产物说明：`WindowsPackageType=None` + `WindowsAppSDKSelfContained=true` → 产物自带 WinUI/WinAppSDK 运行时，**不依赖安装运行时**，解压即用。

## 本地构建（仅 Windows，可选）

```bat
dotnet restore WinMac.sln -p:Platform=x64
dotnet build  WinMac.sln -c Release -p:Platform=x64
dotnet publish src\WinMac.Shell\WinMac.Shell.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o publish -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None
```

## 里程碑

- [ ] M0 脚手架 + CI 绿：可下载到可运行的 `WinMac.exe`
- [ ] M1 主题 + 文字引擎 + 设置（gamma/contrast/softness 可调）
- [ ] M2 Dock 完整版（放大镜、运行指示点、停靠提示、上下文菜单、启动/退出、拖拽、最小化入 Dock、多显示器、Auto-Hide）
- [ ] M3 Launchpad（全屏 App 网格）
- [ ] M4 桌面小组件（时钟/CPU/内存/日历 + 轻量皮肤）