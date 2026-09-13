# WinMac 完整复刻 MyDockFinder + MacType + Rainmeter —— 分阶段实施计划

## Context（背景与目标）

当前项目已交付 MVP（M1–M4，对应 `.trae/documents/winmac-macos-ui-replication.md` 的初版范围），commit `a4627c5` CI 已绿。MVP 具备：Dock（首字母占位块 + 放大镜 + 运行指示点 + tooltip + 右键菜单 + Auto/SmartHide）、Launchpad（网格 + 搜索 + 启动）、时钟/CPU/内存/日历四 Widget + 拖拽、自带 UI 的 mac 观感文字渲染（SoftenEffect 去亚像素彩边）、主题、自启、单实例。

用户目标升级为：**完整对齐 MyDockFinder + MacType + Rainmeter 三款工具的全部能力**，而非停留在 MVP。

现状缺口（按参考软件归类）：
- **MyDockFinder**：真实图标（现为首字母占位块）、拖拽排序、拖入 exe/快捷方式/文件夹/文件、最小化入 Dock（genie）+ 恢复、DWM 窗口预览缩略图、多显示器定位。`TaskMonitor` 目前是 `EnumWindows` 轮询，`NativeMethods` 虽已声明 `SetWinEventHook` 但未应用。
- **MacType**：全局字体渲染 = 原生 C++ DLL 注入 + FreeType 光栅化 + 管理员安装器（托管 C# 无法实现）。
- **Rainmeter**：JSON 皮肤解析器（SimpleSkinParser 仅设计预留）、皮肤布局（位置/缩放）持久化、跨屏皮肤宿主窗口、配置界面。

技术约束（沿用）：开发机为 macOS 无法本地编译，全靠 GitHub Actions（windows-latest）编译验证；CI 现仅支持 .NET/WinUI3 构建，原生子项目需新增编译 job。

## 决策约定

- 按 **M5 → M9 分阶段**推进，每阶段独立且能在 CI 跑绿（尽量小的可验证交付）。这是用户一贯的「MVP 优先、功能完整后统一验证」习惯的延续，避免一个巨型变更盲写后只靠最终 CI 兜底。
- **每阶段完成后立即提交并用 Git Data API 推送触发 CI**（本机 github.com git 协议端口被网络阻断，提交走 GitHub API，已验证可行）。
- **MacType 原生注入（M9）验收降级为「CI 编译通过 + 产物包含 DLL」**；真机注入效果需用户在 Windows 上走查，计划中明示此边界。
- 本机无法编译，所有代码盲写；阶段越小，单轮 CI 可定位的错误越少（参照 MVP 期间 3 轮修错的教训，尽量压缩每阶段改动面）。

## 分阶段路线图

### M5「事件驱动 + 真实图标」
- 目标：`TaskMonitor` 改 `SetWinEventHook` 增量驱动（前台/最小化/关闭/创建/销毁），替换轮询；字母块改真实应用图标 + LRU 缓存池。
- 文件：改 `src/WinMac.Shell/Dock/TaskMonitor.cs`、`src/WinMac.Shell/Dock/DockIconView.cs`、`src/WinMac.Shell/Dock/DockWindow.cs`；改 `src/WinMac.Core/Win32/NativeMethods.cs`；新增 `src/WinMac.Shell/Dock/IconCache.cs`。
- 技术要点：真实图标用 `SHGetFileInfo(SHGFI_ICON|SHGFI_LARGEICON)` 取 `HICON` → 自转像素到 `WriteableBitmap`（WinUI 无 HICON→ImageSource 直转）；`TaskWindow` 增 `AppPath/IconKey`；缓存 `Dictionary<string, SoftwareBitmapSource>`（键=路径+图标档位+DPI），LRU 淘汰；`DockIconView` 内核换 `Image`，保留运行指示点/前台高亮层。图标 key 统一到 exe 路径，为 M6/M7 打基础。
- CI：`dotnet build` 验证（纯托管、无新依赖）。
- 验收：图标显示真实图标、二次进入不重闪（缓存命中）、多 printf 复用。

### M6「DWM 缩略图 + 最小化 genie + 恢复」
- 目标：悬停图标 DWM 窗口预览；最小化入 Dock genie 动画；从图标恢复。
- 文件：`NativeMethods.cs` 补 `DwmRegisterThumbnail/DwmUpdateThumbnailProperties/DwmUnregisterThumbnail`、`PrintWindow`、`EnumDisplayMonitors`；新增 `Shell/Dock/ThumbnailHost.cs`、`Shell/Dock/GenieAnimator.cs`。
- 技术要点：DWM 缩略图是原生位图、WinUI 单 HWND 无法嵌子窗口 → 新建透明 `WS_POPUP` 叠加窗口（非 Managed 的 ControlHost），`DwmRegisterThumbnail` 挂到该 hwnd，`SetWindowPos` 置于 Dock 图标上并保 top-most；`EVENT_SYSTEM_MINIMIZESTART` 驱动。genie：`PrintWindow` 抓目标窗口快照 → 叠加层 `Image` → `Composition` `SpringScalar/SpringVector3` 做 X 向飞行 + squash + 淡出，结束后转入 M6 缩略图/运行态。
- CI：新增 job 发布后跑冒烟（`IsIconic`/hwnd 纯逻辑可单测）；UI 动画效果需真机走查。
- 验收：最小化出现 genie、悬停出缩略图、点击恢复。

