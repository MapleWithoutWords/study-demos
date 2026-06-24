
using AI.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using System.ClientModel;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

var configuration = new ConfigurationBuilder()
    .AddUserSecrets(typeof(AICommon).Assembly)
    .Build();

string baseUrl = configuration.GetSection("AIEndpoint").Value!;
string apiKey = configuration.GetSection("AIApiKey").Value!;
const string model = "glm-5.1";

HttpClientInterceptor.StartInterception();

IChatClient client =
    new OpenAI.Chat.ChatClient(model, new ApiKeyCredential(apiKey), new OpenAI.OpenAIClientOptions { Endpoint = new Uri(baseUrl) })
    .AsIChatClient();
using var functionCallingChatClient = new Microsoft.Extensions.AI.ChatClientBuilder(client)
    .UseFunctionInvocation()
    .Build();
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
        await foreach (var update in functionCallingChatClient.GetStreamingResponseAsync(chatMessages, options))
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

[Description("Get weather information for the specified city")]
string GetWeatherInfo([Description("City name, for example: GuangDong")] string city)
{
    // Simulate weather API call - in real scenario, this would call actual weather service
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