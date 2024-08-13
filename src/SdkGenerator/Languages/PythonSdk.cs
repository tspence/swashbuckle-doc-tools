﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator.Languages;

public class PythonSdk : ILanguageSdk
{
    private string FileHeader(ProjectSchema project)
    {
        return "#\n"
               + $"# {project.ProjectName} for Python\n"
               + "#\n"
               + $"# (c) {project.ProjectStartYear}-{DateTime.UtcNow.Year} {project.CopyrightHolder}\n"
               + "#\n"
               + "# For the full copyright and license information, please view the LICENSE\n"
               + "# file that was distributed with this source code.\n"
               + "#\n"
               + $"# @author     {project.AuthorName} <{project.AuthorEmail}>\n"
               + $"# @copyright  {project.ProjectStartYear}-{DateTime.UtcNow.Year} {project.CopyrightHolder}\n"
               + $"# @link       {project.Python.GithubUrl}\n"
               + "#\n\n";
    }

    private string FixupType(GeneratorContext context, string typeName, bool isArray, bool isReturnHint = false)
    {
        var s = context.Api.ReplaceEnumWithType(typeName);

        switch (s)
        {
            case "uuid":
            case "string":
            case "uri":
            case "email":
            case "date-time":
            case "date":
            case "Uri":
            case "tel":
            case "TestTimeoutException":
                s = "str";
                break;
            case "int32":
            case "integer":
            case "HttpStatusCode":
            case "int64":
                s = "int";
                break;
            case "double":
            case "float":
                s = "float";
                break;
            case "boolean":
                s = "bool";
                break;
            case "File": // A "File" object is an uploadable file
                s = "str";
                break;
            case "binary":
            case "byte[]":
                s = "bytes";
                break;
        }

        foreach (var genericName in context.Project.GenericSuffixes ?? Enumerable.Empty<string>())
        {
            if (s == genericName)
            {
                return $"{genericName}[object]";
            }
            if (s.EndsWith(genericName))
            {
                var innerType = FixupType(context, s[..^genericName.Length], false, false);
                return $"{genericName}[{innerType}]";
            }
        }

        // Is this a generic list?
        if (s.EndsWith("List", StringComparison.OrdinalIgnoreCase))
        {
            return $"List[{FixupType(context, s[..^4], false, false)}]";
        }

        if (isArray)
        {
            s = "List[" + s + "]";    
        }

        return s;
    }

    private async Task ExportSchemas(GeneratorContext context)
    {
        var modelsDir = context.MakePath(context.Project.Python.Folder, "src", context.Project.Python.Namespace, "models");
        await CleanModuleDirectory(context, modelsDir);
        await File.WriteAllTextAsync(Path.Combine(modelsDir, "__init__.py"), string.Empty);

        foreach (var item in context.Api.Schemas)
        {
            if (item.Fields != null && item.Name != context.Project.Python.ResponseClass)
            {
                var sb = new StringBuilder();
                sb.AppendLine(FileHeader(context.Project));

                // The "Future" import apparently must be the very first one in the file
                if (item.Fields.Any(f => f.DataType == item.Name))
                {
                    sb.AppendLine("from __future__ import annotations");
                }

                // Produce imports
                foreach (var import in BuildImports(context, item.Fields))
                {
                    sb.AppendLine(import);
                }

                // Add in all the rest of the imports
                sb.AppendLine("import dataclasses");
                sb.AppendLine();
                sb.AppendLine("@dataclasses.dataclass");
                sb.AppendLine($"class {item.Name}:");
                sb.Append(MakePythonDoc(context, item.DescriptionMarkdown, 4, null));
                sb.AppendLine();
                foreach (var f in item.Fields)
                {
                    sb.AppendLine($"    {f.Name}: {FixupType(context, f.DataType, f.IsArray)} | None = None");
                    
                    // Do we have a docstring?
                    if (!string.IsNullOrWhiteSpace(f.DescriptionMarkdown))
                    {
                        sb.Append(MakePythonDoc(context, f.DescriptionMarkdown, 4, null));
                        sb.AppendLine();
                    }
                }

                sb.AppendLine();

                var modelPath = Path.Combine(modelsDir, item.Name.WordsToSnakeCase() + ".py");
                await File.WriteAllTextAsync(modelPath, sb.ToString());
            }
        }
    }

