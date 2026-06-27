# 番外篇：幕后功臣——HTTP拦截器与AI调试技巧

> 这是《AI开发解密》系列的番外篇。我们将深入项目中一个不太起眼但至关重要的组件：HTTP拦截器，并分享AI开发中的实用调试技巧。

## 引言

> 本文完整代码在 `AI.Common` 项目中，克隆仓库后可直接查看和使用。
> 项目地址：https://github.com/MapleWithoutWords/AIStudyDemos

在前面的系列文章中，我们多次提到“通过HTTP拦截器查看底层通信”。这个能力在AI开发中**极其重要**——因为AI SDK的高度封装虽然让代码更简洁，但也隐藏了大量关键信息：

- 模型到底收到了什么内容？
- Function Calling的请求和响应长什么样？
- Embedding请求的参数和返回值是什么结构？
- Token消耗了多少？

当AI的行为不符合预期时，**查看原始HTTP报文往往是最快的定位手段**。

## HTTP拦截器的实现

### 核心思路

我们的目标是：**在不修改任何业务代码的前提下，自动拦截所有HttpClient的请求和响应**。

技术方案：使用**HarmonyLib**库Hook `HttpClient`的构造函数，在所有HttpClient实例中注入一个日志记录Handler。

### 为什么选择HarmonyLib？

