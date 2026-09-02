# elfin

Windows 桌面小精灵：桌面陪伴优先，其次办公效率，再次 AI 助手。

当前为 **Phase 6 离线 Productivity**。Phase 0–5 基线已确认通过。已提供番茄钟、任务/标签、统计、提醒、睡眠恢复、宠物联动及控制中心入口；所有核心办公能力离线可用，没有实现 Phase 7+ 的 AI、TTS 或自然语言提醒。

## 文档与结构

- [V1 执行规范](docs/Windows桌面小精灵_V1_Codex执行规范.md)
- [V1 开发设计文档](docs/Windows桌面小精灵_V1_开发设计文档_Codex版.docx)
- [Phase 0 开发报告](docs/Phase-0-开发报告.md)
- [Phase 1 开发报告](docs/Phase-1-开发报告.md)
- [Phase 2 开发报告](docs/Phase-2-开发报告.md)
- [Phase 3 开发报告](docs/Phase-3-开发报告.md)
- [Phase 4 开发报告](docs/Phase-4-开发报告.md)
- [Phase 4 人工测试文档](docs/Phase-4-人工测试文档.md)
- [Phase 5 开发报告](docs/Phase-5-开发报告.md)
- [Phase 5 人工测试文档](docs/Phase-5-人工测试文档.md)
- [Phase 6 开发报告](docs/Phase-6-开发报告.md)
- [Phase 6 人工测试文档](docs/Phase-6-人工测试文档.md)
- [角色包 Schema 1](docs/character-manifest.schema.json)
- Solution：DesktopPet.sln；应用项目位于 src/，测试项目位于 tests/。

## 开发环境

Windows x64 + .NET SDK 10.0.400（见 global.json）。若本机没有 SDK，可在仓库根目录执行：

```powershell
.\tools\Install-DotNetSdk.ps1
```

脚本从 Microsoft 官方源下载，验证 SHA-512 后解压到忽略目录 .tools/dotnet，不修改系统 PATH。网络可重试失败按 1/3/7/15 秒等待；403 不重试。

## 验证

```powershell
.\tools\Verify-Phase6.ps1
.\tools\Verify-Phase6.ps1 -Configuration Release
```

脚本执行 locked restore、build、test，保留 Phase 0–5 回归，并覆盖绝对时间番茄、恢复、Reminder/DST/去重、Task/Tag、统计、本地日期、schema 7 和真实 WPF 启动烟测。最终 Debug/Release 结果见 Phase 6 开发报告和 artifacts/TestResults/。真实 WPF 测试可能短暂显示窗口与托盘图标；自动测试不能替代视觉、系统通知、睡眠/锁屏和多屏/DPI 人工验收。

使用已安装且符合 global.json 的 SDK 时，也可直接执行：

```powershell
dotnet restore DesktopPet.sln --locked-mode
dotnet build DesktopPet.sln --no-restore
dotnet test DesktopPet.sln --no-build --no-restore
```

## 运行 Phase 6

推荐使用启动脚本（默认 Debug + Portable，先还原并构建）：

```powershell
.\tools\Start-Elfin.ps1
.\tools\Start-Elfin.ps1 -Configuration Release
.\tools\Start-Elfin.ps1 -NoBuild
```

脚本优先使用仓库内 SDK，找不到时使用 PATH 中的 dotnet；未安装时提示运行 SDK 安装脚本，不自动下载 SDK。`-NoBuild` 直接启动已有构建，`-Installed` 改用 `%LOCALAPPDATA%/DesktopPet`，`-WhatIf` 仅预览、不构建也不启动。脚本可从任意工作目录通过完整路径调用；检测到已运行的 DesktopPet.App 会提示从托盘退出，不强制结束进程。不会清理或覆盖 UserData，Debug/Release 的便携数据互相独立；普通应用启动仍会按既有逻辑保存设置。首次还原依赖可能联网，离线启动已有构建可用 `-NoBuild`。

使用仓库内 SDK：

```powershell
$env:DOTNET_ROOT = (Resolve-Path .\.tools\dotnet).Path
& .\src\DesktopPet.App\bin\Debug\net10.0-windows\DesktopPet.App.exe --portable
```

