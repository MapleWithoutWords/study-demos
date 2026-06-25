# 从零开始——用C#调用你的第一个大模型

> 这是《AI开发解密》系列的第一篇。我们先从「大模型是什么、能做什么」讲起，再动手写代码，带你从零理解AI应用开发的核心原理。

## 引言

2023年以来，大语言模型（LLM）席卷了整个软件行业。ChatGPT、Claude、DeepSeek、智谱GLM……这些名字你可能已经听到耳朵起茧。但作为开发者，我们不能只停留在「用」的层面——我们需要**理解**它。

这个系列的目标，是带你从.NET开发者的视角，**一层一层拆开AI应用开发的全貌**。从最基础的模型调用，到记忆系统，再到工具调用和Agent架构——每一篇都在前一篇的基础上叠加一层能力。

而这一切的起点，就是今天这篇文章。

## 大模型到底是什么？

抛开复杂的数学和神经网络架构，从**软件开发者**的视角来看，大语言模型可以简化为一个非常朴素的概念：

### 本质上，它是一个函数

```
输入（Prompt）  →  大语言模型  →  输出（Completion）
```

你可以把它理解成一个超级强大的**字符串到字符串的映射函数**：

```
string Ask(string prompt) => ...  // 模型内部有数千亿参数在做这件事
```

给它一段文本（Prompt），它返回一段文本（Completion）。就这么简单。

但"简单"背后有几个关键特征：

| 特征 | 说明 |
|------|------|
| **概率性** | 同样的输入，每次输出可能不同——它是在"预测"下一个最可能的词 |
| **无状态** | 每次调用都是独立的，模型不会"记住"你上次问了什么 |
| **上下文依赖** | 输出质量完全取决于你给的输入（Prompt）有多好 |
| **Token驱动** | 模型不直接处理文字，而是把文字切分成Token（词元）来处理 |

### 什么是Token？

Token是大模型处理文本的基本单位。你可以粗略地理解为"一个词或一个词片段"：

```
"你好世界"  →  [你, 好, 世, 界]          （中文大约1个字 ≈ 1-2个Token）
"Hello World" →  [Hello,  World]         （英文大约1个词 ≈ 1-1.5个Token）
```

Token很重要，因为：
- **API按Token计费**：输入Token + 输出Token = 你的花费
- **模型有上下文窗口限制**：比如8K、32K、128K Token，超过就处理不了
- **速度取决于Token**：生成Token是逐个进行的，Token越多越慢

## 从「裸函数」到「对话API」

上面说的`Ask(prompt)`只是一个概念模型。实际上，大模型服务商提供的是更结构化的**对话API**。以OpenAI兼容协议（目前行业标准）为例，一次API调用由以下几个核心部分组成：

```
┌─────────────────────────────────────────────┐
│              一次API请求的组成                  │
├─────────────┬───────────────────────────────┤
│  model      │ 用哪个模型（如glm-5.1）          │
│             │                               │
│  messages   │ 对话消息列表：                    │
│             │   - system: 系统提示词（人设）     │
│             │   - user: 用户说的话              │
│             │   - assistant: AI之前的回复       │
│             │                               │
│  parameters │ 控制参数：                       │
│             │   - temperature: 创造性（0~1）    │
│             │   - max_tokens: 最大输出长度      │
│             │   - top_p: 采样范围              │
└─────────────┴───────────────────────────────┘
```

### Messages：对话的核心

`messages`是整个请求中最重要的部分。它不是简单的一段文字，而是一个**结构化的消息列表**，每条消息都有一个**角色（role）**：

```
messages = [
    { role: "system",    content: "你是一个专业的编程助手" },   ← 告诉AI它是谁
    { role: "user",      content: "什么是递归？" },            ← 用户的问题
    { role: "assistant", content: "递归是..." },               ← AI之前的回答
    { role: "user",      content: "能给个例子吗？" }           ← 用户的追问
]
```

三种角色的分工：

| 角色 | 职责 | 比喻 |
|------|------|------|
| **System** | 定义AI的身份、行为规则、输出格式 | 相当于"岗位说明书" |
| **User** | 用户的输入 | 相当于"客户的问题" |
| **Assistant** | AI的历史回复 | 相当于"之前的回答记录" |

**关键认知**：因为模型是无状态的，所以每次调用都要把**完整的对话历史**重新发一遍。这就是AI"记忆"的真相——不是模型记住了，而是你每次都帮它复习了一遍。

### 一次完整的请求-响应

理解了上面的概念，一次API调用的全貌就清晰了：

