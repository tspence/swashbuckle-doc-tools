using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SdkGenerator.Slack;

public static class SlackTools
{
    public static async Task<bool> SendMarkdownToSlack(string slackEndpoint, string markdown)
    {
        using var httpClient = new HttpClient();
        using var request = new HttpRequestMessage(new HttpMethod("POST"), slackEndpoint);

        // Fixup Markdown text for Slack formatting
        var sendMarkdown = ("\n" + markdown)
            .Replace("\r", "") // No need for Windows line endings
            .Replace("\n", "\\n")
            .Replace("\\n* ", "\\n• "); // Slack apparently has no bullet points, so have to use emoji bullets
        
        // Even worse, Slack uses their own crappy formatting for Markdown links instead of the official Markdown
        // standard, so we need to fix with a regex
        while (true)
        {
            var match = Regex.Match(sendMarkdown, "\\[(.*?)\\]\\((.*?)\\)");
            if (!match.Success)
            {
                break;
            }
            
            // Group 1 is the text, group 2 is the URL
            // Slack style links in markdown are: <http://www.example.com|This message *is* a link>
            sendMarkdown = sendMarkdown.Replace(match.Captures[0].Value, $"<{match.Groups[2].Value}|{match.Groups[1].Value}>");
        }

        request.Content = new StringContent($"{{ \"text\": \"{sendMarkdown}\"}}");
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