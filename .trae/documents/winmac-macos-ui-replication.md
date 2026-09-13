# WinMac —— Windows 11 复刻 macOS 桌面的一站式方案

## 0. 摘要（Summary）

开发一个常驻 Windows 11 桌面的单进程程序 **WinMac**，用一套统一方案替代「MyDockFinder + MacType + Rainmeter」三类工具：

| 参考工具 | WinMac 对应模块 | 初版范围 |
|---|---|---|
| MyDockFinder | **WinMac.Dock**（底部 Dock）+ **WinMac.Launchpad**（启动台） | Dock 完整版、Launchpad 基础版 |
| Rainmeter | **WinMac.Widgets**（桌面小组件 + 内置时钟/CPU/内存/日历） | 内置件 + 轻量皮肤格式 |
| MacType | **WinMac.Text**（自带 UI 的 macOS 风格字渲染） | 仅自身 UI；全局注入器**单独立项**（Milestone 5） |

- 技术栈：**C# / .NET 8 / WinUI 3（Windows App SDK）+ Win32 互操作**，单进程多窗口（Dock / Launchpad / Widgets / 设置 / 停靠提示）。初版对多窗口叠加层与托盘用少量 P/Invoke 打底。
- **不本地部署**：开发机为 macOS（无 Windows SDK / WinAppSDK / dotnet），所有编译与打包都在 **GitHub Actions（`windows-latest`）** 完成，出「非打包自包含」exe，见 §2.7。
- 硬性约束：**低内存、高性能**（事件驱动、GPU 合成、缓存池、懒加载，见 §4）。
- 工作目录当前为空 → 全新搭建解决方案。

> 重要边界：**MacType 等价物（对所有程序全局替换字体渲染）本质是“注入原生 DLL + 挂钩 GDI/DirectWrite + FreeType 光栅化”**，托管 C# 无法实现且需管理员权限，等同于第二个原生项目。经与你确认：初版只做 **WinMac 自带 UI 的 macOS 风格字渲染**，全局注入器作为独立的原生（C++）子项目另立项，不在本初版范围。

## 1. 现状分析（Current State Analysis）

- 目录 `/Users/jayqqaa12/Documents/work/win_mac/` 为空，无任何代码/历史，是全新（greenfield）项目。
- 开发机为 macOS，**WPF/.NET 桌面程序无法在 macOS 上运行**；本方案所有构建与验证都面向 Windows 11（需 Windows 环境或 CI，见 §7）。方案内的代码结构、接口、配置文件在设计时保持平台清晰，便于本机（Mac 上）评审，但**编译/运行只支持 Windows**。
- 参考实现要点（已调研确认）：
  - MyDockFinder：图标/文字渲染用 **Direct2D + D3D11 + DirectWrite + WinUI Composition**；关键能力 = 放大镜动画、运行指示点、悬停窗口预览（DWM thumbnail）、最小化入 Dock（genie/scale/suck 动画）、拖拽入 Dock、多显示器、多种隐藏模式、上下文菜单。
  - MacType：DLL 注入（Detours/EasyHook）挂钩 GDI32+DirectWrite，用 FreeType 光栅化 + LRU 缓存；macOS 观感 = **灰度 AA（无 ClearType 亚像素彩边）+ 更高 gamma + 更低 contrast + 少 hinting**。
  - Rainmeter：独立透明窗口 + 皮肤脚本 + 可拖拽/缩放。

## 2. 决策与假设（Assumptions & Decisions）

