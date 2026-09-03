# Phase 9 开发报告

## 1. Status / 完成功能 / 主要文件

**PARTIAL**。Voice 功能、Debug/Release 构建和 340 项自动测试全部通过；Windows 11 便携版可创建进程，隔离数据下的真实 WPF 启动、九页面渲染和正常退出 smoke 通过。由于 Windows 控制助手在窗口选择前连续异常退出，未完成可听声音、鼠标操作、真实 OpenAI 凭据和角色嘴型的 15 项人工验收，因此按 DoD 不报告 PASS。

主要文件：

- `Application/Contracts/VoiceServices.cs`、`Configuration/VoiceSettings.cs`、`Voice/*`：TTS、播放、VoiceProfile、SpeechService 和振幅嘴型的强类型边界与策略。
- `Windows/Voice/*`：系统 Voice 枚举、离线 WAV 合成、MediaPlayer 播放和临时文件清理。
- `AI/Providers/OpenAiTtsProvider.cs`、`Services/AiChatService.cs`：可选 OpenAI TTS 和受控自动朗读接入。
- `Infrastructure/Characters/CharacterVoiceProfileReader.cs`：读取已验证角色包的 `voice.json`。
- `App/ViewModels/VoiceSettingsViewModel.cs`、`AiViewModel.cs`、对应 XAML/RESX：中英文 Voice 设置、测试、Read Aloud 和 Stop。
- `PhaseNineVoiceTests.cs`、`PetRuntimeTests.cs`、`tools/Verify-Phase9.ps1`：阶段测试和统一复验入口。

## 2. TTS Provider

`ITtsProvider` 提供 ProviderId、Voice 列表、有界统一 WAV 输出和可取消合成。默认 `WindowsTtsProvider` 使用 `System.Speech` 10.0.11，后台枚举本机已安装 Voice，缺失选择按区域、语言和系统默认安全回退，并映射 Speed/Volume；异步合成可由 `SpeakAsyncCancelAll` 中断，不阻塞 WPF UI。

`OpenAiTtsProvider` 使用现有 Phase 7 活动 OpenAI Profile、BaseUrl 和 Credential Vault，调用 `/audio/speech`，默认模型 `gpt-4o-mini-tts`，Voice 为官方白名单，输出 WAV，限制 16 MiB。401/403 等非可重试响应立即失败；429/5xx/网络故障使用可取消的 1/3/7/15 秒退避。启动和 Voice 枚举不联网；在线失败且设置允许时回退 Windows。

## 3. VoiceProfile / Policy

解析顺序为系统约束 > 有效用户显式覆盖 > 当前角色推荐 > 安全默认。角色只能推荐 Provider、Voice、Speed、Volume，不接受 Key、URL 或文件路径。每次发声重新读取当前角色配置；角色切换不会复用旧推荐，用户 Override 不会被覆盖。非法 Provider/Voice/数值回退到可用 Provider、匹配区域 Voice 和受限默认值。

## 4. Speech Lifecycle

`ISpeechService` 统一 Speak/Stop/IsSpeaking。V1 使用抢占策略：新请求先取消并等待旧请求，保证单 Pet 最大并发播放为 1。Exit、角色切换、指针按下/拖拽、隐藏、锁屏/睡眠和 Runtime shutdown 触发取消；所有成功、取消和失败路径都释放播放资源并退出 Talking。自动朗读受 Enabled、AutoRead、Silent、Focus 和可见/会话状态约束；用户主动朗读可在 Silent/Focus 下按产品策略继续。

## 5. Talking / Lip Sync / Fallback

播放器真正就绪后才进入高优先级 Talking；结束、取消、合成/播放/首帧失败均回到 Idle/安全运行状态。角色同时具备 `mouth-open`/`mouth-closed` 时，`AmplitudeLipSyncProvider` 解析 PCM WAV，以 80 ms 窗口和阈值产生开/闭帧；否则回退 `talking`，再回退当前兼容动画。未实现音素、Viseme 或 STT。

## 6. UI / Security

Settings 增加 Enable、Provider、Voice、Model、Speed、Volume、Auto-read、Silent、Focus Suppression、Online Fallback、Test 和 Stop；AI 页支持选择完整 Assistant 消息后 Read Aloud/Stop。新增文本包含 zh-CN/en-US。

API Key 只通过既有 Credential Vault 读取，并在请求头生成后清零读取缓冲；设置、日志和数据库不保存 Key、Authorization、完整朗读正文或音频。日志只记录枚举事件/状态。临时 WAV 只写入应用拥有的 `%TEMP%/DesktopPet/voice/speech-*.wav`，播放、失败、取消、Dispose 和下次启动均清理。

## 7. 自动 / 人工测试

| 配置 | Restore | Build | Unit | Integration | 结果 |
| --- | --- | --- | ---: | ---: | --- |
| Debug | PASS（locked） | PASS，0 warning / 0 error | 212/212 | 128/128 | PASS |
| Release | PASS（locked） | PASS，0 warning / 0 error | 212/212 | 128/128 | PASS |

覆盖 Windows Voice/真实本机 WAV 合成与 fallback、OpenAI Fake HTTP 成功/403/500/超时/取消/退避、在线转本地、VoiceProfile 优先级和角色重新解析、Settings schema 9、Silent/Focus/隐藏策略、无重叠、Stop/拖拽/切换/退出、Talking 进入/退出/失败回滚、振幅开闭/静音、资源 fallback、无正文日志和临时文件清理。Phase 0–8 全量回归保留。TRX 位于 `artifacts/TestResults/Phase9/{Debug,Release}`。

实际环境：Windows 11 家庭版中文 64 位（10.0.26200）、.NET SDK 10.0.400。两种配置的集成测试均以断网代理和隔离目录真实启动 WPF，遍历九页面并正常退出；另以 `--portable` 创建 Debug 进程后清理。Computer Use 助手重置后仍异常退出，故未点击设置/朗读按钮，也未验证听感、Speed/Volume 主观效果、嘴型视觉同步、中文/英文实际切换、真实 OpenAI TTS、错误 Key 回退和操作中的即时 Stop。这些 15 项人工验收仍需用户执行。

## 8. Open Decisions / Risks

- 系统 Voice 数量、语言和质量由 Windows 10/11 安装状态决定；目标机缺少 zh-CN/en-US Voice 时会安全回退，但无法承诺指定语言音色。
- `MediaPlayer` 实际声卡输出、音量/速率感受、Stop 延迟、混合 DPI 下控制中心交互及角色素材嘴型需要人工验证。
- OpenAI TTS 当前只复用活动的 OpenAI 类型 Profile；Azure/OpenAI-Compatible TTS 未在 Phase 9 扩展，避免把未确认协议伪装为兼容。
- Focus 在开始自动朗读时抑制；本阶段未增加新的环境监听或复杂“朗读中进入 Focus”的分级策略。
- OpenAI 模型、Voice 和 API 行为可能随服务升级变化，Adapter 白名单和默认模型后续需按官方兼容性维护。

## 9. Git 状态

分支 `main`；origin 仍为 `https://github.com/zhiuanL/elfin.git`；基线 tag `v0.8-phase8` 存在；本地 HEAD 比 origin/main 领先 2 个提交。工作树仅含 Phase 9 源码、测试、脚本、规范注记和本报告，未提交、未 push。未加入 API Key、`*.db`、bin/、obj/、logs、本地用户配置或生成音频。

## 10. Phase 10 建议

先由用户按第 7 节补完 Phase 9 的 15 项真实语音人工验收并确认 PASS，再依据设计文档单独下达 Phase 10 指令。本次未进入 Phase 10。