**请求（你发给模型的）：**
```json
{
  "model": "glm-5.1",
  "messages": [
    { "role": "system", "content": "你是一个有帮助的AI助手。" },
    { "role": "user", "content": "用一句话解释什么是API。" }
  ],
  "temperature": 0.7,
  "max_tokens": 1024
}
```

**响应（模型返回的）：**
```json
{
  "id": "chatcmpl-xxx",
  "model": "glm-5.1",
  "choices": [{
    "message": {
      "role": "assistant",
      "content": "API（应用程序编程接口）是一组定义软件组件之间如何交互的规则和协议。"
    },
    "finish_reason": "stop"
  }],
  "usage": {
    "prompt_tokens": 28,
    "completion_tokens": 35,
    "total_tokens": 63
  }
}
```

响应的关键字段：
- `choices[0].message.content`：模型的回复内容
- `finish_reason`：为什么停止（`stop`=正常结束，`length`=达到长度限制）
- `usage`：本次调用消耗的Token数

## 大模型能做什么？

理解了大模型的接口形式，我们再来看看它能做什么。本质上，大模型的能力可以归结为**文本的理解与生成**：

| 能力 | 示例 |
|------|------|
| **问答** | "C#中async和await的作用是什么？" |
| **创作** | "写一篇关于微服务的博客大纲" |
| **翻译** | "把这段话翻译成英文：..." |
| **摘要** | "用100字总结这篇文章：..." |
| **代码生成** | "用C#写一个快速排序算法" |
| **数据分析** | "分析这段JSON数据的结构" |
| **推理** | "如果A>B，B>C，那么A和C的关系是？" |

但大模型也有明确的**局限**：

| 局限 | 说明 |
|------|------|
| **知识截止** | 训练数据有截止日期，不知道最新的事 |
| **无法联网** | 不能搜索网页、查天气、查数据库 |
| **无法执行** | 不能运行代码、操作文件、发邮件 |
| **幻觉** | 可能一本正经地胡说八道 |

后面几篇文章，我们将逐一解决这些局限——通过**记忆系统**解决知识遗忘，通过**Function Calling**让AI学会调用工具，通过**MCP协议**让AI接入工具生态。

## 动手实践：用C#调用大模型

理解了大模型的本质和API结构，接下来我们动手写代码。在C#中有两种方式调用大模型：

### 方式一：HttpClient直接调用——看透本质

大模型服务商几乎都遵循OpenAI兼容协议，本质上就是一个RESTful API。最直接的调用方式就是用`HttpClient`发送HTTP请求：

```csharp
using var httpClient = new HttpClient();
httpClient.BaseAddress = new Uri(baseUrl);
httpClient.DefaultRequestHeaders.Authorization = 
    new AuthenticationHeaderValue("Bearer", apiKey);

var jsonContent = JsonSerializer.Serialize(new
{
    model = "glm-5.1",
    messages = new[]
    {
        new { role = "system", content = "你是一个有帮助的AI助手。" },
        new { role = "user", content = "你好，请介绍一下你自己。" }
    },
    temperature = 0.7,
    max_tokens = 1024
});

var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
var response = await httpClient.PostAsync("chat/completions", content);
var responseBody = await response.Content.ReadAsStringAsync();
```

**关键点解析：**

| 字段 | 说明 |
|------|------|
| `model` | 要调用的模型名称，如`glm-5.1`、`gpt-4o`等 |
| `messages` | 对话消息数组，包含`system`（系统提示词）和`user`（用户消息） |
| `temperature` | 控制输出随机性，0-1之间，越高越"发散" |
| `max_tokens` | 限制模型最大输出token数 |
| `chat/completions` | OpenAI兼容协议的标准端点 |

这种方式虽然原始，但它能让你**完全理解请求和响应的原始结构**。在生产环境中，我们更推荐使用SDK。

### 方式二：SDK调用——`Microsoft.Extensions.AI`

理解了HTTP裸调的原理后，我们来看看更优雅的SDK方式。

微软推出的`Microsoft.Extensions.AI`库提供了统一的AI抽象层。核心接口是`IChatClient`，它屏蔽了不同模型供应商的差异：

```csharp
using Microsoft.Extensions.AI;

IChatClient client =
    new OpenAI.Chat.ChatClient(
        model, 
        new ApiKeyCredential(apiKey), 
        new OpenAI.OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
    .AsIChatClient();
```

这里我们用OpenAI SDK创建客户端，再通过`.AsIChatClient()`适配到微软的抽象接口。这意味着——**无论你用的是OpenAI、智谱GLM、DeepSeek还是其他兼容协议的服务商，代码几乎不用改**。

