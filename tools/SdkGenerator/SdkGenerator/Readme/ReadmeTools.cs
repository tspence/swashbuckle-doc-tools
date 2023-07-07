using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RestSharp;
using SdkGenerator.Project;

namespace SdkGenerator.Readme;

public class ReadmeTools
{
    
    private static async Task<RestResponse> CallReadme(GeneratorContext context, string resource, Method method, string body, List<Tuple<string, string>> extraHeaders)
    {
        var client = new RestClient("https://dash.readme.com");
        var request = new RestRequest(resource, method);
        request.AddHeader("Accept", "application/json");
        request.AddHeader("Authorization", $"Basic {context.Project.Readme.ApiKey}");
        foreach (var item in extraHeaders ?? Enumerable.Empty<Tuple<string, string>>())
        {
            request.AddHeader(item.Item1, item.Item2);
        }
        if (context.Project.Readme.ReadmeVersionCode != null)
        {
            request.AddHeader("x-readme-version", context.Project.Readme.ReadmeVersionCode);
        }

        if (body != null)
        {
            request.AddHeader("Content-Type", "application/json");
            request.AddParameter("application/json", body, ParameterType.RequestBody);
        }

        return await client.ExecuteAsync(request);
    }

    private class ReadmeDocModel
    {
        [JsonProperty("hidden")]
        public bool Hidden { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("body")]
        public string Body { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("order")]
        public int Order { get; set; }
    }

    public class ReadmeCategoryModel
    {
        [JsonProperty("title")]
        public string Title { get; set; }
        
        [JsonProperty("_id")]
        public string Id { get; set; }
        
        [JsonProperty("type")]
        public string Type { get; set; }
    }

    public static async Task<List<ReadmeCategoryModel>> GetCategories(GeneratorContext context)
    {
        var results = new List<ReadmeCategoryModel>();
        int page = 1;
        int perPage = 100;

        while (true)
        {
            var resource = $"/api/v1/categories?page={page}&perPage={perPage}";
            var response = await CallReadme(context, resource, Method.Get, null, null);
            if (response.IsSuccessful)
            {
                // Preserve the "hidden" status - only a human being can approve the doc and make it visible
                var content = response.Content;
                var categories = JsonConvert.DeserializeObject<List<ReadmeCategoryModel>>(content);
                if (categories.Count == 0) break;
                results.AddRange(categories);
            }
            else break;
            page = page + 1;
        }

        return results;
    }

    public static async Task UploadGuideToReadme(GeneratorContext context, string categoryId, string schemaName, int order, string markdown)
    {
        var docName = $"/api/v1/docs/{schemaName.ToLower()}";
        var doc = new ReadmeDocModel
        {
            Hidden = false,
            Order = order,
            Title = schemaName,
            Body = markdown,
            Category = categoryId,
        };

        // Check to see if the model exists
        var modelExists = await CallReadme(context, docName, Method.Get, null, null);
        if (modelExists.IsSuccessful)
        {
            // Preserve the "hidden" status - only a human being can approve the doc and make it visible
            var existingDoc = JsonConvert.DeserializeObject<ReadmeDocModel>(modelExists.Content);
            var result = await CallReadme(context, docName, Method.Put, JsonConvert.SerializeObject(doc), null);
            context.Log($"Updated {schemaName}: {result.StatusCode}");
        }
        else
        {
            var result = await CallReadme(context, "/api/v1/docs", Method.Post, JsonConvert.SerializeObject(doc), null);
            context.Log($"Created {schemaName}: {result.StatusCode}");
        }
    }

    public static async Task<ReadmeCategoryModel> CreateCategory(GeneratorContext context, string readmeModelCategory)
    {
        var newCategory = new ReadmeCategoryModel()
        {
            Title = readmeModelCategory,
            Type = "guide",
        };
        var resource = $"/api/v1/categories";
        var response = await CallReadme(context, resource, Method.Post, JsonConvert.SerializeObject(newCategory), null);
        if (response.IsSuccessful)
        {
            // Preserve the "hidden" status - only a human being can approve the doc and make it visible
            var created = JsonConvert.DeserializeObject<ReadmeCategoryModel>(response.Content);
            return created;
        }

        return null;
    }

    public static async Task<bool> UploadSwagger(GeneratorContext context)
    {
        var client = new RestClient();
        var request = new RestRequest($"https://dash.readme.com/api/v1/api-specification/{context.Project.Readme.ReadmeApiDefinitionId}")
        {
            AlwaysMultipartFormData = true,
            FormBoundary = "----------" + Guid.NewGuid()
        };
        request.AddHeader("Accept", "application/json");
        request.AddHeader("Authorization", $"Basic {context.Project.Readme.ApiKey}");
        if (context.Project.Readme.ReadmeVersionCode != null)
        {
            request.AddHeader("x-readme-version", context.Project.Readme.ReadmeVersionCode);
        }
        request.AddFile("spec", context.SwaggerJsonPath);

        var result =  await client.PutAsync(request);
        return result.IsSuccessful;
    }
}