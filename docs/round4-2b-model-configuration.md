# Round 4.2B 模型配置

模型适配器只使用三个配置项：

- `base_url`：OpenAI-Compatible 服务地址；可填写基础地址或完整 `/chat/completions` 地址。
- `api_key`：通过 `ASSISTANT_API_KEY` 提供，仅保存在内存，不接受命令行参数。
- `model`：通过 `ASSISTANT_MODEL` 或 CLI `--model` 提供。

```powershell
$env:ASSISTANT_BASE_URL = "https://api.yygu.cn/v3/llm.chat"
$env:ASSISTANT_API_KEY = "<本机密钥>"
$env:ASSISTANT_MODEL = "deepseek/deepseek-v4-pro"
```

完整地址 `https://api.yygu.cn/v3/llm.chat/chat/completions` 也可直接使用。CLI 只允许 `--base-url` 和 `--model` 覆盖非敏感配置。三项全部缺失时使用离线 RuleBased 模型；部分配置会返回 `ASSISTANT_MODEL_CONFIGURATION`。
