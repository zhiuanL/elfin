# Phase 0 开发报告

日期：2026-08-27  
仓库：`https://github.com/zhiuanL/elfin.git`  
分支：`main`

## 1. Phase

**Phase 0 — 工程骨架。**

本阶段已完成 restore / build / test 及真实 WPF 空控制中心启动验收。停止在 Phase 0；没有开始 Phase 1。

开始前执行并确认了 `git status`、`git branch --show-current`、`git remote -v`。当时仅有用户的 `resource/` 未跟踪，无已跟踪文件的未提交修改。没有修改 origin、初始化仓库、改写历史、暂存、提交或推送；保留全部 resource 文件和原始 Word 文档。

## 2. 完成内容

- 建立 9 项目 Solution，统一 C# 14 / .NET 10、nullable、确定性构建与 warnings-as-errors。
- 使用 global.json 固定 SDK 基线，集中管理 NuGet 版本，并为全部项目生成 packages.lock.json。
- 使用 Microsoft Generic Host 和构造器注入；启用 ValidateOnBuild / ValidateScopes。仅在启动组合根解析根服务，业务代码不使用 Service Locator。
- 建立只显示工程就绪信息的 WPF 控制中心。视图只负责显示与窗口关闭事件，状态由 ViewModel 提供。
- 建立 AppSettings / LogOptions / SecurityLimits 强类型配置；校验未知字段、枚举、版本和范围；使用 temp + flush + replace 原子写入，保留上一版备份。
- 损坏配置复制保留后恢复默认值；来自更新版本的配置拒绝降级，保持原文件不动。
- 建立本地 JSONL 日志，按 UTC 日期和文件大小滚动，限制保留文件数。用户保存日志配置后立即生效。只清理符合程序命名格式的日志。
- 日志接口不接受自由文本或异常正文，只记录事件、错误码、来源、UTC 时间与关联 ID，避免密钥、聊天和记忆进入日志。日志 IO 失败以无敏感内容的 Trace 降级，不递归导致应用崩溃。
- 建立启动、Dispatcher、AppDomain、未观察任务等异常边界；致命 UI 异常退出，提供本地日志/备份位置与关联 ID。
- 建立 Installed / Portable 数据目录策略，SQLite 双库连接工厂、独立迁移账本、迁移前备份、历史校验、并发迁移文件锁和事务回滚。
- 建立 Repository 抽象及 SQLite 基类；尚未创建业务 Repository 或业务表。
- 建立领域枚举、实例标识、情绪值对象、物理像素坐标、办公 DTO、Character Package DTO 和模块接口骨架。
- 建立显式注册的 Command Registry、基础 WPF ICommand、zh-CN / en-US 资源系统。
- 建立 Windows DPAPI 数据保护实现，密钥存储仅保留 ISecretStore 接口，未实现 AI Key 配置/持久化。
- 建立 Update / Sync / CrashReporting 的离线禁用实现；AI、TTS、PetRuntime、显示器等后续模块没有注册虚假可用实现。
- 建立隔离 SDK 准备脚本及 Debug / Release 可复现验证脚本；准备 win-x64 self-contained 发布配置，但本阶段不构建安装程序。
- 按执行规范要求，在原 Markdown 执行规范中追加 Phase 0 契约注释，并同步测试；未改写原始设计需求。

## 3. 新增项目

Solution：`DesktopPet.sln`。

| Project | 职责 |
|---|---|
| DesktopPet.App | WPF、组合根、应用生命周期、空控制中心及 ViewModel |
| DesktopPet.Application | 用例边界、配置契约、启动协调、命令、模块服务接口 |
| DesktopPet.Domain | Pet/情绪/移动/显示器/办公领域值与 Repository 契约 |
| DesktopPet.Infrastructure | JSON 配置、文件目录、SQLite、迁移、日志、资源系统、NoOp 服务 |
| DesktopPet.Windows | Windows 平台适配边界；当前实现 DPAPI 数据保护 |
| DesktopPet.CharacterSdk | 强类型角色包/动画 DTO、Provider 与 Validator 契约 |
| DesktopPet.AI | Chat / Conversation / Memory / Tool 契约；无网络 Provider 实现 |
| DesktopPet.Tests.Unit | 17 个单元测试 |
| DesktopPet.Tests.Integration | 25 个文件、数据库、DPAPI 与真实 WPF 集成测试 |

