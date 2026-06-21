using Microsoft.Extensions.AI;
using System.ClientModel;

IChatClient client =
    new OpenAI.Chat.ChatClient("mimo-v2.5-pro", new ApiKeyCredential("tp-ctqrehoezox60yzmkcolsskgvxsajdduazi14i0aazu7zp26"), new OpenAI.OpenAIClientOptions { Endpoint = new Uri("https://token-plan-cn.xiaomimimo.com/v1/") })
    .AsIChatClient();

List<ChatMessage> chatMessages =
[
    new ChatMessage(ChatRole.System, "你是一个有用的AI助手，请用中文回答用户的问题。")
];

Console.WriteLine("AI 对话已启动（输入 'exit' 退出）");
Console.WriteLine(new string('-', 50));

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
        var fullResponse = "";
        await foreach (var update in client.GetStreamingResponseAsync(chatMessages))
        {
            if (update.Text is not null)
            {
                Console.Write(update.Text);
                fullResponse += update.Text;
            }
        }

        Console.WriteLine();
        chatMessages.Add(new ChatMessage(ChatRole.Assistant, fullResponse));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n[错误] {ex.Message}");
    }
}
