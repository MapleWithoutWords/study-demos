# 持久化记忆——基于Redis向量搜索的AI长期记忆

> 这是《AI开发解密》系列的第三篇。我们将把内存中的向量记忆持久化到Redis，实现跨会话的AI长期记忆。

## 引言

> 本文完整代码在 `AIVectorMemeoryStoreConsole3` 项目中，克隆仓库后配置好API Key和Redis环境直接运行即可体验效果。
> 项目地址：https://github.com/MapleWithoutWords/AIStudyDemos

上一篇文章中，我们用内存向量 + 余弦相似度实现了智能的上下文检索。但问题也随之而来：

- 程序一重启，所有记忆丢失
- 无法跨会话共享记忆（比如不同终端、不同用户）
- 内存存储量有限，不适合大规模应用

解决方案：**把向量存到支持向量搜索的数据库中**。本文以Redis为例演示整个流程，实际生产中不一定要选Redis，可以根据你的场景选择合适的向量数据库（如Qdrant、Milvus、pgvector等）。

## 环境准备

### 安装Redis Stack

向量搜索需要Redis Stack（包含RediSearch模块）。最简单的方式是用Docker：

```bash
docker run -d --name redis-stack -p 6379:6379 redis/redis-stack:latest
```

## 数据模型设计

首先定义向量存储的数据模型：

```csharp
public class VectorModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Data { get; set; } = string.Empty;           // 原始文本内容
    public float[] Embedding { get; set; } = Array.Empty<float>(); // 向量数据
    public Dictionary<string, object> Metadata { get; set; } = new(); // 元数据（如role）
    public string? UserId { get; set; }                         // 用户标识
    public string? Hash { get; set; }
    public string? AgentId { get; set; }                        // 生成向量的模型标识
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

查询结果需要附带相似度得分：

```csharp
public class QueryVectorItemDto
{
    public VectorModel Vector { get; set; } = new();
    public float Score { get; set; }  // 余弦相似度得分
}
```

`Metadata`字段用于存储额外信息，比如这条记录是`user`消息还是`assistant`消息——这在构建上下文时非常重要。

## 从零实现RedisVectorStore

### 连接Redis并创建向量索引

```csharp
var connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:6379");
var redisVectorStore = new RedisVectorStore(connectionMultiplexer, "chat_memory");

// 确保向量索引存在（2048维，对应embedding-3模型）
await redisVectorStore.EnsureCollectionExistsAsync(2048, allowRecreation: false);
```

### 索引创建的核心逻辑

Redis的向量搜索需要先创建索引。这是最关键的一步：

```csharp
private Task CreateIndexAsync(ISearchCommands ft, int vectorSize)
{
    var schema = new Schema()
        .AddTextField("id")
        .AddTextField("data")
        .AddTextField("user_id")
        .AddTextField("metadata")
        .AddNumericField("created_at")
        .AddNumericField("updated_at")
        .AddVectorField("embedding",
            Schema.VectorField.VectorAlgo.HNSW,
            new Dictionary<string, object>
            {
                ["TYPE"] = "FLOAT32",       // 向量数据类型
                ["DIM"] = vectorSize,       // 向量维度（2048）
                ["DISTANCE_METRIC"] = "COSINE"  // 余弦距离
            });

    bool success = ft.Create(_indexName,
        new FTCreateParams()
            .On(IndexDataType.HASH)
            .Prefix(_keyPrefix),
        schema);

    if (!success) throw new Exception("Failed to create Redis vector index.");
    return Task.CompletedTask;
}
```

**关键参数解析：**

- **HNSW算法**：Hierarchical Navigable Small World，目前最主流的近似最近邻搜索算法，兼顾速度和精度
- **FLOAT32**：单精度浮点，每个向量占`2048 × 4 = 8KB`
- **COSINE**：余弦距离度量，与第二篇的余弦相似度对应

### 向量序列化

Redis存储的是字节数组，所以需要在`float[]`和`byte[]`之间转换：

```csharp
private byte[] SerializeVector(float[] vector)
{
    var bytes = new byte[vector.Length * sizeof(float)];
    Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
    return bytes;
}

