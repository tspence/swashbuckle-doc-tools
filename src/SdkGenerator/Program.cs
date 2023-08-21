using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CommandLine;
using Newtonsoft.Json;
using SdkGenerator.Diff;
using SdkGenerator.Languages;
using SdkGenerator.Markdown;
using SdkGenerator.Project;

namespace SdkGenerator;

public static class Program
{
    private class BaseOptions
    {
        [Option('p', "project", Required = true, HelpText = "Specify a project file")]
        public string ProjectFile { get; set; }
        
        [Option('l', "log", HelpText = "Write errors to a log file on disk. If null, writes to stdout")]
        public string LogPath { get; set; }
    }

    [Verb("build", HelpText = "Build the SDK")]
    private class BuildOptions : BaseOptions
    {
        [Option('t', "template", HelpText = "To build only one single template, specify this option")]
        public string TemplateName { get; set; }
    }
    
    [Verb("create", HelpText = "Create a new template file for a new SDK")]
    private class CreateOptions : BaseOptions
    {
    }

    
    [Verb("diff", HelpText = "Report on differences from previous swagger file")]
    private class DiffOptions : BaseOptions
    {
        [Option('o', "old", HelpText = "Path to the older version of the swagger file")]
        public string OldVersion { get; set; }
    }

    private static Type[] LoadVerbs()
    {
        return Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.GetCustomAttribute<VerbAttribute>() != null).ToArray();
    }

    public static async Task Main(string[] args)
    {
        var types = LoadVerbs();
        var parsed = Parser.Default.ParseArguments(args, types);
        await parsed.WithParsedAsync<CreateOptions>(CreateTask);
        await parsed.WithParsedAsync<BuildOptions>(BuildTask);
        await parsed.WithParsedAsync<DiffOptions>(DiffTask);
        await parsed.WithNotParsedAsync(HandleErrors);
    }

    private static async Task HandleErrors(IEnumerable<Error> errors)
    {
        await Task.CompletedTask;
        var errList = errors.ToList();
        Console.WriteLine($"Found {errList.Count} errors.");
    }

    private static async Task DiffTask(DiffOptions options)
    {
        // Load the current state of the project
        var newContext = await GeneratorContext.FromFile(options.ProjectFile, options.LogPath);
        newContext.Api = await DownloadFile.GenerateApi(newContext);
        Console.WriteLine($"Comparing {newContext.Project.SwaggerUrl} against old version {options.OldVersion}");
        
        // Load the previous version of the swagger file from disk, and compare it
        var oldContext = await GeneratorContext.FromSwaggerFileOnDisk(options.OldVersion, options.LogPath);
        var diffs = PatchNotesGenerator.Compare(oldContext, newContext);
        
        // Print out human readable description
        Console.WriteLine(diffs.ToSummaryMarkdown());
    }

    private static async Task CreateTask(CreateOptions options)
    {
        var newSchema = new ProjectSchema();
        newSchema.Environments = new[] { new EnvironmentSchema() };
        newSchema.Readme = new ReadmeSiteSchema();
        newSchema.Csharp = new LanguageSchema();
        newSchema.Java = new LanguageSchema();
        newSchema.Python = new LanguageSchema();
        newSchema.Ruby = new LanguageSchema();
        newSchema.Typescript = new LanguageSchema();
        await File.WriteAllTextAsync(options.ProjectFile, JsonConvert.SerializeObject(newSchema, Formatting.Indented));
        Console.WriteLine($"Created new project file {options.ProjectFile}");
    }

    private static async Task BuildTask(BuildOptions options)
    {
        var context = await GeneratorContext.FromFile(options.ProjectFile, options.LogPath);

        // Fetch the environment and version number
        Console.WriteLine($"Retrieving swagger file from {context.Project.SwaggerUrl}");
        context.Api = await DownloadFile.GenerateApi(context);
        if (context.Api == null)
        {
            Console.WriteLine("Unable to retrieve API and version number successfully.");
            return;
        }
        Console.WriteLine($"Retrieved swagger file. Version: {context.Version4}");
        
        // Generate patch notes
        context.PatchNotes = await DownloadFile.GeneratePatchNotes(context);
        
        // Let's do some software development kits, if selected
        bool anyExported = false;
        foreach (var t in typeof(Program).Assembly.GetTypes())
        {
            if (t.GetInterfaces().Contains(typeof(ILanguageSdk)))
            {
                var obj = (ILanguageSdk)Activator.CreateInstance(t);
                if (obj != null && (options.TemplateName is null || string.Equals(options.TemplateName, obj.LanguageName(), StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"Exporting {obj.LanguageName()}");
                    await obj.Export(context);
                    anyExported = true;
                }
            }
        }

        // Where do we want to send the documentation? 
        if (options.TemplateName == null || options.TemplateName == "readme")
        {
            anyExported = true;
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
                Console.WriteLine(
                    "To output documentation files, specify either a Readme API key or a swagger folder.");
            }
        }

        if (!anyExported)
        {
            Console.WriteLine($"No template '{options.TemplateName}' found.");
        }
        else
        {
            Console.WriteLine("Done!");
        }
    }
}