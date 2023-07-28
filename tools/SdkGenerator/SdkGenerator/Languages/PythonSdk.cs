using System;
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

    private string FixupType(GeneratorContext context, string typeName, bool isArray, bool isParamHint = false, bool isReturnHint = false)
    {
        var s = typeName;
        if (context.Api.IsEnum(typeName))
        {
            s = context.Api.FindSchema(typeName).EnumType;
        }

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
                s = "Response";
                break;
        }

        foreach (var genericName in context.Project.GenericSuffixes ?? Enumerable.Empty<string>())
        {
            if (s.EndsWith(genericName))
            {
                s = $"{genericName}[{s[..^genericName.Length]}]";
                return s;
            }
        }

        if (!isParamHint && !isReturnHint)
        {
            s = "object";
        }

        if (isArray)
        {
            if (isParamHint)
            {
                s = "list[object]";
            }
            else
            {
                s = "list[" + s + "]";    
            }
        }

        return s;
    }

    private async Task ExportSchemas(GeneratorContext context)
    {
        var modelsDir = Path.Combine(context.Project.Python.Folder, "src", context.Project.Python.Namespace, "models");
        await CleanModuleDirectory(modelsDir);

        foreach (var item in context.Api.Schemas)
        {
            if (item.Fields != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine(FileHeader(context.Project));

                // The "Future" import apparently must be the very first one in the file
                if (item.Fields.Any(f => f.DataType == item.Name))
                {
                    sb.AppendLine("from __future__ import annotations");
                }

                // Add in all the rest of the imports
                sb.AppendLine("from dataclasses import dataclass");
                sb.AppendLine();
                sb.AppendLine("@dataclass");
                sb.AppendLine($"class {item.Name}:");
                sb.Append(MakePythonDoc(context, item.DescriptionMarkdown, 4, null));
                sb.AppendLine();
                foreach (var f in item.Fields)
                {
                    sb.AppendLine($"    {f.Name}: {FixupType(context, f.DataType, f.IsArray)} | None = None");
                }

                sb.AppendLine();

                if (item.Name.Equals("ErrorResult", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"    @classmethod");
                    sb.AppendLine($"    def from_json(cls, data: dict):");
                    sb.AppendLine($"        obj = cls()");
                    sb.AppendLine($"        for key, value in data.items():");
                    sb.AppendLine($"            if hasattr(obj, key):");
                    sb.AppendLine($"                setattr(obj, key, value)");
                    sb.AppendLine($"        return obj");
                }

                // Add helper methods for users to serialize objects
                sb.AppendLine($"    def to_dict(self) -> dict:");
                sb.AppendLine($"        return dataclass.asdict(self)");

                var modelPath = Path.Combine(modelsDir, item.Name.WordsToSnakeCase() + ".py");
                await File.WriteAllTextAsync(modelPath, sb.ToString());
            }
        }
    }

    private async Task CleanModuleDirectory(string pyModuleDir)
    {
        Directory.CreateDirectory(pyModuleDir);

        var initFile = Path.Combine(pyModuleDir, "__init__.py");
        if (!File.Exists(initFile))
        {
            await File.Create(initFile).DisposeAsync();
        }

        foreach (var pyFile in Directory.EnumerateFiles(pyModuleDir, "*.py").Where(f => !f.EndsWith("__init__.py")))
        {
            File.Delete(pyFile);
        }
    }

    private async Task ExportEndpoints(GeneratorContext context)
    {
        var clientsDir = Path.Combine(context.Project.Python.Folder, "src", context.Project.Python.Namespace, "clients");
        await CleanModuleDirectory(clientsDir);

        // Gather a list of unique categories
        foreach (var cat in context.Api.Categories)
        {
            var sb = new StringBuilder();

            // Let's see if we have to do any imports
            var imports = BuildImports(context, cat);

            // Construct header
            sb.Append(FileHeader(context.Project));
            sb.AppendLine(
                $"from {context.Project.Python.Namespace}.{context.Project.Python.ResponseClass.ProperCaseToSnakeCase()} import {context.Project.Python.ResponseClass}");
            sb.AppendLine($"from {context.Project.Python.Namespace}.models.errorresult import ErrorResult");
            foreach (var import in imports.Distinct())
            {
                sb.AppendLine(import);
            }

            sb.AppendLine();
            sb.AppendLine($"class {cat}Client:");
            sb.AppendLine("    \"\"\"");
            sb.AppendLine($"    API methods related to {cat}");
            sb.AppendLine("    \"\"\"");
            sb.AppendLine(
                $"    from {context.Project.Python.Namespace}.{context.Project.Python.ClassName.ProperCaseToSnakeCase()} import {context.Project.Python.ClassName}");
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
                    var isFileDownload = endpoint.ReturnDataType.DataType is "byte[]" or "binary" or "File";
                    var originalReturnDataType = FixupType(context, endpoint.ReturnDataType.DataType,
                        endpoint.ReturnDataType.IsArray, isReturnHint: true);
                    string returnDataType;
                    if (!isFileDownload)
                    {
                        returnDataType = $"{context.Project.Python.ResponseClass}[{originalReturnDataType}]";
                    }
                    else
                    {
                        returnDataType = "Response";
                    }

                    // Figure out the parameter list
                    var hasBody = (from p in endpoint.Parameters where p.Location == "body" select p).Any();
                    var paramListStr = string.Join(", ", from p in endpoint.Parameters select $"{p.Name}: {FixupType(context, p.DataType, p.IsArray, isParamHint: true)}");
                    var bodyJson = string.Join(", ", from p in endpoint.Parameters where p.Location == "query" select $"\"{p.Name}\": {p.Name}");
                    var fileUploadParam = (from p in endpoint.Parameters where p.Location == "form" select p).FirstOrDefault();

                    // Write the method
                    sb.AppendLine($"    def {endpoint.Name.WordsToSnakeCase()}(self, {paramListStr}) -> {returnDataType}:");
                    sb.Append(MakePythonDoc(context, endpoint.DescriptionMarkdown, 8, endpoint.Parameters));
                    sb.AppendLine(endpoint.Path.Contains('{')
                        ? $"        path = f\"{endpoint.Path}\""
                        : $"        path = \"{endpoint.Path}\"");
                    sb.AppendLine(
                        $"        result = self.client.send_request(\"{endpoint.Method.ToUpper()}\", path, {(hasBody ? "body" : "None")}, {(string.IsNullOrWhiteSpace(paramListStr) ? "None" : "{" + bodyJson + "}")}, {(fileUploadParam == null ? "None" : fileUploadParam.Name)})");
                    if (isFileDownload)
                    {
                        sb.AppendLine("        return result");
                    }
                    else
                    {
                        bool isHandled = false;
                        sb.AppendLine("        if result.status_code >= 200 and result.status_code < 300:");
                        if (originalReturnDataType.StartsWith("list", StringComparison.OrdinalIgnoreCase))
                        {
                            // Use a list comprehension to unpack array responses
                            sb.AppendLine(
                                $"            return {context.Project.Python.ResponseClass}(True, result.status_code, [{endpoint.ReturnDataType.DataType}(**item) for item in result.json()], None)");
                            isHandled = true;
                        }

                        if (!isHandled)
                        {
                            foreach (var genericName in context.Project.GenericSuffixes ?? Enumerable.Empty<string>())
                            {
                                if (originalReturnDataType.StartsWith(genericName, StringComparison.OrdinalIgnoreCase))
                                {
                                    // Fetch results don't unpack as expected, use from_json helper method
                                    sb.AppendLine(
                                        $"            return {context.Project.Python.ResponseClass}(True, result.status_code, {genericName}.from_json(result.json(), {endpoint.ReturnDataType.DataType[..^genericName.Length]}), None)");                            
                                    context.LogError("halp");
                                    isHandled = true;
                                }
                            }
                        }
                        if (!isHandled)
                        {
                            sb.AppendLine(
                                $"            return {context.Project.Python.ResponseClass}(True, result.status_code, {originalReturnDataType}(**result.json()), None)");
                        }

                        sb.AppendLine("        else:");
                        sb.AppendLine(
                            $"            return {context.Project.Python.ResponseClass}(False, result.status_code, None, ErrorResult.from_json(result.json()))");
                    }
                }
            }

            // Write this category to a file
            var classPath = Path.Combine(clientsDir, cat.WordsToSnakeCase() + "_client.py");
            await File.WriteAllTextAsync(classPath, sb.ToString());
        }
    }

    private List<string> BuildImports(GeneratorContext context, string cat)
    {
        var imports = new List<string>();
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
                if (endpoint.ReturnDataType.DataType is "File" or "byte[]" or "binary")
                {
                    imports.Add("from requests.models import Response");
                }
                else
                {
                    AddImport(context, imports, endpoint.ReturnDataType.DataType);
                }
            }
        }

        imports.Sort();
        return imports.Distinct().ToList();
    }

    private void AddImport(GeneratorContext context, List<string> imports, string dataType)
    {
        if (context.Api.IsEnum(dataType) || dataType is null or "TestTimeoutException" or "File" or "byte[]" or "binary" or "string")
        {
            return;
        }

        if (dataType is "ActionResultModel")
        {
            imports.Add($"from {context.Project.Python.Namespace}.models.actionresultmodel import ActionResultModel");
        }
        else
        {
            foreach (var genericName in context.Project.GenericSuffixes ?? Enumerable.Empty<string>())
            {
                if (dataType.EndsWith(genericName))
                {
                    imports.Add($"from {context.Project.Python.Namespace}.{genericName.WordsToSnakeCase()} import {genericName}");
                    dataType = dataType[..^genericName.Length];
                }
            }

            imports.Add($"from {context.Project.Python.Namespace}.models.{dataType.WordsToSnakeCase()} import {dataType}");
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
                sb.AppendLine($"{prefix}{p.Name} : {FixupType(context, p.DataType, p.IsArray, isParamHint: true)}");
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

        await ExportSchemas(context);
        await ExportEndpoints(context);

        // Let's try using Scriban to populate these files
        await ScribanFunctions.ExecuteTemplate(context, 
            Path.Combine(".", "templates", "python", "ApiClient.py.scriban"),
            Path.Combine(context.Project.Python.Folder, "src", context.Project.Python.Namespace, context.Project.Python.ClassName.ProperCaseToSnakeCase() + ".py"));
        await ScribanFunctions.ExecuteTemplate(context, 
            Path.Combine(".", "templates", "python", "__init__.py.scriban"),
            Path.Combine(context.Project.Python.Folder, "src", context.Project.Python.Namespace, "__init__.py"));
        await Extensions.PatchFile(context, Path.Combine(context.Project.Python.Folder, "setup.cfg"), "version = [\\d\\.]+",
            $"version = {context.OfficialVersion}");
    }
    
    public string LanguageName()
    {
        return "Python";
    }
}