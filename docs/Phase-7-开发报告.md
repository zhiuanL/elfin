# Phase 7 开发报告

## 1. Status

**PARTIAL**。Phase 7 代码、`ai.db` migration、Debug/Release 构建和 307 项自动测试全部通过；无 AI 配置、禁用网络代理下的真实 WPF 启动/九页面遍历/退出通过。用户已确认所配置 Provider 的模型获取和连接测试通过；其余真实 Provider 与完整聊天链路尚未全部人工验收，因此按 DoD 不报告 PASS。

## 2. 完成功能

- OpenAI、DeepSeek、Azure OpenAI、OpenAI-Compatible 共用 `IChatModelProvider`，支持连接测试、模型发现、SSE Streaming、Stop、Timeout 与有限 Retry。
- Provider 类型切换会填入官方默认 URL 或明确的可编辑 URL 模板；输入 Key 后可调用模型列表接口并在可编辑下拉框中选择模型。
- 强类型 Provider Profile；Key 支持 DPAPI 保存、Session Only、替换、删除，不进入普通配置或数据库正文。
- Main/Temporary/Topic 会话、按角色隔离、流式 partial + Interrupted、Provider 切换不绑定会话。
- 集中 Context Budget：Persona、结构化 Memory、旧摘要、最近消息、当前输入。
- Memory 查看、新增、编辑、删除、按角色/全部清空、自动开关、敏感过滤、去重和有界检索。
- 白名单 `emotionHint`、`animationSemantic`、`ttsPreference`；前两者经 PetRuntime 门面执行，TTS 只保留提示字段。
- 控制中心新增 AI 页面及中英文文本：会话、消息、输入、Send/Stop/Retry、Provider Setup 和 Memory 管理。
- 聊天输入框支持 `Enter` 发送、`Shift+Enter` 换行；键盘手势只适配到现有 `SendCommand`，发送流程仍位于 ViewModel/Application Service。

## 3. 主要文件

- `DesktopPet.AI/Contracts/ChatContracts.cs`：Provider、会话、消息、Memory、Context、Credential 和 Hint 强类型契约。
- `DesktopPet.AI/Providers/ChatCompletionsProvider.cs`：四类 Chat Completions/SSE 适配及集中退避策略。
- `DesktopPet.AI/Security/AiCredentialVault.cs`：Saved/Session Only Key 生命周期。
- `DesktopPet.AI/Services/AiServices.cs`、`AiChatService.cs`：Profile、Context、Memory、Streaming 与 Hint。
- `Infrastructure/Persistence/SqliteAiRepositories.cs`、`SqliteMigration.cs`：AI Repository 与 ai.db v2。
- `Infrastructure/Security/DpapiFileSecretStore.cs`：CurrentUser DPAPI 密文文件存储。
- `Infrastructure/Characters/CharacterPersonaSource.cs`：经 Character Application 边界加载 Persona。
- `App/ViewModels/AiViewModel.cs`、`Views/Pages/AiPage.*`：AI 控制中心 UI。
- `tests/*/PhaseSeven*.cs`、`tools/Verify-Phase7.ps1`：阶段测试与复验入口。

## 4. Provider / Credential

OpenAI、DeepSeek、OpenAI-Compatible 使用 Bearer；Azure OpenAI 使用 `api-key`，当前 Azure v1 路径为 `/openai/v1/chat/completions`，模型发现使用同一根地址下的 `/models`。上层不依赖具体 SDK。401/403 立即终止；429、5xx、临时网络错误最多按 1/3/7/15 秒退避，等待可取消。HttpClient 由 DI 单例复用。

Profile 只保存 `SecretReference`。Saved Key 由 `IDataProtectionService` 的 CurrentUser DPAPI 加密后写入独立 credential 目录；Session Only 只存在进程内并在 Dispose 时清零。UI 保存后清空输入，不提供读取完整已保存 Key。未保存的输入 Key 在获取模型时只进入临时 Session Credential，并在请求结束后立即删除；已有 Profile 可复用其保存的 Key。

## 5. ai.db / Conversation

ai.db v2 只向前新增 `AiProviderProfiles`、`Conversations`、`Messages`、`Memories`、`MemoryTags`、`AiUsage` 和 `AiCharacterPreferences`。唯一部分索引保证每个 Character 最多一个 Main；Temporary/Topic 独立。Message/Memory 正文以实体 ID 分目的加密为 BLOB，Provider/Model/状态/Token 元数据保持结构化。升级失败整体回滚并保留 v2 前状态的测试通过。

## 6. Streaming / Context

SSE delta 经 `IAsyncEnumerable<ChatDelta>` 实时传入 UI；取消和异常保存已接收正文，分别标记 Interrupted/Failed。应用退出、角色/会话/Provider 切换和 Stop 都发出取消。Context 总字符预算 12000，最近消息上限 20，相关 Memory 上限 8；不会无限发送历史，也未引入 Vector DB、Embedding、RAG 或 Agent。

