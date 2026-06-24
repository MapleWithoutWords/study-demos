
using AI.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using System.ClientModel;
using System.Net.Security;
using System.Numerics.Tensors;
using System.Text;
using System.Text.Json;

var configuration = new ConfigurationBuilder()
    .AddUserSecrets(typeof(AICommon).Assembly)
    .Build();

string baseUrl = configuration.GetSection("AIEndpoint").Value!;
string apiKey = configuration.GetSection("AIApiKey").Value!;
const string model = "glm-5.1";
const string embeddingModel = "embedding-3";

//想看下请求和响应的内容，可以使用下面的拦截器
HttpClientInterceptor.StartInterception();
//await AICallSimpleMemoryAsync();

await AICallVectorMemoryAsync();

async Task AICallSimpleMemoryAsync(bool isNeedReasoningContent = false)
{
    IChatClient client =
        new OpenAI.Chat.ChatClient(model, new ApiKeyCredential(apiKey), new OpenAI.OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
        .AsIChatClient();

    List<ChatMessage> chatMessages =
    [
        new ChatMessage(ChatRole.System, "你是一个有用的AI助手，请用中文回答用户的问题。")
    ];

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

        chatMessages.Add(new ChatMessage(ChatRole.User, userInput));

        Console.Write("\nAI: ");
        try
        {
            StringBuilder sb = new StringBuilder();
            await foreach (var update in client.GetStreamingResponseAsync(chatMessages))
            {
                foreach (var item in update.Contents)
                {
                    if (item is TextReasoningContent textReasoning && isNeedReasoningContent)
                    {
                        Console.Write(textReasoning.Text);
                    }
                    else if (item is TextContent text)
                    {
                        Console.Write(text.Text);
                        sb.Append(text.Text);
                    }
                    else if (item is UsageContent usageContent)
                    {
                        Console.WriteLine();
                        Console.WriteLine(JsonSerializer.Serialize(usageContent.Details));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[错误] {ex.Message}");
        }
    }
}

async Task AICallVectorMemoryAsync(bool isNeedReasoningContent = false)
{
    IChatClient client =
        new OpenAI.Chat.ChatClient(model, new ApiKeyCredential(apiKey), new OpenAI.OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
        .AsIChatClient();

    // 创建 Embedding 生成器
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator =
        new OpenAI.Embeddings.EmbeddingClient(embeddingModel, new ApiKeyCredential(apiKey), new OpenAI.OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
        .AsIEmbeddingGenerator();

    // 存储所有对话记录及其对应的向量
    List<ChatMessageRecord> allMessages = [];

    const int topK = 6;             // 最多选取 topK 条最相关的历史对话
    const float threshold = 0.3f;   // 余弦相似度阈值

    Console.WriteLine("=== 向量记忆模式 ===");
    Console.WriteLine($"使用 Embedding 模型: {embeddingModel}");
    Console.WriteLine($"相似度阈值: {threshold}, TopK: {topK}");
    Console.WriteLine("每轮对话只会选取与当前问题最相关的历史记录发送给AI\n");

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

        // 1. 为当前用户输入生成向量
        var userEmbedding = await embeddingGenerator.GenerateAsync(userInput);
        float[] userVector = userEmbedding.Vector.ToArray();

        // 2. 计算与所有历史对话的余弦相似度，选出最相关的
        var relevantMessages = SelectRelevantMessages(allMessages, userVector, topK, threshold);

        // 3. 构建本次发送给 AI 的消息列表
        List<ChatMessage> chatMessages =
        [
            new ChatMessage(ChatRole.System,
                "你是一个有用的AI助手，请用中文回答用户的问题。" +
                "下面是与用户当前问题相关的历史对话记录，请参考这些上下文来回答。")
        ];

        // 加入相关历史对话
        foreach (var record in relevantMessages)
        {
            chatMessages.Add(record.Message);
        }

        // 加入当前用户消息
        chatMessages.Add(new ChatMessage(ChatRole.User, userInput));

        // 打印本次发送的信息
        int historyCount = relevantMessages.Count;
        Console.WriteLine($"\n[信息] 共 {allMessages.Count} 条历史记录，选取 {historyCount} 条相关记录发送");

        // 4. 流式调用 AI
        Console.Write("\nAI: ");
        StringBuilder sb = new StringBuilder();
        try
        {
            await foreach (var update in client.GetStreamingResponseAsync(chatMessages))
            {
                foreach (var item in update.Contents)
                {
                    if (item is TextReasoningContent textReasoning && isNeedReasoningContent)
                    {
                        Console.Write(textReasoning.Text);
                    }
                    else if (item is TextContent text)
                    {
                        Console.Write(text.Text);
                        sb.Append(text.Text);
                    }
                    else if (item is UsageContent usageContent)
                    {
                        Console.WriteLine();
                        Console.WriteLine(JsonSerializer.Serialize(usageContent.Details));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[错误] {ex.Message}");
        }

        // 5. 将本轮对话存入历史记录（包含向量）
        allMessages.Add(new ChatMessageRecord(
            new ChatMessage(ChatRole.User, userInput),
            userEmbedding.Vector.ToArray()));

        if (sb.Length > 0)
        {
            var aiText = sb.ToString();
            var aiEmbedding = await embeddingGenerator.GenerateAsync(aiText);
            allMessages.Add(new ChatMessageRecord(
                new ChatMessage(ChatRole.Assistant, aiText),
                aiEmbedding.Vector.ToArray()));
        }
    }
}

/// <summary>
/// 根据余弦相似度从历史记录中选取最相关的消息
/// </summary>
List<ChatMessageRecord> SelectRelevantMessages(List<ChatMessageRecord> allMessages, float[] queryVector, int topK, float threshold)
{
    if (allMessages.Count == 0) return [];

    ReadOnlySpan<float> querySpan = queryVector;

    // 计算每条历史记录与当前查询的余弦相似度
    var scored = new List<(ChatMessageRecord Record, float Score)>();
    foreach (var record in allMessages)
    {
        float similarity = TensorPrimitives.CosineSimilarity(
            new ReadOnlySpan<float>(record.Embedding),
            querySpan);
        if (similarity >= threshold)
        {
            scored.Add((record, similarity));
        }
    }

    // 按相似度降序排列，取 TopK
    return scored
        .OrderByDescending(x => x.Score)
        .Take(topK)
        .Select(x => x.Record)
        .ToList();
}

/// <summary>
/// 对话记录，包含消息内容和对应的向量
/// </summary>
record ChatMessageRecord(ChatMessage Message, float[] Embedding);