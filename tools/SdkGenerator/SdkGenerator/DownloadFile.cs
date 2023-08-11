using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JsonDiffPatchDotNet;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SdkGenerator.Diff;
using SdkGenerator.Project;
using SdkGenerator.Schema;
using Semver;

namespace SdkGenerator;

public static class DownloadFile
{
    private static readonly HttpClient HttpClient = new();

    /// <summary>
    /// Download the swagger file
    /// </summary>
    /// <param name="project"></param>
    /// <param name="semver2"></param>
    /// <returns></returns>
    private static async Task<string> DownloadSwagger(ProjectSchema project, string semver2)
    {
        // Downloads json as a string to compare
        var response = await HttpClient.GetAsync(project.SwaggerUrl);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to retrieve Swagger file from {project.SwaggerUrl} - {response.StatusCode}");
            return string.Empty;
        }
        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json))
        {
            Console.WriteLine($"Failed to retrieve Swagger file from {project.SwaggerUrl} - content is empty");
            return string.Empty;
        }

        // Cleanup the JSON text
        return FixupSwagger(project, json, semver2);
    }

    /// <summary>
    /// Compare two swagger files and produce a difference
    /// </summary>
    /// <param name="oldSwagger"></param>
    /// <param name="newSwagger"></param>
    /// <returns></returns>
    public static string CompareSwagger(string oldSwagger, string newSwagger)
    {
        var jdp = new JsonDiffPatch();
        JToken diffResult = jdp.Diff(oldSwagger, newSwagger);
        return diffResult.ToString();
    }

    /// <summary>
    /// The version number is year.month.build (EX. 21.0.629)
    /// In general semver is supposed to be three digits, but some have four, so let's make all possibilities
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    private static async Task<string> FindVersionNumber(GeneratorContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Project.VersionNumberUrl) ||
            string.IsNullOrWhiteSpace(context.Project.VersionNumberRegex))
        {
            return "1.0.0.0";
        }

        // Attempt to retrieve this page and scan for the version number
        try
        {
            var contents = await HttpClient.GetStringAsync(context.Project.VersionNumberUrl);
            var r = new Regex(context.Project.VersionNumberRegex);
            var match = r.Match(contents);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        catch (Exception ex)
        {
            context.LogError($"Failed to load {context.Project.VersionNumberUrl}: {ex.Message}");
        }

        return "1.0.0.0";
    }

    /// <summary>
    /// Cleanup an existing swagger JSON
    /// </summary>
    /// <param name="project"></param>
    /// <param name="swagger"></param>
    /// <param name="semver2"></param>
    /// <returns></returns>
    private static string FixupSwagger(ProjectSchema project, string swagger, string semver2)
    {
        var jObject = JObject.Parse(swagger);

        // Give us a better version number
        jObject["info"]["title"] = project.ProjectName;
        jObject["info"]["version"] = semver2;

        // Erase the list of server URLs and replace with ones from the project file
        if (project.Environments?.Length > 0)
        {
            var servers = new JArray();
            foreach (var env in project.Environments)
            {
                var server = new JObject();
                server.Add("url", env.Url);
                servers.Add(server);
            }

            jObject.Add("servers", servers);
        }

        // Remove OAuth2 security definition - it's just for Swagger UI
        jObject["components"]!["securitySchemes"]!["oauth2"]!.Parent!.Remove();

        // Add links to the document data definitions
        if (project.Readme != null)
        {
            foreach (var endpoint in jObject["paths"])
            {
                foreach (var path in endpoint.Children())
                {
                    // Does this have a "Success" response?
                    foreach (var method in path.Values())
                    {
                        if (method is JObject methodObj)
                        {
                            var token =
                                methodObj.SelectToken("responses.200.content.['application/json'].schema.$ref") ??
                                methodObj.SelectToken("responses.201.content.['application/json'].schema.$ref") ??
                                methodObj.SelectToken(
                                    "responses.200.content.['application/json'].schema.items.$ref") ??
                                methodObj.SelectToken(
                                    "responses.201.content.['application/json'].schema.items.$ref");
                            if (token != null)
                            {
                                var cleanModelName = token.ToString();
                                cleanModelName = cleanModelName.Substring(cleanModelName.LastIndexOf('/') + 1);
                                foreach (var genericName in project.GenericSuffixes ?? Enumerable.Empty<string>())
                                {
                                    cleanModelName = cleanModelName.Replace(genericName, "");
                                }

                                // Special case for how Swashbuckle handles arrays
                                if (cleanModelName.EndsWith("List"))
                                {
                                    cleanModelName = cleanModelName[..^4];
                                }
                                if (methodObj.SelectToken("description") is JValue desc)
                                {
                                    desc.Value = desc.Value?.ToString().ReflowMarkdown() + "\r\n\r\n" +
                                                 $"### Data Definition\r\n\r\nSee [{cleanModelName}](../docs/{cleanModelName.ToLower()}) for the complete data definition.";
                                }
                            }
                        }
                    }
                }
            }
        }

        return JsonConvert.SerializeObject(jObject, Formatting.Indented);
    }

    /// <summary>
    /// Export data definitions to their own markdown files
    /// </summary>
    /// <param name="context">The SDK generator context</param>
    public static ApiSchema GatherSchemas(GeneratorContext context)
    {
        // Gather schemas from the file
        using var doc = JsonDocument.Parse(context.SwaggerJson);

        // Collect all the schemas / data models
        var schemaList = new List<SchemaItem>();
        var components = doc.RootElement.GetProperty("components");
        var schemas = components.GetProperty("schemas");
        foreach (var schema in schemas.EnumerateObject())
        {
            var item = SchemaFactory.MakeSchema(context, schema);
            if (item != null)
            {
                schemaList.Add(item);
            }
        }

        schemaList.Add(new()
        {
            Name = "ErrorResult",
            DescriptionMarkdown = "Represents a failed API request.",
            Fields = new()
            {
                new()
                {
                    Name = "type",
                    DescriptionMarkdown = "A description of the type of error that occurred.",
                    DataType = "string",
                    Nullable = false,
                },
                new()
                {
                    Name = "title",
                    DescriptionMarkdown = "A short title describing the error.",
                    DataType = "string",
                    Nullable = false,
                },
                new()
                {
                    Name = "status",
                    DescriptionMarkdown = "If an error code is applicable, this contains an error number.",
                    DataType = "int32",
                    Nullable = false,
                },
                new()
                {
                    Name = "detail",
                    DescriptionMarkdown = "If detailed information about this error is available, this value contains more information.",
                    DataType = "string",
                    Nullable = false,
                },
                new()
                {
                    Name = "instance",
                    DescriptionMarkdown = "If this error corresponds to a specific instance or object, this field indicates which one.",
                    DataType = "string",
                    Nullable = false,
                },
                new()
                {
                    Name = "content",
                    DescriptionMarkdown = "The full content of the HTTP response.",
                    DataType = "string",
                    Nullable = false,
                }
            },
        });

        // Collect all the APIs
        var endpointList = new List<EndpointItem>();
        var paths = doc.RootElement.GetProperty("paths");
        foreach (var endpoint in paths.EnumerateObject())
        {
            var item = SchemaFactory.MakeEndpoint(context, endpoint);
            if (item != null)
            {
                endpointList.AddRange(item);
            }
        }

        // Convert into an API schema
        return new ApiSchema
        {
            Semver2 = context.Version2,
            Semver3 = context.Version3,
            Semver4 = context.Version4,
            Schemas = schemaList.OrderBy(s => s.Name).ToList(),
            Endpoints = endpointList,
            Categories = (from e in endpointList where !e.Deprecated orderby e.Category select e.Category).Distinct().ToList()
        };
    }

    public static async Task<ApiSchema> GenerateApi(GeneratorContext context)
    {
        context.Version4 = await FindVersionNumber(context);

        // If we couldn't download the version number, don't try generating anything
        if (context.Version4 == "1.0.0.0")
        {
            context.LogError("Unable to find version number using regex");
            return null;
        }

        var segments = context.Version4.Split(".");
        context.Version2 = $"{segments[0]}.{segments[1]}";
        context.Version3 = $"{segments[0]}.{segments[1]}.{segments[2]}";
        context.OfficialVersion = context.Project.SemverMode == 3 ? context.Version3 : context.Version2;
        context.SwaggerJson = await DownloadSwagger(context.Project, context.OfficialVersion);

        // Save to the swagger folder
        if (Directory.Exists(context.Project.SwaggerSchemaFolder))
        {
            context.SwaggerJsonPath = Path.Combine(context.Project.SwaggerSchemaFolder, $"swagger-{context.OfficialVersion}.json");
            await File.WriteAllTextAsync(context.SwaggerJsonPath, context.SwaggerJson);
        }

        // Export data definitions to markdown files
        return GatherSchemas(context);
    }

    public static async Task<SwaggerDiff> GeneratePatchNotes(GeneratorContext context)
    {
        // List all files in the swagger folder
        string mostRecentFile = null;
        SemVersion mostRecentVersion = null;
        foreach (var file in Directory.GetFiles(context.Project.SwaggerSchemaFolder))
        {
            // Determine semantic version of the file
            var fileName = Path.GetFileNameWithoutExtension(file);
            var dashPos = fileName.IndexOf('-'); 
            if (dashPos > 0)
            {
                var versionString = fileName[(dashPos+1)..];
                if (versionString != context.OfficialVersion)
                {
                    var version = SemVersion.Parse(versionString, SemVersionStyles.Any);
                    if (mostRecentVersion == null || mostRecentVersion.ComparePrecedenceTo(version) < 0)
                    {
                        mostRecentVersion = version;
                        mostRecentFile = file;
                    }
                }
            }
        }
        
        // If no files found, can't determine differences
        if (mostRecentFile == null)
        {
            return new SwaggerDiff();
        }
        
        // Compare these two files
        var oldContext = await GeneratorContext.FromSwaggerFileOnDisk(mostRecentVersion.ToString(), context.LogPath);
        oldContext.Api = DownloadFile.GatherSchemas(oldContext);
        return PatchNotesGenerator.Compare(oldContext, context);
    }
}