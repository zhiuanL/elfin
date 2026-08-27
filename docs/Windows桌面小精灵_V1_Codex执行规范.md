# Windows 桌面小精灵 V1 — Codex 执行规范

> 状态：可进入开发  
> 需求置信度：95.2%  
> 日期：2026-08-27  
> 技术栈：C# / WPF / .NET 10 LTS / Windows 10+11 / SQLite

## 0. Codex 工作方式（必须遵守）

1. **按 Phase 顺序开发**，不得一次性实现全部功能。
2. 每个 Phase 完成后必须：`dotnet build` → 单元测试 → 集成/手工验收 → 修复后再进入下一 Phase。
3. 不得重新进行产品选型。本文“V1 必做”均为已确认需求。
4. 核心离线优先：AI、网络、在线 TTS 故障不能影响 Pet Core、番茄钟、提醒、统计、角色包。
5. UI 不直接访问 SQLite / Win32 / 文件系统 / AI SDK；经 Service/Repository/Adapter。
6. V1 单角色，但 `PetRuntime` 不得做全局单例，必须预留未来多实例。
7. 角色包必须使用强类型 DTO + Schema Version + Validator；禁止 `Dictionary<string, object>` 作为主模型。
8. AI 只能调用 `IAiToolRegistry` 注册的工具；严禁模型输出方法名后反射/动态执行。
9. 任何 API Key 不得写日志、普通配置文件、SQLite 明文字段或诊断包。
10. 每次修改跨模块契约时同步更新测试与本文件对应设计注释。

## 1. 产品不变式

- 第一核心：桌面陪伴；第二：办公效率；第三：AI 助手。
- 陪伴优先级：自主行为 > 用户互动 > 主动说话 > 环境感知 > AI 陪伴。
- 默认移动：`Hybrid + SmartHybrid`。
- V1 行为内容：丰富行为 + 情绪；底层按生命感架构预留 Environment/Utility/Memory。
- V1 单角色；未来多角色。
- 核心功能完全离线；只有 AI/在线 TTS 等可选模块联网。
- Windows 10 + Windows 11；正式发布 x64 Self-contained。

## 2. Solution 结构

```text
DesktopPet.sln
├─ DesktopPet.App
├─ DesktopPet.Application
├─ DesktopPet.Domain
├─ DesktopPet.Infrastructure
├─ DesktopPet.Windows
├─ DesktopPet.CharacterSdk
├─ DesktopPet.AI
├─ DesktopPet.Tests.Unit
└─ DesktopPet.Tests.Integration
```

依赖方向：`UI -> Application -> Domain`，Infrastructure/Windows/AI 通过接口注入。**Domain/Pet Core 不依赖 AI。**

## 3. V1 功能清单

### 3.1 桌面精灵
- 透明、无边框、置顶、ShowInTaskbar=false。
- 默认视觉尺寸约 220×220；角色逻辑资源建议 512×512。
- 拖拽、单击、双击、右键、连续点击识别、基础 HitArea。
- 智能鼠标穿透：手动切换、全局快捷键临时穿透、专注/全屏可自动降噪。
- 双击打开控制中心；右键常用操作；托盘兜底。

### 3.2 移动
```csharp
enum MovementMode { Fixed, Local, Desktop, Hybrid }
enum HybridMovementStrategy { Anchor, Roaming, Scenario, SmartHybrid }
enum DisplayPolicy { PrimaryOnly, LockedCurrent, SelectedMonitors, AllMonitors }
```
- 默认：`Hybrid`, `SmartHybrid`, `LockedCurrent`。
- 用户运动风格：安静 / 自然 / 活泼。
- 角色包可提供推荐速度、缓动、惯性、停顿频率；配置优先级：系统安全限制 > 用户 > 角色推荐 > 引擎默认。
- 轻物理：缓动 + 加减速 + 轻惯性；不做复杂刚体。
- 多屏：支持虚拟桌面负坐标、WorkingArea、PerMonitorV2 DPI、热插拔、显示器邻接图。

