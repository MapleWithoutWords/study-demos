# 打通AI的工具生态——MCP协议实战

> 这是《AI开发解密》系列的第五篇。我们将学习MCP（Model Context Protocol）协议，让AI能够动态发现和调用外部工具服务，打通整个工具生态。

## 引言

> 本文完整代码在 `AIMCPCallingConsole5` 项目中，克隆仓库后配置好API Key直接运行即可体验效果。
> 项目地址：https://github.com/MapleWithoutWords/AIStudyDemos

上一篇文章的Function Calling让AI获得了“做事”的能力。但你可能注意到了一个问题：

```csharp
// Function Calling：工具在编译时就写死了
var options = new ChatOptions
{
    Tools = [
        AIFunctionFactory.Create(GetWeatherInfo),  // 写死在代码里
        AIFunctionFactory.Create(SearchWeb),       // 想加新工具？改代码重新编译
    ]
};
```

这种方式有**三个明显局限**：

| 问题 | 说明 |
|------|------|
| **静态注册** | 工具必须在编译时定义，无法动态添加 |
| **本地执行** | 工具函数必须在你的进程中运行 |
| **重复开发** | 每个AI应用都要自己实现相同的工具 |

想象一下：如果你能让AI**动态发现**互联网上已有的各种工具服务——数据库查询、文件操作、API调用、代码执行……而不用为每个工具都写一遍代码，那该多好？

这就是**MCP（Model Context Protocol）** 要解决的问题。

## 什么是MCP？

MCP是由Anthropic提出的**开放标准协议**，定义了AI模型与外部工具之间的通信规范。它的核心理念是：

> **一次实现，处处接入。**

工具提供方只需实现一次MCP Server，任何支持MCP的AI应用都能直接使用。

### 核心概念

```
┌───────────────────────────────────────────┐
│              你的AI应用（MCP Client）        │
│                                           │
│  ┌─────────┐    ┌─────────────────────┐   │
│  │  大模型  │    │    MCP Client SDK    │   │
│  │ (LLM)   │    │                     │   │
│  └────┬────┘    └──────────┬──────────┘   │
│       │                    │              │
└───────┼────────────────────┼──────────────┘
        │                    │
        │              Stdio / HTTP
        │                    │
┌───────┼────────────────────┼──────────────┐
│       │     MCP Server A（NuGet搜索）      │
│       │     ┌──────────────┴──────────┐   │
│       │     │  Tool: search_nuget     │   │
│       │     │  Tool: get_package_info │   │
│       │     └─────────────────────────┘   │
│       │                                   │
│       │     MCP Server B（数据库查询）      │
│       │     ┌─────────────────────────┐   │
│       │     │  Tool: run_query        │   │
│       │     │  Tool: list_tables      │   │
│       │     └─────────────────────────┘   │
└───────┼───────────────────────────────────┘
```

- **MCP Client**：你的AI应用，负责发现工具、调用工具
- **MCP Server**：独立的工具服务进程，暴露工具列表和执行能力
- **Transport**：Client与Server之间的通信方式（Stdio或HTTP）
- **Tools**：Server暴露的工具，包含名称、描述、参数Schema

## 创建MCP客户端

### 配置传输方式

MCP支持两种传输方式：

| 方式 | 适用场景 | 特点 |
|------|---------|------|
| **Stdio** | 本地进程通信 | 通过标准输入输出，简单可靠 |
| **HTTP+SSE** | 远程服务 | 支持跨网络调用 |

本示例使用Stdio传输，连接NuGet MCP Server：

```csharp
using ModelContextProtocol.Client;

var config = new StdioClientTransport(
    new StdioClientTransportOptions()
    {
        Command = "dnx",
        Arguments = [
            "NuGet.Mcp.Server", 
            "--source", "https://api.nuget.org/v3/index.json", 
            "--yes"
        ]
    }
);
```

这里做了什么？

