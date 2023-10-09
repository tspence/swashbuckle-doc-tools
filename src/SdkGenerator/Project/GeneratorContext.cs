using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SdkGenerator.Diff;
using SdkGenerator.Schema;

namespace SdkGenerator.Project;

public class GeneratorContext : IDisposable
{
    public ProjectSchema Project { get; set; }
    public ApiSchema Api { get; set; }
    private StreamWriter ErrorStream { get; set; }
    public string Version2 { get; set; }
    public string Version3 { get; set; }
    public string Version4 { get; set; }
    public string OfficialVersion { get; set; }
    public string SwaggerJson { get; set; }
    public string SwaggerJsonPath { get; set; }
    public string LogPath { get; set; }
    public SwaggerDiff PatchNotes { get; set; }

    private GeneratorContext()
    {
    }

    public void LogError(string message)
    {
        if (string.IsNullOrEmpty(LogPath))
        {
            Console.WriteLine("  " + message);
        }
        else
        {
            if (ErrorStream == null && Project.SwaggerSchemaFolder != null)
            {
                ErrorStream = new StreamWriter(Path.Combine(Project.SwaggerSchemaFolder, "errors.log"));
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

    public static async Task<GeneratorContext> FromFile(string filename, string logPath)
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
            Api = null,
            LogPath = logPath,
        };
        if (project.SwaggerSchemaFolder != null)
        {
            Directory.CreateDirectory(project.SwaggerSchemaFolder);
        }

        return context;
    }

    public static async Task<GeneratorContext> FromSwaggerFileOnDisk(string swaggerFilename, string logPath)
    {
        // Retrieve project
        if (!File.Exists(swaggerFilename))
        {
            Console.WriteLine($"Swagger file could not be found: {swaggerFilename}");
            return null;
        }

        // Ensure the folder for collecting swagger files exists
        var swaggerJson = await File.ReadAllTextAsync(swaggerFilename);
        var context = new GeneratorContext()
        {
            Project = new ProjectSchema(),
            Api = null,
            LogPath = logPath,
            SwaggerJson = swaggerJson,
            OfficialVersion = DownloadFile.GetVersion(swaggerJson)
        };
        context.Api = DownloadFile.GatherSchemas(context);
        return context;
    }

    public bool IsGenericSchema(string itemName)
    {
        return (Project.GenericSuffixes ?? Enumerable.Empty<string>()).Any(genericName => itemName.EndsWith(genericName));
    }

    public string RemoveGenericSchema(string typeName)
    {
        foreach (var genericName in Project.GenericSuffixes ?? Enumerable.Empty<string>())
        {
            if (typeName.EndsWith(genericName))
            {
                typeName = RemoveGenericSchema(typeName[..^genericName.Length]);
            }
        }

        return typeName;
    }

    public string ApplyGenerics(string typeName, string prefix, string suffix)
    {
        foreach (var genericName in Project.GenericSuffixes ?? Enumerable.Empty<string>())
        {
            if (typeName.EndsWith(genericName))
            {
                typeName = $"{genericName}<{ApplyGenerics(typeName[..^genericName.Length], prefix, suffix)}>";
            }
        }

        return typeName;
    }
}