    private async Task CleanModuleDirectory(GeneratorContext context, string pyModuleDir)
    {
        await Task.CompletedTask;
        Directory.CreateDirectory(pyModuleDir);

        foreach (var pyFile in Directory.EnumerateFiles(pyModuleDir, "*.py"))
        {
            if (!pyFile.EndsWith(context.Project.Python.ResponseClass + ".py", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(pyFile);
            }
        }
    }

    private async Task ExportEndpoints(GeneratorContext context)
    {
        var clientsDir = context.MakePath(context.Project.Python.Folder, "src", context.Project.Python.Namespace, "clients");
        await CleanModuleDirectory(context, clientsDir);
        await File.WriteAllTextAsync(Path.Combine(clientsDir, "__init__.py"), string.Empty);

        // Gather a list of unique categories
        foreach (var cat in context.Api.Categories)
        {
            var sb = new StringBuilder();

            // Let's see if we have to do any imports
            var imports = BuildImports(context, cat);

            // Construct header
            sb.Append(FileHeader(context.Project));
            foreach (var import in imports.Distinct())
            {
                sb.AppendLine(import);
            }

            sb.AppendLine($"from {context.Project.Python.Namespace}.tools import remove_empty_elements");
            sb.AppendLine("import dataclasses");
            sb.AppendLine("import json");
            sb.AppendLine("import dacite");
            sb.AppendLine();
            sb.AppendLine($"class {cat}Client:");
            sb.AppendLine("    \"\"\"");
            sb.AppendLine($"    API methods related to {cat}");
            sb.AppendLine("    \"\"\"");
            sb.AppendLine(
                $"    from {context.Project.Python.Namespace}.{context.Project.Python.ClassName.WordsToSnakeCase()} import {context.Project.Python.ClassName}");
            sb.AppendLine();
            sb.AppendLine($"    def __init__(self, client: {context.Project.Python.ClassName}):");
            sb.AppendLine("        self.client = client");

            // Run through all APIs
            foreach (var endpoint in context.Api.Endpoints)
            {
                if (endpoint.Category == cat && !endpoint.Deprecated)
                {
                    sb.AppendLine();

                    // Is this a file download API?
                    var isFileDownload = endpoint.ReturnDataType.DataType is "byte[]" or "binary" or "File" or "byte";
                    var originalReturnDataType = FixupType(context, endpoint.ReturnDataType.DataType,
                        endpoint.ReturnDataType.IsArray, isReturnHint: true);
                    string returnDataType;
                    if (isFileDownload)
                    {
                        returnDataType = context.Project.Python.ResponseClass + "[bytes]";
                    }
                    else
                    {
                        returnDataType = originalReturnDataType;
                    }

                    // Figure out the parameter list
                    var bodyParam = endpoint.Parameters.FirstOrDefault(p => p.Location == "body");
                    var hasBody = bodyParam != null;
                    var paramListStr = string.Join(", ", from p in endpoint.Parameters select $"{p.Name.ToVariableName()}: {FixupType(context, p.DataType, p.IsArray)}{(p.Nullable ? " | None" : "")}");
                    var bodyJson = string.Join(", ", from p in endpoint.Parameters where p.Location == "query" select $"\"{p.Name}\": {p.Name.ToVariableName()}");
                    var fileUploadParam = (from p in endpoint.Parameters where p.Location == "form" select p).FirstOrDefault();

                    // Write the method
                    if (string.IsNullOrWhiteSpace(paramListStr))
                    {
                        sb.AppendLine($"    def {endpoint.Name.WordsToSnakeCase()}(self) -> {returnDataType}:");
                    }
                    else
                    {
                        sb.AppendLine($"    def {endpoint.Name.WordsToSnakeCase()}(self, {paramListStr}) -> {returnDataType}:");
                    }
                    sb.Append(MakePythonDoc(context, endpoint.DescriptionMarkdown, 8, endpoint.Parameters));
                    sb.AppendLine(endpoint.Path.Contains('{')
                        ? $"        path = f\"{endpoint.Path}\""
                        : $"        path = \"{endpoint.Path}\"");
                    
                    // Assemble query parameter object, skipping none values
                    sb.AppendLine("        queryParams = {}");
                    foreach (var p in endpoint.Parameters.Where(p => p.Location == "query"))
                    {
                        sb.AppendLine($"        if {p.Name.ToVariableName()}:");
                        sb.AppendLine($"            queryParams['{p.Name}'] = {p.Name.ToVariableName()}");
                    }
                    
                    // Is this uploading a JSON array or object?
                    if (bodyParam != null && bodyParam.IsArray)
                    {
                        sb.AppendLine("        bodyArray = []");
                        sb.AppendLine("        for item in body:");
                        sb.AppendLine("            bodyArray.append(remove_empty_elements(dataclasses.asdict(item)))");
                        sb.AppendLine($"        result = self.client.send_request(\"{endpoint.Method.ToUpper()}\", path, bodyArray, queryParams, None)");
                    }
                    else
                    {
                        sb.AppendLine(
                            $"        result = self.client.send_request(\"{endpoint.Method.ToUpper()}\", path, {(hasBody ? "remove_empty_elements(dataclasses.asdict(body))" : "None")}, queryParams, {(fileUploadParam == null ? "None" : fileUploadParam.Name)})");
                    }
                    sb.AppendLine("        if result.status_code >= 200 and result.status_code < 300:");
                    string innerType = originalReturnDataType;
                    if (isFileDownload)
                    {
                        sb.AppendLine($"            return {context.Project.Python.ResponseClass}[bytes](None, True, False, result.status_code, result.content)");
                        innerType = "bytes";
                    }
                    else
                    {
                        // Remove the outer response class shell if present
                        if (originalReturnDataType.StartsWith(context.Project.Python.ResponseClass + "["))
                        {
                            innerType = innerType.Substring(context.Project.Python.ResponseClass.Length + 1,
                                innerType.Length - context.Project.Python.ResponseClass.Length - 2);
                        }
                        
                        // Deserialize lists in the python way
                        if (innerType.StartsWith("List["))
                        {
                            var elementType = innerType.Substring(5, innerType.Length - 6);
                            sb.AppendLine("            data = []");
                            sb.AppendLine("            for dict in json.loads(result.content)['data']:");
                            sb.AppendLine($"                data.append({elementType}(**dict))");
                            sb.AppendLine(
                                $"            return {context.Project.Python.ResponseClass}[{innerType}](None, True, False, result.status_code, data)");
                        }
                        else
                        {
                            sb.AppendLine(
                                $"            data = dacite.from_dict(data_class={innerType}, data=json.loads(result.content)['data'])");
                            sb.AppendLine(
                                $"            return {context.Project.Python.ResponseClass}[{innerType}](None, True, False, result.status_code, data)");
                        }
                    }
                    sb.AppendLine($"        else:");
                    sb.AppendLine($"            response = {context.Project.Python.ResponseClass}[{innerType}](None, False, True, result.status_code, None)");
                    sb.AppendLine($"            response.load_error(result)");
                    sb.AppendLine($"            return response");
                }
            }

            // Write this category to a file
            var classPath = Path.Combine(clientsDir, cat.WordsToSnakeCase() + "client.py");
            await File.WriteAllTextAsync(classPath, sb.ToString());
        }
    }

    private List<string> BuildImports(GeneratorContext context, List<SchemaField> fields)
    {
        var imports = new List<string>();
        foreach (var field in fields)
        {
            if (!field.Deprecated && field.DataTypeRef != null)
            {
                AddImport(context, imports, field.DataType);
            }
        }

        imports.Sort();
        AddListImport(imports);
        return imports.Distinct().ToList();
    }

    private List<string> BuildImports(GeneratorContext context, string cat)
    {
        var imports = new List<string>();
        imports.Add(
            $"from {context.Project.Python.Namespace}.models.{context.Project.Python.ResponseClass.WordsToSnakeCase()} import {context.Project.Python.ResponseClass}");
        foreach (var endpoint in context.Api.Endpoints)
        {
            if (endpoint.Category == cat && !endpoint.Deprecated)
            {
                foreach (var p in endpoint.Parameters)
                {
                    if (p.DataTypeRef != null)
                    {
                        AddImport(context, imports, p.DataType);
                    }
                }

                // The return type of a file download has special rules
                if (endpoint.ReturnDataType.DataType is "File" or "byte[]" or "binary" or "byte" or "bytearray" or "bytes")
                {
                    // bytes, the immutable list of raw data, is supported natively
                }
                else
                {
                    AddImport(context, imports, endpoint.ReturnDataType.DataType);
                }
            }
        }

        AddListImport(imports);
        imports.Sort();
        return imports.Distinct().ToList();
    }

    private void AddImport(GeneratorContext context, List<string> imports, string dataType)
    {
        if (context.Api.FindEnum(dataType) != null || string.IsNullOrWhiteSpace(dataType) || dataType is "TestTimeoutException" or "File" or "byte[]" or "binary" or "string" or "HttpStatusCode" || dataType == context.Project.Python.ResponseClass)
        {
            return;
        }

        if (dataType.EndsWith("List", StringComparison.OrdinalIgnoreCase))
        {
            AddListImport(imports);
            AddImport(context, imports, dataType.Substring(0, dataType.Length - 4));
            return;
        }

        foreach (var genericName in context.Project.GenericSuffixes ?? Enumerable.Empty<string>())
        {
            if (dataType.EndsWith(genericName))
            {
                AddImport(context, imports, genericName);
                AddImport(context, imports, dataType[..^genericName.Length]);
                return;
            }
        }

        // Check for duplicates
        string importText = $"from {context.Project.Python.Namespace}.models.{dataType.WordsToSnakeCase()} import {dataType}";
        if (!imports.Contains(importText))
        {
            imports.Add(importText);
        }
    }

    private static void AddListImport(List<string> imports)
    {
        string importListText = "from typing import List";
        if (!imports.Contains(importListText))
        {
            imports.Add(importListText);
        }
    }

    private string MakePythonDoc(GeneratorContext context, string description, int indent, List<ParameterField> parameters)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return "";
        }

