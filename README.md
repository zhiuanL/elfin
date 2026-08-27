# elfin

Windows 桌面小精灵：桌面陪伴优先，其次办公效率，再次 AI 助手。

当前为 **Phase 1 窗口基础（PARTIAL：待完成人工交互验收）**。已接入透明置顶占位窗口、拖拽、位置持久化、托盘和窗口命令；未实现角色运行时、动画、办公或 AI 业务。

## 文档与结构

- [V1 执行规范](docs/Windows桌面小精灵_V1_Codex执行规范.md)
- [V1 开发设计文档](docs/Windows桌面小精灵_V1_开发设计文档_Codex版.docx)
- [Phase 0 开发报告](docs/Phase-0-开发报告.md)
- [Phase 1 开发报告](docs/Phase-1-开发报告.md)
- Solution：DesktopPet.sln；应用项目位于 src/，测试项目位于 tests/。

## 开发环境

Windows x64 + .NET SDK 10.0.400（见 global.json）。若本机没有 SDK，可在仓库根目录执行：

```powershell
.\tools\Install-DotNetSdk.ps1
```

脚本从 Microsoft 官方源下载，验证 SHA-512 后解压到忽略目录 .tools/dotnet，不修改系统 PATH。网络可重试失败按 1/3/7/15 秒等待；403 不重试。

## 验证

```powershell
.\tools\Verify-Phase1.ps1
.\tools\Verify-Phase1.ps1 -Configuration Release
```

脚本执行 locked restore、build、test；包含全部 Phase 0 回归，以及真实 WPF 双窗口渲染、位置恢复、Windows 适配测试，可能短暂显示窗口与托盘图标。结果位于 artifacts/TestResults/。首次恢复依赖需要网络，应用正常启动不需要网络。自动测试不能替代透明视觉、拖拽手感和托盘交互的人工验收。

使用已安装且符合 global.json 的 SDK 时，也可直接执行：

```powershell
dotnet restore DesktopPet.sln --locked-mode
dotnet build DesktopPet.sln --no-restore
dotnet test DesktopPet.sln --no-build --no-restore
```

## 运行 Phase 1 窗口

使用仓库内 SDK：

```powershell
$env:DOTNET_ROOT = (Resolve-Path .\.tools\dotnet).Path
& .\src\DesktopPet.App\bin\Debug\net10.0-windows\DesktopPet.App.exe --portable
```

先完成 Debug 构建，再直接启动包含 PerMonitorV2 manifest 的 exe。默认使用 %LOCALAPPDATA%/DesktopPet；--portable 使用程序输出目录下 UserData。

拖动占位小精灵移动，双击打开控制中心，右键打开常用菜单。控制中心关闭默认隐藏到托盘；小精灵关闭请求仅隐藏小精灵。真正退出请使用托盘或控制中心的“退出程序”。窗口显示/隐藏状态、物理像素位置和置顶偏好由 Settings Service 保存。

配置 schema 1 自动升级为 2；controlCenterCloseBehavior 可选 HideToTray（默认）或 Exit，完整设置界面留待 Phase 5。请勿运行多个实例共用同一数据目录；多实例互斥尚未纳入本阶段。

双库仅有版本化迁移账本，没有业务表。UI 文本来自 zh-CN/en-US 资源；用户配置位于 config/settings.json。API Key 不允许写入此文件、源码、数据库明文字段或日志；安全存储的业务接入留待 Phase 7。

## 阶段约束

仅按 docs 逐 Phase 开发。进入修改前先检查 Git 状态、main 分支和 origin；不要改写历史、覆盖用户修改或自动开始下一 Phase。