### 3.3 行为/情绪
```text
Mood 0..100
Energy 0..100
Boredom 0..100
Affinity 0..100
```
Utility：
```text
FinalScore = BaseWeight × CharacterModifier × EmotionModifier × EnvironmentModifier × UserPreferenceModifier × CooldownGate
```
- Idle 永远可执行。
- 行为必须有 cooldown / recent-behavior suppression。
- EnvironmentContext：TimeOfDay、UserIdleDuration、Pomodoro、ForegroundWindowState、SessionState、RecentInteraction/Behavior。
- V1 前台窗口只做状态感知与降噪；预留窗口边缘互动。

## 4. Character Package

### 4.1 安装最低条件
- `manifest.json`
- `preview.png`
- `fallback.png`
- 至少一个 `idle`

### 4.2 等级
- Basic：最低运行要求。
- Standard：主要动作 + Persona + 本地文案 + 情绪映射。
- Full：Standard + HitArea + 行为配置 + Voice + Talking/LipSync 等。
- 作者声明 `targetTier`；程序校验得出 `actualTier` 与完整度百分比。

### 4.3 校验等级
- Fatal：拒绝安装。
- Error：若可自动降级则允许安装。
- Warning：提示，不阻塞。
- Developer Diagnostics：`errorCode/jsonPath/expected/actual/suggestion`。

### 4.4 安全
- 防 Zip Slip。
- ZIP 总大小/文件数量/单文件大小/图片尺寸/帧数上限集中在 `SecurityLimits`。
- 白名单扩展名；禁止 EXE/DLL/脚本。
- 临时目录验证完成后原子安装。

### 4.5 动画
```csharp
public interface IAnimationProvider
{
    bool CanRender(AnimationDefinition definition);
    Task PreloadAsync(AnimationDefinition definition, CancellationToken ct);
    Task PlayAsync(AnimationRequest request, CancellationToken ct);
    void Stop();
}
```
V1：`StaticPngAnimationProvider`、`PngSequenceAnimationProvider`。预留 Layered2D/Live2D。
行为系统只使用 semantic ID，例如 `idle/happy/talking`，不得引用文件名。

## 5. 番茄钟 / Reminder / 统计

### Pomodoro
- Start/Pause/Resume/Stop。
- Focus/ShortBreak/LongBreak 时长自定义，自动阶段切换。
- Task + Tags。
- 日/周/月、今日时长、番茄数、连续专注天数。
- 事件联动 Pet/TTS/气泡。

### Reminder
- 相对一次性、绝对时间、重复提醒。
- CRUD + Enable/Disable。
- 渠道：Pet Bubble、Pet Action、Windows Notification、Sound；默认前三开、声音关。
- Missed：默认智能补发；重复提醒只补最近一次；策略可配置。

### 时间原则
**业务真相使用 UTC 绝对时间戳，不能依赖 Tick 递减。** 锁屏、睡眠、进程暂停后重新计算。

## 6. AI Chat

### Provider
- OpenAI
- DeepSeek
- Azure OpenAI
- OpenAI-Compatible（Base URL + API Key + Model）

```csharp
public interface IChatModelProvider
{
    string ProviderId { get; }
    Task<TestConnectionResult> TestConnectionAsync(AiConnectionSettings settings, CancellationToken ct);
    IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, CancellationToken ct);
}
```
- 支持流式、取消、超时、有限重试。
- API Key 默认使用 Credential Manager/DPAPI；允许“不保存”。
- 第一次进入 AI 页面才引导配置并测试连接。

### 会话
- 每角色一个长期 Main Conversation。
- 可创建 Temporary/Topic Conversation。
- 短期上下文按会话隔离；长期记忆按角色共享。

