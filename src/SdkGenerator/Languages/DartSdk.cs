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
        Console.WriteLine("Exporting Dart...");
        
        await ExportSchemas(context);
        await ExportEndpoints(context);

        await ScribanFunctions.ExecuteTemplate(context, 
            "SdkGenerator.Templates.dart.ApiInterface.dart.scriban",
            context.MakePath(context.Project.Dart.Folder, context.Project.Dart.ClassName + ".dart"));
        await ScribanFunctions.ExecuteTemplate(context, 
            "SdkGenerator.Templates.dart.ApiClient.dart.scriban",
            context.MakePath(context.Project.Dart.Folder, context.Project.Dart.ClassName + "Impl.dart"));
    }

    private async Task ExportEndpoints(GeneratorContext context)
    {
        var clientsDir = context.MakePath(context.Project.Dart.Folder, "clients");
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

            // Produce imports
            foreach (var import in BuildImports(context, cat))
            {
                sb.AppendLine(import);
            }

            sb.AppendLine();
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
                // File handling is still TBD
                var isFileApi = 
                    (from p in endpoint.Parameters where p.Location == "form" select p).Any()
                    || endpoint.ReturnDataType.DataType == "byte";
                if (endpoint.Category == cat && !endpoint.Deprecated && !isFileApi)
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
                    var bodyParam = (from p in endpoint.Parameters where p.Location == "body" select p).FirstOrDefault();
                    
                    // Write the method
                    var cleansedPath  = endpoint.Path.Replace("{", "${");
                    if (hasQueryParams)
                    {
                        cleansedPath += "?${queryString}";
                    }
                    sb.AppendLine(
                        $"  Future<{context.Project.Dart.ResponseClass}<{returnType}>> {endpoint.Name.ToCamelCase()}({paramListStr}) async {{");
                    if (hasQueryParams)
                    {
                        sb.AppendLine($"    Map<String, dynamic> queryParameters = {{");
                        sb.Append(string.Join("",
                            from p in endpoint.Parameters
                            where p.Location == "query"
                            select $"      '{FixupStringLiteral(p.Name)}': {FixupVariableName(p.Name)},\n"));
                        sb.AppendLine($"    }};");
                        sb.AppendLine("    String queryString = Uri(queryParameters: queryParameters).query;");
                    }

                    if (bodyParam != null)
                    {
                        if (bodyParam.IsArray)
                        {
                            sb.AppendLine(
                                $"    var json = jsonEncode(body, toEncodable: (e) => {bodyParam.DataType}.toJson(e as {bodyParam.DataType}));");
                            sb.AppendLine($"    var value = await _client.{endpoint.Method.ToLower()}(\"{cleansedPath}\", jsonDecode(json));");
                        }
                        else
                        {
                            sb.AppendLine($"    var value = await _client.{endpoint.Method.ToLower()}(\"{cleansedPath}\", {bodyParam.DataType}.toJson(body));");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"    var value = await _client.{endpoint.Method.ToLower()}(\"{cleansedPath}\");");
                    }

                    // Construct the result
                    sb.AppendLine($"    {returnType}? data;");
                    sb.AppendLine($"    if (value.data != null)");
                    sb.AppendLine($"    {{");
                    if (returnType == "dynamic")
                    {
                        sb.AppendLine($"      data = value.data;");
                    } 
                    else if (returnType.StartsWith("List<"))
                    {
                        sb.AppendLine($"      data = {returnType}.from(value.data.map((i) => {returnType.Substring(5,returnType.Length-6)}.fromJson(i)));");
                    }
                    else
                    {
                        sb.AppendLine($"      data = {returnType}.fromJson(value.data);");
                    }
                    sb.AppendLine($"    }}");
                    sb.AppendLine(
                        $"    return {context.Project.Dart.ResponseClass}<{returnType}>(value.success, value.error, value.statusCode, data);");
                    // sb.AppendLine($"    var result = {context.Project.Dart.ResponseClass}<{returnType}>();");
                    // sb.AppendLine($"    result.success = value.success;");
                    // sb.AppendLine($"    result.hasError = value.hasError;");
                    // sb.AppendLine($"    result.error = value.error;");
                    // sb.AppendLine($"    result.statusCode = value.statusCode;");
                    // sb.AppendLine($"    return result;");
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


    private List<string> BuildImportsForSchema(GeneratorContext context, SchemaItem item)
    {
        var imports = new List<string>();
        foreach (var field in item.Fields)
        {
            if (!field.Deprecated && field.DataType != item.Name)
            {
                AddImport(context, imports, field.DataType);
            }
        }
        imports.Sort();
        return imports.Distinct().ToList();
    }
    
    private List<string> BuildImports(GeneratorContext context, string cat)
    {
        var imports = new List<string>();
        imports.Add($"import '../{context.Project.Dart.ResponseClass}.dart';");
        imports.Add($"import '../{context.Project.Dart.ClassName}.dart';");
        foreach (var endpoint in context.Api.Endpoints)
        {
            if (endpoint.Category == cat && !endpoint.Deprecated)
            {
                // Do we have a list body parameter?
                var bodyParam = (from p in endpoint.Parameters where p.Location == "body" select p).FirstOrDefault();
                if (bodyParam is { IsArray: true } && !imports.Contains("import 'dart:convert';"))
                {
                    imports.Add("import 'dart:convert';");
                }
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

        imports.Sort();
        return imports.Distinct().ToList();
    }

    private void AddImport(GeneratorContext context, List<string> imports, string dataType)
    {
        if (context.Api.FindEnum(dataType) != null || string.IsNullOrWhiteSpace(dataType) || dataType is "TestTimeoutException" or "File" or "byte[]" or "binary" or "string" or "HttpStatusCode" || dataType == context.Project.Python.ResponseClass)
        {
            return;
        }
        
        // Ignore basic types
        if (dataType is "date-time" or "uuid" or "int32" or "boolean" or "object" or "date" or "double")
        {
            return;
        }

        // File handling is still TBD 
        if (dataType == "byte" || dataType == "byte[]")
        {
            return;
        }

        var rawDataType = context.RemoveGenericSchema(dataType);
        if (rawDataType.EndsWith("List"))
        {
            rawDataType = rawDataType.Substring(0, rawDataType.Length - 4);
        }

        if (!string.IsNullOrWhiteSpace(rawDataType))
        {
            var thisImport = $"import '../models/{rawDataType}.dart';";
            if (!imports.Contains(thisImport))
            {
                imports.Add(thisImport);
            }
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
        var modelsDir = context.MakePath(context.Project.Dart.Folder, "models");
        Directory.CreateDirectory(modelsDir);
        foreach (var modelFile in Directory.EnumerateFiles(modelsDir, "*.dart"))
        {
            File.Delete(modelFile);
        }

        foreach (var item in context.Api.Schemas)
        {
            var handwritten = (context.Project.Dart.HandwrittenClasses ?? Enumerable.Empty<string>()).ToList();
            handwritten.Add(context.Project.Dart.ResponseClass);
            if (handwritten.Contains(item.Name))
            {
                continue;
            }
            if (item.Fields != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine(FileHeader(context.Project));
                sb.AppendLine();

                // Produce imports
                foreach (var import in BuildImportsForSchema(context, item))
                {
                    sb.AppendLine(import);
                }

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
                        sb.AppendLine($"    {FixupType(context, field.DataType, field.IsArray, field.Nullable)} {field.Name};");
                        // Apparently getters and setters aren't quite mandatory
                        // lets leave this around for now
                        // sb.AppendLine();
                        // sb.AppendLine(
                        //     $"    {FixupType(context, field.DataType, field.IsArray, field.Nullable)} get get{field.Name.ToProperCase()} {{");
                        // sb.AppendLine(
                        //     $"      return {field.Name};");
                        // sb.AppendLine("    }");
                        // sb.AppendLine();
                        // sb.Append(field.DescriptionMarkdown.ToDartDoc(4));
                        // sb.AppendLine(
                        //     $"    set set{field.Name.ToProperCase()}({FixupType(context, field.DataType, field.IsArray, field.Nullable)} newValue) {{");
                        // sb.AppendLine(
                        //     $"      {field.Name} = newValue;");
                        // sb.AppendLine("    }");
                    }
                }
                
                // Basic constructor
                sb.AppendLine();
                sb.AppendLine($"  {item.Name}({{");
                foreach (var field in item.Fields)
                {
                    if (!field.Deprecated)
                    {
                        sb.AppendLine($"    {(field.Nullable ? "" : "required ")}this.{field.Name.ToCamelCase()},");
                    }
                }
                sb.AppendLine($"  }});");
                
                // Implement JSON deserialization logic
                // https://stackoverflow.com/questions/55292633/how-to-convert-json-string-to-json-object-in-dart-flutter
                sb.AppendLine();
                sb.AppendLine($"  {item.Name}.fromJson(Map<String, dynamic> json) :");
                foreach (var field in item.Fields)
                {
                    if (!field.Deprecated)
                    {
                        sb.AppendLine($"    {field.Name} = json['{field.Name}'],");
                    }
                }

                // Convert the last comma into a semicolon
                sb.Length -= Environment.NewLine.Length + 1;
                sb.AppendLine(";");
                sb.AppendLine();
                
                // JSON serialization
                sb.AppendLine($"  static Map<String, dynamic> toJson({item.Name} item) {{");
                sb.AppendLine($"    final data = <String, dynamic>{{}};");
                foreach (var field in item.Fields)
                {
                    if (!field.Deprecated)
                    {
                        sb.AppendLine($"    data['{field.Name}'] = item.{field.Name};");
                    }
                }
                sb.AppendLine($"    return data;");
                sb.AppendLine($"  }}");

                sb.AppendLine("}");
                var classPath = Path.Combine(modelsDir, item.Name + ".dart");
                await File.WriteAllTextAsync(classPath, sb.ToString());
            }
        }    
    }

    private string FixupType(GeneratorContext context, string typeName, bool isArray, bool isNullable)
    {
        var s = context.Api.ReplaceEnumWithType(typeName);

        switch (s)
        {
            case "string":
            case "uuid":
            case "date":
            case "uri":
            case "Uri":
            case "tel":
            case "email":
            case "HttpStatusCode":
                s = "String";
                break;
            case "date-time":
                s = "DateTime";
                break;
            case "int32":
            case "integer":
                s = "int";
                break;
            case "double":
                s = "double";
                break;
            case "int64":
                s = "Int64";
                break;
            case "float":
                s = "Float";
                break;
            case "object":
                s = "Object";
                break;
            case "boolean":
                s = "bool";
                break;
            case "File":
            case "binary":
            case "byte":
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

        s = context.RemoveGenericSchema(s);
        if (s.EndsWith("List"))
        {
            s = $"List<{s[..^4]}>";
        }

        if (isNullable)
        {
            s = s + "?";
        }

        if (string.IsNullOrWhiteSpace(s))
        {
            return "dynamic";
        }
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