1. **框架用 WinUI 3（Windows App SDK / .NET 8），与 MyDockFinder 同源**：原生支持 Win11 云母/亚克力模糊、圆角与高性能 GPU 合成，观感最接近 macOS。WinUI 3 对多窗口叠加层（WS_EX_TOOLWINDOW/LAYERED/TRANSPARENT 穿透）与拖盘支持较弱，由少量 Win32 P/Invoke 打底（见 §2.8），作为初版的工程代价。
2. **单进程多窗口**（Shell 一个 exe 内创建 Dock / Launchpad / 各 Widget / 设置窗口），共享图标缓存、配置与主题上下文 → 内存与进程开销最低。
3. **组件渲染走 WinUI 3 Composition / Visual + 图标位图池**：放大镜 = 以鼠标 X 为轴心对邻近图标施加 Scale 合成变换（GPU 组合层、GPU 虚拟化），图标位图缓存池复用，避免每帧重贴图/重采样。若测得 4K 放大镜不达标，再局部换 Direct2D 作性能后手。
4. **文字引擎初版 = 灰度 AA + GPU 着色器曲线（gamma/softness）**：WinUI/WPF 文本 + 一个轻量 ShaderEffect 做 gamma/对比/柔化（GPU、零分配、低内存），对停靠提示与组件标签生效，观感接近 macOS 灰度质感。**不依赖第三方字体**（SF Pro 不可再分发）；提供可选系统/等宽点缀字体回退链。
5. **事件驱动代替轮询**：前台/窗口/最小化状态用 `SetWinEventHook`（EVENT_SYSTEM_FOREGROUND/MINIMIZESTART/END/CLOSE），CPU/内存采样用低频定时器（默认 2 秒一次、可关），不整夜空转。
6. **MVP 即“可完整日常使用”**：Dock 完整版 + Launchpad 基础 + 时钟/CPU/内存/日历组件 + 设置 + 主题(浅/深) + 开机自启 + 单实例。全局字体注入器、窗口预览 DWM thumbnail、高级皮肤脚本（Lua）等列为后续。
7. **构建与分发 = GitHub Actions**：`windows-latest` runner 支持 WinUI 3 编译与打包。初版走**非打包自包含**（csproj `WindowsPackageType=None`，WinAppSDK 自包含），产物为 zip/安装包，附在 workflow artifact 或 tag Release。MSIX（开发者模式 + 证书）可作为后续分发选项。工作流见 `.github/workflows/build.yml`（§7）。

## 3. 解决方案结构（Proposed Changes + 文件）

新建 **`.NET 8 + WinUI 3`** 解决方案 `WinMac.sln`，目录布局：

```
win_mac/
├─ WinMac.sln
├─ src/
│  ├─ WinMac.Core/            # 类库：配置、Win32 互操作、服务、模型
│  │  ├─ Configuration/       #   AppConfig.cs, ConfigStore.cs (JSON, %LocalAppData%\WinMac)
│  │  ├─ Win32/               #   NativeMethods.cs (P/Invoke 全集)
│  │  ├─ WindowInterop/       #   WindowEnumerator, ForegroundWindowTracker(WinEventHook),
│  │  │                       #   MinimizeTracker, ShellHookService, MonitorService
│  │  ├─ Model/               #   AppEntry, DockIcon, WindowInfo
│  │  ├─ Services/            #   AppRegistryService, IconCache, ProcessHelper,
│  │  │                       #   ThemeManager, RunningAppResolver, SingleInstance(Mutex),
│  │  │                       #   AutoStartService, IdleService
│  │  └─ Diagnostics/         #   WinMacPerfLogger (内存/帧率采样，自检用)
│  ├─ WinMac.Text/            # 类库：灰度 AA + shader 曲线字渲染
│  │  ├─ MacTextRenderer.cs   #   面向 TextLayout 的渲染入口
│  │  ├─ MacTextBlock.cs      #   替换 TextBlock 的控件（用于停靠提示、组件标签）
│  │  ├─ SoftenEffect.cs      #   ShaderEffect(gamma/softness/contrast)
│  │  └─ TextProfile.cs       #   macOS light/dark 曲线预设 + 用户可调
│  ├─ WinMac.UI/              # 类库：主题样式、控件、托盘、设置窗口
│  │  ├─ Themes/              #   浅/深色资源字典（macOS 观感配色/圆角/模糊)
│  │  ├─ Controls/            #   AcrylicPanel(背景模糊), CapsuleButton...
│  │  ├─ Settings/            #   SettingsWindow.cs/xaml (侧栏 System-Settings 风格)
│  │  └─ Tray/                #   TrayIcon.cs, TrayMenu
│  ├─ WinMac.Dock/            # 类库：Dock + 启动台窗口
│  │  ├─ DockWindow.cs/xaml   #   全宽停靠条(WS_EX_LAYERED+TOOLWINDOW, 每像素alpha)
│  │  ├─ DockIconView.cs/xaml #   单图标项（Scale/RunIndicator/Tooltip 触发）
│  │  ├─ Magnification.cs     #   放大镜：由光标 X 计算各图标 scale/offset
│  │  ├─ DockTooltipWindow.cs #   停靠提示（MacTextBlock 渲染）
│  │  ├─ DockMenuWindow.cs    #   上下文菜单（启动/退出/隐藏/偏好）
│  │  ├─ MinimizeToDock.cs    #   最小化→Dock(genie/scale) 动画 + 恢复
│  │  ├─ DockDragDrop.cs      #   拖拽图标排序/拖入 exe/快捷方式/文件夹/文件
│  │  ├─ LaunchpadWindow.cs/xaml # 全屏 App 网格
│  │  └─ dwm/PreviewThumbnail.cs # (预留) DWM thumbnail 窗口预览
│  ├─ WinMac.Widgets/         # 类库：组件宿主 + 内置件 + 轻量皮肤
│  │  ├─ WidgetWindow.cs/xaml #   独立透明可拖拽/缩放的组件窗口
│  │  ├─ Widgets/             #   ClockWidget, CpuWidget, MemWidget, CalendarWidget
│  │  ├─ Skin/                #   SimpleSkinParser.cs (JSON 描述→布局)，Rainmeter 皮肤转初版子集
│  │  └─ Sensor/              #   SystemSampler(轻量 CPU/内存采样, 低频)
│  └─ WinMac.Shell/           # WinUI3 单工程(exe)：入口，WindowsPackageType=None
│     ├─ App.xaml.cs / MainWindow / MainController.cs (引导、单实例、模块生命周期)
│     └─ (引用上述所有类库)
├─ .github/workflows/build.yml # windows-latest 编译 + 打包 + 发布 artifact/Release
└─ tests/ (可选 .NET 单测，覆盖 Core 纯逻辑)
```