1. **`Command = "dnx"`**：启动dnx进程
2. **`Arguments`**：告诉dnx运行NuGet MCP Server，指定NuGet源
3. **Stdio通信**：Client通过进程的标准输入/输出与Server交换JSON-RPC消息

### 连接并发现工具

```csharp
// 创建MCP客户端并连接
var mcpClient = await McpClient.CreateAsync(config);

// 动态发现Server暴露的所有工具
var tools = await mcpClient.ListToolsAsync();

Console.WriteLine("=== 可用工具列表 ===");
foreach (var tool in tools)
{
    Console.WriteLine($"  {tool.Name}: {tool.Description}");
}
```

**这就是MCP的魔力——运行时动态发现工具！** 你不需要事先知道有哪些工具，MCP Client会自动获取Server暴露的全部工具列表，包括名称、描述和参数Schema。

## 将MCP工具接入AI对话

发现工具后，将它们注入到AI对话中：

```csharp
// 创建AI客户端（同前几篇）
IChatClient client =
    new OpenAI.Chat.ChatClient(
        model, 
        new ApiKeyCredential(apiKey), 
        new OpenAI.OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
    .AsIChatClient();

// 启用自动函数调用
using var functionCallingChatClient = new ChatClientBuilder(client)
    .UseFunctionInvocation()
    .Build();

// 对话循环
while (true)
{
    Console.Write("Prompt: ");
    List<ChatMessage> messages = [];
    messages.Add(new(ChatRole.User, Console.ReadLine()));

    // 关键：直接把MCP工具列表传给ChatOptions.Tools
    await foreach (ChatResponseUpdate update in functionCallingChatClient
        .GetStreamingResponseAsync(messages, new() { Tools = [.. tools] }))
    {
        foreach (var item in update.Contents)
        {
            if (item is TextReasoningContent textReasoning)
            {
                Console.Write(textReasoning.Text);
            }
            else if (item is TextContent text)
            {
                Console.Write(text.Text);
            }
        }
    }
    Console.WriteLine();
}
```

核心代码只有一行关键改动：

```csharp
// Function Calling：手动注册本地函数
new ChatOptions { Tools = [AIFunctionFactory.Create(GetWeatherInfo)] }

// MCP：直接传入动态发现的MCP工具列表
new ChatOptions { Tools = [.. tools] }
```

`[.. tools]`使用了C#的spread语法，将`IList<McpTool>`展开为工具数组。

## MCP vs Function Calling 深度对比

| 维度 | Function Calling | MCP |
|------|-----------------|-----|
| **工具来源** | 编译时硬编码 | 运行时动态发现 |
| **执行位置** | 本地进程内 | 独立进程/远程服务 |
| **工具更新** | 需要修改代码+重新编译 | 只需更新Server，Client零改动 |
| **工具数量** | 受限于代码维护能力 | 可接入任意多的Server |
| **协议标准** | OpenAI Tool协议 | MCP开放标准 |
| **跨语言** | 函数必须在你的应用中实现 | ✅ Server可以是任何语言 |
| **部署复杂度** | 简单（同一进程） | 稍复杂（多进程管理） |
| **适用场景** | 小型应用、少量工具 | Agent平台、工具生态集成 |

### 一个直观的比喻

- **Function Calling** = 你雇了几个全职员工（工具写死在代码里）
- **MCP** = 你接入了一个人才市场（按需发现和调用各种工具服务）

## MCP的底层通信

通过HTTP拦截器，我们可以观察到MCP的完整交互过程：

### 1. 初始化握手

Client启动时，会与Server进行JSON-RPC握手：

```json
// Client → Server
{"jsonrpc":"2.0","method":"initialize","params":{"protocolVersion":"2024-11-05",...}}

// Server → Client
{"jsonrpc":"2.0","result":{"protocolVersion":"2024-11-05","capabilities":{...}}}
```

### 2. 工具发现

```json
// Client → Server
{"jsonrpc":"2.0","method":"tools/list"}

// Server → Client
{
  "result": {
    "tools": [
      {
        "name": "search_nuget",
        "description": "Search for NuGet packages",
        "inputSchema": {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Search query" }
          },
          "required": ["query"]
        }
      }
    ]
  }
}
```