### 长期记忆
- 用户可查看、编辑、删除。
- 自动记忆可关闭。
- 敏感内容默认不自动保存。
- V1：SQLite 结构化记忆 + 标签/关键词；预留未来 Embedding/Vector Search。
- Context：最近 N 条 + 历史摘要 + 相关记忆。

### AI -> Pet
模型输出的 emotion/animation 仅作为 hint，经 `ResponseInterpreter` 白名单映射；不得让模型指定文件路径/执行代码。

## 7. AI Tool Registry

每个 Tool 必须包含：
```text
ToolId
Description
InputJsonSchema
RiskLevel
ConfirmationPolicy
CanUserDisable
ExecuteAsync
```

默认能力：
- Pomodoro：查询、开始、暂停、继续、停止。
- Reminder：创建、查询、修改、删除、启停。
- UI：打开设置、打开指定页面。
- Pet：显示/隐藏、静默、穿透、移动模式等。
- Settings：仅白名单字段允许修改。

风险：
- Low：可直接执行并回显。
- Medium：默认确认/可撤销。
- High：必须确认，保护不可关闭。
- Forbidden：Shell、任意文件执行、任意注册表、密钥读取、下载执行等不得注册。

所有 Tool 调用写脱敏审计日志。

## 8. TTS / Lip Sync
- V1 有 TTS，无 STT；预留 `ISpeechToTextProvider`。
- `WindowsTtsProvider` 默认；`ITtsProvider` 可扩展在线 Provider。
- 每角色独立 VoiceProfile；角色包只给推荐值，不包含密钥。
- 在线失败可回退 Windows TTS。
- LipSync V1：音量/VAD 驱动 mouth-open/mouth-closed；缺失资源自动回退 talking/普通动画；预留 Viseme。

## 9. UI
- Fluent 基础 + 轻角色主题；浅/深/跟随系统。
- Windows 10 允许视觉降级，功能一致。
- zh-CN + en-US；禁止硬编码中文。
- 首页默认陪伴优先，可排序/隐藏卡片。
- 页面：Home / AI / Pomodoro / Reminders / Stats / Characters / Settings。
- 控制中心 Close 默认 Hide to Tray。

首启仅：角色 → 活动方式 → 说话频率 → 开机启动。AI 配置后置。

## 10. 数据与安全

### app.db
建议表：
- SchemaMigrations
- UserProfile
- PomodoroSessions
- Tasks
- Tags
- TaskTags
- Reminders
- ReminderExecutions
- AppAuditEvents

### ai.db
- SchemaMigrations
- Conversations
- Messages
- Memories
- MemoryTags
- AiProviderProfiles（无 Key 明文）
- AiUsage
- AiToolAudit

数据访问必须 Repository + Service；UI 不准直接 SQL。

### 加密
- API Key：Credential Manager/DPAPI。
- 敏感 AI/用户档案字段：`IDataProtectionService` 加密。
- 普通统计无需强制加密。

### 备份
- 可选设置/业务/AI/角色包。
- API Key 默认不进入备份。
- 包含聊天/记忆等隐私数据时默认加密。
- 版本化加密封装；建议 AES-256-GCM + 现代 KDF；必须支持密码解密恢复且无后门。
- 恢复前校验并建立回滚点。

## 11. 电源、会话与恢复
- 锁屏/睡眠：暂停动画与自主行为，业务计时按绝对时间继续（遵循用户设置）。
- 唤醒：解析错过提醒、恢复番茄、可选欢迎行为。
- 全屏：降低主动说话/移动。
- 必须恢复：角色/位置/设置、番茄、提醒、数据库一致性、必要 AI 消息。
- 不必恢复：具体动画帧、低价值行为队列。

## 12. 性能
- 模式：Auto（默认）/ PowerSaver / Balanced / HighQuality。
- 禁止长期 16ms 轮询。
- 隐藏、锁屏、静止时停止或降低渲染。
- 角色图片缓存有明确释放策略。
- 先建立 `IPerformancePolicy`，实际 FPS/冷却参数集中配置，Alpha 再调优。