### 关键文件职责与怎么实现（why / how）

- **WinMac.Core/Win32/NativeMethods.cs**：集中 P/Invoke——`EnumWindows`/`GetWindowThreadProcessId`/`GetWindowText`、`SetWindowLongPtr`（加 `WS_EX_TOOLWINDOW`+`WS_EX_LAYERED`、穿透 `WS_EX_TRANSPARENT`）、`SetWindowPos`、`DwmGetWindowAttribute`、`ShowWindow`/`IsIconic`、`MonitorFromPoint`/`GetMonitorInfo`、`SystemParametersInfo`（DPI）、启动项注册表（`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`）。
- **WinMac.Windows.ForegroundWindowTracker**：`SetWinEventHook`（Foreground/Minimize/Close）+ 内存窗口接管队列，仅事件触发，不计时轮询 → Dock 运行指示点/最小化动画实时且省电。
- **WinMac.Dock/Magnification.cs**：对 Dock 图标数组，某个图标距鼠标的水平距离越小 scale 越大；逐帧仅更新受影响图标的 `RenderTransform`，开启 `BitmapCache`，光标静止时冻结（`IsFrozen`）→ 平滑且低 CPU。
- **WinMac.Dock/DockIconView**：图标 Image 用 **IconCache** 输出（从 exe 提取原图标 → 下采样为 64/128 两档位图池，LRU 复用），运行指示点用几何形状（非贴图）。
- **WinMac.Text/MacTextBlock**：组合 WPF `TextBlock` + 灰度 AA + `SoftenEffect`，给出 mac 质感渲染参数默认值（gamma≈1.8, contrast≈1.0, softness 0x）供配置。
- **WinMac.Widgets/WidgetWindow**：独立无边框透明窗口，Capthicanel 绘制可调闕存；`SystemSampler` 低频采 CPU/RAM。
- **WinMac.Shell/MainController**：启动顺序 = 单实例校验 → 载配置 → 初始化图层/托盘 → 按配置创建 Dock/Launchpad/Widgets。全部模块懒加载，未启用模块不占窗口与线程。

## 4. 性能与低内存策略（Performance & Memory）

- **单进程多窗口**，绝不每模块开独立 exe → 省去进程/CLR 开销。
- **事件驱动**：前台窗口、最小化、窗口关闭全部走 `SetWinEventHook` 回调，杜绝轮询。
- **图标/位图池 + LRU**：图标一次提取下采样，两档缓存复用；工具栏与浏览器缩略图按需生成，闲置即释放。
- **合成层 GPU**：放大镜/软效果走 WPF 组合层（DirectX under the hood）+ `BitmapCache`，避免每帧重贴图。
- **GC 调优**：禁用 Server GC（桌面场景默认工作站 GC），关掉不必要的 `AppContext` 特性；绑定一律 `x:Bind/Compiled`（XamlC）减少反射箱分配。
- **懒加载 + 可禁用模块**：各模块窗口按配置创建，关闭即销毁；CPU/内存采样默认低频且可关。
- **自检 `WinMacPerfLogger`**：内置肉观察内存峰值 + 放缩镜帧时间采样，用于回归对比，不改其则不下调。