[HarmonyLib](https://harmony.pardeike.net/) 是一个强大的.NET运行时补丁库，可以在不修改源码的情况下拦截和修改方法行为。它被广泛用于游戏Mod开发，在AI开发调试中也大有用武之地。

```xml
<PackageReference Include="Lib.Harmony" />
```

### LoggingDelegatingHandler：日志记录器

首先实现一个`DelegatingHandler`，它可以在HTTP请求管道中"插入"日志逻辑：

```csharp
public class LoggingDelegatingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // === 请求阶段 ===
        Console.WriteLine("\n========== HTTP REQUEST ==========");
        Console.WriteLine($"{request.Method} {request.RequestUri}");
        
        if (request.Content != null)
        {
            var requestContent = await request.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"Body: {FormatJson(requestContent)}");
        }

        // === 执行实际请求 ===
        var response = await base.SendAsync(request, cancellationToken);

        // === 响应阶段 ===
        Console.WriteLine("\n========== HTTP RESPONSE ==========");
        Console.WriteLine($"Status: {(int)response.StatusCode} {response.StatusCode}");
        
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        Console.WriteLine($"Body: {FormatJson(responseContent)}");

        return response;
    }

    private string FormatJson(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions 
            { WriteIndented = true });
        }
        catch
        {
            return json;  // 非JSON内容直接返回
        }
    }
}
```

这个Handler做的事情很简单：
1. 在请求发出前，打印请求方法、URL和Body
2. 在响应返回后，打印状态码和响应Body
3. JSON内容自动格式化，方便阅读

### HarmonyLib Hook：自动注入Handler

有了日志Handler，怎么让它在**所有HttpClient实例**中生效？答案是Hook构造函数：

```csharp
using HarmonyLib;

public static class HttpClientInterceptor
{
    private static Harmony? _harmony;

    public static void StartInterception()
    {
        _harmony = new Harmony("AIStudyDemo.Patch");

        // 获取HttpClient的(HttpMessageHandler, bool)构造函数
        var httpClientType = typeof(HttpClient);
        var fullConstructor = httpClientType.GetConstructor(
            new[] { typeof(HttpMessageHandler), typeof(bool) });

        if (fullConstructor != null)
        {
            // 注册前缀补丁
            var prefix = typeof(HttpClientConstructorPatches)
                .GetMethod(nameof(HttpClientConstructorPatches
                    .HttpClientFullConstructorPrefix));
            _harmony.Patch(fullConstructor, new HarmonyMethod(prefix));
        }
    }
}
```

### 构造函数补丁

当任何代码创建`new HttpClient(handler)`时，我们的补丁方法会被自动调用：

```csharp
internal static class HttpClientConstructorPatches
{
    public static void HttpClientFullConstructorPrefix(
        ref HttpMessageHandler handler, bool disposeHandler)
    {
        // 如果已经有LoggingDelegatingHandler，避免重复包装
        if (handler is not LoggingDelegatingHandler)
        {
            handler = HttpClientInterceptor.CreateHandler(handler);
        }
    }
}
```

`ref HttpMessageHandler handler`——通过`ref`关键字，我们可以**替换**原始传入的Handler，把`LoggingDelegatingHandler`包在外面：

```
原始管道：  HttpClient → SocketsHttpHandler
补丁后管道：HttpClient → LoggingDelegatingHandler → SocketsHttpHandler
```

### CreateHandler：保持原有Handler链

```csharp
internal static LoggingDelegatingHandler CreateHandler(HttpMessageHandler? innerHandler = null)
{
    var handler = new LoggingDelegatingHandler();

    if (innerHandler != null && handler.InnerHandler == null)
    {
        // 将原始Handler设为InnerHandler，保持管道完整
        var innerHandlerProperty = typeof(DelegatingHandler).GetProperty("InnerHandler");
        if (innerHandlerProperty?.CanWrite == true)
        {
            innerHandlerProperty.SetValue(handler, innerHandler);
        }
    }

    return handler;
}
```

### 使用方法

`LoggingDelegatingHandler`有两种使用方式，适用场景截然不同：

#### 方式一：手动注入（单个HttpClient实例）

```csharp
var client = new HttpClient(new LoggingDelegatingHandler());
```

这种方式只会拦截**你手动创建的这一个HttpClient实例**。优点是使用简单、作用域明确；缺点是**无法拦截SDK内部创建的HttpClient**——而AI SDK（如OpenAI SDK、MCP Client）在内部会自行创建HttpClient，你根本拿不到它们的实例。

#### 方式二：HarmonyLib全局Hook（所有HttpClient实例）

```csharp
HttpClientInterceptor.StartInterception();
```

这种方式通过Hook `HttpClient`的构造函数，**自动拦截之后创建的所有HttpClient实例**，包括SDK内部创建的。这正是本项目采用的方式——只需在程序入口调用一行，就能捕获AI SDK发出的每一个HTTP请求。

#### 两种方式对比

| 维度 | 手动注入 | HarmonyLib全局Hook |
|------|---------|-------------------|
| **拦截范围** | 仅指定的HttpClient实例 | 所有HttpClient实例 |
| **能否拦截SDK内部请求** | ❌ 不能 | ✅ 能 |
| **使用复杂度** | 简单，一行代码 | 需要HarmonyLib依赖 |
| **适用场景** | 自己控制的HttpClient | 调试AI SDK、第三方库的内部请求 |
| **侵入性** | 低，不修改全局行为 | 高，影响所有HttpClient |

> **本项目选择全局Hook的原因**：AI SDK（OpenAI SDK、MCP Client等）在内部创建HttpClient，我们无法手动给它们注入Handler。只有通过HarmonyLib Hook构造函数，才能在不修改SDK代码的前提下拦截所有请求。

在任何Demo的入口调用一行即可开启全局拦截：

```csharp
HttpClientInterceptor.StartInterception();
```

之后所有HTTP通信都会被自动记录，无需修改任何业务代码。

## AI开发中的调试技巧

### 技巧一：观察Token消耗

每次API响应都包含`usage`信息，记录token消耗对成本优化至关重要：

```csharp
else if (item is UsageContent usageContent)
{
    Console.WriteLine();
    Console.WriteLine(JsonSerializer.Serialize(usageContent.Details));
}
```

典型的token统计：

```json
{
  "input_tokens": 1250,
  "output_tokens": 380,
  "total_tokens": 1630
}
```

### 技巧二：理解System Prompt的重要性

System Prompt直接影响AI的行为。通过拦截器观察实际发送的System Prompt，可以排查很多"AI行为不符合预期"的问题：

```json
{
  "messages": [
    {
      "role": "system",
      "content": "你是一个有用的AI助手。下面是与用户当前问题相关的历史对话记录..."
    }
  ]
}
```

如果发现AI"忽略了历史记录"，检查System Prompt是否清楚地指示了"请参考这些上下文来回答"。

### 技巧三：调试Function Calling

Function Calling的多轮通信是最需要调试的场景。通过拦截器你可以确认：

1. **工具描述是否正确传递**：`tools`数组中的`description`和`parameters`是否完整
2. **模型是否做出了正确的调用决策**：`tool_calls`中的函数名和参数是否合理
3. **函数结果是否正确回传**：`tool`角色的消息内容是否是函数真实返回

### 技巧四：调试向量搜索

Embedding生成和向量搜索也需要调试：

```
========== HTTP REQUEST ==========
POST https://open.bigmodel.cn/api/paas/v4/embeddings
Body: {
  "model": "embedding-3",
  "input": "广州今天天气怎么样？",
  "dimensions": 2048
}

========== HTTP RESPONSE ==========
Status: 200 OK
Body: {
  "data": [{
    "embedding": [0.023, -0.041, 0.087, ...],  // 2048维向量
    "index": 0
  }]
}
```

通过观察原始请求，你可以确认：
- 向量维度是否正确
- 输入文本是否被正确编码
- Embedding模型是否返回了预期格式的数据

### 技巧五：配置管理最佳实践

```bash
# 初始化UserSecrets（只需执行一次）
dotnet user-secrets init --project AI.Common/AI.Common.csproj

# 设置密钥
dotnet user-secrets set "AIEndpoint" "https://open.bigmodel.cn/api/paas/v4/" \
    --project AI.Common/AI.Common.csproj

dotnet user-secrets set "AIApiKey" "your-api-key-here" \
    --project AI.Common/AI.Common.csproj
```

UserSecrets存储在：
- Windows: `%APPDATA%\Microsoft\UserSecrets\`
- macOS/Linux: `~/.microsoft/usersecrets/`

**关键**：UserSecrets文件**不在项目目录中**，因此不会被Git追踪。

### 技巧六：处理常见错误

| 错误现象 | 可能原因 | 排查方法 |
|---------|---------|---------|
| `401 Unauthorized` | API Key无效或过期 | 检查Authorization Header |
| `404 Not Found` | Endpoint地址错误或模型不存在 | 检查BaseAddress和model参数 |
| `429 Too Many Requests` | 超过API调用频率限制 | 查看响应Header中的Retry-After |
| `context_length_exceeded` | 输入token超过模型限制 | 查看usage中的input_tokens |
| 函数未被调用 | Description描述不清晰 | 检查tools数组的function定义 |

## 完整拦截器的生命周期管理

```csharp
// 启动拦截
HttpClientInterceptor.StartInterception();

// ... 执行AI调用 ...

// 停止拦截（可选）
HttpClientInterceptor.StopInterception();
```

`StopInterception`会调用`_harmony.UnpatchAll`移除所有补丁，恢复HttpClient的原始行为。这在单元测试或需要临时拦截的场景中很有用。

## 小结

这篇文章我们学习了：

1. **HTTP拦截器的设计思路**：HarmonyLib Hook + DelegatingHandler管道
2. **自动注入的原理**：Patch构造函数，替换Handler链
3. **AI开发调试的六大技巧**：Token观察、System Prompt调试、Function Calling调试、向量搜索调试、配置管理、错误排查
4. **UserSecrets**安全存储API Key的完整用法

HTTP拦截器虽然是一个"辅助工具"，但在AI开发中它的重要性不亚于核心业务代码。**理解底层通信是成为AI开发高手的必经之路**。

---

> 完整代码见项目：
> - [AI.Common/HttpClientInterceptor.cs](../AI.Common/HttpClientInterceptor.cs)
> - [AI.Common/LoggingDelegatingHandler.cs](../AI.Common/LoggingDelegatingHandler.cs)
>
> 项目地址：https://github.com/MapleWithoutWords/AIStudyDemos
