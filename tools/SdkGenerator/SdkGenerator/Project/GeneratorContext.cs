using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
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

    private GeneratorContext()
    {
    }

    public void Log(string message)
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

    public void Dispose()
    {
        ErrorStream?.Dispose();
    }

    public static async Task<GeneratorContext> FromFile(string filename)
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
        };
        if (project.SwaggerSchemaFolder != null)
        {
            Directory.CreateDirectory(project.SwaggerSchemaFolder);
        }

        return context;
    }
}