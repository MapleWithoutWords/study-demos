using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // 从环境变量读取 API Key（也可以直接在代码中设置，但推荐使用环境变量）
        var apiKey = Environment.GetEnvironmentVariable("XIAOMI_API_KEY") ?? "YOUR_API_KEY";

        // TODO: 替换为小米模型的实际 HTTP API 端点
        var endpoint = "https://api.example.mi.com/v1/models/your-model/invoke";

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // 构建请求体（根据小米模型 API 要求调整字段名和结构）
        var requestBody = new
        {
            // 如果 API 使用 "prompt" 或 "input"，请相应修改
            prompt = "请用中文简短回答：介绍一下HTTP请求的基本概念。",
            // 示例可选参数
            max_tokens = 512,
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var resp = await http.PostAsync(endpoint, content);
            var respText = await resp.Content.ReadAsStringAsync();

            if (resp.IsSuccessStatusCode)
            {
                Console.WriteLine("Response:");
                Console.WriteLine(respText);
                return 0;
            }
            else
            {
                Console.WriteLine($"Request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                Console.WriteLine(respText);
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error while calling model API:");
            Console.WriteLine(ex.Message);
            return 2;
        }
    }
}
