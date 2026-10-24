# 测试用例与验收清单

## 1. 自动化测试

```powershell
cd C:\Users\KC\Desktop\PromptPolisher
dotnet test PromptPolisher.sln
```

当前：**40 passed, 0 failed**。

覆盖范围：

| 测试类 | 覆盖 |
|---|---|
| `PromptTemplateRendererTests` | 9 个占位符替换、参数变化、保留术语、未知占位符、缺少 user_prompt、空模板 |
| `RedactionTests` | API Key、Bearer、Cookie、password、普通文本、Preview |
| `HistoryRepositoryTests` | SQLite 增/查/删/清、字段往返 |
| `OpenAiCompatibleProviderTests` | 非流式、SSE 流式、401、429、缺 Key、空结果 |
| `PolisherServiceTests` | 重试、无效请求、手动模式不写历史、关闭历史 |
| `WebChatProviderTests` | 未确认合规、未登录、委托发送 |
| `JsonSettingsServiceTests` | 默认加载、保存/加载往返、不写 API Key、用户模板优先于内置默认、内置默认兜底、编辑后重启仍生效 |

## 2. 本地 Mock 服务

用于不依赖真实 API 的端到端验收：

```powershell
cd C:\Users\KC\Desktop\PromptPolisher
python tools\mock_openai_server.py
```

应用设置：

- 提供方：`本地 Mock（验收用）`（默认禁用，先勾选“启用”）
- Base URL：`http://127.0.0.1:8765/v1`
- 模型：`mock` / `slow` / `empty` / `error`
- API Key：`test` / `bad` / `rate`

Mock 会把收到的 **system prompt 原样回显**，所以改变润色参数后，输出会跟着变——可以直接验证“参数 → 系统 Prompt”的联动。

## 3. 手工测试用例

| ID | 场景 | 步骤 | 预期 |
|---|---|---|---|
| TC-01 | 首次启动 | 删除 `%LocalAppData%\PromptPolisher` 后运行 | 自动创建 settings.json、system-prompt.txt、history.db、logs |
| TC-02 | 保存 API Key | 设置 → 选 API 提供方 → 输入 Key → 保存密钥 → 保存 | 状态显示“已保存（不回显）”；settings.json/日志中无 Key |
| TC-03 | Mock 非流式成功 | 启用 mock，关闭“流式显示”，输入 Prompt → 润色 | 输出包含“【MOCK 润色结果】”和渲染后的 system prompt |
| TC-04 | 参数联动 | 把“润色强度”从轻度改深度，再润色 | 输出中的“润色强度：”文本变化 |
| TC-05 | 流式显示 | 开启流式，润色 | 输出逐步追加，界面不卡死 |
| TC-06 | 复制/导出 | 点击“复制结果”/“导出 .txt” | 剪贴板/文件内容与输出一致 |
| TC-07 | 历史 | 设置开启历史 → 润色 → 打开历史 | 能看到记录；搜索命中；删除单条/清空生效 |
| TC-08 | 超时 | mock 模型填 `slow`，超时设 5 秒 | 提示 `TIMEOUT`，可重试 |
| TC-09 | 401 | mock Key 填 `bad` | 提示 `AUTH_REQUIRED` |
| TC-10 | 429 | mock Key 填 `rate` | 提示 `RATE_LIMITED` |
| TC-11 | 空结果 | mock 模型填 `empty` | 提示 `EMPTY_RESULT` |
| TC-12 | 500 | mock 模型填 `error` | 提示 `PROVIDER_ERROR` |
| TC-13 | 手动模式 | 选“手动模式”→润色 | 弹出窗口显示最终 Prompt；复制后粘贴结果 → 输出/历史更新 |
| TC-14 | 模板校验 | 设置 → 模板 → 删除 `{{user_prompt}}` → 校验 | 提示必须包含 `{{user_prompt}}`；保存被阻止 |
| TC-15 | 模板恢复 | 修改模板 → 恢复默认 | 模板回到程序目录 `config\system-prompt.default.txt` 的内容 |
| TC-23 | 模板持久化 | 修改模板 → 保存 → 关闭应用 → 重新启动 | 设置页仍显示修改后的模板；不会被内置默认覆盖 |
| TC-24 | 内置模板缺失兜底 | 删除 `config\system-prompt.default.txt` 后点“恢复默认” | 回退到代码内置常量，应用不崩溃 |
| TC-16 | 网页合规 | 选网页版 → 不勾确认 → 润色 | 提示 `COMPLIANCE_CONSENT_REQUIRED`，不发送 |
| TC-17 | 网页登录 | 选网页版 → 确认合规 → 润色（未登录） | 提示登录；内嵌窗口打开；登录后点击“完成登录” |
| TC-18 | 网页验证码 | 若页面出现验证码 | 返回 `CAPTCHA_REQUIRED`，不尝试绕过 |
| TC-19 | 取消 | 润色中点“取消” | 状态“已取消”，请求中止 |
| TC-20 | 历史关闭 | 设置关闭“保存历史” → 润色 | 历史无新记录 |
| TC-21 | 历史正文关闭 | 关闭“保存 Prompt 正文” → 润色 | 历史只存元数据（正文为 `[未保存]`） |
| TC-22 | 打包 | `dotnet publish` 后运行 exe | 能启动、能润色、配置/历史在当前用户目录 |

## 4. 验收标准对照

| prompt.txt 验收项 | 结果 | 证据 |
|---|---|---|
| 输入 Prompt 后后台调用并返回结果 | ✅ | TC-03（API）/ TC-13（手动）/ TC-17（网页） |
| 切换参数后系统 Prompt 变化 | ✅ | TC-04 + 单测 |
| 模板可修改且重启后保留 | ✅ | TC-15 / TC-23 / TC-24 + `JsonSettingsServiceTests` |
| 首次登录保存会话，后续复用 | ✅ | WebView2 共享 user data folder；TC-17 |
| 会话失效有提示 | ✅ | `AUTH_REQUIRED` + 登录入口 |
| 不弹出外部浏览器 | ✅ | 内嵌 WebView2 / 屏幕外窗口 |
| 密码/Cookie/Token 不明文 | ✅ | Credential Manager/DPAPI；WebView2 加密 profile；日志脱敏 |
| 复制结果/查看历史 | ✅ | TC-06 / TC-07 |
| 网络/超时/登录失效/空结果提示 | ✅ | TC-08 ~ TC-12 |
| 网页自动化合规边界 | ✅ | `docs/COMPLIANCE.md` + 确认框 |
| 不可行时给 API/手动替代 | ✅ | 官方 API + 手动模式 |

## 5. 已知未覆盖

- 未对真实 DeepSeek/豆包网页做自动化回归（页面会变，且涉及平台条款）；
- 未做代码签名/安装包（仅 `dotnet publish`）；
- 未做多用户/服务端并发测试；
- 未做 4K/高 DPI 的逐项视觉回归（WPF 默认 DPI 感知）。