private float[] DeserializeVector(byte[] bytes)
{
    var floats = new float[bytes.Length / sizeof(float)];
    Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
    return floats;
}
```

`Buffer.BlockCopy`是最快的数组拷贝方式，比逐元素转换快几个数量级。

### 插入向量数据

```csharp
public async Task InsertAsync(List<VectorModel> vectors, CancellationToken ct = default)
{
    foreach (var vector in vectors)
    {
        var key = $"{_keyPrefix}{vector.Id}";
        var hashEntries = new HashEntry[]
        {
            new("id", vector.Id),
            new("data", vector.Data),
            new("user_id", vector.UserId ?? string.Empty),
            new("metadata", JsonSerializer.Serialize(vector.Metadata)),
            new("created_at", vector.CreatedAt.Ticks),
            new("updated_at", vector.UpdatedAt?.Ticks),
            new("embedding", SerializeVector(vector.Embedding))  // 向量序列化为bytes
        };
        await _db.HashSetAsync(key, hashEntries);
    }
}
```

每条记录以Redis Hash的形式存储，key格式为`chat_memory:{id}`。

### KNN向量搜索

这是整个系统最核心的部分——在Redis中执行KNN（K-Nearest Neighbors）向量搜索：

```csharp
public Task<List<QueryVectorItemDto>> SearchAsync(
    float[] queryVector, string? userId = null, int limit = 100)
{
    var ft = _db.FT();

    // 构建KNN搜索查询
    var queryStr = userId != null ? $"@user_id:{EscapeRedisQuery(userId)}" : "*";
    var searchQuery = $"{queryStr}=>[KNN {limit} @embedding $query_vector AS __embedding_score]";

    var fullQuery = new Query(searchQuery)
        .SetSortBy("__embedding_score")    // 按距离排序
        .Limit(0, limit)
        .ReturnFields("id", "data", "user_id", "metadata", 
                       "created_at", "__embedding_score")
        .Dialect(2);                       // 必须使用Dialect 2

    fullQuery.AddParam("query_vector", SerializeVector(queryVector));

    var results = ft.Search(_indexName, fullQuery);

    var searchResults = new List<QueryVectorItemDto>();
    foreach (var doc in results.Documents)
    {
        var vectorItem = ParseVectorModel(doc);
        var scoreValue = doc["__embedding_score"];
        // Redis返回的是距离，转换为相似度：1 - distance
        var score = float.TryParse(scoreValue.ToString(), out var s) 
            ? 1.0f - s : 0.0f;

        searchResults.Add(new QueryVectorItemDto
        {
            Vector = vectorItem,
            Score = score
        });
    }

    return Task.FromResult(searchResults);
}
```

**关键细节：**

1. **查询语法**：`*=>[KNN 6 @embedding $query_vector AS __embedding_score]`
   - `*`表示不过滤，`@user_id:xxx`可按用户过滤
   - `KNN 6`表示找最近的6个邻居
   - `@embedding`是向量字段名
   - `$query_vector`是参数化的查询向量
2. **距离转相似度**：Redis返回余弦距离（0-2），`1 - distance`转换为相似度（-1到1）
3. **Dialect 2**：向量搜索必须使用Dialect 2查询引擎

## 持久化记忆的完整对话流程

将Redis向量存储接入对话系统：

```csharp
async Task RedisVectorMemoryAsync()
{
    // 初始化AI客户端和Embedding生成器（同前两篇）
    IChatClient chatClient = ...;
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator = ...;

    const int topK = 6;
    const float threshold = 0.5f;  // 比内存版更高的阈值（Redis搜索更精准）

    while (true)
    {
        var userInput = Console.ReadLine();

        // 1. 生成查询向量
        var userEmbedding = await embeddingGenerator.GenerateAsync(userInput);
        float[] userVector = userEmbedding.Vector.ToArray();

        // 2. 从Redis检索相关历史
        var relevantItems = await redisVectorStore.SearchAsync(userVector, limit: topK);
        var filteredItems = relevantItems
            .Where(item => item.Score >= threshold)
            .OrderByDescending(item => item.Score)
            .ToList();

        // 3. 构建上下文
        List<ChatMessage> chatMessages =
        [
            new ChatMessage(ChatRole.System,
                "你是一个有用的AI助手。下面是相关的历史对话记录，请参考这些上下文来回答。" +
                "如果历史记录与当前问题无关，请忽略历史记录直接回答。")
        ];

        foreach (var item in filteredItems)
        {
            var role = item.Vector.Metadata.TryGetValue("role", out var r) 
                ? r.ToString() : "user";
            var chatRole = role == "assistant" ? ChatRole.Assistant : ChatRole.User;
            chatMessages.Add(new ChatMessage(chatRole, item.Vector.Data));
        }

        chatMessages.Add(new ChatMessage(ChatRole.User, userInput));

        // 4. 调用AI（流式输出）
        // ... 同前几篇 ...

        // 5. 持久化到Redis
        var vectorsToInsert = new List<VectorModel>
        {
            new() {
                Id = Guid.NewGuid().ToString("N"),
                Data = userInput,
                Embedding = userVector,
                UserId = "default",
                Metadata = new Dictionary<string, object> { ["role"] = "user" },
                CreatedAt = DateTime.UtcNow
            }
        };

        if (sb.Length > 0)
        {
            var aiEmbedding = await embeddingGenerator.GenerateAsync(sb.ToString());
            vectorsToInsert.Add(new() {
                Id = Guid.NewGuid().ToString("N"),
                Data = sb.ToString(),
                Embedding = aiEmbedding.Vector.ToArray(),
                UserId = "default",
                Metadata = new Dictionary<string, object> { ["role"] = "assistant" },
                CreatedAt = DateTime.UtcNow
            });
        }

        await redisVectorStore.InsertAsync(vectorsToInsert);
    }
}
```

### 清除记忆功能

持久化存储还带来了新的需求——清除记忆：

```csharp
if (userInput.Equals("clear", StringComparison.OrdinalIgnoreCase))
{
    var existingItems = await redisVectorStore.ListAsync(limit: 10000);
    foreach (var item in existingItems)
    {
        await redisVectorStore.DeleteAsync(item.Id);
    }
    Console.WriteLine("[系统] 已清除所有历史记忆。");
    continue;
}
```

## 内存版 vs Redis版对比

| 维度 | 内存版（第二篇） | Redis版（本篇） |
|------|----------------|----------------|
| **持久化** | ❌ 重启丢失 | ✅ 持久存储 |
| **跨会话** | ❌ 不支持 | ✅ 支持 |
| **搜索方式** | 手动计算余弦相似度 | Redis原生KNN搜索 |
| **相似度计算** | `TensorPrimitives.CosineSimilarity` | Redis引擎内部计算 |
| **多用户** | ❌ 不支持 | ✅ 通过`UserId`过滤 |
| **性能** | 小数据量快 | 大数据量更优 |
| **部署成本** | 零 | 需要Redis Stack |

## 小结

这篇文章我们学习了：

1. **Redis向量搜索**的完整实现：索引创建、向量序列化、KNN搜索
2. **HNSW算法**和**COSINE距离**的配置
3. **`NRedisStack`**的RediSearch API使用
4. 从内存记忆到**持久化记忆**的升级
5. 完整的CRUD操作和清除记忆功能

到这里，我们的AI已经拥有了"长期记忆"。但AI只能被动回答问题——它还不能**主动做事**。

**下一篇**，我们将赋予AI一项新能力：**调用外部函数**，让AI从"能说话"变成"能做事"。

---

> 完整代码见项目：
> - [AIVectorMemeoryStoreConsole3/Program.cs](../AIVectorMemeoryStoreConsole3/Program.cs)
> - [AIVectorMemeoryStoreConsole3/RedisVectorStore.cs](../AIVectorMemeoryStoreConsole3/RedisVectorStore.cs)
> - [AIVectorMemeoryStoreConsole3/VectorModel.cs](../AIVectorMemeoryStoreConsole3/VectorModel.cs)
>
> 项目地址：https://github.com/MapleWithoutWords/AIStudyDemos