先从托盘退出旧程序，再完成 Debug 构建，最后直接启动包含 PerMonitorV2 manifest 的 exe。默认使用 %LOCALAPPDATA%/DesktopPet；--portable 使用程序输出目录下 UserData，切换 Debug/Release 输出目录不会自动迁移便携数据。

首次启动会安装随构建分发的两个“开发测试”角色包；这是测试素材，不是正式用户角色。空配置优先选择动作更完整的橙色 Standard，已有有效角色选择保持不变。蓝色 Basic 仅有静态 idle，缺少的行为被过滤；要验收 blink/happy/rest，请在角色诊断中激活 Standard。原始 resource/ 不会被修改。

控制中心“角色”页点击“选择 ZIP…”，在 Windows 资源管理器选择角色包。路径自动回填后可先校验，再“导入并启用”；选择本身不会安装，取消会保留原状态。文件夹选择与语义播放等开发工具仍保留在“诊断”页。

角色页提供预览、等级/完整度、能力、校验诊断、启用和删除。当前角色不可删除，删除其他角色必须二次确认；同 ID 包拒绝覆盖。诊断页可输入 idle / blink / happy / rest / talking 临时请求语义播放，并显示状态、情绪、最近动作及上次决策评分。

拖动小精灵移动，双击打开控制中心，右键打开常用菜单。控制中心关闭默认隐藏到托盘；小精灵关闭请求仅隐藏小精灵。真正退出请使用托盘或控制中心的“退出程序”。窗口状态、物理像素位置和激活角色标识由 Settings Service 保存。

配置 schema 1–6 自动升级为 7，保留备份及既有偏好；新增番茄时长、长休间隔、自动阶段和漏提醒窗口的强类型配置。Home、显示器、运动、语言、关闭行为、可见性、置顶和 Productivity 设置均经 Settings Service 持久化。请勿运行多个实例共用同一数据目录；全应用多实例互斥尚未纳入本阶段。

在“设置 → 移动”选择固定 / 小范围 / 全桌面 / 混合并应用。默认混合 + 当前显示器 + 自然：围绕 Home 活动，空闲后偶尔扩大范围；“拖拽后更新 Home”可单独关闭。自主行为由调度器择机执行，不是点击设置后立即移动；固定模式只允许手动拖动。

跨屏需明确选择“所有显示器”，或“指定显示器”并填写诊断区显示的设备 ID（逗号分隔）。仅共享连续矩形工作区的相邻屏幕允许直线跨越；存在空洞、不同工作区边缘或 DPI 变化时保守取消/校正，不穿越不可见区域。没有任何指定屏幕在线时暂停自主移动并保留可见窗口。

“切换鼠标穿透”可开关穿透；“临时穿透 8 秒”自动恢复。无法点击小精灵时，使用托盘的“恢复鼠标交互”。隐藏、退出、下次启动均恢复交互，不持久化可能使窗口无法点击的模式。移动诊断为手动刷新快照。

角色包仍为 Schema 1；旧包无需修改且缺少 walk 时使用 idle/fallback。新包可选声明 manifest.visualAnchor（0..1 的窗口画布锚点，默认底部中点）、supportsMirroring、movement 推荐值，并在 animations 声明 walk / walk-left / walk-right。依赖新能力的包可将 minimumAppVersion 设为 0.4.0；字段格式见角色包 Schema。系统上限优先于用户设置，用户设置优先于角色推荐。

app.db 已通过 v2 migration 保存番茄 Session、Task/Tag、Reminder 与去重执行记录；ai.db 仍只有迁移账本。统计从 app.db 持久化 Session 按本地日期推导。UI 文本来自 zh-CN/en-US 资源；用户配置位于 config/settings.json。API Key 不允许写入此文件、源码、数据库明文字段或日志；安全存储的业务接入留待 Phase 7。

## 阶段约束

仅按 docs 逐 Phase 开发。进入修改前先检查 Git 状态、main 分支和 origin；不要改写历史、覆盖用户修改或自动开始下一 Phase。
