# elfin

Windows 桌面小精灵：桌面陪伴优先，其次办公效率，再次 AI 助手。

当前为 **Phase 3 本地行为与情绪运行时（PARTIAL：待人工行为验收）**。Phase 0–2 基线已确认通过。现已接入状态机、可取消调度、情绪、Utility 权重与冷却、单击反馈及开发诊断；保留角色包/PNG 能力，没有实现 Phase 4 自主移动、办公或 AI 业务。

## 文档与结构

- [V1 执行规范](docs/Windows桌面小精灵_V1_Codex执行规范.md)
- [V1 开发设计文档](docs/Windows桌面小精灵_V1_开发设计文档_Codex版.docx)
- [Phase 0 开发报告](docs/Phase-0-开发报告.md)
- [Phase 1 开发报告](docs/Phase-1-开发报告.md)
- [Phase 2 开发报告](docs/Phase-2-开发报告.md)
- [Phase 3 开发报告](docs/Phase-3-开发报告.md)
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
.\tools\Verify-Phase3.ps1
.\tools\Verify-Phase3.ps1 -Configuration Release
```

脚本执行 locked restore、build、test，保留 Phase 0–2 回归，并增加虚拟时间调度、情绪/Utility、角色切换和生命周期测试。Debug/Release 各 214 项通过，0 警告、0 错误；结果位于 artifacts/TestResults/。真实 WPF 测试可能短暂显示窗口、文件选择框与托盘图标。首次恢复依赖需要网络，应用启动不需要网络。自动测试和三分钟真实进程运行检查不能替代视觉、点击、拖拽和托盘的人工验收。

使用已安装且符合 global.json 的 SDK 时，也可直接执行：

```powershell
dotnet restore DesktopPet.sln --locked-mode
dotnet build DesktopPet.sln --no-restore
dotnet test DesktopPet.sln --no-build --no-restore
```

## 运行 Phase 3

使用仓库内 SDK：

```powershell
$env:DOTNET_ROOT = (Resolve-Path .\.tools\dotnet).Path
& .\src\DesktopPet.App\bin\Debug\net10.0-windows\DesktopPet.App.exe --portable
```

先从托盘退出旧程序，再完成 Debug 构建，最后直接启动包含 PerMonitorV2 manifest 的 exe。默认使用 %LOCALAPPDATA%/DesktopPet；--portable 使用程序输出目录下 UserData，切换 Debug/Release 输出目录不会自动迁移便携数据。

首次启动会安装随构建分发的两个“开发测试”角色包；这是测试素材，不是正式用户角色。空配置优先选择动作更完整的橙色 Standard，已有有效角色选择保持不变。蓝色 Basic 仅有静态 idle，缺少的行为被过滤；要验收 blink/happy/rest，请在角色诊断中激活 Standard。原始 resource/ 不会被修改。

控制中心的“角色开发诊断”点击“选择 ZIP…”或“选择文件夹…”，在 Windows 原生选择框中选择角色包 ZIP 或包含 manifest.json 的包根目录。路径自动回填后点击“校验”或“导入”；选择本身不会安装或激活，取消保留原路径。仍支持手工粘贴绝对路径。

导入后选择角色并点击“激活”，输入 idle / blink / happy / rest / talking 可临时请求语义播放，结束后恢复自主调度。单击产生情绪反馈，拖拽期间暂停行为。下方“行为开发诊断”显示状态、情绪、最近动作及上次决策的评分/冷却快照，不是实时倒计时。移除当前角色前必须先激活其他角色。同 ID 包拒绝覆盖；正式角色管理页面留待 Phase 5。

拖动小精灵移动，双击打开控制中心，右键打开常用菜单。控制中心关闭默认隐藏到托盘；小精灵关闭请求仅隐藏小精灵。真正退出请使用托盘或控制中心的“退出程序”。窗口状态、物理像素位置和激活角色标识由 Settings Service 保存。

配置 schema 1/2/3 自动升级为 4，保留备份及窗口偏好。稳定 Mood/Energy/Affinity 按角色保存，Boredom、帧号、冷却和近期动作不跨启动恢复。controlCenterCloseBehavior 可选 HideToTray（默认）或 Exit。请勿运行多个实例共用同一数据目录；全应用多实例互斥尚未纳入本阶段。

双库仅有版本化迁移账本，没有业务表。UI 文本来自 zh-CN/en-US 资源；用户配置位于 config/settings.json。API Key 不允许写入此文件、源码、数据库明文字段或日志；安全存储的业务接入留待 Phase 7。

## 阶段约束

仅按 docs 逐 Phase 开发。进入修改前先检查 Git 状态、main 分支和 origin；不要改写历史、覆盖用户修改或自动开始下一 Phase。
