using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator.Languages;

public static class TypescriptSdk
{
    private static string FileHeader(ProjectSchema project)
    {
        return "/**\n"
               + $" * {project.ProjectName} for TypeScript\n"
               + " *\n"
               + $" * (c) {project.ProjectStartYear}-{DateTime.UtcNow.Year} {project.CopyrightHolder}\n"
               + " *\n"
               + " * For the full copyright and license information, please view the LICENSE\n"
               + " * file that was distributed with this source code.\n"
               + " *\n"
               + $" * @author     {project.AuthorName} <{project.AuthorEmail}>\n"
               + $" * @copyright  {project.ProjectStartYear}-{DateTime.UtcNow.Year} {project.CopyrightHolder}\n"
               + $" * @link       {project.Typescript.GithubUrl}\n"
               + " */\n";
    }

    private static string FixupType(ApiSchema api, string typeName, bool isArray, bool nullable)
    {
        var s = typeName;
        if (api.IsEnum(typeName))
        {
            s = api.FindSchema(typeName).EnumType;
        }

        switch (s)
        {
            case "tel":
            case "Uri":
                s = "string";
                break;
            case "TestTimeoutException":
                s = "ErrorResult";
                break;
            case "File":
                s = "string"; // This is a file upload using a filename
                break;
            case "float":
            case "int64":
            case "double":
            case "integer":
            case "int32":
                s = "number";
                break;
            case "email":
            case "date":
            case "uri":
            case "date-time":
            case "uuid":
                s = "string";
                break;
            case "binary":
                s = "Blob";
                break;
            default:
                s = typeName;
                break;
        }

        if (isArray)
        {
            s += "[]";
        }

        if (s.EndsWith("FetchResult") && !s.EndsWith("SummaryFetchResult"))
        {
            s = $"FetchResult<{s[..^11]}>";
        }

        if (nullable)
        {
            s += " | null";
        }

        return s;
    }

    private static async Task ExportSchemas(ProjectSchema project, ApiSchema api)
    {
        var modelsDir = Path.Combine(project.Typescript.Folder, "src", "models");
        Directory.CreateDirectory(modelsDir);
        foreach (var modelFile in Directory.EnumerateFiles(modelsDir, "*.ts"))
        {
            File.Delete(modelFile);
        }

        foreach (var item in api.Schemas)
        {
            var sb = new StringBuilder();
            sb.AppendLine(FileHeader(project));
            foreach (var import in GetImports(api, item))
            {
                sb.AppendLine(import);
            }

            if (item.Fields != null)
            {
                sb.AppendLine();
                sb.Append(item.DescriptionMarkdown.ToJavaDoc(0));
                sb.AppendLine($"export type {item.Name} = {{");
                foreach (var field in item.Fields)
                {
                    if (!field.Deprecated)
                    {
                        sb.AppendLine();
                        sb.Append(field.DescriptionMarkdown.ToJavaDoc(2));
                        sb.AppendLine(
                            $"  {field.Name}: {FixupType(api, field.DataType, field.IsArray, field.Nullable)};");
                    }
                }

                sb.AppendLine("};");
            }

            var modelPath = Path.Combine(modelsDir, item.Name + ".ts");
            await File.WriteAllTextAsync(modelPath, sb.ToString());
        }
    }

