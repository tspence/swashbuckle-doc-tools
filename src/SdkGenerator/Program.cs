using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
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
using Semver;

namespace SdkGenerator;

public static class Program
{
    [Verb("compare", HelpText = "Compare one swagger file to another")]
    private class CompareOptions
    {
        [Option('o', "old", Required = true, HelpText = "Older swagger file")]
        public string OldFile { get; set; }
        
        [Option('n', "new", Required = true, HelpText = "Newer swagger file")]
        public string NewFile { get; set; }
    }
    
    private class BaseOptions
    {
        [Option('p', "project", Required = true, HelpText = "Specify a project file")]
        public string ProjectFile { get; set; }
        
        [Option('l', "log", HelpText = "Write errors to a log file on disk. If null, writes to stdout")]
        public string LogPath { get; set; }
    }

    [Verb("embedded", HelpText = "List embedded resources")]
    private class ListEmbeddedResourcesOptions
    {
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
        [Option('o', "old", Required = true, HelpText = "Path to the older version of the swagger file")]
        public string OldVersion { get; set; }
    }

    [Verb("complete-patch-notes", HelpText = "Create unified patch notes for all swagger files in a folder")]
    private class CompletePatchNotesOptions
    {
        [Option('f', "folder", Required = true, HelpText = "Path to the folder containing swagger files")]
        public string Folder { get; set; }
    }

    [Verb("get-patch-notes", HelpText = "Get patch notes in Markdown for the current build")]
    private class GetPatchNotesOptions : BaseOptions
    {
    }

    [Verb("get-release-name", HelpText = "Get the release name for the current build")]
    private class GetReleaseNameOptions : BaseOptions
    {
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
        await parsed.WithParsedAsync<ListEmbeddedResourcesOptions>(ListEmbeddedResourcesTask);
        await parsed.WithParsedAsync<CreateOptions>(CreateTask);
        await parsed.WithParsedAsync<BuildOptions>(BuildTask);
        await parsed.WithParsedAsync<DiffOptions>(DiffTask);
        await parsed.WithParsedAsync<CompareOptions>(CompareTask);
        await parsed.WithParsedAsync<CompletePatchNotesOptions>(CompletePatchNotesTask);
        await parsed.WithParsedAsync<GetPatchNotesOptions>(GetPatchNotesTask);
        await parsed.WithParsedAsync<GetReleaseNameOptions>(GetReleaseNameTask);
        await parsed.WithNotParsedAsync(HandleErrors);
    }

    private static async Task GetReleaseNameTask(GetReleaseNameOptions options)
    {
        var rootContext = await GeneratorContext.FromFile(options.ProjectFile, null);
        var allSwaggerFiles = Directory.GetFiles(rootContext.MakePath(rootContext.Project.SwaggerSchemaFolder)).ToList();
        if (allSwaggerFiles.Count < 2)
        {
            return;
        }
        allSwaggerFiles.Sort(new SwaggerFileSemVerSorter());
        var currentFile = allSwaggerFiles[0];
        Console.WriteLine($"Release {Path.GetFileNameWithoutExtension(currentFile.Substring(currentFile.LastIndexOf('-') + 1))}");
    }

    private static async Task GetPatchNotesTask(GetPatchNotesOptions options)
    {
        // Scan the folder for patch notes files
        var rootContext = await GeneratorContext.FromFile(options.ProjectFile, null);
        
        // List all files in the swagger folder
        var allSwaggerFiles = Directory.GetFiles(rootContext.MakePath(rootContext.Project.SwaggerSchemaFolder)).ToList();
        if (allSwaggerFiles.Count < 2)
        {
            return;
        }
        allSwaggerFiles.Sort(new SwaggerFileSemVerSorter());
        var currentFile = allSwaggerFiles[0];
        var mostRecentFile = allSwaggerFiles[1];
        
        // Load in the current and previous files
        var prevContext = await GeneratorContext.FromSwaggerFileOnDisk(mostRecentFile, null);
        var currentContext = await GeneratorContext.FromSwaggerFileOnDisk(currentFile, null);
        var patchNotes = PatchNotesGenerator.Compare(prevContext, currentContext);
        Console.WriteLine(patchNotes.ToSummaryMarkdown());
    }

    private static Task ListEmbeddedResourcesTask(ListEmbeddedResourcesOptions arg)
    {
        var assembly = typeof(Program).GetTypeInfo().Assembly;
        Console.WriteLine("Embedded resources within this assembly:");
        foreach (var name in assembly.GetManifestResourceNames())
        {
            Console.WriteLine($"* {name}");
        }

        return Task.CompletedTask;
    }

    private static async Task HandleErrors(IEnumerable<Error> errors)
    {
        await Task.CompletedTask;
        var errList = errors.ToList();
        Console.WriteLine($"Found {errList.Count} errors.");
    }

    private static async Task CompletePatchNotesTask(CompletePatchNotesOptions arg)
    {
        // Collect all swagger files from the folder
        var versions = new List<GeneratorContext>();
        foreach (var file in Directory.GetFiles(arg.Folder))
        {
            if (file.EndsWith(".json"))
            {
                try
                {
                    Console.WriteLine($"Processing {file}...");
                    var newContext = await GeneratorContext.FromSwaggerFileOnDisk(file, null);
                    versions.Add(newContext);
                }
                catch
                {
                    Console.WriteLine($"Unable to parse {file} - not a swagger file?");
                }
            }
        }
        
        // Sanity test
        if (versions.Count < 2)
        {
            Console.WriteLine($"Only {versions.Count} swagger files in the folder.  Cannot generate patch notes.");
            return;
        }
        
        // Sort them based on their version numbers
        versions.Sort(new ContextSorter());
        for (int i = 1; i < versions.Count; i++)
        {
            var diffs = PatchNotesGenerator.Compare(versions[i - 1],versions[i]);
            Console.WriteLine(diffs.ToSummaryMarkdown());
        }
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

    private static async Task CompareTask(CompareOptions options)
    {
        // Load the current state of the project
        var oldContext = await GeneratorContext.FromSwaggerFileOnDisk(options.OldFile, null);
        var newContext = await GeneratorContext.FromSwaggerFileOnDisk(options.NewFile, null);
        Console.WriteLine($"Comparing {oldContext.Version4} against old version {newContext.Version4}");
        
        // Load the previous version of the swagger file from disk, and compare it
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
                await MarkdownGenerator.UploadSchemas(context, context.Project.Readme.Format ?? "list");
                Console.WriteLine("Uploaded to Readme.");
            }
            
            if (context.Project?.GenerateMarkdownFiles == true)
            {
                Console.WriteLine($"Writing documentation to {context.Project.SwaggerSchemaFolder}...");
                await MarkdownGenerator.WriteMarkdownFiles(context, context.Project.Readme?.Format ?? "list");
                Console.WriteLine("Finished writing documentation files.");
            }
            else
            {
                Console.WriteLine("To output documentation files in markdown, set GenerateMarkdownFiles to TRUE and specify a swagger folder.");
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