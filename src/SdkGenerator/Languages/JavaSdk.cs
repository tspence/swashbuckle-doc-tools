﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator.Languages;

public class JavaSdk : ILanguageSdk
{
    private string FileHeader(ProjectSchema project)
    {
        return "\n/**\n"
               + $" * {project.ProjectName} for Java\n"
               + " *\n"
               + $" * (c) {project.ProjectStartYear}-{DateTime.UtcNow.Year} {project.CopyrightHolder}\n"
               + " *\n"
               + " * For the full copyright and license information, please view the LICENSE\n"
               + " * file that was distributed with this source code.\n"
               + " *\n"
               + $" * @author     {project.AuthorName} <{project.AuthorEmail}>\n"
               + $" * @copyright  {project.ProjectStartYear}-{DateTime.UtcNow.Year} {project.CopyrightHolder}\n"
               + $" * @link       {project.Java.GithubUrl}\n"
               + " */\n\n";
    }

    private List<string> _reserved = new() { "static" };

    private string JavaTypeName(GeneratorContext context, string typeName, bool isArray)
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
        }

        if (isArray)
        {
            s += "[]";
        }
        
        // Handle lists, which are congruent to an array
        if (s.EndsWith("List", StringComparison.OrdinalIgnoreCase))
        {
            return JavaTypeName(context, s.Substring(0, s.Length - 4), true);
        }
        
        // Is this a generic class?
        foreach (var genericName in context.Project.GenericSuffixes ?? Enumerable.Empty<string>())
        {
            if (s.EndsWith(genericName))
            {
                var innerType = s[..^genericName.Length];
                s = $"{genericName}<{JavaTypeName(context, innerType, false)}>";
            }
        }

        if (string.IsNullOrWhiteSpace(s))
        {
            return "Object";
        }
        return s;
    }

    private string FixupType(GeneratorContext context, string typeName, bool isArray, bool nullable)
    {
        var s = JavaTypeName(context, typeName, isArray);
        return nullable ? $"@Nullable {s}" : $"@NotNull {s}";
    }

    private async Task ExportSchemas(GeneratorContext context)
    {
        var modelsDir = Path.Combine(context.Project.Java.Folder, "src", "main", "java",
            context.Project.Java.Namespace.Replace('.', Path.DirectorySeparatorChar), "models");
        Directory.CreateDirectory(modelsDir);
        foreach (var modelFile in Directory.EnumerateFiles(modelsDir, "*.java"))
        {
            File.Delete(modelFile);
        }

        foreach (var item in context.Api.Schemas)
        {
            if (item.Fields != null && !context.Project.Java.HandwrittenClasses.Contains(item.Name))
            {
                var sb = new StringBuilder();
                sb.AppendLine(FileHeader(context.Project));
                sb.AppendLine($"package {context.Project.Java.Namespace}.models;");
                sb.AppendLine();
                foreach (var import in GetImports(context, item))
                {
                    if (!string.IsNullOrWhiteSpace(import) &&
                        !import.StartsWith($"import {context.Project.Java.Namespace}.models."))
                    {
                        sb.AppendLine(import);
                    }
                }

                sb.AppendLine("import org.jetbrains.annotations.NotNull;");
                sb.AppendLine("import org.jetbrains.annotations.Nullable;");

                // Add class and header
                sb.AppendLine();
                sb.Append(item.DescriptionMarkdown.ToJavaDoc(0));
                sb.AppendLine($"public class {item.Name}");
                sb.AppendLine("{");

                // First do the fields
                foreach (var field in item.Fields)
                {
                    if (!field.Deprecated)
                    {
                        // This is the field; we collect all fields
                        sb.AppendLine(
                            $"    private {FixupType(context, field.DataType, field.IsArray, field.Nullable)} {field.Name.ToCamelCase().ToVariableName(_reserved)};");
                    }
                }

                // Next all the getters/setters
                sb.AppendLine();
                foreach (var field in item.Fields)
                {
                    if (!field.Deprecated)
                    {
                        // For whatever reason, Java wants the field description to be the "return" value of the getter
                        sb.Append(field.DescriptionMarkdown.ToJavaDoc(4, "The field " + field.Name));
                        sb.AppendLine(
                            $"    public {FixupType(context, field.DataType, field.IsArray, field.Nullable)} get{field.Name.ToProperCase()}() {{ return this.{field.Name.ToCamelCase().ToVariableName(_reserved)}; }}");

                        // For whatever reason, Java wants the field description to be the "value" param of the setter
                        var pf = new ParameterField
                        {
                            Name = "value",
                            DescriptionMarkdown = "The new value for " + field.Name
                        };
                        sb.Append(field.DescriptionMarkdown.ToJavaDoc(4, null, new List<ParameterField> { pf }));
                        sb.AppendLine(
                            $"    public void set{field.Name.ToProperCase()}({FixupType(context, field.DataType, field.IsArray, field.Nullable)} value) {{ this.{field.Name.ToCamelCase().ToVariableName(_reserved)} = value; }}");
                    }
                }

                sb.AppendLine("};");
                var classPath = Path.Combine(modelsDir, item.Name + ".java");
                await File.WriteAllTextAsync(classPath, sb.ToString());
            }
        }
    }

    private async Task ExportEndpoints(GeneratorContext context)
    {
        var clientsDir = Path.Combine(context.Project.Java.Folder, "src", "main", "java",
            context.Project.Java.Namespace.Replace('.', Path.DirectorySeparatorChar), "clients");
        Directory.CreateDirectory(clientsDir);
        foreach (var clientsFile in Directory.EnumerateFiles(clientsDir, "*.java"))
        {
            File.Delete(clientsFile);
        }

        // Gather a list of unique categories
        foreach (var cat in context.Api.Categories)
        {
            var sb = new StringBuilder();

            // Construct header
            sb.AppendLine(FileHeader(context.Project));
            sb.AppendLine($"package {context.Project.Java.Namespace}.clients;");
            sb.AppendLine();
            sb.AppendLine($"import {context.Project.Java.Namespace}.{context.Project.Java.ClassName};");
            sb.AppendLine($"import {context.Project.Java.Namespace}.RestRequest;");
            sb.AppendLine("import org.jetbrains.annotations.NotNull;");
            sb.AppendLine("import org.jetbrains.annotations.Nullable;");
            sb.AppendLine("import com.google.gson.reflect.TypeToken;");
            foreach (var import in GetImports(context, cat))
            {
                sb.AppendLine(import);
            }

            sb.AppendLine();
            sb.AppendLine("/**");
            sb.AppendLine($" * Contains all methods related to {cat}");
            sb.AppendLine(" */");
            sb.AppendLine($"public class {cat}Client");
            sb.AppendLine("{");
            sb.AppendLine($"    private {context.Project.Java.ClassName} client;");
            sb.AppendLine();
            sb.AppendLine("    /**");
            sb.AppendLine($"     * Constructor for the {cat} API collection");
            sb.AppendLine("     *");
            sb.AppendLine(
                $"     * @param client A {{@link {context.Project.Java.Namespace}.{context.Project.Java.ClassName}}} platform client");
            sb.AppendLine("     */");
            sb.AppendLine($"    public {cat}Client(@NotNull {context.Project.Java.ClassName} client) {{");
            sb.AppendLine("        super();");
            sb.AppendLine("        this.client = client;");
            sb.AppendLine("    }");

            // Run through all APIs
            foreach (var endpoint in context.Api.Endpoints)
            {
                if (endpoint.Category == cat && !endpoint.Deprecated)
                {
                    sb.AppendLine();
                    sb.Append(endpoint.DescriptionMarkdown.ToJavaDoc(4,
                        $"A {{@link {context.Project.Java.Namespace}.{context.Project.Java.ResponseClass}}} containing the results",
                        endpoint.Parameters));

                    // Figure out the parameter list
                    var paramListStr = string.Join(", ", from p in endpoint.Parameters
                        select $"{FixupType(context, p.DataType, p.IsArray, !p.Required)} {p.Name.ToVariableName(_reserved)}");

                    // What is our return type?
                    string rawReturnType = endpoint.ReturnDataType.DataType;
                    if (rawReturnType.EndsWith(context.Project.Java.ResponseClass, StringComparison.OrdinalIgnoreCase))
                    {
                        rawReturnType = rawReturnType.Substring(0,
                            rawReturnType.Length - context.Project.Java.ResponseClass.Length);
                    }
                    var returnType = JavaTypeName(context, rawReturnType, endpoint.ReturnDataType.IsArray);
                    var requestType = returnType == "byte[]" ? "BlobRequest" : $"RestRequest<{returnType}>";

                    // Write the method
                    sb.AppendLine(
                        $"    public @NotNull {context.Project.Java.ResponseClass}<{returnType}> {endpoint.Name.ToCamelCase()}({paramListStr})");
                    sb.AppendLine("    {");
                    sb.AppendLine(
                        $"        {requestType} r = new {requestType}(this.client, \"{endpoint.Method.ToUpper()}\", \"{endpoint.Path}\");");

                    // Add parameters options
                    foreach (var o in endpoint.Parameters)
                    {
                        switch (o.Location)
                        {
                            case "body":
                                sb.AppendLine("        if (body != null) { r.AddBody(body); }");
                                break;
                            case "query":
                                sb.AppendLine(
                                    $"        if ({o.Name.ToVariableName(_reserved)} != null) {{ r.AddQuery(\"{o.Name}\", {o.Name.ToVariableName(_reserved)}.toString()); }}");
                                break;
                            case "path":
                                sb.AppendLine(
                                    $"        r.AddPath(\"{{{o.Name}}}\", {o.Name.ToVariableName(_reserved)} == null ? \"\" : {o.Name.ToVariableName(_reserved)}.toString());");
                                break;
                            case "form":
                                break;
                            default:
                                throw new Exception("Unknown location " + o.Location);
                        }
                    }
                    sb.AppendLine($"        return r.Call(new TypeToken<AstroResult<{returnType}>>() {{}}.getType());");
                    sb.AppendLine("    }");
                }
            }

            // Close out the namespace
            sb.AppendLine("}");

            // Write this category to a file
            var classPath = Path.Combine(clientsDir, $"{cat}Client.java");
            await File.WriteAllTextAsync(classPath, sb.ToString());
        }
    }

    private void AddImport(GeneratorContext context, string name, HashSet<string> list)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (name.EndsWith("List", StringComparison.OrdinalIgnoreCase))
        {
            AddImport(context, name.Substring(0, name.Length - 4), list);
            return;
        }
        
        foreach (var genericName in context.Project.GenericSuffixes ?? Enumerable.Empty<string>())
        {
            if (name.EndsWith(genericName))
            {
                list.Add(genericName);
                var innerType = name[..^genericName.Length];
                AddImport(context, innerType, list);
                return;
            }
        }
        
        if (context.Api.FindEnum(name) == null)
        {
            list.Add(name);
        }
    }

    private List<string> GetImports(GeneratorContext context, string category)
    {
        var types = new HashSet<string>();
        foreach (var endpoint in context.Api.Endpoints)
        {
            if (endpoint.Category == category && !endpoint.Deprecated)
            {
                AddImport(context, endpoint.ReturnDataType.DataType, types);
                foreach (var p in endpoint.Parameters)
                {
                    AddImport(context, p.DataType, types);
                }
            }
        }

        // Deduplicate the list and generate import statements
        return (from t in types select GetImportForType(context, t)).Distinct().ToList();
    }

    private List<string> GetImports(GeneratorContext context, SchemaItem schema)
    {
        var types = new HashSet<string>();
        foreach (var field in schema.Fields)
        {
            if (!field.Deprecated)
            {
                AddImport(context, field.DataType, types);
            }
        }

        // Deduplicate the list and generate import statements
        return (from t in types select GetImportForType(context, t)).Distinct().ToList();
    }

    private string GetImportForType(GeneratorContext context, string type)
    {
        foreach (var genericName in context.Project.GenericSuffixes ?? Enumerable.Empty<string>())
        {
            if (type == genericName)
            {
                return $"import {context.Project.Java.Namespace}.{type};";
            }
        }
        
        switch (type)
        {
            case "string":
            case "uuid":
            case "object":
            case "int32":
            case "integer":
            case "boolean":
            case "double":
            case "array":
            case "email":
            case "uri":
            case "float":
            case "date":
            case "date-time":
            case "binary":
            case "File":
            case "byte[]":
                return null;
            default:
                return $"import {context.Project.Java.Namespace}.models.{type};";
        }
    }

    public async Task Export(GeneratorContext context)
    {
        if (context.Project.Java == null)
        {
            return;
        }

        await ExportSchemas(context);
        await ExportEndpoints(context);

        // Let's try using Scriban to populate these files
        await ScribanFunctions.ExecuteTemplate(context, 
            Path.Combine(".", "templates", "java", "ApiClient.java.scriban"),
            Path.Combine(context.Project.Java.Folder, "src", "main", "java",
                context.Project.Java.Namespace.Replace('.', Path.DirectorySeparatorChar),
                context.Project.Java.ClassName + ".java"));
        await ScribanFunctions.ExecuteTemplate(context,
            Path.Combine(".", "templates", "java", "pom.xml.scriban"),
            Path.Combine(context.Project.Java.Folder, "pom.xml"));
        await ScribanFunctions.ExecuteTemplate(context, 
            Path.Combine(".", "templates", "java", "RestRequest.java.scriban"),
            Path.Combine(context.Project.Java.Folder, "src", "main", "java",
                context.Project.Java.Namespace.Replace('.', Path.DirectorySeparatorChar), "RestRequest.java"));
    }
    
    public string LanguageName()
    {
        return "Java";
    }
}