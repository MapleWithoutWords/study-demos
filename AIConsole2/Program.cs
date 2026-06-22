using Microsoft.Extensions.AI;
using System.ClientModel;
using System.Text.Json;

IChatClient client =
    new OpenAI.Chat.ChatClient("glm-5.1", new ApiKeyCredential("b1cc8a05c01a4dea99638a7111c14797.ECh52AZuMglnWiag"), new OpenAI.OpenAIClientOptions { Endpoint = new Uri("https://open.bigmodel.cn/api/paas/v4/") })
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
            foreach (var item in update.Contents)
            {
                if (item is TextReasoningContent textReasoning)
                {
                    Console.Write(textReasoning.Text);
                }
                else if (item is TextContent text)
                {
                    Console.Write(text.Text);
                    fullResponse += text.Text;
                }
                else if (item is UsageContent  usageContent)
                {
                    Console.WriteLine();
                    Console.WriteLine(JsonSerializer.Serialize(usageContent.Details));
                }
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
