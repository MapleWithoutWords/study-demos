using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// ====== 配置区域 ======
const string baseUrl = "https://token-plan-cn.xiaomimimo.com/v1/";
const string apiKey = "tp-ctqrehoezox60yzmkcolsskgvxsajdduazi14i0aazu7zp26"; // 替换为你的 API Key
const string model = "mimo-v2.5-pro"; // 替换为实际模型名称

using var httpClient = new HttpClient();
httpClient.BaseAddress = new Uri(baseUrl);
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

// 构建请求体
var requestBody = new
{
    model = model,
    messages = new[]
    {
        new { role = "system", content = "你是一个有帮助的AI助手。" },
        new { role = "user", content = "你好，请介绍一下你自己。" }
    },
    temperature = 0.7,
    max_tokens = 1024
};

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

var jsonContent = JsonSerializer.Serialize(requestBody, jsonOptions);
Console.WriteLine(">>> 请求内容:");
Console.WriteLine(jsonContent);
Console.WriteLine();

try
{
    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
    var response = await httpClient.PostAsync("chat/completions", content);

    var responseBody = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        Console.WriteLine($">>> 请求失败，状态码: {(int)response.StatusCode} {response.StatusCode}");
        Console.WriteLine(responseBody);
        return;
    }

    // 解析响应
    var chatResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody, jsonOptions);
    var assistantMessage = chatResponse?.Choices?.FirstOrDefault()?.Message?.Content;

    Console.WriteLine(">>> 模型回复:");
    Console.WriteLine(assistantMessage ?? "(无回复内容)");

    Console.WriteLine();
    Console.WriteLine($">>> Token 用量: prompt={chatResponse?.Usage?.PromptTokens}, " +
                      $"completion={chatResponse?.Usage?.CompletionTokens}, " +
                      $"total={chatResponse?.Usage?.TotalTokens}");
}
catch (Exception ex)
{
    Console.WriteLine($">>> 请求异常: {ex.Message}");
}

// ====== 响应模型 ======
public class ChatCompletionResponse
{
    public string? Id { get; set; }
    public string? Model { get; set; }
    public List<Choice>? Choices { get; set; }
    public Usage? Usage { get; set; }
}

public class Choice
{
    public int Index { get; set; }
    public Message? Message { get; set; }
    public string? FinishReason { get; set; }
}

public class Message
{
    public string? Role { get; set; }
    public string? Content { get; set; }
}

public class Usage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}
