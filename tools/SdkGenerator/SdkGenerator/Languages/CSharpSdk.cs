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

public static class CSharpSdk
{
    private static string FileHeader(ProjectSchema project)
    {
        return "/***\n"
               + $" * {project.ProjectName} for C#\n"
               + " *\n"
               + $" * (c) {project.ProjectStartYear}-{DateTime.UtcNow.Year} {project.CopyrightHolder}\n"
               + " *\n"
               + " * For the full copyright and license information, please view the LICENSE\n"
               + " * file that was distributed with this source code.\n"
               + " *\n"
               + $" * @author     {project.AuthorName} <{project.AuthorEmail}>\n"
               + $" * @copyright  {project.ProjectStartYear}-{DateTime.UtcNow.Year} {project.CopyrightHolder}\n"
               + $" * @link       {project.Csharp.GithubUrl}\n"
               + " */\n"
               + "\n\n";
    }

    private static string MakeNullable(string typeName)
    {
        if (typeName.EndsWith('?'))
        {
            typeName = typeName[..^1];
        }

        // Only reference types need to be made nullable
        if (typeName is "decimal" or "bool" or "int" or "int64" or "Guid" or "DateTime")
        {
            return typeName + '?';
        }

        // All other types are inherently nullable in NetStandard 2.0
        return typeName;
    }

    private static string NullableFixup(string typeName, bool allowNulls)
    {
        if (allowNulls)
        {
            return MakeNullable(typeName);
        }

        // Okay, we don't want it to be nullable
        if (typeName.EndsWith('?'))
        {
            return typeName[..^1];
        }

        return typeName;
    }

    private static string FixupType(GeneratorContext context, string typeName, bool isArray, bool isNullable)
    {
        var s = typeName;
        if (context.Api.IsEnum(typeName))
        {
            s = context.Api.FindSchema(typeName).EnumType;
        }

        switch (s)
        {
            case "integer":
            case "int32":
                s = "int";
                break;
            case "int64":
                s = "int64";
                break;
            case "double":
            case "float":
                s = "decimal";
                break;
            case "boolean":
                s = "bool";
                break;
            case "date-time":
                s = "DateTime";
                break;
            case "date":
            case "Uri":
            case "uri":
            case "tel":
            case "email":
            case "File":
                s = "string"; // We convert "file" to "filename" in processing
                break;
            case "binary":
                s = "byte[]";
                break;
            case "uuid":
                s = "Guid";
                break;
            case "TestTimeoutException":
                s = "ErrorResult";
                break;
        }

        if (isArray)
        {
            s += "[]";
        }

        foreach (var genericName in context.Project.GenericSuffixes ?? Enumerable.Empty<string>())
        {
            if (s.EndsWith(genericName))
            {
                s = s[..^genericName.Length];
            }
        }

        if (s.EndsWith("List"))
        {
            s = s[..^4] + "[]";
        }

        s = NullableFixup(s, isNullable);
        if (string.IsNullOrWhiteSpace(s))
        {
            s = "string";
        }

        return s;
    }

    private static async Task ExportSchemas(GeneratorContext context)
    {
        var modelsDir = Path.Combine(context.Project.Csharp.Folder, "src", "Models");
        Directory.CreateDirectory(modelsDir);
        foreach (var modelFile in Directory.EnumerateFiles(modelsDir, "*.cs"))
        {
            File.Delete(modelFile);
        }

        foreach (var item in context.Api.Schemas)
        {
            // Is this one of the handwritten schemas?  If so, skip it
            var handwritten = (context.Project.Csharp.HandwrittenClasses ?? Enumerable.Empty<string>()).ToList();
            handwritten.Add(context.Project.Csharp.ResponseClass);
            if (handwritten.Contains(item.Name))
            {
                continue;
            }
            var sb = new StringBuilder();
            sb.AppendLine(FileHeader(context.Project));
            sb.AppendLine("#pragma warning disable CS8618");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine($"namespace {context.Project.Csharp.Namespace}.Models");
            sb.AppendLine("{");
            if (item.Fields != null)
            {
                sb.AppendLine();
                sb.Append(MarkdownToDocblock(item.DescriptionMarkdown, 4));
                sb.AppendLine($"    public class {item.Name}");
                sb.AppendLine("    {");
                foreach (var field in item.Fields)
                {
                    if (!field.Deprecated)
                    {
                        var fieldType = FixupType(context, field.DataType, field.IsArray, field.Nullable);
                        // Add commentary for date-only fields
                        var markdown = field.DescriptionMarkdown;
                        if (field.DataType == "date" && fieldType == "string")
                        {
                            markdown +=
                                "\n\n" +
                                "This is a date-only field stored as a string in ISO 8601 (YYYY-MM-DD) format.";
                        }

                        sb.AppendLine();
                        sb.Append(MarkdownToDocblock(markdown, 8));
                        sb.AppendLine($"        public {MakeNullable(fieldType)} {field.Name.ToProperCase()} {{ get; set; }}");
                    }
                }

                sb.AppendLine("    }");
            }

            sb.AppendLine("}");
            var modelPath = Path.Combine(modelsDir, item.Name + ".cs");
            await File.WriteAllTextAsync(modelPath, sb.ToString());
        }
    }