    private static async Task ExportEndpoints(ProjectSchema project, ApiSchema api)
    {
        var clientsDir = Path.Combine(project.Typescript.Folder, "src", "clients");
        Directory.CreateDirectory(clientsDir);
        foreach (var clientsFile in Directory.EnumerateFiles(clientsDir, "*.ts"))
        {
            File.Delete(clientsFile);
        }

        // Gather a list of unique categories
        var categories = (from e in api.Endpoints where !e.Deprecated select e.Category).Distinct().ToList();
        foreach (var cat in categories)
        {
            var sb = new StringBuilder();

            // Construct header
            sb.AppendLine(FileHeader(project));
            sb.AppendLine($"import {{ {project.Typescript.ClassName} }} from \"..\";");
            sb.AppendLine($"import {{ {project.Typescript.ResponseClass} }} from \"..\";");
            foreach (var import in GetImports(api, cat))
            {
                sb.AppendLine(import);
            }

            sb.AppendLine();
            sb.AppendLine($"export class {cat}Client {{");
            sb.AppendLine($"  private readonly client: {project.Typescript.ClassName};");
            sb.AppendLine();
            sb.AppendLine("  /**");
            sb.AppendLine("   * Internal constructor for this client library");
            sb.AppendLine("   */");
            sb.AppendLine($"  public constructor(client: {project.Typescript.ClassName}) {{");
            sb.AppendLine("    this.client = client;");
            sb.AppendLine("  }");

            // Run through all APIs
            foreach (var endpoint in api.Endpoints)
            {
                if (endpoint.Category == cat && !endpoint.Deprecated)
                {
                    sb.AppendLine();
                    sb.Append(endpoint.DescriptionMarkdown.ToJavaDoc(2, null, endpoint.Parameters));

                    // Figure out the parameter list. For parameters, we'll use ? to indicate nullability.
                    var paramListStr = string.Join(", ", from p in endpoint.Parameters
                        orderby p.Required descending
                        select $"{p.Name}{(p.Required ? "" : "?")}: {FixupType(api, p.DataType, p.IsArray, false)}");

                    // Do we need to specify options?
                    var options = (from p in endpoint.Parameters where p.Location == "query" select p).ToList();

                    // What is our return type?
                    var returnType = FixupType(api, endpoint.ReturnDataType.DataType,
                        endpoint.ReturnDataType.IsArray, false);
                    var isFileUpload = (from p in endpoint.Parameters where p.Location == "form" select p).Any();

                    // Are we using the blob method?
                    var requestMethod = returnType == "Blob" ? "requestBlob" : $"request<{returnType}>";
                    if (isFileUpload)
                    {
                        requestMethod = "fileUpload";
                    }

                    // Write the method
                    sb.AppendLine($"  {endpoint.Name.ToCamelCase()}({paramListStr}): Promise<{project.Typescript.ResponseClass}<{returnType}>> {{");
                    sb.AppendLine($"    const url = `{endpoint.Path.Replace("{", "${")}`;");
                    if (options.Count > 0)
                    {
                        sb.AppendLine("    const options = {");
                        sb.AppendLine("      params: {");
                        foreach (var o in options)
                        {
                            sb.AppendLine($"        {o.Name},");
                        }

                        sb.AppendLine("      },");
                        sb.AppendLine("    };");
                    }

                    var hasBody = (from p in endpoint.Parameters where p.Location == "body" select p).Any();
                    var optionsStr = options.Count > 0 ? ", options" : ", null";
                    var bodyStr = isFileUpload ? ", filename" : hasBody ? ", body" : ", null";
                    sb.AppendLine($"    return this.client.{requestMethod}(\"{endpoint.Method}\", url{optionsStr}{bodyStr});");
                    sb.AppendLine("  }");
                }
            }

            // Close out the namespace
            sb.AppendLine("}");

            // Write this category to a file
            var classPath = Path.Combine(clientsDir, $"{cat}Client.ts");
            await File.WriteAllTextAsync(classPath, sb.ToString());
        }
    }

    private static void AddImport(ApiSchema api, string name, List<string> list)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (name.EndsWith("FetchResult") && !name.EndsWith("SummaryFetchResult"))
        {
            if (!list.Contains("FetchResult"))
            {
                list.Add("FetchResult");
            }

            var innerType = name[..^11];
            AddImport(api, innerType, list);
        }
        else if (!api.IsEnum(name) && !list.Contains(name))
        {
            list.Add(name);
        }
    }

    private static List<string> GetImports(ApiSchema api, string category)
    {
        var types = new List<string>();
        foreach (var endpoint in api.Endpoints)
        {
            if (endpoint.Category == category && !endpoint.Deprecated)
            {
                AddImport(api, endpoint.ReturnDataType.DataType, types);
                foreach (var p in endpoint.Parameters)
                {
                    AddImport(api, p.DataType, types);
                }
            }
        }

        return GenerateImportsFromList(types);
    }

    private static List<string> GetImports(ApiSchema api, SchemaItem item)
    {
        var types = new List<string>();
        foreach (var field in (item?.Fields).EmptyIfNull())
        {
            if (field?.DataType != item?.Name)
            {
                AddImport(api, field?.DataType, types);
            }
        }

        return GenerateImportsFromList(types);
    }

    private static List<string> GenerateImportsFromList(List<string> types)
    {
        // Deduplicate the list and generate import statements
        var imports = new List<string>();
        foreach (var t in types)
        {
            switch (t)
            {
                case "FetchResult":
                    imports.Add("import { FetchResult } from \"..\";");
                    break;
                case "ActionResultModel":
                    imports.Add("import { ActionResultModel } from \"..\";");
                    break;
                case "TestTimeoutException":
                    imports.Add("import { ErrorResult } from \"..\";");
                    break;
                case "binary":
                    imports.Add("import { Blob } from \"buffer\";");
                    break;
                case "string":
                case "uuid":
                case "object":
                case "int32":
                case "date":
                case "date-time":
                case "File":
                case "boolean":
                case "array":
                case "email":
                case "double":
                case "float":
                case "uri":
                    break;
                default:
                    imports.Add("import { " + t + " } from \"..\";");
                    break;
            }
        }

        return imports;
    }

    public static async Task Export(GeneratorContext context)
    {
        if (context.Project.Typescript == null)
        {
            return;
        }

        await ExportSchemas(context.Project, context.Api);
        await ExportEndpoints(context.Project, context.Api);

        // Let's try using Scriban to populate these files
        await ScribanFunctions.ExecuteTemplate(context, 
            Path.Combine(".", "templates", "ts", "ApiClient.ts.scriban"),
            Path.Combine(context.Project.Typescript.Folder, "src", context.Project.Typescript.ClassName + ".ts"));
        await ScribanFunctions.ExecuteTemplate(context,
            Path.Combine(".", "templates", "ts", "index.ts.scriban"),
            Path.Combine(context.Project.Typescript.Folder, "src", "index.ts"));

        // Patch the version number in package.json
        await Extensions.PatchFile(context, Path.Combine(context.Project.Typescript.Folder, "package.json"),
            "\"version\": \"[\\d\\.]+\",",
            $"\"version\": \"{context.OfficialVersion}\",");
    }
}