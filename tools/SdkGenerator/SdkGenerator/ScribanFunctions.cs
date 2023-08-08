using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator;

public static class ScribanFunctions
{
    
    public static async Task PatchOrTemplate(GeneratorContext context, string outputFilename, string templateName,
        string regex, string replacement)
    {
        if (File.Exists(outputFilename))
        {
            await PatchFile(context, outputFilename, regex, replacement);
        }
        else
        {
            await ExecuteTemplate(context, templateName, outputFilename);
        }
    }
        
    private static async Task PatchFile(GeneratorContext context, string filename, string regex, string replacement)
    {
        if (!File.Exists(filename))
        {
            context.LogError($"Unable to find file {filename}");
            return;
        }

        var text = await File.ReadAllTextAsync(filename);
        var match = Regex.Match(text, regex);
        if (match.Success)
        {
            var newText = text.Replace(match.ToString(), replacement);
            await File.WriteAllTextAsync(filename, newText);
        }
        else
        {
            context.LogError($"Failed to patch file {filename} - no match found.");
        }
    }

    public static async Task ExecuteTemplate(GeneratorContext context, string templateName, string outputFile)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile));
            var templateText = await File.ReadAllTextAsync(templateName);
            var template = Template.Parse(templateText);
            var scriptObject1 = new ScriptObject();
            scriptObject1.Import(typeof(Extensions));
            var templateContext = new TemplateContext();
            templateContext.PushGlobal(scriptObject1);
            templateContext.SetValue(new ScriptVariableGlobal("api"), context.Api);
            templateContext.SetValue(new ScriptVariableGlobal("project"), context.Project);
            var result = await template.RenderAsync(templateContext);
            await File.WriteAllTextAsync(outputFile, result);
        }
        catch (Exception e)
        {
            context.LogError($"Failed to execute template {templateName}: {e.Message}");
        }
    }
}