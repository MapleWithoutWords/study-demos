
using AI.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using System.ClientModel;
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

// 配置传输选项

var config = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint= new Uri("http://localhost:5144/mcp"),
     TransportMode= HttpTransportMode.AutoDetect,
});

// 创建 MCP 客户端实例
var mcpClient = await McpClient.CreateAsync(config);

var tools = await mcpClient.ListToolsAsync();
foreach (var item in tools)
{
    Console.WriteLine(item.Name);
}

using var functionCallingChatClient = new Microsoft.Extensions.AI.ChatClientBuilder(client)
    .UseFunctionInvocation()
    .Build();

while (true)
{
    Console.Write("Prompt: ");
    List<ChatMessage> messages = [];
    messages.Add(new(ChatRole.User, Console.ReadLine()));

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