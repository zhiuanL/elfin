# elfin

Windows 桌面小精灵：桌面陪伴优先，其次办公效率，再次 AI 助手。

当前仅完成 **Phase 0 工程基础**，尚未实现小精灵窗口、动画、办公或 AI 业务。

## 文档与结构

- [V1 执行规范](docs/Windows桌面小精灵_V1_Codex执行规范.md)
- [V1 开发设计文档](docs/Windows桌面小精灵_V1_开发设计文档_Codex版.docx)
- [Phase 0 开发报告](docs/Phase-0-开发报告.md)
- Solution：DesktopPet.sln；应用项目位于 src/，测试项目位于 tests/。

## 开发环境

Windows x64 + .NET SDK 10.0.400（见 global.json）。若本机没有 SDK，可在仓库根目录执行：

```powershell
.\tools\Install-DotNetSdk.ps1
```

脚本从 Microsoft 官方源下载，验证 SHA-512 后解压到忽略目录 .tools/dotnet，不修改系统 PATH。网络可重试失败按 1/3/7/15 秒等待；403 不重试。

## 验证

```powershell
.\tools\Verify-Phase0.ps1
.\tools\Verify-Phase0.ps1 -Configuration Release
```

脚本执行 locked restore、build、test；包含真实 WPF 窗口渲染测试，可能短暂显示控制中心。结果位于 artifacts/TestResults/。首次恢复依赖需要网络，应用正常启动不需要网络。

使用已安装且符合 global.json 的 SDK 时，也可直接执行：

```powershell
dotnet restore DesktopPet.sln --locked-mode
dotnet build DesktopPet.sln --no-restore
dotnet test DesktopPet.sln --no-build --no-restore
```

## 运行空控制中心

使用仓库内 SDK：

```powershell
$env:DOTNET_ROOT = (Resolve-Path .\.tools\dotnet).Path
& .\.tools\dotnet\dotnet.exe run --project .\src\DesktopPet.App --no-build -- --portable
```

默认使用 %LOCALAPPDATA%/DesktopPet；--portable 使用程序输出目录下 UserData。Phase 0 关闭窗口即退出，托盘与隐藏策略属于 Phase 1。

双库仅有版本化迁移账本，没有业务表。UI 文本来自 zh-CN/en-US 资源；用户配置位于 config/settings.json。API Key 不允许写入此文件、源码、数据库明文字段或日志；安全存储的业务接入留待 Phase 7。

## 阶段约束

仅按 docs 逐 Phase 开发。进入修改前先检查 Git 状态、main 分支和 origin；不要改写历史、覆盖用户修改或自动开始下一 Phase。
