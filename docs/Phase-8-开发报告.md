# Phase 8 开发报告

## 1. Status

**PARTIAL**。Phase 8 实现、ai.db v3 migration、Debug/Release 构建及 321 项自动测试全部通过；真实 WPF 启动、页面遍历和退出 smoke 通过。尚未使用用户的真实 Provider 逐项完成人工 Tool Calling 验收，因此按 DoD 不报告 PASS。

## 2. 完成功能 / 主要文件

- DesktopPet.AI/Contracts/ToolContracts.cs：强类型 Tool、风险、确认、执行结果和审计契约。
- DesktopPet.AI/Tools/*：Schema 校验、集中脱敏、注册表及 Pomodoro、Reminder、UI、Pet、Settings 白名单工具。
- DesktopPet.AI/Providers/ChatCompletionsProvider.cs：发送 tools，流式聚合多个 tool_calls，映射 assistant/tool 消息。
- DesktopPet.AI/Services/AiChatService.cs：有界工具循环、取消、失败回传、最终回复和最大轮次保护。
- Infrastructure/Persistence/SqliteMigration.cs、SqliteAiRepositories.cs：ai.db v3 AiToolAudit。
- App/ViewModels/AiViewModel.cs、Views/Pages/AiPage.xaml：Tool 总开关、列表、风险、单项启停、确认偏好和轻量审计。
- tools/Verify-Phase8.ps1、PhaseEightAiToolTests.cs、PhaseEightAiToolPersistenceTests.cs：阶段复验入口与测试。

## 3. Tool Registry / Orchestrator

Provider 只收到当前启用且非 Forbidden 的定义。响应按 ToolCallId → Registry → JSON Schema → 风险/确认 → 既有 Application Service/Command → 结构化 ToolResult → Provider 执行。每次对话最多 4 个工具轮次、8 次执行；达到上限后只允许一次无工具最终回复，Provider 若仍违规请求工具则返回本地安全终止消息。对同一 ConversationId + ToolCallId 使用进程内并发去重，避免网络或模型重复调用造成二次副作用。

## 4. Risk / Confirmation

Low 直接执行；Medium 默认确认，仅在强类型设置明确选择“允许可撤销操作免确认”时跳过；High 每次强制确认且无法通过偏好关闭。确认框默认 Deny，并显示工具名、说明、脱敏参数摘要和风险。用户拒绝返回 Denied/user_denied，不执行业务，聊天仍可继续生成回复。Forbidden 定义不会进入可用注册表或 Provider payload。

## 5. Tool 列表

- Pomodoro：get/start/pause/resume/stop，复用 IPomodoroService。
- Reminder：list/create/update/enable/disable/delete，复用 IReminderService；delete 为 High。
- UI：openPage/openSettings/showControlCenter，复用 Navigation/Command Registry。
- Pet：show/hide/setMovementMode/setClickThrough，通过既有 Command/Settings 边界。
- Settings：仅 motionStyle/theme/alwaysOnTop 明确白名单；不能提交整个 Settings 对象。

未注册 Shell、文件、进程、注册表、任意 SQL/Win32/URL 下载、Credential 或 API Key 读取工具。

## 6. Audit / Security

AiToolAudit 记录 UTC 时间、ConversationId、ToolCallId、ToolId、Risk、集中脱敏 ParameterSummary、Confirmation、Status、Duration 和 ErrorCategory。数据库唯一索引保证同会话 ToolCallId 审计去重。摘要不保存完整聊天、Memory、API Key、Authorization、密码或普通文本正文；明显敏感字段固定为 [redacted]，其他文本只记录长度。审计写入失败会被安全日志边界捕获，但不会把已成功业务执行伪装成失败，从而降低重复执行风险。

## 7. Provider Integration

四类 Provider 继续复用 Chat Completions 适配层。内部带点 ToolId 映射为 Provider 兼容函数名，流式 arguments 按 index 拼接，可处理一轮多个调用；assistant tool_calls 与 role=tool/tool_call_id 按协议回传。原有 401/403 不重试及 1/3/7/15 秒可取消退避保持不变。

## 8. UI

AI 页面新增中英文 Tool 标签页：全局开关、Medium 确认偏好、19 个 V1 工具的说明/风险/状态、单项启停和最近 30 条脱敏审计。UI 不直接访问 JSON 或 SQLite。WPF 确认适配器只负责显示确认，风险决策仍在 Registry。

## 9. 自动 / 人工测试

| 配置 | Restore | Build | Unit | Integration | 结果 |
| --- | --- | --- | ---: | ---: | --- |
| Debug | PASS（locked） | PASS，0 warning / 0 error | 199/199 | 122/122 | PASS |
| Release | PASS（locked） | PASS，0 warning / 0 error | 199/199 | 122/122 | PASS |

自动覆盖 Schema、Low/Medium/High/Forbidden、确认/拒绝、禁用、重复 ToolCallId、集中脱敏、多个 tool_calls、完整编排回传、工具失败后继续、最大轮次、19 项 V1 catalog、audit migration/repository/settings 持久化，以及 Phase 0–7 全量回归。TRX 位于 artifacts/TestResults/Phase8/{Debug,Release}。

实际环境：Windows 11 x64、.NET SDK 10.0.400。Debug/Release 集成测试均真实启动 WPF 进程，完成九页面实例化/遍历、Pet 渲染和正常退出；这是 smoke，不等同于真实 Provider 工具人工验收。Pomodoro/Reminder/UI/Pet/Settings 的真实模型调用、Allow/Deny 对话框、不同 Provider、多轮组合调用仍待用户人工验证。

## 10. Open Decisions / Risks

- Medium 的免确认仅适用于当前标记为可撤销的创建/修改类操作；产品若需更细的逐工具策略，应在后续单独定义，不能放宽 High。
- 幂等缓存为进程内本轮防重；应用崩溃后 Provider 重放旧 ToolCallId 的跨进程 exactly-once 语义未承诺。数据库审计唯一键可观测重复，但不作为所有业务库的分布式事务。
- OpenAI-Compatible 服务对工具名、并行 tool_calls 和流式碎片的兼容程度不同，需逐 Provider 人工验证。
- Windows 10、断网/超时发生在确认前后、混合多个 Medium/High 调用的交互体验仍需人工验收。

## 11. Git 状态

分支 main，本地 HEAD 因 Phase 7 基线提交领先 origin/main 1 个提交；origin 未修改，基线 tag v0.7-phase7 已确认存在。工作树只含 Phase 8 源码、测试、脚本和本报告；未提交、未 push。未加入 API Key、*.db、bin/、obj/、logs、本地用户配置或测试临时数据。

## 12. Phase 9 建议

先由用户完成 Phase 8 真实 Provider 与人工 Tool 验收并确认 PASS。之后再依据设计文档进入 Phase 9；本阶段未实现 Voice、TTS、Lip Sync 或其他 Phase 9+ 功能。