## 7. Persona / Memory

Persona 从已验证角色包经 `ICharacterPackageStore` 获取；无 Persona 使用离线默认 Assistant Persona。Memory 按 Character 隔离，正文加密；检索综合 Category、Tag、Keyword、Importance、Recency。自动保存默认关闭，执行 Candidate → credential/password/token 敏感检查 → 规范化去重 → 重要性门槛 → 持久化。

## 8. AI → Pet

回复末尾只识别 `<pet-hint>` 中三个允许字段。Emotion 值白名单；Animation/TTS 只接受短语义标识符，路径、方法/类字段、Shell 和任意额外字段均拒绝。Animation Semantic 交给现有 PetRuntime/AnimationResolver，未知语义使用已有 fallback；未实现 TTS、Lip Sync 或 Tool Calling。

## 9. UI / Security

AI 页首次无 Profile 时显示可跳过的 Setup。包含 Conversation List、Messages、Input、Send/Stop/Retry、Temporary/Topic、Memory CRUD/Auto 和 Provider 的类型/名称/BaseUrl/Model/PasswordBox/保存方式/Timeout/Test/Active/Delete。Provider 类型改变时自动填入 OpenAI/DeepSeek 默认 URL；Azure OpenAI/OpenAI-Compatible 因主机由用户账户决定，填入必须替换的模板。Model 使用可编辑下拉框，既能加载 `/models` 返回项，也允许 Azure deployment name 或兼容服务不支持模型列表时手工输入。UI 不访问 SQL/JSON；PasswordBox code-behind 只完成控件到 ViewModel 的输入适配和清空同步。日志模型未扩展为记录消息、Memory、API Key 或 Authorization。

## 10. 自动测试

| 配置 | Restore | Build | Unit | Integration | 结果 |
| --- | --- | --- | ---: | ---: | --- |
| Debug | PASS（locked） | PASS，0 warning / 0 error | 187/187 | 120/120 | PASS |
| Release | PASS（locked） | PASS，0 warning / 0 error | 187/187 | 120/120 | PASS |

覆盖四类 endpoint/认证、Provider 默认 URL、模型列表请求/解析/排序、输入 Key 的临时 Session Credential 及请求后删除、401/403、429/5xx、1/3/7/15 退避、timeout/cancel、SSE/partial、Credential Saved/Replace/Delete/Session、Main 唯一性、三类会话、消息顺序/状态、Context/Persona/budget、Memory CRUD/隔离/开关/敏感/去重/limit、密文 roundtrip/明文扫描、migration/rollback、Safe Hint 及 Phase 0–6 回归。TRX 位于 `artifacts/TestResults/Phase7/{Debug,Release}`。

## 11. 人工测试

环境：Windows 11 家庭版中文版 x64，10.0.26200。真实 Release WPF 进程在无 Provider、不可用代理和隔离数据目录下完成启动、九页面实例化/遍历及正常退出，Pet/Pomodoro/Reminder 未因 AI 配置缺失而失败。用户人工反馈：所配置 Provider 的“获取模型”和“测试连接”均通过。错误 Key、其余 Provider、Streaming、Stop/Retry、重启历史、角色切换、Memory/Persona/Hint、断网聊天、Key Replace/Delete 尚未全部人工验证；UI 自动化辅助程序连续两次因 Windows sandbox helper 初始化失败，未声称这些项目已验收。

## 12. Open Decisions / Risks

- Provider 采用共同 Chat Completions SSE 协议；接口保持稳定，未来若某 Provider 停止兼容可在适配层替换，不影响 Application/UI。
- 自动 Memory 只从用户消息做本地轻量候选提取，避免隐式额外模型请求；更复杂抽取策略等待产品确认。
- Hint 使用明确的 `<pet-hint>` 尾部 JSON 协议；未来若改为原生结构化输出，仅替换 Interpreter/Provider 映射。
- 已配置 Provider 的模型列表和连接已由用户联网验证；其余 Provider 的模型名、配额、区域、代理、TLS 和服务端错误格式仍需人工验收。真实 Key 不得写入缺陷附件或诊断包。
- Windows 10、不同网络代理、睡眠/恢复期间 Streaming 和长时间生成尚未人工验证。

## 13. Git 状态

分支 `main`，origin 未修改，基线 tag `v0.6-phase6` 存在。工作树仅含本次 Phase 7 源码、测试、锁文件、脚本和报告；未提交、未 push。`*.db`、`bin/`、`obj/`、logs、本地配置、真实 Key 和测试产物均未加入 Git。

## 14. Phase 8 建议

等待用户完成 Phase 7 真实 Provider 与人工 UI 验收并确认 PASS 后，再按设计文档实现 Tool Calling、风险分级、确认和审计；不要在 Phase 7 分支继续实现 Phase 8。
