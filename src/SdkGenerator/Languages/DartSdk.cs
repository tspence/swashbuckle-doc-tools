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
        var clientsDir = Path.Combine(context.Project.Dart.Folder, "clients");
        Directory.CreateDirectory(clientsDir);
        foreach (var clientsFile in Directory.EnumerateFiles(clientsDir, "*.dart"))
        {
            File.Delete(clientsFile);
        }

        // Gather a list of unique categories
        foreach (var cat in context.Api.Categories)
        {
            var sb = new StringBuilder();

            // Construct header
            sb.AppendLine(FileHeader(context.Project));

            sb.AppendLine($"/// Contains all methods related to {cat}");
            sb.AppendLine($"class {cat}Client");
            sb.AppendLine("{");
            sb.AppendLine($"  final {context.Project.Dart.ClassName} _client;");
            sb.AppendLine();
            sb.AppendLine($"  /// Constructor for the {cat} API collection");
            sb.AppendLine($"  {cat}Client({context.Project.Dart.ClassName} client) : _client = client;");
            sb.AppendLine();

            // Run through all APIs
            foreach (var endpoint in context.Api.Endpoints)
            {
                if (endpoint.Category == cat && !endpoint.Deprecated)
                {
                    sb.AppendLine();
                    sb.Append(endpoint.DescriptionMarkdown.ToDartDoc(2,
                        endpoint.Parameters));

                    // Figure out the parameter list
                    var paramListStr = string.Join(", ", from p in endpoint.Parameters
                        select $"{FixupType(context, p.DataType, p.IsArray, !p.Required)} {FixupVariableName(p.Name)}");

                    // What is our return type?
                    var returnType = FixupType(context, endpoint.ReturnDataType.DataType,
                        endpoint.ReturnDataType.IsArray, false);

                    // Do we have query or body parameters?
                    var hasQueryParams = (from p in endpoint.Parameters where p.Location == "query" select p).Any();
                    var hasBodyParams = (from p in endpoint.Parameters where p.Location == "body" select p).Any();
                    
                    // Write the method
                    var cleansedPath  = endpoint.Path.Replace("{", "${");
                    if (hasQueryParams)
                    {
                        cleansedPath += "?${queryString}";
                    }
                    sb.AppendLine(
                        $"  Future<{returnType}> {endpoint.Name.ToCamelCase()}({paramListStr}) async {{");
                    if (hasQueryParams)
                    {
                        sb.AppendLine($"    Map queryParameters = {{");
                        sb.Append(string.Join("",
                            from p in endpoint.Parameters
                            where p.Location == "query"
                            select $"      '{FixupStringLiteral(p.Name)}': {FixupVariableName(p.Name)},\n"));
                        sb.AppendLine($"    }};");
                        sb.AppendLine("    String queryString = Uri(queryParameters).query;");
                    }

                    if (hasBodyParams)
                    {
                        sb.AppendLine($"    return _client.{endpoint.Method.ToLower()}(\"{cleansedPath}\", body)");
                    }
                    else
                    {
                        sb.AppendLine($"    return _client.{endpoint.Method.ToLower()}(\"{cleansedPath}\")");
                    }
                    sb.AppendLine("      .then((value) {");
                    sb.AppendLine($"        return {context.Project.Dart.ResponseClass}.fromContent(value);");
                    sb.AppendLine("      });");
                    sb.AppendLine("  }");
                }
            }

            // Close out the namespace
            sb.AppendLine("}");

            // Write this category to a file
            var classPath = Path.Combine(clientsDir, $"{cat}Client.dart");
            await File.WriteAllTextAsync(classPath, sb.ToString());
        }
    }

    private string FixupStringLiteral(string literal)
    {
        StringBuilder sb = new StringBuilder();
        foreach (var c in literal)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('\\');
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string FixupVariableName(string incomingName)
    {
        StringBuilder sb = new StringBuilder();
        foreach (var c in incomingName)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }
        return sb.ToString();
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
        var s = context.Api.ReplaceEnumWithType(typeName);

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
        s = context.ApplyGenerics(s, "<", ">");

        return s;
    }

    private string FileHeader(ProjectSchema project)
    {
        return "///\n"
               + $"/// {project.ProjectName} for Dart\n"
               + "///\n"
               + $"/// (c) {project.ProjectStartYear}-{DateTime.UtcNow.Year} {project.CopyrightHolder}\n"
               + "///\n"
               + "/// For the full copyright and license information, please view the LICENSE\n"
               + "/// file that was distributed with this source code.\n"
               + "///\n"
               + $"/// @author     {project.AuthorName} <{project.AuthorEmail}>\n"
               + $"/// @copyright  {project.ProjectStartYear}-{DateTime.UtcNow.Year} {project.CopyrightHolder}\n"
               + $"/// @link       {project.Dart.GithubUrl}\n"
               + "///\n";    
    }

    public string LanguageName()
    {
        return "Dart";
    }
}