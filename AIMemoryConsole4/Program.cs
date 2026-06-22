using Microsoft.Extensions.AI;
using System.ClientModel;
using System.Numerics.Tensors;

// ========== 配置 ==========
const string ApiKey = "b1cc8a05c01a4dea99638a7111c14797.ECh52AZuMglnWiag";
const string Endpoint = "https://open.bigmodel.cn/api/paas/v4/";
const string ChatModel = "glm-5.1";
const string EmbeddingModel = "embedding-3";
const int TopK = 6;                    // 检索最相关的 K 条历史消息
const float SimilarityThreshold = 0.3f; // 相似度阈值，低于此值的历史消息不纳入
const string SystemPrompt = "你是一个有用的AI助手，请用中文回答用户的问题。你会根据相关的历史对话来回答，保持上下文连贯性。";

// ========== 初始化客户端 ==========
var chatClient =
    new OpenAI.Chat.ChatClient(ChatModel, new ApiKeyCredential(ApiKey), new OpenAI.OpenAIClientOptions { Endpoint = new Uri(Endpoint) })
    .AsIChatClient();

var embeddingGenerator =
    new OpenAI.Embeddings.EmbeddingClient(EmbeddingModel, new ApiKeyCredential(ApiKey), new OpenAI.OpenAIClientOptions { Endpoint = new Uri(Endpoint) })
    .AsIEmbeddingGenerator();

// ========== 记忆存储 ==========
var memory = new List<MemoryEntry>();

Console.WriteLine("AI 记忆对话已启动（输入 'exit' 退出，输入 'memory' 查看记忆条数）");
Console.WriteLine($"模型: {ChatModel} | Embedding: {EmbeddingModel} | TopK: {TopK} | 阈值: {SimilarityThreshold}");
Console.WriteLine(new string('-', 60));

while (true)
{
    Console.Write("\n你: ");
    var userInput = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(userInput))
        continue;

    if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"再见！本次会话共积累了 {memory.Count} 条记忆。");
        break;
    }

    if (userInput.Equals("memory", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"当前记忆条数: {memory.Count}");
        for (int i = 0; i < memory.Count; i++)
        {
            var entry = memory[i];
            var preview = entry.Text.Length > 40 ? entry.Text[..40] + "..." : entry.Text;
            Console.WriteLine($"  [{i + 1}] ({entry.Role}) {preview}");
        }
        continue;
    }

    try
    {
        // 1. 生成用户输入的 Embedding
        var userEmbeddings = await embeddingGenerator.GenerateAsync([userInput]);
        var userVector = userEmbeddings[0].Vector;

        // 2. 计算余弦相似度，检索最相关的历史消息
        var relevantHistory = RetrieveRelevantMessages(memory, userVector, TopK, SimilarityThreshold);

        // 3. 构建发送给 AI 的消息列表
        var chatMessages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt)
        };

        // 加入相关历史消息（按时间顺序排列）
        foreach (var entry in relevantHistory.OrderBy(e => e.Timestamp))
        {
            chatMessages.Add(new ChatMessage(
                entry.Role == "user" ? ChatRole.User : ChatRole.Assistant,
                entry.Text));
        }

        // 加入当前用户输入
        chatMessages.Add(new ChatMessage(ChatRole.User, userInput));

        // 4. 显示检索信息
        if (relevantHistory.Count > 0)
        {
            Console.WriteLine($"  [检索到 {relevantHistory.Count} 条相关记忆]");
        }

        // 5. 流式调用 AI
        Console.Write("\nAI: ");
        var fullResponse = "";

        await foreach (var update in chatClient.GetStreamingResponseAsync(chatMessages))
        {
            foreach (var item in update.Contents)
            {
                if (item is TextContent text)
                {
                    Console.Write(text.Text);
                    fullResponse += text.Text;
                }
            }
        }

        Console.WriteLine();

        // 6. 将用户消息和 AI 回复存入记忆
        memory.Add(new MemoryEntry("user", userInput, userVector.ToArray(), DateTime.Now));

        if (!string.IsNullOrWhiteSpace(fullResponse))
        {
            var assistantEmbeddings = await embeddingGenerator.GenerateAsync([fullResponse]);
            memory.Add(new MemoryEntry("assistant", fullResponse, assistantEmbeddings[0].Vector.ToArray(), DateTime.Now));
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n[错误] {ex.Message}");
    }
}

// ========== 检索相关消息 ==========
static List<MemoryEntry> RetrieveRelevantMessages(
    List<MemoryEntry> memory,
    ReadOnlyMemory<float> queryVector,
    int topK,
    float threshold)
{
    if (memory.Count == 0)
        return [];

    var scored = new List<(MemoryEntry Entry, float Score)>();

    foreach (var entry in memory)
    {
        var similarity = CosineSimilarity(queryVector.Span, entry.Embedding);
        if (similarity >= threshold)
        {
            scored.Add((entry, similarity));
        }
    }

    return scored
        .OrderByDescending(x => x.Score)
        .Take(topK)
        .Select(x => x.Entry)
        .ToList();
}

// ========== 余弦相似度计算 ==========
static float CosineSimilarity(ReadOnlySpan<float> a, float[] b)
{
    var minLen = Math.Min(a.Length, b.Length);
    if (minLen == 0) return 0f;

    return TensorPrimitives.CosineSimilarity(a[..minLen], b.AsSpan()[..minLen]);
}

// ========== 记忆条目 ==========
record MemoryEntry(string Role, string Text, float[] Embedding, DateTime Timestamp);
