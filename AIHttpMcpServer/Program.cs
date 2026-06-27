var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer(options =>
{
    options.ServerInfo= new  ModelContextProtocol.Protocol.Implementation
    {
        Name = "AIHttpMcpServer",
        Description = "A simple AI HTTP MCP server.",
        Version = "1.0.0"
    };
})
    .WithHttpTransport()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

//app.UseMiddleware<McpAuthenticationMiddleware>
//app.UseAuthorization();

app.MapGet("/", () => "Hello World!");

app.MapMcp("/mcp");

app.Run();