App / Windows / Integration 目标为 `net10.0-windows`；其余项目为 `net10.0`。App 和集成测试为 x64。

## 4. 主要新增文件

| 文件 | 职责 |
|---|---|
| global.json、Directory.Build.props、Directory.Packages.props | SDK、编译规范、集中包版本 |
| src/DesktopPet.App/Bootstrap/AppBootstrapper.cs | DI 组合根、选项验证 |
| src/DesktopPet.App/Bootstrap/DesktopApplication.cs | 协调初始化并显示空控制中心 |
| src/DesktopPet.App/App.xaml.cs | WPF 生命周期与进程级异常边界 |
| src/DesktopPet.App/Views/MainWindow.xaml | 无业务功能的控制中心 |
| src/DesktopPet.App/ViewModels/MainWindowViewModel.cs | 资源化文本、初始化状态、关闭命令 |
| src/DesktopPet.Application/Startup/RecoveryCoordinator.cs | app.db → ai.db → 配置的基础启动顺序 |
| src/DesktopPet.Application/Commands/CommandRegistry.cs | 仅允许显式注册的强类型命令分发 |
| src/DesktopPet.Application/Contracts/ | 后续 Pet、办公、Windows、语音、安全模块端口 |
| src/DesktopPet.CharacterSdk/CharacterDefinition.cs | 数据驱动角色及动画契约 |
| src/DesktopPet.Infrastructure/Configuration/JsonSettingsService.cs | 校验、原子保存、损坏/未来版本保护 |
| src/DesktopPet.Infrastructure/Persistence/SqliteDatabaseMigrator.cs | 校验历史、升级前备份、事务迁移和并发保护 |
| src/DesktopPet.Infrastructure/Persistence/SqliteMigration.cs | 独立迁移定义、内容校验和及 V1 初始迁移 |
| src/DesktopPet.Infrastructure/Diagnostics/RollingFileAppLogger.cs | 结构化、安全字段、滚动和保留策略 |
| src/DesktopPet.Infrastructure/Localization/Strings*.resx | 英文默认回退及中英文 UI 资源 |
| src/DesktopPet.Windows/Security/DpapiDataProtectionService.cs | 当前 Windows 用户、用途隔离的数据加解密 |
| tests/DesktopPet.Tests.Integration/StartupSmokeTests.cs | 实际 WPF 进程渲染、重启、损坏库故障边界 |
| tools/Install-DotNetSdk.ps1、tools/Verify-Phase0.ps1 | 固定 SDK 准备与验收入口 |

其余主要类型和测试可在对应项目中查看；所有新增源文件和 9 份锁文件都保留在工作区，未暂存。

## 5. 架构决策

依赖方向：

```text
App → Application → Domain
                 → CharacterSdk → Domain
Infrastructure → Application
Windows        → Application
AI             → Application
```

App 在组合根引用并注入适配器。Domain / CharacterSdk / Application 不引用 AI、WPF、SQLite 或 Windows Adapter；有编译程序集边界测试。

- Domain 即本项目的 Core，不另建重复的 Core 项目。
- 单例仅用于无角色业务状态的基础服务。没有 PetRuntime 实现、CurrentPet 或全局可变角色状态。
- 动画通过 AnimationSemantic / AnimationDefinition / IAnimationProvider 表达；核心代码无角色名称判断、素材文件名或格式相关行为。
- 显示器边界明确使用物理像素坐标，与 WPF DIP 区分；保留负坐标、DPI、WorkingArea、邻接关系。没有假设单屏，也没有实现后续移动算法。
- 角色包 Validator / Schema 文件、实际 PNG Provider 属于 Phase 2；本阶段仅定义模型与接口，不将骨架误称为已可导入角色包。
- UI 与 ViewModel 不执行 SQL、不访问 Win32、不读取业务文件；启动组合根只负责配置/依赖接线。
- AI 连接配置只包含 SecretReference，不含明文 Key 字段。DPAPI 不提供明文回退；ISecretStore 的实际安全持久化属于 Phase 7。
- 日志采用受控字段白名单，不依赖正则猜测并删除自由文本中的密钥。完整诊断包属于 Phase 10。
- SDK 为 10.0.400，运行时 10.0.11；Microsoft 库固定 10.0.11。依据 [Microsoft .NET 10 下载页](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) 和官方 NuGet 源核验版本，实际安装也通过官方元数据 SHA-512 校验。