## 13. 安装/更新/日志
- 正式版 + Portable。
- x64 Self-contained。
- V1 不联网自动更新：`IUpdateService + NoOpUpdateService`。
- 本地结构化滚动日志，一键打开目录/导出诊断包。
- 诊断包自动脱敏。
- `ICrashReportingService` V1 空实现，不上传。

## 14. Phase 顺序与退出条件

| Phase | 内容 | 最小退出条件 |
|---|---|---|
| 0 | Solution/DI/Config/Log/Test | Build + Test，空控制中心可启动 |
| 1 | Pet Window | 透明/置顶/拖拽/托盘/位置恢复 |
| 2 | Character/Animation | 安装 Basic 包、Validator、PNG/序列帧、fallback |
| 3 | Runtime | 状态机/Behavior/Emotion/Utility，基础自主行为 |
| 4 | Movement/Display | 4 模式、SmartHybrid、多屏、DPI、穿透 |
| 5 | Control Center | Home/Settings/Character Manager/Hotkeys |
| 6 | Productivity | Pomodoro/Reminder/Task/Tag/Stats/睡眠恢复 |
| 7 | AI | Provider/API Key/Streaming/Conversation/Memory |
| 8 | AI Tools | Registry/风险确认/审计/内部工具 |
| 9 | Voice | Windows TTS/Provider/LipSync |
| 10 | Hardening | i18n/Backup/Diagnostics/Performance/Installer |

## 15. 每个 Phase 的 Codex 输出要求

Codex 每次只处理一个 Phase，并输出：
1. 修改/新增文件清单。
2. 关键架构决定。
3. 完整实现。
4. `dotnet build` / `dotnet test` 结果。
5. 手工验收步骤。
6. 已知限制（只允许属于后续 Phase 的限制）。
7. 不得在没有测试结果时声称完成。

## 16. 必测边界
- 无网络启动。
- Provider 超时/401/429/5xx。
- 100/125/150% DPI，多显示器、负坐标、热插拔。
- 睡眠跨越番茄/提醒。
- 损坏 ZIP、Zip Slip、超大角色资源。
- DB migration 成功/失败回滚。
- 错误备份密码/损坏备份。
- Tool 参数错误/高风险拒绝/取消确认。
- 控制中心关闭后后台继续。

## 17. 禁止实现
- 巨型 `MainWindow.xaml.cs`。
- 角色系统用动态弱类型 Dictionary。
- 角色包执行代码。
- API Key 明文落盘/日志。
- AI 任意反射执行。
- `Thread.Sleep` 驱动 UI。
- Tick 递减作为提醒/番茄唯一真相。
- 启动强制联网。

## 18. Definition of Done
- Windows 10/11 基础回归通过；100/125/150% DPI。
- 未配置 AI 时仍是完整可用软件。
- AI/TTS 网络故障不影响核心。
- 角色包损坏不崩溃、不污染安装目录。
- 关键状态崩溃后可恢复。
- 安装版与 Portable 均可启动/备份/恢复。
- 所有 V1 必做项有测试或手工验收记录。

## 19. 当前保留的调优项（不是需求缺口）
- 移动速度/加速度/冷却秒数。
- 角色 ZIP 默认容量/帧数限制。
- Context 最近消息 N 值与摘要阈值。
- Auto 性能模式的具体 FPS。
这些统一放入策略/配置并在 Alpha 阶段调优，不改变架构。

## 20. Phase 0 实施契约注释（2026-08-27）