        var sb = new StringBuilder();
        var prefix = "".PadLeft(indent);

        // According to some documentation, python "summary" lines can be on the line following the """.
        // Let's see if that works.
        sb.AppendLine($"{prefix}\"\"\"");

        // Remove the auto-generated text
        var pos = description.IndexOf("### ", StringComparison.Ordinal);
        if (pos > 0)
        {
            description = description[..pos];
        }

        // Wrap at 72 column width maximum
        sb.AppendLine(description.WrapMarkdown(72, prefix));

        // Add documentation for parameters
        if (parameters != null)
        {
            sb.AppendLine();
            sb.AppendLine($"{prefix}Parameters");
            sb.AppendLine($"{prefix}----------");
            foreach (var p in parameters)
            {
                sb.AppendLine($"{prefix}{p.Name} : {FixupType(context, p.DataType, p.IsArray)}");
                sb.AppendLine(p.DescriptionMarkdown.WrapMarkdown(72, $"{prefix}    "));
            }
        }

        sb.AppendLine($"{prefix}\"\"\"");

        return sb.ToString();
    }

    public async Task Export(GeneratorContext context)
    {
        if (context.Project.Python == null)
        {
            return;
        }
        Console.WriteLine("Exporting Python...");

        await ExportSchemas(context);
        await ExportEndpoints(context);

        // Let's try using Scriban to populate these files
        await ScribanFunctions.ExecuteTemplate(context, 
            "SdkGenerator.Templates.python.ApiClient.scriban",
            context.MakePath(context.Project.Python.Folder, "src", context.Project.Python.Namespace, context.Project.Python.ClassName.WordsToSnakeCase() + ".py"));
        await ScribanFunctions.ExecuteTemplate(context, 
            "SdkGenerator.Templates.python.__init__.scriban",
            context.MakePath(context.Project.Python.Folder, "src", context.Project.Python.Namespace, "__init__.py"));
        await ScribanFunctions.PatchOrTemplate(context, context.MakePath(context.Project.Python.Folder, "pyproject.toml"), 
            "SdkGenerator.Templates.python.pyproject.toml.scriban",
            "version = \"[\\d\\.]+\"",
            $"version = \"{context.Api.Semver3}\"");
    }
    
    public string LanguageName()
    {
        return "Python";
    }
}