using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AIHttpMcpServer;

[McpServerToolType]
public class TestMcpTool
{
    [McpServerTool]
    [Description("Search for a user by their username.")]
    public string SearchUser(string userName)
    {
        return $"Searching for user: {userName}";
    }

    [McpServerTool]
    [Description("Add a new user by their username.")]
    public string AddUser(string userName)
    {
        return $"Adding user: {userName}";
    }
}
