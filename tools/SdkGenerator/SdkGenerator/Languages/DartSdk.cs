using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator.Languages;

public class DartSdk : ILanguageSdk
{   
    public async Task Export(GeneratorContext context)
    {
        if (context.Project.Dart == null)
        {
            return;
        }
        
        await ExportSchemas(context);
        await ExportEndpoints(context);

        await ScribanFunctions.ExecuteTemplate(context, 
            Path.Combine(".", "templates", "dart", "ApiInterface.dart.scriban"),
            Path.Combine(context.Project.Dart.Folder, context.Project.Dart.ClassName + ".dart"));
        await ScribanFunctions.ExecuteTemplate(context, 
            Path.Combine(".", "templates", "dart", "ApiClient.dart.scriban"),
            Path.Combine(context.Project.Dart.Folder, context.Project.Dart.ClassName + "Impl.dart"));
    }

    private async Task ExportEndpoints(GeneratorContext context)
    {
        await Task.CompletedTask;
    }

    private async Task ExportSchemas(GeneratorContext context)
    {
        var modelsDir = Path.Combine(context.Project.Dart.Folder, "models");
        Directory.CreateDirectory(modelsDir);
        foreach (var modelFile in Directory.EnumerateFiles(modelsDir, "*.dart"))
        {
            File.Delete(modelFile);
        }

        foreach (var item in context.Api.Schemas)
        {
            if (item.Fields != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine(FileHeader(context.Project));
                sb.AppendLine();

                // Add class and header
                sb.AppendLine();
                sb.Append(item.DescriptionMarkdown.ToDartDoc(0));
                sb.AppendLine($"class {item.Name}");
                sb.AppendLine("{");

                // First do the fields
                foreach (var field in item.Fields)
                {
                    if (!field.Deprecated)
                    {
                        sb.AppendLine();
                        sb.Append(field.DescriptionMarkdown.ToDartDoc(4));
                        sb.AppendLine(
                            $"    {FixupType(context, field.DataType, field.IsArray, field.Nullable)} get {field.Name.ToCamelCase()} => {GetDefaultValue(context, field)};");
                    }
                }

                sb.AppendLine("};");
                var classPath = Path.Combine(modelsDir, item.Name + ".dart");
                await File.WriteAllTextAsync(classPath, sb.ToString());
            }
        }    
    }

    private string GetDefaultValue(GeneratorContext context, SchemaField field)
    {
        if (field.Nullable)
        {
            return "null";
        }

        if (field.IsArray)
        {
            return "[]";
        }

        switch (field.DataType)
        {
            case "string":
            case "uuid":
            case "date-time":
            case "date":
            case "uri":
            case "Uri":
            case "tel":
            case "email":
                return "\"\"";
            case "int32":
            case "double":
            case "int64":
            case "float":
            case "integer":
                return "0";
            case "object":
                return "null";
            case "boolean":
                return "false";
            case "File":
            case "binary":
                return "[]";
        }

        return "null";
    }


    private string FixupType(GeneratorContext context, string typeName, bool isArray, bool isNullable)
    {
        var s = typeName;
        if (context.Api.IsEnum(typeName))
        {
            s = context.Api.FindSchema(typeName).EnumType;
        }

        switch (s)
        {
            case "string":
            case "uuid":
            case "date-time":
            case "date":
            case "uri":
            case "Uri":
            case "tel":
            case "email":
                s = "String";
                break;
            case "int32":
            case "integer":
                s = "Integer";
                break;
            case "double":
                s = "Double";
                break;
            case "int64":
                s = "Long";
                break;
            case "float":
                s = "Float";
                break;
            case "object":
                s = "Object";
                break;
            case "boolean":
                s = "Boolean";
                break;
            case "File":
            case "binary":
                s = "byte[]";
                break;
            case "TestTimeoutException":
                s = "ErrorResult";
                break;
        }

        if (isArray)
        {
            s = $"List<{s}>";
        }

        if (isNullable)
        {
            s = s + "?";
        }
        
        // Is this a generic class?
        foreach (var genericName in context.Project.GenericSuffixes ?? Enumerable.Empty<string>())
        {
            if (s.EndsWith(genericName))
            {
                s = $"{genericName}<{s[..^genericName.Length]}>";
            }
        }

        return s;
    }

    private string FileHeader(ProjectSchema project)
    {
        return "///\n"
               + $"/// {project.ProjectName} for Dart\n"
               + "///\n"
               + $"/// (c) 2021-{DateTime.UtcNow.Year} {project.CopyrightHolder}\n"
               + "///\n"
               + "/// For the full copyright and license information, please view the LICENSE\n"
               + "/// file that was distributed with this source code.\n"
               + "///\n"
               + $"/// @author     {project.AuthorName} <{project.AuthorEmail}>\n"
               + $"/// @copyright  2021-{DateTime.UtcNow.Year} {project.CopyrightHolder}\n"
               + $"/// @link       {project.Dart.GithubUrl}\n"
               + "///\n\n";    }

    public string LanguageName()
    {
        return "Dart";
    }
}