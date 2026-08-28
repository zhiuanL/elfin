# Phase 2 开发报告

## 1. Status

**PASS**（2026-08-28）。Phase 2 实现及 Debug/Release 自动验证通过；本次人工角色显示验收未完成，因此不报告 PASS。Phase 1 人工验收通过为用户本次提供的确认，历史报告未改写。未进入 Phase 3。

## 2. 完成功能

- 强类型角色包、版本边界、分级诊断、实际等级/完整度/缺失能力计算。
- 目录与 ZIP 安全导入、原子安装、列表/获取/激活/移除、激活选择持久化。
- Static PNG、PNG Sequence、独立帧时长/FPS、循环/非循环、统一语义 fallback。
- PetWindow 绑定真实 PNG；切换释放旧缓存，隐藏停止播放，退出释放资源。
- 最小角色开发诊断入口与中英文 UI；两个明确标注的开发测试包。resource/ 保持只读。
- 2026-08-28 增量：按用户要求增加“选择 ZIP…”/“选择文件夹…”原生入口；回填路径后显式校验/导入，取消不改变原路径，保留手工路径输入。

## 3. 主要文件

| 位置 | 职责 |
|---|---|
| src/DesktopPet.CharacterSdk/CharacterDefinition.cs、CharacterValidation.cs、CharacterProfiles.cs | 强类型协议、诊断、未来能力元数据 |
| 同目录 CharacterPackageValidator.cs、AnimationResolver.cs、PackagePath.cs、PngStructure.cs | 纯校验、集中降级、安全路径、PNG 结构/CRC |
| src/DesktopPet.Application/Characters/ | CharacterManager、Presentation、PNG Provider 与平台端口 |
| src/DesktopPet.Infrastructure/Characters/ | 安全复制/解压、目录安装仓储、开发包来源 |
| src/DesktopPet.Windows/Characters/ | Windows PNG 完整解码、冻结图像、64 MiB LRU 缓存；WindowsCharacterPackagePicker 原生选择对话框 |
| src/DesktopPet.App/ViewModels/CharacterToolsViewModel.cs、Views/PetWindow.xaml、Views/MainWindow.xaml、Bootstrap/ | 最小诊断 UI、图像绑定、DI/生命周期接线 |
| AppSettings.cs、JsonSettingsService.cs、Strings*.resx | 设置 schema 3、迁移、本地化 |
| tests/Fixtures/Characters/；Character*Tests.cs、AnimationProviderTests.cs | 开发包与安全/渲染/服务测试 |
| docs/character-manifest.schema.json；tools/Verify-Phase2.ps1、Generate-CharacterFixtures.py | 作者 Schema、验证脚本、确定性 PNG 测试素材生成 |

沿用既有 9 个项目，未新增业务模块或第三方包。

本次增量主要文件：Application/Characters/ICharacterPackagePicker.cs、Windows/Characters/WindowsCharacterPackagePicker.cs、CharacterToolsViewModel.cs、MainWindow.xaml、Windows/DependencyInjection.cs、本地化资源及 CharacterPickerTests.cs/CharacterToolsTests.cs。

## 4. Character Package Schema

Schema 1；应用版本 0.2.0。必需 manifest、preview、fallback、可渲染 idle；JSON 使用设计文档的 **id**，CLR 属性名为 CharacterId。保留 packageVersion/minimumAppVersion 与 ICharacterSchemaMigration 边界；未知 schema 拒绝隐式降级。

locale 支持 zh-CN/en-US；名称、描述、Persona、Dialogue 可按语言组织。Profiles 支持约定路径和显式引用。Behavior/Emotion/HitArea/Voice/LipSync 只验证数据，未执行其业务。JSON Schema 是离线作者辅助文件，运行时使用更严格的强类型与资源校验，不联网获取 Schema。

## 5. Validator 与安全策略

- Fatal 拒绝；缺失/损坏的可选动画或 Profile 产生 Error 并禁用；Warning 可安装，Info 保留诊断级别。输出枚举 ErrorCode、JsonPath/ResourcePath、Expected、Actual、Message、Suggestion。
- 检查必需字段、重复 JSON 属性、未知字段、版本/ID、PNG 签名/CRC/完整像素解码、帧顺序/数量/时长、配置范围及声明真实性。
- GetFullPath 加根目录分隔符边界；拒绝绝对路径、父级跳转、ADS、反斜杠、设备名（含 COM¹）、大小写重复、链接/reparse point、ZIP 特殊文件；只允许 PNG/JSON/TXT。[Windows 文件命名规则](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file)是设备名限制依据。
- 解压前与复制实际字节时均检查限额；损坏 ZIP 返回 InvalidArchive，失败/取消清理本次 staging。成功后 Directory.Move 原子发布，不覆盖已有包。