### 3. 工具调用

当AI决定调用某个MCP工具时：

```json
// Client → Server
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "search_nuget",
    "arguments": { "query": "Microsoft.Extensions.AI" }
  }
}

// Server → Client
{
  "result": {
    "content": [
      { "type": "text", "text": "Found packages: ..." }
    ]
  }
}
```

### 完整流程图

```
用户："帮我搜一下Microsoft.Extensions.AI的最新版本"
  ↓
AI模型：我需要调用search_nuget工具
  ↓
MCP Client → [JSON-RPC] → NuGet MCP Server
  ↓
Server执行搜索，返回结果
  ↓
MCP Client将结果回传给AI模型
  ↓
AI模型基于搜索结果生成人类可读的回答
```

## 构建你自己的MCP Server

虽然本文重点是MCP Client，但了解Server的结构有助于理解整个协议。一个最小MCP Server的核心逻辑：

```csharp
// 伪代码：MCP Server的基本结构
var server = new McpServer();

server.AddTool("get_current_time", 
    description: "获取当前时间",
    handler: () => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

server.AddTool("query_database",
    description: "执行SQL查询",
    parameters: new { sql = "string" },
    handler: (args) => database.Execute(args.sql));

await server.StartAsync();  // 监听Stdio或HTTP
```

MCP Server本质上就是一个**暴露工具列表 + 执行工具调用**的服务进程。你可以用任何语言（Python、Node.js、Go、Rust……）实现MCP Server，只要遵循MCP的JSON-RPC协议即可。

## 实战场景：AI + MCP能做什么？

| MCP Server | AI能做的事 |
|------------|-----------|
| **NuGet Server** | 搜索、查询NuGet包信息 |
| **Database Server** | 查询数据库、分析数据 |
| **File System Server** | 读写文件、管理目录 |
| **GitHub Server** | 查看PR、创建Issue、管理仓库 |
| **Browser Server** | 打开网页、提取信息、自动化操作 |
| **Docker Server** | 管理容器、查看日志 |

当你的AI应用接入MCP生态，它的能力边界将**无限扩展**——因为总有人在做新的MCP Server。

## 安全注意事项

MCP工具是真正执行操作的，所以**安全至关重要**：

| 风险 | 应对措施 |
|------|---------|
| AI调用危险工具（如删除文件） | 工具白名单机制，只暴露安全的工具 |
| 参数注入（如SQL注入） | Server端做好参数校验和转义 |
| 过度授权 | 最小权限原则，MCP Server只拥有必要的权限 |
| 敏感数据泄露 | 工具返回结果脱敏 |

## 小结

这篇文章我们学习了：

1. **MCP协议**的核心概念：Client、Server、Tools、Transport
2. **动态工具发现**：运行时获取Server暴露的工具列表
3. **Stdio传输**：通过进程间标准输入输出通信
4. **与AI对话集成**：将MCP工具直接注入`ChatOptions.Tools`
5. **MCP vs Function Calling**的本质区别：静态绑定 vs 动态生态
6. **底层通信**：JSON-RPC协议的握手、发现、调用流程

## 系列回顾

到这里，我们的AI开发解密系列已经覆盖了AI应用开发的核心技术栈：

```
第1篇：基础对话  →  AI能说话
第2篇：短期记忆  →  AI能记住上下文
第3篇：长期记忆  →  AI能跨会话记忆
第4篇：函数调用  →  AI能执行操作
第5篇：MCP协议  →  AI能连接工具生态
```

从一个只会回答问题的聊天机器人，成长为一个拥有记忆、能调用工具、可以接入生态的**AI Agent**。这正是现代AI应用开发的完整进化路径。

---

> 完整代码见项目：[AIMCPCallingConsole5/Program.cs](../AIMCPCallingConsole5/Program.cs)
>
> 项目地址：https://github.com/MapleWithoutWords/AIStudyDemos