## 5. MVP 里程碑（Milestone）

- **M0 脚手架(+CI 绿)**：`WinMac.sln` + `WinMac.Core`(纯 .NET)/`WinMac.Text`/`WinMac.Shell`(WinUI3 非打包) 骨架；**GitHub Actions 在 `windows-latest` 上把非打包自包含 exe 编译打包产出 artifact 并成功**；Core 配置/单实例/托盘/自启可用。（本机无 Windows 环境，M0 的"编译通过"由 CI 验证。）
- **M1 主题 + 文字引擎 + 设置**：浅/深主题、`MacTextBlock`+`SoftenEffect` 出 mac 质感、设置窗口可调 gamma/contrast/softness。
- **M2 Dock 完整版**：放大镜、运行指示点、停靠提示（`MacTextBlock`）、上下文菜单、启动/退出、拖拽排序与拖入、最小化入 Dock（genie）+ 恢复、多显示器定位、Auto-Hide/Smart-Hide（预留 DWM 窗口预览）。
- **M3 Launchpad**：全屏 App 网格，搜索框，点击启动。
- **M4 Widgets**：时钟/CPU/内存/日历四内置件 + `WidgetWindow` 拖拽缩放 + 简单 JSON 皮肤。
- **M4 完成 = MVP 可用**。任何里程碑内不引入全局注入器。（后续 Milestone 5 独立立项：原生 C++ 注入 DLL + FreeType + 管理员安装器，本方案不展开。）

## 6. 依赖清单（NuGet / 包）

- `Microsoft.WindowsAppSDK`（WinUI 3 / Composition，NuGet 自动还原于 CI）。
- `Microsoft.Windows.SDK.BuildTools`（CI 编译所需 SDK 构建工具）。
- 配置序列化：`System.Text.Json`（内置）。
- **托盘（WinUI 3 框架内无原生方案）**：用 `Microsoft.Toolkit.Uwp.Notifications` 不适用；初版用轻量 Win32 `Shell_NotifyIcon` P/Invoke 自封装（减少第三方依赖与内存）。
- 不引入 Vortice/DirectWrite 原生绑定（见决策 4）；不引入字体库、不引入脚本引擎占大内存（Rainmeter 皮肤用 JSON 子集，不做 Lua）。

## 7. 构建 / 验证（GitHub CI）

> 开发机为 macOS，无 Windows SDK，本程序**完全不本地编译**；构建被封装在 GitHub Actions。`.github/workflows/build.yml` 需在你的 GitHub 仓库启用后运行。

- **任务 `build`（push/PR/手动）**：`windows-latest` → `dotnet restore` → `dotnet build -c Release`（`WindowsAppSDKSelfContained=true` 出自包含） → 拷贝产物 + WinAppSDK 运行时 → 压缩为 `WinMac-x64.zip` → 上传 workflow artifact。
- **任务 `release`（push tag v*）**：复用 build 产物 → 创建 GitHub Release + 附加 zip（可后续换成 Inno Setup 单安装包）。
- **本地验证（Windows，可选）**：`dotnet build WinMac.sln -c Release /p:WindowsAppSDKSelfContained=true`。
- **M0 验收 = 该 workflow 首次在 `windows-latest` 跑绿，并能从 artifact/Release 下载到可运行的 `WinMac.exe`。**
- **功能验收（可见于真实 Windows）**：M1–M4 的交互项（走查表见下）需由你在 Windows 上运行 CI 产物验证。

### M1–M4 走查表
- **M2 Dock**：放大镜顺滑（帧率自检 >50）、运行指示点随窗口增删、Dock 启动/退出应用、拖入/拖拽排序、最小化 genie 到对应图标并可恢复；任务栏可隐藏被替代。
- **M1 文字**：设置里调 gamma/contrast，停靠提示与组件标签观感随之变化、接近 macOS 灰度。
- **M3 Launchpad**：网格、搜索过滤、启动应用。
- **M4 Widgets**：四组件跨屏、拖拽缩放正确、CPU/内存低频刷新无卡顿。
- **内存回归**：连续开关应用 + 长时间挂机，`WinMacPerfLogger` 内存峰值稳定、不明显泄漏。