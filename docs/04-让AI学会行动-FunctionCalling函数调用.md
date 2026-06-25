# 让AI学会行动——Function Calling函数调用

> 这是《AI开发解密》系列的第四篇。我们将赋予AI一项革命性能力：调用外部函数，让AI从"能说话"变成"能做事"。

## 引言

前三篇文章让我们的AI拥有了对话能力和记忆力。但有一个根本性的局限：**AI只能基于训练数据回答问题，它无法获取实时信息，也无法执行任何操作**。

比如你问AI："今天广州天气怎么样？"，它只能抱歉地回答："我无法获取实时天气信息。"

**Function Calling**（函数调用）就是解决这个问题的关键技术——让AI能够"决定"调用哪些外部工具，并基于工具返回的结果给出最终回答。

## Function Calling的工作原理

很多人误解Function Calling是AI直接调用了函数。实际上，**AI并不执行任何代码**，它只是"告诉"你它想调用哪个函数、传什么参数。真正的执行逻辑由你的程序完成。

完整的交互流程如下：

```
┌──────────┐     ①用户提问      ┌──────────┐
│          │ ──────────────────→ │          │
│   你的   │                    │  大模型   │
│   程序   │ ②模型返回：         │          │
│          │ "我想调用           │          │
│          │  GetWeatherInfo    │          │
│          │  (city=广州)"      │          │
│          │ ←────────────────── │          │
│          │                    │          │
│          │ ③执行本地函数       │          │
│          │ GetWeatherInfo     │          │
│          │ ("广州")           │          │
│          │ → 得到天气数据      │          │
│          │                    │          │
│          │ ④把函数结果回传     │          │
│          │ ──────────────────→ │          │
│          │                    │          │
│          │ ⑤模型基于结果       │          │
│          │  生成最终回答       │          │
│          │ ←────────────────── │          │
└──────────┘                    └──────────┘
```

**关键认知**：模型做了**决策**（调什么函数、传什么参数），你的程序做了**执行**（实际调用函数），模型再做**总结**（基于结果生成回答）。

## 定义AI可调用的函数

在C#中，定义一个AI可调用函数只需要两步：

### 1. 编写函数并用`[Description]`描述

```csharp
[Description("Get weather information for the specified city")]
string GetWeatherInfo(
    [Description("City name, for example: GuangDong")] string city)
{
    // 模拟天气API调用
    var weatherData = new
    {
        city,
        temperature = "30°C",
        condition = "Sunny",
        humidity = "65%",
        windSpeed = "10 km/h"
    };

    return JsonSerializer.Serialize(weatherData);
}
```

**`[Description]`特性至关重要**——模型正是通过这些描述来理解：
- 这个函数是做什么的？
- 参数应该怎么填？

描述越准确，模型的调用决策越正确。

### 2. 注册为AI工具

```csharp
var options = new ChatOptions
{
    Tools = [AIFunctionFactory.Create(GetWeatherInfo)]
};
```

`AIFunctionFactory.Create()`会反射函数签名和Description特性，自动生成符合OpenAI Tool协议的工具描述JSON。

## 启用自动函数调用

默认的`IChatClient`不会自动执行函数调用。我们需要用`ChatClientBuilder`包装一层：

```csharp
IChatClient client =
    new OpenAI.Chat.ChatClient(
        model, 
        new ApiKeyCredential(apiKey), 
        new OpenAI.OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
    .AsIChatClient();

// 关键：用UseFunctionInvocation()启用自动函数执行
using var functionCallingChatClient = new ChatClientBuilder(client)
    .UseFunctionInvocation()
    .Build();
```

`UseFunctionInvocation()`做了什么？它注册了一个**中间件管道**：

1. 拦截模型的响应
2. 检测是否包含函数调用请求
3. 如果有，自动执行对应的本地函数
4. 把函数结果回传给模型
5. 返回模型的最终回答

整个过程对开发者**完全透明**——你只需要像普通对话一样调用`GetStreamingResponseAsync`。

## 完整的Function Calling对话

```csharp
var options = new ChatOptions
{
    Tools = [AIFunctionFactory.Create(GetWeatherInfo)]
};

List<ChatMessage> chatMessages =
[
    new ChatMessage(ChatRole.System, "你是一个有用的AI助手，请用中文回答用户的问题。")
];

while (true)
{
    Console.Write("\n你: ");
    var userInput = Console.ReadLine();

    if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("再见！");
        break;
    }

    chatMessages.Add(new ChatMessage(ChatRole.User, userInput));

    Console.Write("\nAI: ");
    StringBuilder sb = new StringBuilder();
    await foreach (var update in functionCallingChatClient
        .GetStreamingResponseAsync(chatMessages, options))
    {
        foreach (var item in update.Contents)
        {
            if (item is TextReasoningContent textReasoning)
            {
                Console.Write(textReasoning.Text);  // 思考过程
            }
            else if (item is TextContent text)
            {
                Console.Write(text.Text);           // 最终回答
                sb.Append(text.Text);
            }
        }
    }
}
```

