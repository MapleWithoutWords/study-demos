

using Microsoft.Extensions.AI;
using System.ClientModel;
IChatClient client =
    new OpenAI.Chat.ChatClient("mimo-v2.5-pro", new ApiKeyCredential("tp-ctqrehoezox60yzmkcolsskgvxsajdduazi14i0aazu7zp26"), new OpenAI.OpenAIClientOptions { Endpoint = new Uri("https://token-plan-cn.xiaomimimo.com/v1/") })
    .AsIChatClient();

List<ChatMessage> chatmessages = [
    new ChatMessage
{
    Role = ChatRole.System
}];
client.GetStreamingResponseAsync()