## 6. 数据库状态

默认目录：

```text
Installed: %LOCALAPPDATA%/DesktopPet/
Portable:  <AppRoot>/UserData/
  config/settings.json
  data/app.db
  data/ai.db
  characters/
  cache/
  logs/
  backups/
```

- `app.db`：Phase 0 schema version 1，仅有 SchemaMigrations。
- `ai.db`：Phase 0 schema version 1，仅有独立 SchemaMigrations。
- 未提前创建 Pomodoro / Reminder / Conversation 等业务表；随对应 Phase 新增迁移。
- History 保存 Version / Name / Checksum / AppliedAtUtc；版本必须从 1 连续递增，拒绝缺号、重复、已应用脚本修改与自动降级。
- 没有迁移账本的非空旧数据库拒绝自动接管，不覆盖或删除已有表。
- 升级已有版本前用 SQLite Online Backup 生成独立备份，包含 WAL 中已提交数据；不简单复制 .db 文件。
- 每个数据库的全部待执行迁移和历史记录在同一事务中提交；失败或取消回滚。两个库之间不伪装为跨文件原子事务。
- `app.db` 失败阻止启动。`ai.db` 失败时原库保持未完成升级前状态，返回不可用标记、显示提示，核心继续启动。
- 自动测试使用独立临时数据根，结束后仅清理该测试专属 GUID 目录；没有往正式用户数据目录写入测试记录。

## 7. 测试结果

本机：Windows x64，OS build 10.0.26200；.NET SDK 10.0.400 / Runtime 10.0.11。

实际执行的底层命令如下（通过 tools/Verify-Phase0.ps1 分别运行 Debug 和 Release）：

```powershell
dotnet restore DesktopPet.sln --locked-mode --packages .packages
dotnet build DesktopPet.sln --no-restore --configuration Debug
dotnet test DesktopPet.sln --no-build --no-restore --configuration Debug --logger 'trx;LogFilePrefix=phase-0' --results-directory artifacts/TestResults/Debug

dotnet restore DesktopPet.sln --locked-mode --packages .packages
dotnet build DesktopPet.sln --no-restore --configuration Release
dotnet test DesktopPet.sln --no-build --no-restore --configuration Release --logger 'trx;LogFilePrefix=phase-0' --results-directory artifacts/TestResults/Release
```

实际使用仓库 `.tools/dotnet/dotnet.exe`，不是 PATH 中原来的旧版 .NET。

| 配置 | Restore | Build errors | Build warnings | Unit | Integration | Failed / Skipped |
|---|---|---:|---:|---:|---:|---:|
| Debug | 成功，locked mode | 0 | 0 | 17/17 | 25/25 | 0 / 0 |
| Release | 成功，locked mode | 0 | 0 | 17/17 | 25/25 | 0 / 0 |

每套共 42 个测试。TRX 位于 `artifacts/TestResults/Debug/` 与 `artifacts/TestResults/Release/`。

覆盖：默认值、情绪范围、负坐标、层级依赖、双语资源、异常隐私、禁用服务、命令分发/重复/取消、启动顺序、AI 故障隔离、双库首次创建与幂等性、旧库升级和 WAL 备份、迁移失败/取消回滚、历史篡改/降级/无账本旧库/并发锁、配置原子保存及损坏恢复/未来版本保护、日志保留/用户文件保护/策略生效、DPAPI 用途隔离及篡改失败。

WPF 验收不是只解析 XAML：测试启动真实独立进程，窗口触发 ContentRendered 后才正常退出；覆盖首次启动、再次启动、app.db 损坏阻止启动、ai.db 损坏仍显示控制中心。使用不可用 HTTP/HTTPS 代理模拟在线能力不可达；未配置任何 AI，且应用启动路径没有注册网络 Provider。这不等同于已完成整个 V1 的断网回归或系统级断网矩阵。

首轮发现的生命周期接口命名冲突和 WPF System.IO 显式引用问题均已修复。没有屏蔽编译警告，也没有删除失败测试。

手工复验步骤：

