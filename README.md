# Tribon M3 Automation Probe

独立的 .NET 8 技术探针，验证“读取上下文/标注 → 移动一个标注 → 刷新 → 复读并校验”的最小闭环。

## 支持动作

`context.get`、`annotation.export`、`annotation.move`。当前提供可运行的 `MockTribonAdapter` 和文件协议传输层；真实 Tribon API 未知部分不虚构。

## 运行

```powershell
dotnet build
dotnet test
dotnet run --project src/AM.TribonAutomationProbe.Console -- run-all --adapter mock
dotnet run --project src/AM.TribonAutomationProbe.Console -- context --adapter mock
```

Mock 成功后会输出 Context、Export annotations、Move annotation、Refresh、Validation、Probe 均 succeeded，并在 `receipts/` 生成 JSON 回执。File Bridge 使用 `tribon-bridge/{inbox,processing,output,failed,archive,logs}`，写请求先落 `.tmp` 后原子重命名，结果轮询有超时和取消支持。

## 限制与下一步

不包含自然语言、大模型、聊天 UI、自动排版、任意 Tribon 命令或保存/Undo。接入现场前需要人工确认 Tribon M3 的会话、图纸、视图、标注枚举、稳定 ID、移动和刷新入口，并由 Tribon 侧脚本实现 `docs/tribon-script-contract.md`。