    private static string MarkdownToDocblock(string markdown, int indent, List<ParameterField> parameterList = null)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "";
        }

        var sb = new StringBuilder();
        var prefix = "".PadLeft(indent) + "///";

        // Add summary section
        sb.AppendLine($"{prefix} <summary>");
        foreach (var line in markdown.Split("\n"))
        {
            if (line.StartsWith("###"))
            {
                break;
            }

            sb.AppendLine($"{prefix} {HttpUtility.HtmlEncode(line)}".TrimEnd());
        }

        sb.AppendLine($"{prefix} </summary>");

        // Add documentation for parameters
        if (parameterList != null)
        {
            foreach (var p in parameterList)
            {
                sb.AppendLine(
                    $"{prefix} <param name=\"{p.Name.ToVariableName()}\">{p.DescriptionMarkdown.ToSingleLineMarkdown()}</param>");
            }
        }

        return sb.ToString();
    }

    private static async Task ExportEndpoints(GeneratorContext context)
    {
        var clientsDir = Path.Combine(context.Project.Csharp.Folder, "src", "Clients");
        Directory.CreateDirectory(clientsDir);
        foreach (var clientFile in Directory.EnumerateFiles(clientsDir, "*.cs"))
        {
            File.Delete(clientFile);
        }

        // Gather a list of unique categories
        foreach (var cat in context.Api.Categories)
        {
            var sb = new StringBuilder();

            // Construct header
            sb.AppendLine(FileHeader(context.Project));
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Net.Http;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine($"using {context.Project.Csharp.Namespace}.Models;");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine($"namespace {context.Project.Csharp.Namespace}.Clients");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// API methods related to {cat}");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    public class {cat}Client");
            sb.AppendLine("    {");
            sb.AppendLine($"        private readonly {context.Project.Csharp.ClassName} _client;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Constructor");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        public {cat}Client({context.Project.Csharp.ClassName} client)");
            sb.AppendLine("        {");
            sb.AppendLine("            _client = client;");
            sb.AppendLine("        }");

            // Run through all APIs
            foreach (var endpoint in context.Api.Endpoints)
            {
                if (endpoint.Category == cat && !endpoint.Deprecated)
                {
                    sb.AppendLine();
                    sb.Append(MarkdownToDocblock(endpoint.DescriptionMarkdown, 8, endpoint.Parameters));

                    // Figure out the parameter list
                    var paramList = new List<string>();
                    foreach (var p in from p in endpoint.Parameters orderby p.Required descending select p)
                    {
                        var isNullable = !p.Required || p.Nullable;
                        var typeName = FixupType(context, p.DataType, p.IsArray, isNullable);
                        var paramText = $"{typeName} {p.Name.ToVariableName()}{(isNullable ? " = null" : "")}";
                        paramList.Add(paramText);
                    }

                    // Do we need to specify options?
                    var options = (from p in endpoint.Parameters where p.Location == "query" select p).ToList();
                    var isFileUpload = (from p in endpoint.Parameters where p.Location == "form" select p).Any();

                    // What is our return type?  Note that we're assuming this can be non-nullable since the
                    // response class already handles the case where the value ends up being null.
                    var returnType = FixupType(context, endpoint.ReturnDataType.DataType, endpoint.ReturnDataType.IsArray,
                        false);

                    // Write the method
                    var modernSignature =
                        $"        public async Task<{context.Project.Csharp.ResponseClass}<{returnType}>> {endpoint.Name.ToProperCase()}({string.Join(", ", paramList)})";
                    sb.AppendLine(modernSignature);

                    sb.AppendLine("        {");
                    sb.AppendLine($"            var url = $\"{endpoint.Path}\";");

                    // Add query string parameter options
                    if (options.Count > 0)
                    {
                        sb.AppendLine("            var options = new Dictionary<string, object>();");
                        foreach (var o in options)
                        {
                            sb.AppendLine(
                                !o.Required
                                    ? $"            if ({o.Name.ToVariableName()} != null) {{ options[\"{o.Name}\"] = {o.Name.ToVariableName()}; }}"
                                    : $"            options[\"{o.Name}\"] = {o.Name.ToVariableName()};");
                        }
                    }

                    // Determine the HTTP method
                    var method = $"HttpMethod.{endpoint.Method.ToProperCase()}";
                    if (string.Equals(endpoint.Method, "PATCH", StringComparison.InvariantCultureIgnoreCase))
                    {
                        method = "new HttpMethod(\"PATCH\")";
                    }

                    // Send the request
                    var hasBody = (from p in endpoint.Parameters where p.Location == "body" select p).Any();
                    var optionsStr = options.Count > 0 ? ", options" : ", null";
                    var bodyStr = hasBody ? ", body" : ", null";
                    var fileStr = isFileUpload ? ", filename" : ", null";
                    sb.AppendLine(
                        $"            return await _client.Request<{returnType}>({method}, url{optionsStr}{bodyStr}{fileStr});");
                    sb.AppendLine("        }");
                }
            }

            // Close out the class and namespace
            sb.AppendLine("    }");
            sb.AppendLine("}");

            // Write this category to a file
            var modulePath = Path.Combine(clientsDir, $"{cat}Client.cs");
            await File.WriteAllTextAsync(modulePath, sb.ToString());
        }
    }

    public static async Task Export(GeneratorContext context)
    {
        if (context.Project.Csharp == null)
        {
            return;
        }

        await ExportSchemas(context);
        await ExportEndpoints(context);

        // Let's try using Scriban to populate these files
        await ScribanFunctions.ExecuteTemplate(context, 
            Path.Combine(".", "templates", "csharp", "ApiClient.cs.scriban"),
            Path.Combine(context.Project.Csharp.Folder, "src", context.Project.Csharp.ClassName + ".cs"));
        await ScribanFunctions.ExecuteTemplate(context, 
            Path.Combine(".", "templates", "csharp", "sdk.nuspec.scriban"),
            Path.Combine(context.Project.Csharp.Folder, context.Project.Csharp.ClassName + ".nuspec"));
    }
}