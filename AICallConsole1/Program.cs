using AI.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using System.ClientModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var configuration = new ConfigurationBuilder()
    .AddUserSecrets(typeof(AICommon).Assembly)
    .Build();

string baseUrl = configuration.GetSection("AIEndpoint").Value!;
string apiKey = configuration.GetSection("AIApiKey").Value!;
const string model = "glm-5.1";

//await HttpClientCallLLMAsync();

//想看下请求和响应的内容，可以使用下面的拦截器
//HttpClientInterceptor.StartInterception();
await SDKCallLLMAsync(isStream: true);

async Task HttpClientCallLLMAsync()
{
    //生产要使用 HttpClientFactory
    using var httpClient = new HttpClient();
    httpClient.BaseAddress = new Uri(baseUrl);
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    var jsonContent = JsonSerializer.Serialize(new
    {
        model = model,
        messages = new[]
        {
        new { role = "system", content = "你是一个有帮助的AI助手。" },
        new { role = "user", content = "你好，请介绍一下你自己。" }
    },
        temperature = 0.7,
        max_tokens = 1024
    });
    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
    Console.WriteLine($"》》》请求内容：\r\n    {jsonContent}");

    var response = await httpClient.PostAsync("chat/completions", content);
    var responseBody = await response.Content.ReadAsStringAsync();

    Console.WriteLine($"》》》返回内容：\r\n    {responseBody}");
}

async Task SDKCallLLMAsync(bool isStream = false, bool isNeedReasoningContent = true)
{
    IChatClient client =
        new OpenAI.Chat.ChatClient(model, new ApiKeyCredential(apiKey), new OpenAI.OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
        .AsIChatClient();

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
        try
        {
            if (isStream)
            {
                await foreach (var update in client.GetStreamingResponseAsync(chatMessages))
                {
                    foreach (var item in update.Contents)
                    {
                        if (item is TextReasoningContent textReasoning&& isNeedReasoningContent)
                        {
                            Console.Write($"{textReasoning.Text}");
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
            }
            else
            {
                var response = await client.GetResponseAsync(chatMessages);
                Console.WriteLine(response.Text);
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[错误] {ex.Message}");
        }
    }
}
