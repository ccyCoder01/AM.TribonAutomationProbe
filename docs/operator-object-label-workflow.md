# 船舶设计智能助手：对象标签工作流

## 范围

当前桌面工作流只覆盖单张已打开图纸中的对象标签：

1. 运行只读 preflight；
2. 展示已存在、待创建、重复文字、文字冲突和检查错误；
3. 绑定 preflight operation ID、plan hash 和 READY_TO_CREATE operation 集合；
4. 经操作员明确确认后执行 Apply；
5. 展示写入回执；
6. 操作员完成视觉复核；
7. 通过 Tribon `File → Save` 手动保存一次。

当前不包含自动排版、多图纸回归和安装器。

## 运行前条件

- Tribon 已打开目标图纸；
- FileBridge 处于空闲状态：`inbox=0`、`processing=0`；
- 已部署并选择自包含的
  `AM.TribonAutomationProbe.Console.exe`；
- `AM_TRIBON_BRIDGE_ROOT` 或界面中的 FileBridge 根目录指向正确环境；
- 当前 Worker `Start.py` 保持已验证版本，不得被桌面项目替换。

## 只读检查

1. 检查 Console 路径、FileBridge 根目录、超时和轮询间隔；
2. 点击“执行只读检查”；
3. 界面提示请求进入 FileBridge 后，在 Tribon 当前图纸中运行
   `Start.py` **恰好一次**；
4. 等待 Console 返回 JSON；
5. 核对：
   - `DrawingWritePerformed=False`；
   - `SavePerformed=False`；
   - 待创建数量；
   - plan hash；
   - operation 列表；
   - 重复、冲突和检查错误。

以下任一情况都不得执行 Apply：

- preflight 状态为 `BLOCKED`；
- 重复文字、文字冲突或检查错误不为 0；
- FileBridge 尚未空闲；
- 运行配置在 preflight 后发生变化；
- operation 集合或 plan hash 无法确认。

## 受控 Apply

1. 勾选明确授权复选框；
2. 点击“创建缺失标签”；
3. 在二次确认对话框中再次核对数量、preflight ID 和 plan hash；
4. 确认后，界面仅调用已验证 Console 命令：
   `apply-missing-object-labels`；
5. 请求进入 FileBridge 后，在 Tribon 当前图纸中运行
   `Start.py` **恰好一次**；
6. 等待并检查写入回执。

桌面程序不会执行 SAVEWORK。Console 和 FileBridge 仍负责校验：

- `--allow-write=true`；
- `--confirm-write=true`；
- confirmed preflight operation ID；
- confirmed plan hash；
- confirmed operation ID 集合；
- created/failed operation 集合；
- runtime handle 与 drawing write count；
- `SavePerformed=False`。

## 视觉复核与保存

Apply 成功后，先检查：

- 每个标签的文字；
- 标签与对象的对应关系；
- 位置和可读性；
- 样式；
- 遮挡和重叠；
- 是否出现非预期标签。

全部通过后，使用 Tribon `File → Save` 手动保存一次。

## 异常处理

- 超时、取消或 Console 返回错误时，不要连续重复运行 `Start.py`；
- 先检查 `inbox`、`processing`、`output`、`failed` 和 `archive`；
- Apply 状态未知时，不要再次提交写入；
- 界面提示配置已变化时，必须重新运行 preflight；
- 任一结果报告 `SavePerformed=True` 时按安全异常处理，停止后续操作。

## 自然语言计划入口（Round 4.5B）

桌面主界面提供自然语言输入，但自然语言不会直接调用 Tribon：

1. 输入任务并点击“生成执行计划”或按 `Ctrl+Enter`；
2. Desktop 仅启动已发布 Console 的 `assistant-interpret` 命令；
3. Console 通过 `AssistantLanguageModelFactory` 选择规则模型或受控的
   OpenAI-compatible 模型；
4. Desktop 校验结构化解释和计划，确认：
   - 任务全部在白名单内；
   - `ExecutionPerformed=False`；
   - `DrawingWritePerformed=False`；
   - `SavePerformed=False`；
   - 风险、确认要求和计划状态相互一致；
5. 标签类单任务计划可以进入现有只读 preflight；
6. 写入计划仍必须经过精确 preflight 绑定、复选框授权和二次确认。

当前增量只把标签类计划接入确定性执行。几何识别、高亮和清除高亮仅展示计划，
后续增量再分别绑定到对应的确定性 Console 命令。不得把计划预览当作已执行结果。

Desktop 不直接引用 `Adapter.OpenAI`，主界面不接收 API Key。模型配置继续通过
Console 进程环境中的 `ASSISTANT_BASE_URL`、`ASSISTANT_API_KEY` 和
`ASSISTANT_MODEL` 提供。API Key 不得写入源码、命令行、日志或对话记录。