### M7「拖拽排序 + 外部拖放」
- 目标：Dock 图标拖拽换序并持久化；拖入 exe/快捷方式/文件夹/文件入 Dock。
- 文件：`ConfigStore.cs` 增 `DockPinnedItems`；改 `DockWindow.cs`、`DockIconView.cs`；新增 `Shell/Dock/ItemDropHandler.cs`。
- 技术要点：应用内排序用 `PointerPressed`+`ManipulationDelta` 拖动重排（NOACTIVATE 下 OLE 不稳，避免依赖 DragDrop 事件）；外部拖放因 `WS_EX_NOACTIVATE` 可能收不到 OLE → 用 `SetWindowSubclass` 拦 `WM_DROPFILES` + `DragAcceptFiles` 兜底。
- CI：序列化逻辑单测。
- 验收：图标可拖拽换序并重启保持、拖入 exe 加入 Dock。

### M8「多屏定位 + Rainmeter 皮肤系统」
- 目标：Dock/组件多显示器定位；JSON 皮肤解析与拖拽/缩放/持久化；配置面板接入。
- 文件：改 `DockWindow.cs`、`Widgets/WidgetWindow.cs`、`Settings/SettingsWindow.xaml.cs`；新增 `Shell/Skins/SkinParser.cs`、`SkinLayout.cs`、`SkinHostWindow.cs`；`ConfigStore.cs` 增 `Skins.json`。
- 技术要点：多屏用 `EnumDisplayMonitors` + `DisplayArea` 定位；`SkinParser` 解析 JSON 皮肤 → `SkinHostWindow` 内 Canvas 拖拽/缩放组件，坐标+缩放写回 `Skins.json`；复用时钟/CPU/内存组件 + 自绘 graph；配置面板并入设置窗口。
- CI：解析器+布局纯逻辑单测。
- 验收：皮肤跨屏拖动、缩放、重启恢复。

### M9「MacType 原生注入子项目」（独立，最后）
- 目标：全局字体渲染。原生 C++ DLL 注入 + 灰度 GDI/DirectWrite 复写 + 安装器。
- 文件：新建 `src/Native/MacTypeHook/`（CMake 或 vcxproj）、`src/WinMac.Setup.Installer/`；改 `.github/workflows/build.yml` 增原生编译 job。
- 技术要点：**不进 `WinMac.sln`**（dotnet build 只解 .NET）；单独 CI job 用 MSVC `clang-cl`/CMake 编 `MacTypeHook.dll`（Hook `GetGlyphOutline/ExtTextOutW` 复写 GDI 灰度、挂 `IDWriteTextRenderer` 复写 DirectWrite），产物并入 publish zip。注入方案优先 admin helper + `SetWindowsHookEx(WH_GETMESSAGE)`（`AppInit_DLLs` 需关 SecureBoot、风险过高）；提权安装器用 C# `requireAdministrator` 小程序复制 DLL + 写注册表。
- CI：仅保证 64 位 DLL 编译通过 + zip 含 DLL。真机注入效果需用户走查（计划明示为验证边界）。
- 验收：DLL 编译成功进产物；真机注入后系统字体灰度 AA（用户侧走查）。

## 关键风险与取舍

1. **MacType（M9）最不可行、最需真机**：全局 Hook + 重新光栅化是系统级改动，盲写必然反复崩。验收分「编译通过」与「真机走查」两档；该项最后做，避免拖累前面里程碑。
2. **DWM 缩略图 / genie 层叠**：DWM 缩略图与 XAML 图标共存需叠 HWND，销毁时机易产生幽灵残留；genie 用 `Composition`（Spring 动画）而非手写插值，但盲写 Viewbox 坑多，真机再调参。
3. **拖放**：NOACTIVATE + Dock 的 OLE 在 WinAppSDK 下不稳，务必留 `WM_DROPFILES` WndProc 兜底。
4. **最值得先做 = M5**：纯托管、CI 可绿、替换字母块视觉收益最大，是 M6/M7 的地基（图标 key 统一到 path）。
5. 每阶段保持「提交 → 跑绿 → 下一阶段」，压缩单轮盲写面。

## 验证方式

- **CI 门禁**：每个 M 完成后 `git` 提交 → Git Data API 推送 `main` → 轮询 GitHub Actions 至 `Build (WinUI 3 · Self-contained x64)` success（M9 另加原生编译 job）。
- **产物**：artifact `WinMac-x64/` 含可运行 exe（M9 后含 `MacTypeHook.dll`）。
- **真实 Windows 走查（用户侧）**：每阶段视觉/交互项（真实图标、genie 动画、缩略图、拖放、皮肤跨屏、全局字体注入）由用户在 Windows 上运行 CI 产物验证；macOS 端无法模拟，这是本计划的既定边界。
- 纯逻辑可单测部分（缓存键、排序序列化、皮肤解析、事件过滤）尽量在 CI 补单测，降低盲写风险。