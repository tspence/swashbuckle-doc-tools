using System;
using System.IO;
using System.Threading.Tasks;
using CommandLine;
using Newtonsoft.Json;
using SdkGenerator.Languages;
using SdkGenerator.Markdown;
using SdkGenerator.Project;

namespace SdkGenerator;

public static class Program
{
    private class Options
    {
        [Option('p', "Project", HelpText = "Specify a project file")]
        public string ProjectFile { get; set; }
        
        [Option("new", HelpText = "Create a new project file")]
        public string CreateNewProjectFile { get; set; }
    }

    public static async Task Main(string[] args)
    {
        await Parser.Default.ParseArguments<Options>(args)
            .WithParsedAsync(async o =>
            {
                // Did they want to create a new project file?
                if (o.CreateNewProjectFile != null)
                {
                    await CreateNewProject(o.CreateNewProjectFile);
                } else 

                if (o.ProjectFile != null)
                {
                    await GenerateProject(o.ProjectFile);
                }
                else
                {
                    Console.WriteLine("Please specify either --new, -p [file], or --help.");
                }
            });
    }

    private static async Task GenerateProject(string filename)
    {
        var context = await GeneratorContext.FromFile(filename);

        // Fetch the environment and version number
        Console.WriteLine($"Retrieving swagger file from {context.Project.SwaggerUrl}");
        context.Api = await DownloadFile.GenerateApi(context);
        if (context.Api == null)
        {
            Console.WriteLine("Unable to retrieve API and version number successfully.");
            return;
        }

        Console.WriteLine($"Retrieved swagger file. Version: {context.Version2}");

        // Let's do some software development kits, if selected
        await TypescriptSdk.Export(context);
        await CSharpSdk.Export(context);
        await JavaSdk.Export(context);
        await RubySdk.Export(context);
        await PythonSdk.Export(context);

        // Where do we want to send the documentation? 
        if (context.Project?.Readme?.ApiKey != null)
        {
            Console.WriteLine("Uploading to Readme...");
            await MarkdownGenerator.UploadSchemas(context, "list");
            Console.WriteLine("Uploaded to Readme.");
        }
        else if (context.Project?.SwaggerSchemaFolder != null)
        {
            Console.WriteLine($"Writing documentation to {context.Project.SwaggerSchemaFolder}...");
            await MarkdownGenerator.WriteMarkdownFiles(context, "list");
            Console.WriteLine("Finished writing documentation files.");
        }
        else
        {
            Console.WriteLine("To output documentation files, specify either a Readme API key or a swagger folder.");
        }

        Console.WriteLine("Done!");
    }

    private static async Task CreateNewProject(string name)
    {
        var newSchema = new ProjectSchema();
        newSchema.Environments = new[] { new EnvironmentSchema() };
        newSchema.Readme = new ReadmeSiteSchema();
        newSchema.Csharp = new LanguageSchema();
        newSchema.Java = new LanguageSchema();
        newSchema.Python = new LanguageSchema();
        newSchema.Ruby = new LanguageSchema();
        newSchema.Typescript = new LanguageSchema();
        await File.WriteAllTextAsync(name + ".json", JsonConvert.SerializeObject(newSchema, Formatting.Indented));
        Console.WriteLine($"Created new project file {name}.json");
    }
}