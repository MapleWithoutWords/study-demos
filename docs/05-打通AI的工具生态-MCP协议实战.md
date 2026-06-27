# 打通AI的工具生态——MCP协议实战

> 这是《AI开发解密》系列的第五篇。我们将学习MCP（Model Context Protocol）协议，亲手构建一个MCP Server，让AI能够动态发现和调用外部工具服务，打通整个工具生态。

## 引言

> 本文完整代码在 `AIHttpMcpServer`（MCP服务端）和 `AIMCPCallingConsole5`（MCP客户端）两个项目中，克隆仓库后配置好API Key直接运行即可体验效果。
> 项目地址：https://github.com/MapleWithoutWords/AIStudyDemos

上一篇文章的Function Calling让AI获得了"做事"的能力。但你可能注意到了一个问题：

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

想象一下：如果你能让AI**动态发现**已有的各种工具服务——数据库查询、文件操作、API调用、代码执行……而不用为每个工具都写一遍代码，那该多好？

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
│       │     MCP Server A（用户管理）        │
│       │     ┌──────────────┴──────────┐   │
│       │     │  Tool: SearchUser      │   │
│       │     │  Tool: AddUser         │   │
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

## 第一步：构建自定义MCP Server

本文的重点不只是用别人的MCP Server，而是**从零构建我们自己的MCP Server**。在C#中，借助官方的`ModelContextProtocol.AspNetCore`包，这变得非常简单。

### 项目结构

```
AIHttpMcpServer/
├── Program.cs            # 服务入口，配置MCP Server
├── TestMcpTool.cs        # 自定义工具定义
└── AIHttpMcpServer.csproj
```

### 安装依赖

```xml
<PackageReference Include="ModelContextProtocol.AspNetCore" Version="1.4.0" />
```

### 定义MCP工具

在`TestMcpTool.cs`中定义两个用户管理工具：

```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerToolType]
public class TestMcpTool
{
    [McpServerTool]
    [Description("Search for a user by their username.")]
    public string SearchUser(string userName)
    {
        return $"Searching for user: {userName}";
    }

    [McpServerTool]
    [Description("Add a new user by their username.")]
    public string AddUser(string userName)
    {
        return $"Adding user: {userName}";
    }
}
```

和Function Calling类似，**`[Description]`是AI理解工具用途的关键**。区别在于：

| 特性 | Function Calling | MCP Server |
|------|-----------------|------------|
| 工具标注 | `[Description]` | `[McpServerTool]` + `[Description]` |
| 类标注 | 无 | `[McpServerToolType]` |
| 执行位置 | 应用进程内 | 独立服务进程 |

`[McpServerToolType]`告诉MCP框架"这个类里有工具"，`[McpServerTool]`标注具体的工具方法。框架会自动扫描带这两个特性的类和方法，将它们注册为MCP工具。

### 配置MCP Server

`Program.cs`中的配置非常简洁：

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
    {
        Name = "AIHttpMcpServer",
        Description = "A simple AI HTTP MCP server.",
        Version = "1.0.0"
    };
})
    .WithHttpTransport()           // 启用HTTP传输
    .WithStdioServerTransport()    // 同时支持Stdio传输
    .WithToolsFromAssembly();      // 自动扫描程序集中的工具

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapMcp("/mcp");   // 将MCP端点映射到 /mcp 路径

app.Run();
```

逐行解读：

1. **`AddMcpServer`**：注册MCP Server服务，配置Server的名称、描述、版本等元信息
2. **`WithHttpTransport()`**：启用HTTP传输模式，允许远程客户端通过网络连接
3. **`WithStdioServerTransport()`**：同时支持Stdio传输（本地进程间通信）
4. **`WithToolsFromAssembly()`**：**关键一行**——自动扫描当前程序集中所有带`[McpServerToolType]`特性的类，注册其中的工具方法
5. **`MapMcp("/mcp")`**：将MCP协议的HTTP端点映射到`/mcp`路径

启动后，我们的MCP Server就会在`http://localhost:5144/mcp`上监听客户端连接。

### 运行MCP Server

```bash
cd AIHttpMcpServer
dotnet run
```

服务启动后会在控制台显示监听地址。此时MCP Server已经就绪，等待Client连接。

## 第二步：创建MCP Client连接Server

现在切换到客户端项目`AIMCPCallingConsole5`，让我们的AI应用连接到刚才构建的MCP Server。

### 配置HTTP传输

与上一篇文章使用Stdio不同，这里我们使用HTTP传输连接自定义Server：

```csharp
using ModelContextProtocol.Client;

var config = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri("http://localhost:5144/mcp"),
    TransportMode = HttpTransportMode.AutoDetect,
});
```

**`HttpTransportMode.AutoDetect`** 让客户端自动检测Server支持的传输模式（SSE或Streamable HTTP），无需手动指定。

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

运行后你会看到：

```
=== 可用工具列表 ===
  SearchUser: Search for a user by their username.
  AddUser: Add a new user by their username.
```