代码和之前的对话程序几乎一样！唯一的区别是传入了`options`（包含工具列表）。函数调用的复杂逻辑全部被`UseFunctionInvocation()`封装了。

## 透视Function Calling的底层通信

使用HTTP拦截器（见番外篇），我们可以看到Function Calling实际经历了**两次**API请求：

### 第一次请求：模型决策

**请求体（关键部分）：**
```json
{
  "model": "glm-5.1",
  "messages": [
    { "role": "system", "content": "你是一个有用的AI助手..." },
    { "role": "user", "content": "广州今天天气怎么样？" }
  ],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "GetWeatherInfo",
        "description": "Get weather information for the specified city",
        "parameters": {
          "type": "object",
          "properties": {
            "city": {
              "type": "string",
              "description": "City name, for example: GuangDong"
            }
          },
          "required": ["city"]
        }
      }
    }
  ]
}
```

注意`tools`数组——这就是模型"看到的"可用工具列表。

**响应体（关键部分）：**
```json
{
  "choices": [{
    "message": {
      "role": "assistant",
      "tool_calls": [{
        "id": "call_xxx",
        "type": "function",
        "function": {
          "name": "GetWeatherInfo",
          "arguments": "{\"city\":\"广州\"}"
        }
      }]
    }
  }]
}
```

模型没有直接回答，而是返回了一个`tool_calls`——它决定调用`GetWeatherInfo`，参数是`city=广州`。

### 中间执行：本地函数

SDK自动执行`GetWeatherInfo("广州")`，得到：
```json
{"city":"广州","temperature":"30°C","condition":"Sunny","humidity":"65%","windSpeed":"10 km/h"}
```

### 第二次请求：结果回传

```json
{
  "messages": [
    { "role": "system", "content": "..." },
    { "role": "user", "content": "广州今天天气怎么样？" },
    { "role": "assistant", "tool_calls": [...] },
    { "role": "tool", "tool_call_id": "call_xxx", 
      "content": "{\"city\":\"广州\",\"temperature\":\"30°C\",...}" }
  ]
}
```

注意新增的`tool`角色消息——这就是函数执行结果。模型基于这个结果生成最终回答。

### 第二次响应：最终回答

模型这次不再请求调用函数，而是直接用自然语言总结：

> "广州今天天气晴朗，气温30°C，湿度65%，风速10km/h，适合户外活动。"

## 多个函数的场景

你可以同时注册多个函数，模型会智能选择：

```csharp
[Description("获取指定城市的天气信息")]
string GetWeatherInfo([Description("城市名称")] string city) { ... }

[Description("搜索网页信息")]
string SearchWeb([Description("搜索关键词")] string query) { ... }

[Description("计算数学表达式")]
string Calculate([Description("数学表达式")] string expression) { ... }

var options = new ChatOptions
{
    Tools = [
        AIFunctionFactory.Create(GetWeatherInfo),
        AIFunctionFactory.Create(SearchWeb),
        AIFunctionFactory.Create(Calculate)
    ]
};
```

模型会根据用户的问题自动选择合适的函数——甚至可能在一个回答中调用多个函数。

## 最佳实践

### Description的编写原则

```csharp
// ❌ 太简略，模型可能误解
[Description("Get weather")]
string GetWeather(string city) { ... }

// ✅ 清晰描述功能和参数
[Description("获取指定城市的实时天气信息，包括温度、湿度、天气状况等")]
string GetWeatherInfo(
    [Description("城市名称，如：北京、上海、广州，支持中文城市名")] 
    string city) { ... }
```

### 函数设计的注意事项

| 原则 | 说明 |
|------|------|
| **返回值用JSON字符串** | 模型更容易解析结构化的JSON |
| **函数要幂等** | 模型可能在推理过程中多次调用同一函数 |
| **做好错误处理** | 函数失败时返回错误信息而非抛异常，模型能理解错误并调整回答 |
| **参数尽量简单** | 避免复杂的嵌套对象参数 |

## 从Function Calling到Agent

Function Calling是构建**AI Agent**的基石。Agent = LLM + Memory + Tools。我们目前已经拥有了：

- ✅ LLM对话（第一篇）
- ✅ 记忆系统（第二、三篇）
- ✅ 工具调用（本篇）

一个基本的Agent已经初具雏形。但Function Calling还有一个局限：**工具必须在编译时定义**。如果你想让AI动态接入各种第三方工具呢？

这就是下一篇文章的主角——**MCP协议**。

## 小结

这篇文章我们学习了：

1. **Function Calling的原理**：模型做决策，程序做执行，模型做总结
2. **C#中的实现方式**：`[Description]` + `AIFunctionFactory.Create` + `UseFunctionInvocation()`
3. **完整的调用链路**：两次API请求 + 一次本地函数执行
4. **通过HTTP拦截器透视底层通信**，理解`tool_calls`和`tool`角色消息
5. **最佳实践**：Description编写原则和函数设计注意事项

**下一篇**，我们将进入MCP协议的世界，让AI能够动态发现和调用外部工具生态。

---

> 完整代码见项目：[AIFunctionCallingConsole4/Program.cs](../AIFunctionCallingConsole4/Program.cs)