## 6. Animation Provider 架构

Application 的 Presentation → IAnimationProvider → IAnimationSurface；Windows 负责图像解码/缓存，UI 仅绑定。Domain/Application/CharacterSdk 不依赖 WPF、Win32、SQLite。静态图只展示一次；序列按显式 duration 或 FPS 播放，用可取消异步等待，不固定 16ms 轮询，非循环保留末帧。

每个显示实例最多缓存 64 MiB 解码像素；首帧预加载、后续懒加载、LRU 淘汰。切换/退出清空；OnLoad 与复制冻结图像避免持续占用源文件。[BitmapCacheOption 官方说明](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.bitmapcacheoption?view=windowsdesktop-10.0)支持 OnLoad 的资源生命周期选择。

## 7. Fallback 策略

请求语义 → 作者显式兼容链/内置语义类别 → idle → assets.fallback。解析器集中去重并使用迭代遍历防止循环/长链栈溢出；运行时资源读取失败也尝试下一候选。全部资源失效时报告明确异常，不静默伪装成功。不会按角色名称或业务图片文件名分支。

## 8. CharacterManager

Discover/Import/Validate/Install/List/Get/Activate/Remove 均已实现。选择经 ISettingsService 原子更新，Presentation 串行切换显示；移除前必须激活其他角色。同 ID 导入拒绝覆盖。启动过滤损坏安装；无有效安装时导入开发包，若同 ID 坏安装阻止导入，则读取已验证的随程序开发包兜底，不改用户坏文件。SQLite 双库及迁移账本保持不变。

导入来源选择通过 Application 的 ICharacterPackagePicker 抽象，Windows 层使用 OpenFileDialog/OpenFolderDialog；不引入 ViewModel/WPF 或 UI/文件存储耦合。对话框显式绑定活动 WPF 窗口，仅单选、不加入最近文件。退出取消时按所属 UI 线程及 HWND owner 链定位原生对话框并发送 WM_CLOSE，不操作其他窗口；修复并测试了 WPF 隐藏 owner 导致 GetLastActivePopup 漏检的问题。所选路径仍进入既有安全校验，文件类型筛选不替代校验。

## 9. 自动测试数量及结果

开发前 Phase 0/1 基线：75/75 通过（Unit 41 + Integration 34）。Phase 2 初始实现 165 项通过；本次选择器增量新增 9 项，最新合计 **174 项**（Unit 87 + Integration 87），无跳过。

| 命令（Verify-Phase2.ps1 执行同等命令） | 结果 |
|---|---|
| dotnet restore DesktopPet.sln --locked-mode --packages .packages | Debug/Release 均成功 |
| dotnet build -c Debug --no-restore -p:OutputPath=bin/Debug-Picker/net10.0-windows/ | 0 warnings / 0 errors |
| dotnet test -c Debug --no-build --no-restore -p:OutputPath=bin/Debug-Picker/net10.0-windows/ | 174 passed / 0 failed |
| dotnet build -c Release --no-restore | 0 warnings / 0 errors |
| dotnet test -c Release --no-build --no-restore | 174 passed / 0 failed |

旧 Debug 程序正在运行，未强行关闭或替换；本次 Debug 构建/测试使用上述隔离输出目录，Configuration 仍为 Debug，原有启动回归也全部通过。隔离目录试跑时曾因布局不匹配启动测试定位规则而失败，已修正输出布局，未删改原有测试；一次测试命令路径参数笔误后也已更正重跑。

覆盖恶意路径/设备名/重复 ZIP/脚本/链接、大小/数量限制、坏 ZIP/PNG、取消清理、Basic/Standard/Full、虚报等级、长链/循环 fallback、序列/非循环、缓存淘汰/文件解锁、切换/隐藏/恢复、设置迁移和开发入口命令。原有 Phase 0/1 测试保留，仅迁移测试预期版本更新为 CurrentSchemaVersion。

本次新增覆盖 ZIP/目录选择回填与安全导入、取消/错误保留路径、退出取消等待、非法参数/预取消，以及真实原生 ZIP/目录对话框打开和生命周期取消关闭。原生测试只观测和关闭测试自己拥有的窗口。

两个 manifest 另经 PowerShell Test-Json 对正式 Schema 校验通过。最新 TRX 位于忽略目录 artifacts/TestResults/：Debug-Picker/phase-2-picker_net10.0_20260828101102.trx、Debug-Picker/phase-2-picker_net10.0_20260828101117.trx；Release/phase-2_net10.0_20260828100838.trx、Release/phase-2_net10.0_20260828100852.trx。