**这就是MCP的魔力——运行时动态发现工具！** 你不需要事先知道有哪些工具，MCP Client会自动获取Server暴露的全部工具列表，包括名称、描述和参数Schema。

## 第三步：将MCP工具接入AI对话

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
            else if (item is UsageContent usageContent)
            {
                Console.WriteLine();
                Console.WriteLine(JsonSerializer.Serialize(usageContent.Details));
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

现在你可以对AI说"帮我搜索用户Alice"或"添加一个新用户Bob"，AI会自动调用我们自定义MCP Server中的`SearchUser`和`AddUser`工具！

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

## MCP的两种传输方式

MCP支持两种传输方式，适用于不同场景：

| 方式 | 适用场景 | 特点 |
|------|---------|------|
| **Stdio** | 本地进程通信 | 通过标准输入输出，简单可靠 |
| **HTTP+SSE** | 远程服务/跨网络 | 支持HTTP端点，适合分布式部署 |

### Stdio方式（本地进程）

```csharp
var config = new StdioClientTransport(
    new StdioClientTransportOptions()
    {
        Command = "dnx",
        Arguments = ["NuGet.Mcp.Server", "--source", "https://api.nuget.org/v3/index.json", "--yes"]
    }
);
```

Client启动一个子进程，通过进程的标准输入/输出交换JSON-RPC消息。

### HTTP方式（远程服务）

```csharp
var config = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri("http://localhost:5144/mcp"),
    TransportMode = HttpTransportMode.AutoDetect,
});
```

Client通过HTTP连接到远程MCP Server。本文示例正是使用这种方式。

## MCP的底层通信

通过项目中提供的HTTP拦截器（详见[06-番外篇](./06-番外篇-HTTP拦截器与AI调试技巧.md)），我们可以观察到MCP的完整交互过程。MCP底层使用**JSON-RPC 2.0**协议：

### 1. 初始化握手

Client启动时，会与Server进行握手：

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
        "name": "SearchUser",
        "description": "Search for a user by their username.",
        "inputSchema": {
          "type": "object",
          "properties": {
            "userName": { "type": "string" }
          }
        }
      },
      {
        "name": "AddUser",
        "description": "Add a new user by their username.",
        "inputSchema": {
          "type": "object",
          "properties": {
            "userName": { "type": "string" }
          }
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
    "name": "SearchUser",
    "arguments": { "userName": "Alice" }
  }
}

// Server → Client
{
  "result": {
    "content": [
      { "type": "text", "text": "Searching for user: Alice" }
    ]
  }
}
```

### 完整流程图

```
用户："帮我搜索用户Alice"
  ↓
AI模型：我需要调用SearchUser工具
  ↓
MCP Client → [JSON-RPC over HTTP] → 自定义MCP Server (localhost:5144)
  ↓
Server执行SearchUser("Alice")，返回结果
  ↓
MCP Client将结果回传给AI模型
  ↓
AI模型基于结果生成回答："已为您搜索用户Alice的信息"
```

## 构建你自己的MCP Server能做什么？

本文只是用`TestMcpTool`演示了两个简单的用户管理工具。在实际场景中，你可以用同样的模式构建强大的MCP Server：

| MCP Server | AI能做的事 |
|------------|-----------|
| **用户管理Server** | 搜索用户、添加用户、权限管理 |
| **数据库Server** | 执行查询、列出表、分析数据 |
| **文件Server** | 读写文件、管理目录、上传下载 |
| **GitHub Server** | 查看PR、创建Issue、管理仓库 |
| **运维Server** | 查看服务状态、管理容器、分析日志 |

构建MCP Server的核心步骤始终只有三步：

1. **定义工具**：用`[McpServerTool]` + `[Description]`标注方法
2. **注册工具**：用`WithToolsFromAssembly()`自动扫描
3. **暴露端点**：用`MapMcp("/mcp")`映射HTTP路径

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
2. **构建自定义MCP Server**：用`[McpServerTool]`定义工具，`WithToolsFromAssembly()`自动注册
3. **HTTP传输**：通过`HttpClientTransport`连接到远程MCP Server
4. **动态工具发现**：运行时获取Server暴露的工具列表
5. **与AI对话集成**：将MCP工具直接注入`ChatOptions.Tools`
6. **MCP vs Function Calling**的本质区别：静态绑定 vs 动态生态
7. **底层通信**：JSON-RPC协议的握手、发现、调用流程

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

> 完整代码见项目：
> - MCP Server：[AIHttpMcpServer/](../AIHttpMcpServer/)（[Program.cs](../AIHttpMcpServer/Program.cs) | [TestMcpTool.cs](../AIHttpMcpServer/TestMcpTool.cs)）
> - MCP Client：[AIMCPCallingConsole5/Program.cs](../AIMCPCallingConsole5/Program.cs)
>
> 项目地址：https://github.com/MapleWithoutWords/AIStudyDemos