## 构建交互式对话

有了客户端，我们来构建一个可以持续对话的控制台应用：

```csharp
while (true)
{
    Console.Write("\n你: ");
    var userInput = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(userInput))
        continue;

    if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("再见！");
        break;
    }

    List<ChatMessage> chatMessages =
    [
        new ChatMessage(ChatRole.System, "你是一个有用的AI助手，请用中文回答用户的问题。"),
        new ChatMessage(ChatRole.User, userInput)
    ];

    Console.Write("\nAI: ");
    await foreach (var update in client.GetStreamingResponseAsync(chatMessages))
    {
        foreach (var item in update.Contents)
        {
            if (item is TextContent text)
            {
                Console.Write(text.Text);
            }
        }
    }
}
```

### 理解消息角色

`ChatMessage`有三种核心角色：

- **System**：系统提示词，定义AI的行为准则和人设
- **User**：用户输入的消息
- **Assistant**：AI的历史回复（后续讲记忆时会用到）

## 流式输出 vs 一次性响应

### 一次性响应

```csharp
var response = await client.GetResponseAsync(chatMessages);
Console.WriteLine(response.Text);
```

简单直接，但需要等待模型**生成完毕**后才返回。对于长文本生成，用户会感到明显的等待。

### 流式响应（推荐）

```csharp
await foreach (var update in client.GetStreamingResponseAsync(chatMessages))
{
    foreach (var item in update.Contents)
    {
        if (item is TextReasoningContent textReasoning && isNeedReasoningContent)
        {
            Console.Write($"{textReasoning.Text}");
        }
        else if (item is TextContent text)
        {
            Console.Write(text.Text);
        }
        else if (item is UsageContent usageContent)
        {
            Console.WriteLine(JsonSerializer.Serialize(usageContent.Details));
        }
    }
}
```

流式输出有三个内容类型需要注意：

| 类型 | 说明 |
|------|------|
| `TextReasoningContent` | 模型的"思考过程"（推理类模型如DeepSeek-R1会输出） |
| `TextContent` | 最终的正文输出 |
| `UsageContent` | Token用量统计信息 |

流式输出的体验远优于等待完整响应——用户几乎可以**实时**看到AI的回答。

## 配置管理：安全存储API Key

**永远不要**把API Key硬编码或提交到Git。我们使用.NET的UserSecrets机制：

```csharp
var configuration = new ConfigurationBuilder()
    .AddUserSecrets(typeof(AICommon).Assembly)
    .Build();

string baseUrl = configuration.GetSection("AIEndpoint").Value!;
string apiKey = configuration.GetSection("AIApiKey").Value!;
```

设置方式：

```bash
dotnet user-secrets init --project AI.Common/AI.Common.csproj
dotnet user-secrets set "AIEndpoint" "https://open.bigmodel.cn/api/paas/v4/" --project AI.Common/AI.Common.csproj
dotnet user-secrets set "AIApiKey" "your-api-key-here" --project AI.Common/AI.Common.csproj
```

UserSecrets存储在用户目录下，不会被Git追踪，是开发阶段管理密钥的最佳实践。

## 核心架构一览

```
用户输入
  ↓
构建 ChatMessage 列表 (System + User)
  ↓
IChatClient.GetStreamingResponseAsync()
  ↓
逐块接收 StreamingResponseUpdate
  ↓
解析 TextReasoningContent / TextContent / UsageContent
  ↓
实时输出到控制台
```

## 小结

这篇文章我们学习了：

1. **大模型的本质**：一个从字符串到字符串的映射函数，具有概率性、无状态、上下文依赖、Token驱动四大特征
2. **对话API的组成**：model + messages（system/user/assistant） + 控制参数
3. **Token的概念**：大模型处理文本的基本单位，直接影响成本和速度
4. **大模型的能力与局限**：能做问答、创作、翻译、推理，但无法联网、无法执行、有知识截止
5. **两种调用方式**：HttpClient裸调 vs SDK封装，前者帮助理解原理，后者用于生产
6. **`Microsoft.Extensions.AI`** 统一抽象层，一套代码兼容多个模型服务商
7. **流式输出**的实现细节和三种内容类型的区分
8. **安全配置** API Key的最佳实践

大模型本身是无状态的、无法联网的、不能执行操作的——但别担心，这正是我们后面几篇文章要解决的问题。

**下一篇**，我们将让AI拥有“记忆”——不再每次都从零开始对话，而是能记住之前的上下文。

---

> 完整代码见项目：[AICallConsole1/Program.cs](../AICallConsole1/Program.cs)
