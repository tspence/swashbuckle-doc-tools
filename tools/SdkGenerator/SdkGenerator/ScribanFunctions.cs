using System;
using System.IO;
using System.Threading.Tasks;
using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator;

public static class ScribanFunctions
{
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
            var result = await template.RenderAsync(context);
            await File.WriteAllTextAsync(outputFile, result);
        }
        catch (Exception e)
        {
            context.LogError($"Failed to execute template {templateName}: {e.Message}");
        }
    }
}