- 已按第 2 节创建 9 个项目。Domain 是本项目的 Core 边界；Application/Domain/CharacterSdk 不引用 AI、WPF、SQLite 或 Windows Adapter。
- Phase 0 仅提供空控制中心和工程基础；未实现 Pet Window、托盘、角色渲染、行为、移动、办公业务、AI Provider 或 TTS。
- 数据库 V1 migration 只建立各自的 SchemaMigrations 账本。业务表随对应 Phase 用新版本 migration 引入，禁止修改已经应用的迁移脚本。
- IAppDataDirectories / ISettingsService / IDatabaseMigrator / IRecoveryCoordinator 分别负责目录、强类型配置、迁移和启动顺序；UI 不持有数据库连接。
- IAppLogger 只接受枚举事件、UTC 时间、错误码、来源和关联 ID；Configure 接收已经校验的 LogOptions。异常正文、聊天、记忆与密钥不进入日志 API。
- IAppLifetime 负责退出，ICommandRegistry 是仅接受显式注册命令的基础设施。Phase 0 关闭窗口即退出；Phase 1 有托盘后再实现默认 Hide to Tray，避免没有托盘时留下不可访问的后台进程。
- IAnimationProvider、IChatModelProvider 沿用本规范签名。角色、运行时、办公、AI Tool、Voice、Win32 等其余契约当前为强类型骨架，不代表后续功能已实现，也不注册虚假可用 Provider。
- app.db 迁移失败阻止启动；ai.db 迁移失败保持回滚并报告 AI 存储不可用，本地核心继续启动。此处“迁移失败停止启动”与“AI 故障不得影响核心”的冲突记录于 Phase 0 开发报告 Open Decisions；后续 AI 模块必须遵守不可用状态，不得绕过失败继续读写。
- 配置新于当前 schema 时拒绝降级；损坏配置保留原文件后恢复默认值。数据目录默认 Installed，--portable 明确选择便携数据根。
- 网络请求重试遵守仓库指令：可重试失败等待 1/3/7/15 秒；HTTP 403 立即抛出且不重试。目前只有 SDK 准备脚本联网，应用启动不联网。
- 已为上述跨模块契约同步建立单元与集成测试；细节及实际结果见 Phase-0-开发报告.md。

## 21. Phase 1 实施契约注释（2026-08-27）

- IWindowService 由 Phase 0 占位签名收敛为异步窗口生命周期契约；Show/Hide/Toggle/OpenControlCenter/CloseControlCenter/Exit 显式注册到 ICommandRegistry，未来输入源复用此入口，不直接调用 WPF 或 Win32。
- IPetWindow / IControlCenterWindow / ITrayService / IDisplayService / IUiDispatcher 为 Application 平台端口；Windows 层实现 HWND、拖拽、工作区、DPI 与托盘。IDisplayTopologyService 的邻接、漫游等仍留待 Phase 4。
- PetWindow 使用 220 DIP 的中性矢量占位，无角色包、语义动画或正式 Character Runtime。App 与 Window Code-Behind 仅承担框架接线；ApplicationHostController 管理宿主和异常边界。
- Settings schema 2 新增 PetWindow（物理像素坐标、显示器标识、可见性、置顶）和 ControlCenterCloseBehavior；schema 1 迁移保留原有偏好。ISettingsService.UpdateAsync 在配置锁内读取最新快照并原子替换，窗口不得直接写 JSON 或 SQLite。
- 恢复使用当前显示器 WorkingArea；负坐标合法，无效位置回到可见区域。原点不按 DPI 全局除法换算，尺寸由 DIP/DPI 得到物理像素。WPF 处理 WM_DPICHANGED，平台适配只在处理后通知应用校正，避免二次缩放。
- 控制中心默认关闭即隐藏到托盘；PetWindow 的关闭请求映射 HidePet；Exit 保存状态、释放托盘、真正关闭窗口并停止宿主。保存失败仍执行清理，并通过现有异常边界报告。
- 新增位置、DPI、生命周期、配置迁移、托盘命令接线及真实窗口/进程测试；Phase 0 测试全部保留。人工验收状态和环境限制见 Phase-1-开发报告.md。当前不进入 Phase 2。
