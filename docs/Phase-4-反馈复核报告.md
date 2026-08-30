# Phase 4 反馈复核报告

## 1. Status

**PARTIAL** — 已复核用户填写的人工反馈并完成 Debug/Release 全量自动回归；M03、M06 的现场体验未达到预期，方向素材、多屏/混合 DPI、热插拔及 Windows 10 环境仍缺失。本次未进入 Phase 5。

## 2. 人工反馈判定

| 分类 | 用例 | 判定 |
| --- | --- | --- |
| 用户确认通过 | W01、M01、M02、M05、H01、L01、L02、L03 | 保留为人工 PASS，共 8 项 |
| 现场未达预期 | M03 | FAIL；用户未观察到全桌面大范围移动 |
| 概率事件未取得证据 | M04 | BLOCKED；观察到移动但未观察到范围扩大 |
| 现场未达预期 | M06 | FAIL；三种运动风格视觉区分不明显 |

M03 的下拉绑定已核对为 `MovementMode.Desktop`，运行时会保存并重新配置该枚举；未发现 UI 映射串位。新增确定性测试验证：全桌面在当前屏工作区取样，500 次样本中可产生超过 Lively 局部半径三倍的安全目标。用户现场结果与策略结果仍不一致，不能直接改为 PASS。

M04 的 SmartHybrid 设计为交互空闲满两分钟后，每次规划以 20% 概率尝试漫游，不保证在某个时间点立即扩大。新增 2,000 次确定性统计测试验证：119 秒前没有超局部半径目标，空闲后扩大目标数量处于预设统计边界。现场没有取得扩大范围证据，因此保持 BLOCKED。

M06 的 Quiet/Natural/Lively 参数分别为 40/80/140 DIP/s、45/25/15 秒间隔、80/120/180 DIP 半径、45%/25%/10% 停顿概率；同距离轨迹时长按 Quiet > Natural > Lively 排序。参数实现正确不代表用户能明显感知，故保留 FAIL 作为体验调优项。

## 3. 其余待测项判定

- M07、H02、H04、A01、L04–L07、R01–R03：相关取消、持久化、离屏恢复、fallback、生命周期、角色导入和真实 WPF 启动已有自动覆盖，但没有本次对应人工操作证据，保持“人工未测（自动回归通过）”。
- H03、A02–A05：缺少自定义锚点/方向不对称角色包，人工 BLOCKED；纯数学、方向解析、镜像和 fallback 自动测试通过。
- H05：`updateHomeOnDrag=false` 的人工配置流程和实际拖动现象均未执行，保持人工未测。
- D01–D10：当前复核环境只有单屏 96 DPI / 100%，无负坐标副屏、混合 DPI、热插拔或 Windows 10 实机，全部按环境 BLOCKED。100/125/150/200% 坐标数学测试和真实当前屏 DPI 探测通过，不能替代实机矩阵。

## 4. 自动证据

环境：main / b4b07f3，含保留中的 Phase 4 未提交修改；Windows 11 25H2，Build 26200.9168，x64；`\\.\DISPLAY5` 单屏 2048×1152，96 DPI。

| 配置 | Build | Unit | Integration |
| --- | --- | --- | --- |
| Debug | 0 warnings / 0 errors | 148 passed | 111 passed |
| Release | 0 warnings / 0 errors | 148 passed | 111 passed |

TRX：

- Debug：`artifacts/TestResults/Debug/phase-4_net10.0_20260830220452.trx`、`phase-4_net10.0_20260830220509.trx`
- Release：`artifacts/TestResults/Release/phase-4_net10.0_20260830220457.trx`、`phase-4_net10.0_20260830220516.trx`

本地 Debug 测试数据日志中有 289 次 `MovementStarted` 和 289 次 `MovementStopped`，无 `MovementFailed`。该日志未记录模式和目标坐标，且最终保存配置为 Local / Natural / LockedCurrent，所以它只能证明移动生命周期稳定，不能证明 M03 的全桌面范围。

## 5. 限制与后续 Phase 4 动作

本次尝试按 computer-use 技能进行真实 UI 复核，但 Windows 控制后端初始化失败：`windows sandbox failed: helper_unknown_error: setup refresh had errors`，重置后 Node 控制进程仍异常退出。未使用无约束 UI 脚本绕过，也未虚报点击、拖动或视觉结果。

在 Phase 4 判定 PASS 前，建议先用新的独立便携副本复现 M03，并增加不会泄露隐私的“已应用模式 / 起点 / 目标 / 距离 / 结束原因”诊断证据；再基于同一角色、同一 Home、足够样本复验 M06，决定是否调整视觉参数。随后补方向素材与真实多屏/DPI 矩阵。本报告不授权进入 Phase 5。

## 6. Git

未提交、未 push；未修改 origin、Tag 或历史。测试产物仍在忽略的 `artifacts/`，未加入数据库、日志、UserData、bin/obj 或密钥。

最终检查时存在一个本轮开始前已启动的便携 Debug 进程（PID 21588，2026-08-30 14:58:40 启动，`--portable`）。复核没有擅自结束该进程，因此“测试实例已退出/无残留”仍须由测试者通过托盘正常退出后确认。
