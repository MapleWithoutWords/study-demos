using Microsoft.Extensions.AI;
using System.ClientModel;
using System.Text.Json;

IChatClient client =
    new OpenAI.Chat.ChatClient("glm-5.1", new ApiKeyCredential("b1cc8a05c01a4dea99638a7111c14797.ECh52AZuMglnWiag"), new OpenAI.OpenAIClientOptions { Endpoint = new Uri("https://open.bigmodel.cn/api/paas/v4/") })
    .AsIChatClient();

