using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SdkGenerator.Diff;
using SdkGenerator.Schema;

namespace SdkGenerator.Project;

public class GeneratorContext : IDisposable
{
    /// <summary>
    /// The folder where the project file lives - all paths should be relative to this
    /// </summary>
    public string ProjectFolder { get; set; } = string.Empty;

    public ProjectSchema Project { get; set; } = new();
    public ApiSchema Api { get; set; } = new();
    private StreamWriter? ErrorStream { get; set; }
    public string Version2 { get; set; } = string.Empty;
    public string Version3 { get; set; } = string.Empty;
    public string Version4 { get; set; } = string.Empty;
    public string OfficialVersion { get; set; } = string.Empty;
    public string SwaggerJson { get; set; } = string.Empty;
    public string SwaggerJsonPath { get; set; } = string.Empty;
    public string? LogPath { get; set; }
    public SwaggerDiff PatchNotes { get; set; } = null!;

    private GeneratorContext()
    {
    }

    public void LogError(string message)
    {
        if (!string.IsNullOrEmpty(LogPath))
        {
            Console.WriteLine("  " + message);
            
            if (ErrorStream == null && Project.SwaggerSchemaFolder != null)
            {
                ErrorStream = new StreamWriter(MakePath(Project.SwaggerSchemaFolder, "errors.log"));
            }

            if (ErrorStream != null)
            {
                ErrorStream.WriteLine(message);
            }
        }
    }

    public void Dispose()
    {
        ErrorStream?.Dispose();
    }
    
    public static GeneratorContext FromApiSchema(ApiSchema api, ProjectSchema project)
    {
        return new GeneratorContext()
        {
            Api = api,
            Project = project,
        };
    }

    public static async Task<GeneratorContext?> FromFile(string filename, string? logPath)
    {
        // Retrieve project
        if (!File.Exists(filename))
        {
            Console.WriteLine($"Project file could not be found: {filename}");
            return null;
        }

        // Parse the 
        var text = await File.ReadAllTextAsync(filename);
        var project = JsonConvert.DeserializeObject<ProjectSchema>(text);
        if (project is null)
        {
            Console.WriteLine("Could not parse project file");
            return null;
        }
        
        // Ensure the folder for collecting swagger files exists
        var context = new GeneratorContext()
        {
            Project = project,
            ProjectFolder = Path.GetDirectoryName(filename) ?? ".",
            Api = new ApiSchema(),
            LogPath = logPath ?? string.Empty,
        };
        if (project.SwaggerSchemaFolder != null)
        {
            Directory.CreateDirectory(project.SwaggerSchemaFolder);
        }

        return context;
    }

    public static async Task<GeneratorContext?> FromSwaggerFileOnDisk(GeneratorContext? context, string swaggerFilename, string? logPath)
    {
        // Retrieve project
        if (!File.Exists(swaggerFilename))
        {
            Console.WriteLine($"Swagger file could not be found: {swaggerFilename}");
            return null;
        }

        // Ensure the folder for collecting swagger files exists
        var swaggerJson = await File.ReadAllTextAsync(swaggerFilename);
        if (context == null)
        {
            context = new GeneratorContext()
            {
                Project = new ProjectSchema(),
                Api = new ApiSchema(),
                LogPath = logPath,
                SwaggerJson = swaggerJson,
                OfficialVersion = DownloadFile.GetVersion(swaggerJson)
            };
        }
        else
        {
            context.LogPath = logPath ?? context.LogPath;
            context.SwaggerJson = swaggerJson;
            context.OfficialVersion = DownloadFile.GetVersion(swaggerJson);
            context.Version4 = context.OfficialVersion;
            var segments = context.Version4.Split(".");
            context.Version2 = $"{segments[0]}.{segments[1]}";
            context.Version3 = $"{segments[0]}.{segments[1]}.{segments[2]}";
            Console.WriteLine($"Official version number is {context.OfficialVersion}");
        }
        context.Api = DownloadFile.GatherSchemas(context);
        return context;
    }

    public bool IsGenericSchema(string itemName)
    {
        return (Project.GenericSuffixes ?? Enumerable.Empty<string>()).Any(genericName => itemName.EndsWith(genericName));
    }

    public string RemoveGenericSchema(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return string.Empty; 
        }
        foreach (var genericName in Project.GenericSuffixes ?? Enumerable.Empty<string>())
        {
            if (genericName.Length > 0 && typeName.EndsWith(genericName))
            {
                typeName = RemoveGenericSchema(typeName[..^genericName.Length]);
            }
        }

        return typeName;
    }

    public string[] ExtractGenerics(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return []; 
        }

        var list = new List<string>();
        while (typeName.Length > 0)
        {
            bool anyFound = false;
            foreach (var genericName in Project.GenericSuffixes ?? Enumerable.Empty<string>())
            {
                if (genericName.Length > 0 && typeName.EndsWith(genericName))
                {
                    list.Add(genericName);
                    typeName = typeName[..^genericName.Length];
                    anyFound = true;
                    break;
                }
            }

            if (!anyFound)
            {
                list.Add(typeName);
                break;
            }            
        }

        return list.ToArray();
    }
    
    public string MakePath(params string?[] elements)
    {
        var filteredElements = new List<string>();
        foreach (var s in elements)
        {
            if (!string.IsNullOrWhiteSpace(s))
            {
                filteredElements.Add(s);
            }
        }
        filteredElements.Insert(0, ProjectFolder);
        return Path.Combine(filteredElements.ToArray());
    }

    public bool IsIgnoredEndpoint(string itemName, string path)
    {
        return Project.IgnoredEndpoints != null && (Project.IgnoredEndpoints.Contains(itemName, StringComparer.OrdinalIgnoreCase)
                || Project.IgnoredEndpoints.Contains(path, StringComparer.OrdinalIgnoreCase));
    }

    public bool IsIgnoredCategory(string cat)
    {
        return Project.IgnoredCategories != null && Project.IgnoredCategories.Contains(cat, StringComparer.OrdinalIgnoreCase);
    }
    
    public async Task<SwaggerDates> GetSwaggerDates()
    {
        return await SwaggerDates.FromDatesFile(Project.PatchNotes?.DatesFile); 
    }
}