1. 运行 `tools/Verify-Phase0.ps1`；需要 Release 时加 `-Configuration Release`。
2. 用本地 SDK 运行 `src/DesktopPet.App`，传入 `--portable` 时检查输出目录下 UserData；不传时使用 LocalAppData。
3. 确认仅显示 Phase 0 工程就绪控制中心，无小精灵/角色/计时/AI 页面；关闭窗口应退出进程。
4. 重新启动，检查配置及双库仍可读、迁移无重复执行。
5. 将 settings.json 的 culture 改为 en-US 后重启检查英文；正式 UI 设置页在后续阶段实现。
6. Windows 10/11 双系统及 100/125/150% 多屏 DPI 全矩阵尚未执行，不能据此宣称 V1 全平台验收通过。

## 8. Open Decisions

1. 用户最后要求阅读“三个项目文档”，但开始时 docs 只有明确指定的执行规范 Markdown 和 V1 设计 Word 两份。两份均已完整读取；没有用 resource 中的早期草案替代缺失的第三份。新生成的本报告不是原有第三份需求文档。
2. 设计 §19 的“迁移失败终止启动”与设计/规范的“AI 故障不得影响离线核心”有交叉冲突。当前最小处理：核心库失败阻断；AI 库失败回滚并禁用 AI 存储。后续 Phase 7 必须消费该不可用状态；恢复 UI 和完整诊断导出在 Phase 10 落地。
3. “关闭控制中心默认隐藏托盘”是 V1 要求，但托盘明确在 Phase 1。本阶段关闭即退出，避免提前实现托盘或留下无法找回的后台程序；Phase 1 再切换策略。
4. 数据库业务表清单是 V1 目标，不是 Phase 0 已完成项。本阶段只建立迁移账本与测试框架，避免未实现业务前锁死表结构。
5. 角色 DTO 当前是版本字段和 Provider 契约骨架；完整 JSON Schema、资源安全 Validator、序列化细节与兼容策略由 Phase 2 按文档完成。
6. Portable 检测方式未明确；当前采用显式 `--portable`，默认 Installed。安装器品牌、Inno Setup/WiX、签名与打包方式留待 Phase 10，不影响当前分层。
7. 初始安全限额和日志默认值集中配置，未把这些工程调优值当成最终产品参数。

## 9. Risks

- 本次只在当前 Windows x64 环境验证；Windows 10 实机/VM、多显示器混合 DPI、热插拔、锁屏睡眠等仍需在对应阶段回归。
- DPAPI 密文绑定当前 Windows 用户；不能直接充当可跨机器恢复的密码备份格式。Phase 10 应独立实现设计要求的版本化密码加密备份。
- AI 库损坏的可用性标记必须由后续 AI 模块强制遵守，不能绕过启动故障直接建连读写。
- 迁移备份没有自动删除策略，避免丢失恢复点；长期备份管理与空间提醒属于后续数据管理阶段。
- 当前日志有意不保存原始异常正文和堆栈；后续诊断包需在隐私白名单内增强诊断信息，不允许直接放开敏感数据。
- win-x64 self-contained 发布 profile 已准备，但本阶段没有生成/验收正式安装包、Portable 发布包或自动更新。
- 本地 SDK 与包缓存均在 Git 忽略目录；其他开发机需先安装匹配 SDK 或运行准备脚本。
- Phase 0 没有业务功能；上述未来模块接口存在不意味着小精灵、AI、提醒等已能使用。

## 10. 下一阶段建议

**Phase 1：Pet Window。**

在用户下一条明确指令后，按规范实现透明置顶小精灵窗口、拖拽、托盘、关闭策略、位置保存及基础 DPI；继续通过 Application/Windows 边界处理系统能力。本次不开始。

建议提交名：`phase-0-solution-bootstrap`。本次没有暂存、提交或推送。最终 Git 状态与 diff --stat 在交付时展示；diff --stat 默认不包含未跟踪的新项目文件。

### 交付前 Git 检查

`git status`：main 与 origin/main 同步；3 个已跟踪文件修改（.gitignore、README.md、执行规范），84 个 Phase 0 新文件未跟踪；原有 resource/ 下 5 个文件继续保持未跟踪且未改动。工作区未暂存。

`git diff --stat`：

```text
3 files changed, 74 insertions(+), 1 deletion(-)
```

该统计仅包含已跟踪文件，不包含 84 个新增文件。`git diff --check` 无空白错误；Git 提示 LF 将在后续检出时转换为 CRLF，这是本机行尾设置提示，不是构建警告，也没有为此修改 Git 配置。9 个项目均存在 packages.lock.json。