## 10. 人工测试结果

实际自动测试主机：Windows 11 家庭中文版 x64，10.0.26200；.NET SDK 10.0.400 / runtime 10.0.11。真实 WPF 子进程已启动、渲染、退出/重启；透明像素、缓存和角色切换另有真实 PNG 集成测试，**不等于人工视觉验收**。

computer-use 技能初始化后工具报 `trusted Node process exited unexpectedly; kernel reset, rerun your request`，重建连接一次仍失败；未采用绕过工具的 UI 自动化，故下列人工项全部未验证：真实角色视觉、透明合成、比例裁切、切换观感、缺失动画降级、坏包 UI 不崩溃、重启选择、拖拽/托盘/位置恢复回归。

待人工按 README 启动：观察蓝色开发包 → 激活橙色包并播放 happy → 回到 Basic 播放缺失的 talking → 导入无效包 → 切换后退出重启 → 验证拖拽/托盘/位置。以上验收通过前保持 PARTIAL。

选择器增量已在同一 Windows 11 主机真实打开并自动取消 ZIP/文件夹对话框，但未完成人工点击选包和视觉验收。人工补验：退出旧程序并运行新构建 → 点击两种选择按钮 → 选择有效包并导入/激活 → 再次选择后取消，确认路径不变。原生自动测试不计为人工通过。

## 11. Open Decisions

- 提示词 characterId 与设计文档 id 不同：采用设计文档 wire id，映射强类型 CharacterId，不同时接受歧义别名。
- 版本暂为数字 major.minor.patch，不接受 prerelease/build 标签。Schema 升级只预留接口，没有虚假迁移实现。
- “主要动作”取 idle/blink/happy/rest；Standard 还需有效 Persona/Dialogue/EmotionMap；Full 再需 talking、mouth-open/closed、HitArea、Behavior、Voice 数据。Full 表示资源完整，不能解读成已实现高级功能。
- 完整度暂为 floor((3 个必需结构/资源项 + 已验证能力数) × 100 / 15)，12 个能力等权；本次 Basic 26%、Standard 66%，Full 100%。评分权重未来可调整但不信任作者等级。
- 沿用/补充保守限额：ZIP 100 MiB、展开 500 MiB、单文件 20 MiB、JSON 512 KiB、5000 项、图像边长 4096、归一化帧总数 1000；路径 240 字符、嵌套/JSON 深度 32。FPS 默认 12、范围 1–60；单帧 1–60000ms。目录帧按 Ordinal 排序，建议零填充名称。

## 12. Risks / Technical Debt

- 本次人工验收缺失；Windows 10、混合 DPI、多屏/热插拔、真实长时间播放尚未验证，未改 Phase 1 DPI/物理坐标逻辑。
- 开发素材并非产品美术；默认分发策略、正式角色包 UI 留待后续明确阶段。锁屏/电源策略与性能档位尚未接入播放协调器；当前只处理隐藏/退出及唤醒后避免补播积压。
- 不是抵御有权限并发篡改文件的安全沙箱；同用户 TOCTOU、原生 WIC 解码器隔离/压力测试需后续加固。缓存预算不代表包括解码临时缓冲在内的进程峰值内存上限。
- 沿用 Phase 1 的单实例使用约束；禁止多个进程共用数据目录。强制杀进程/磁盘故障可能留下被忽略的 staging/removed 目录，不能把它们当成有效安装；自动孤儿清理尚未引入。

## 13. Git 状态

main；origin 保持 https://github.com/zhiuanL/elfin.git；v0.0-phase0、v0.1-phase1 保留。Phase 2 开始时工作区干净；本次选择器增量开始时已有未提交的 Phase 2 改动，均予保留。已检查 status、diff --stat、diff 和 whitespace；resource/、历史 Phase 1 报告、Git 历史未修改。

未 stage、commit、push、rebase、reset 或改 remote。当前 22 个已跟踪文件修改、48 个新增文件，共 70 个文件（含原有 Phase 2 改动）。候选文件未包含 API Key、数据库、bin/obj、日志、本地配置、测试临时数据；生成物留在忽略目录。Git 的 LF→CRLF 提示为现有换行配置提示，不是构建警告；已检查 EOF 空白。

## 14. Phase 3 建议

先完成人工 Phase 2 验收；收到明确指令后才进入 Phase 3 的状态机、行为调度、情绪/Utility 与基础自主行为，复用本阶段语义动画入口。本次停止，不实现 Phase 3。
