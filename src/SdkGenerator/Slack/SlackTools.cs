using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace SdkGenerator.Slack;

public static class SlackTools
{
    public static async Task<bool> SendMarkdownToSlack(string slackEndpoint, string markdown)
    {
        using var httpClient = new HttpClient();
        using var request = new HttpRequestMessage(new HttpMethod("POST"), slackEndpoint);

        request.Content = new StringContent($"{{\"text\":\"{markdown}\"}}");
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine("Error sending markdown to Slack:");
            Console.WriteLine(await response.Content.ReadAsStringAsync());
        }
        return response.IsSuccessStatusCode;
    }
}