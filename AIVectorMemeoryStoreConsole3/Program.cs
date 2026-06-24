using AI.Common;
using AIVectorMemeoryStoreConsole3;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using System.ClientModel;
using System.Text;
using System.Text.Json;

var configuration = new ConfigurationBuilder()
    .AddUserSecrets(typeof(AICommon).Assembly)
    .Build();

string baseUrl = configuration.GetSection("AIEndpoint").Value!;
string apiKey = configuration.GetSection("AIApiKey").Value!;
const string model = "glm-5.1";
const string embeddingModel = "embedding-3";
const int embeddingDimensions = 2048; // embedding-3 模型的向量维度

// 连接 Redis
var connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:6379,abortConnect=false");
var redisVectorStore = new RedisVectorStore(connectionMultiplexer, "chat_memory");

// 确保 Redis 向量索引存在
await redisVectorStore.EnsureCollectionExistsAsync(embeddingDimensions, allowRecreation: false);

HttpClientInterceptor.StartInterception();

await RedisVectorMemoryAsync();

async Task RedisVectorMemoryAsync()
{

    // 初始化 AI Chat 客户端
    IChatClient chatClient =
        new OpenAI.Chat.ChatClient(model, new ApiKeyCredential(apiKey), new OpenAI.OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
            .AsIChatClient();

    // 初始化 Embedding 生成器
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator =
        new OpenAI.Embeddings.EmbeddingClient(embeddingModel, new ApiKeyCredential(apiKey), new OpenAI.OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
            .AsIEmbeddingGenerator(embeddingDimensions);

    // 记忆参数
    const int topK = 6;             // 最多选取 6 条最相关的历史对话
    const float threshold = 0.5f;   // 余弦相似度阈值

    Console.WriteLine("=== 基于 Redis 向量存储的 AI 记忆对话 ===");
    Console.WriteLine($"模型: {model}, Embedding: {embeddingModel}");
    Console.WriteLine($"相似度阈值: {threshold}, TopK: {topK}");
    Console.WriteLine("历史对话将持久化存储到 Redis，支持跨会话记忆");
    Console.WriteLine("输入 'exit' 退出，输入 'clear' 清除历史记忆\n");

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

        // 清除记忆命令
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

        // 1. 为当前用户输入生成 embedding 向量
        var userEmbedding = await embeddingGenerator.GenerateAsync(userInput);
        float[] userVector = userEmbedding.Vector.ToArray();

        // 2. 从 Redis 中检索与当前问题最相关的历史对话
        var relevantItems = await redisVectorStore.SearchAsync(userVector, limit: topK);

        // 过滤低于阈值的结果
        var filteredItems = relevantItems
            .Where(item => item.Score >= threshold)
            .OrderByDescending(item => item.Score)
            .ToList();

        // 3. 构建发送给 AI 的消息列表
        List<ChatMessage> chatMessages =
        [
            new ChatMessage(ChatRole.System,
            "你是一个有用的AI助手，请用中文回答用户的问题。" +
            "下面是与用户当前问题相关的历史对话记录，请参考这些上下文来回答。" +
            "如果历史记录与当前问题无关，请忽略历史记录直接回答。")
        ];

        // 将相关历史对话加入上下文
        foreach (var item in filteredItems)
        {
            var role = item.Vector.Metadata.TryGetValue("role", out var r) ? r.ToString() : "user";
            var chatRole = role == "assistant" ? ChatRole.Assistant : ChatRole.User;
            chatMessages.Add(new ChatMessage(chatRole, item.Vector.Data));
        }

        // 加入当前用户消息
        chatMessages.Add(new ChatMessage(ChatRole.User, userInput));

        // 打印检索信息
        Console.WriteLine($"\n[信息] 从 Redis 检索到 {filteredItems.Count} 条相关历史记录");

        // 4. 流式调用 AI
        Console.Write("\nAI: ");
        StringBuilder sb = new StringBuilder();
        try
        {
            await foreach (var update in chatClient.GetStreamingResponseAsync(chatMessages))
            {
                foreach (var item in update.Contents)
                {
                    if (item is TextContent text)
                    {
                        Console.Write(text.Text);
                        sb.Append(text.Text);
                    }
                }
            }
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[错误] {ex.Message}");
        }

        // 5. 将本轮对话持久化到 Redis（用户消息 + AI 回复）
        var now = DateTime.UtcNow;
        var vectorsToInsert = new List<VectorModel>();

        // 存储用户消息
        vectorsToInsert.Add(new VectorModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Data = userInput,
            Embedding = userVector,
            UserId = "default",
            Metadata = new Dictionary<string, object> { ["role"] = "user" },
            CreatedAt = now,
            UpdatedAt = now,
        });

        // 存储 AI 回复
        if (sb.Length > 0)
        {
            var aiText = sb.ToString();
            var aiEmbedding = await embeddingGenerator.GenerateAsync(aiText);
            vectorsToInsert.Add(new VectorModel
            {
                Id = Guid.NewGuid().ToString("N"),
                Data = aiText,
                Embedding = aiEmbedding.Vector.ToArray(),
                UserId = "default",
                Metadata = new Dictionary<string, object> { ["role"] = "assistant" },
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await redisVectorStore.InsertAsync(vectorsToInsert);
    }
}