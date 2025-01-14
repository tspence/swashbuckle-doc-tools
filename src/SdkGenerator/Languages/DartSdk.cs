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

        // pre sort the categories so that the order of imports is correct
        context.Api.Categories = context.Api.Categories.OrderBy(s => $"{s.CamelCaseToSnakeCase()}_client").ToList();

        await ScribanFunctions.ExecuteTemplate(context, 
            "SdkGenerator.Templates.dart.ApiInterface.dart.scriban",
            context.MakePath(context.Project.Dart.Folder, context.Project.Dart.ClassName.CamelCaseToSnakeCase() + ".dart"));
        await ScribanFunctions.ExecuteTemplate(context, 
            "SdkGenerator.Templates.dart.ApiClient.dart.scriban",
            context.MakePath(context.Project.Dart.Folder, context.Project.Dart.ClassName.CamelCaseToSnakeCase() + "_impl.dart"));
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
            sb.AppendLine($"  /// Constructor for the {cat} API collection");
            sb.AppendLine($"  {cat}Client(this._client);");
            sb.AppendLine();
            sb.AppendLine($"  final {context.Project.Dart.ClassName} _client;");
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

                    // Figure out the parameter list - optional ones separate from required
                    var paramListStr = string.Join(", ", from p in endpoint.Parameters where p.Required
                        select $"{FixupType(context, p.DataType, p.IsArray, !p.Required)} {FixupVariableName(p.Name)}");
                    var optionalParamListStr = string.Join(", ", from p in endpoint.Parameters where p.Required == false
                        select $"{FixupType(context, p.DataType, p.IsArray, !p.Required)} {FixupVariableName(p.Name)}");
                    if (!string.IsNullOrWhiteSpace(optionalParamListStr))
                    {
                        if (string.IsNullOrWhiteSpace(paramListStr))
                        {
                            paramListStr = $"{{ {optionalParamListStr} }}";
                        }
                        else
                        {
                            paramListStr += $", {{ {optionalParamListStr} }}";
                        }
                    }

                    // What is our return type?
                    var returnType = FixupType(context, endpoint.ReturnDataType.DataType,
                        endpoint.ReturnDataType.IsArray, false);

                    // Do we have query or body parameters?
                    var hasQueryParams = (from p in endpoint.Parameters where p.Location == "query" select p).Any();
                    var bodyParam = (from p in endpoint.Parameters where p.Location == "body" select p).FirstOrDefault();
                    
                    // Write the method
                    var cleansedPath = endpoint.Path;
                    sb.AppendLine(
                        $"  Future<{context.Project.Dart.ResponseClass}<{returnType}>> {endpoint.Name.ToCamelCase()}({paramListStr}) async {{");
                    if (hasQueryParams)
                    {
                        sb.AppendLine($"    final queryParameters = {{");
                        sb.Append(string.Join("",
                            from p in endpoint.Parameters
                            where p.Location == "query"
                            select $"      '{FixupStringLiteral(p.Name)}': {FixupVariableName(p.Name)},\n".Replace("'\\$", "r'$")));
                        sb.AppendLine($"    }};");
                    }

                    var jsonType = returnType.StartsWith("List<") ? "List<Map<String, dynamic>>" : "Map<String, dynamic>";
                    sb.Append($"    final value = await _client.{endpoint.Method.ToLower()}<{jsonType}>('{cleansedPath}'");
                    
                    if (bodyParam != null)
                    {
                        if (bodyParam.IsArray)
                        {
                            sb.Append($", data: jsonEncode(body.map({bodyParam.DataType}.toJson).toList())");
                            // sb.AppendLine($"    final value = await _client.{endpoint.Method.ToLower()}<{jsonType}>('{cleansedPath}', jsonEncode(json));");
                        }
                        else
                        {
                            sb.Append($", data: jsonEncode({bodyParam.DataType}.toJson(body))");
                        }
                    }
                    
                    if (hasQueryParams)
                    {
                        sb.Append(", queryParameters: queryParameters");
                    }
                    
                    sb.AppendLine(");");

                    // Construct the result
                    sb.AppendLine($"    {returnType}{(returnType != "dynamic" ? "?" : "")} data;");
                    sb.AppendLine($"    final responseData = value.data;");
                    sb.AppendLine($"    if (responseData != null)");
                    sb.AppendLine($"    {{");
                    if (returnType == "dynamic")
                    {
                        sb.AppendLine($"      data = responseData;");
                    } 
                    else if (returnType.StartsWith("List<"))
                    {
                        var innerType = returnType.Substring(5, returnType.Length - 6);
                        var innerDartType = FixupType(context, innerType, false, false);
                        sb.AppendLine($"      data = List<{innerDartType}>.from(responseData.map({innerDartType}.fromJson));");
                    }
                    else
                    {
                        sb.AppendLine($"      data = {returnType}.fromJson(responseData);");
                    }
                    sb.AppendLine($"    }}");
                    sb.AppendLine(
                        $"    return {context.Project.Dart.ResponseClass}<{returnType}>.fromSuccess(data, value.statusCode);");
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
            var classPath = Path.Combine(clientsDir, $"{cat.CamelCaseToSnakeCase()}_client.dart");
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
                AddImport(context, imports, field.DataType, field.IsArray);
            }
        }
        imports.Sort((left, right) =>
        {
            if (left == right)
            {
                return 0;
            }
            
            if (!left.StartsWith("import '.") && !right.StartsWith("import '."))
            {
                return left.CompareTo(right);
            }

            if (!left.StartsWith("import '."))
            {
                return -1;
            }
            
            if (!right.StartsWith("import '."))
            {
                return 1;
            }
            
            var leftUps = left.LastIndexOf("../", StringComparison.CurrentCultureIgnoreCase);
            var rightUps = right.LastIndexOf("../", StringComparison.CurrentCultureIgnoreCase);
            if (leftUps > rightUps)
            {
                return -1;
            }
            
            if (rightUps > leftUps)
            {
                return 1;
            }
            
            return left.CompareTo(right);
        });
        return imports.Distinct().ToList();
    }
    
    private List<string> BuildImports(GeneratorContext context, string cat)
    {
        var imports = new List<string>();
        imports.Add($"import '../{context.Project.Dart.ResponseClass.CamelCaseToSnakeCase()}.dart';");
        imports.Add($"import '../{context.Project.Dart.ClassName.CamelCaseToSnakeCase()}.dart';");
        foreach (var endpoint in context.Api.Endpoints)
        {
            if (endpoint.Category == cat && !endpoint.Deprecated)
            {
                // Do we have a list body parameter?
                var bodyParam = (from p in endpoint.Parameters where p.Location == "body" select p).FirstOrDefault();
                if (bodyParam != null)
                {
                    imports.Add("import 'dart:convert';");
                }
                foreach (var p in endpoint.Parameters)
                {
                    if (p.DataTypeRef != null)
                    {
                        AddImport(context, imports, p.DataType, p.IsArray);
                    }
                }

                // The return type of a file download has special rules
                if (endpoint.ReturnDataType.DataType is "File" or "byte[]" or "binary" or "byte" or "bytearray" or "bytes")
                {
                    // bytes, the immutable list of raw data, is supported natively
                }
                else
                {
                    AddImport(context, imports, endpoint.ReturnDataType.DataType, endpoint.ReturnDataType.IsArray);
                }
            }
        }

        imports.Sort((left, right) =>
        {
            if (left == right)
            {
                return 0;
            }
            
            if (!left.StartsWith("import '.") && !right.StartsWith("import '."))
            {
                return left.CompareTo(right);
            }

            if (!left.StartsWith("import '."))
            {
                return -1;
            }
            
            if (!right.StartsWith("import '."))
            {
                return 1;
            }
            
            var leftUps = left.LastIndexOf("../", StringComparison.CurrentCultureIgnoreCase);
            var rightUps = right.LastIndexOf("../", StringComparison.CurrentCultureIgnoreCase);
            if (leftUps > rightUps)
            {
                return -1;
            }
            
            if (rightUps > leftUps)
            {
                return 1;
            }
            
            return left.CompareTo(right);
        });

        return imports.Distinct().ToList();
    }

    private void AddImport(GeneratorContext context, List<string> imports, string dataType, bool isArray)
    {
        if (isArray && IsJsonConvertType(dataType))
        {
            imports.Add("import 'dart:convert';");
        }
        
        if (context.Api.FindEnum(dataType) != null || string.IsNullOrWhiteSpace(dataType) || dataType == context.Project.Dart.ResponseClass)
        {
            return;
        }

        if (dataType is "TestTimeoutException" or "File" or "byte[]" or "binary" or "HttpStatusCode" or "date-time" or "uuid" or "int32" or "boolean" or "object" or "date" or "double" or "string")
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
            var thisImport = $"import '../models/{rawDataType.CamelCaseToSnakeCase()}.dart';";
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
        
        // Dart doesn't like variables that start with an underscore
        var name = sb.ToString();
        if (name.StartsWith('_'))
        {
            name = name[1..];
        }

        return name;
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
                        if (field.IsArray)
                        {
                            // we only want the type used inside the list object
                            var dartType = FixupType(context, field.DataType, false, false);
                            if (IsJsonConvertType(field.DataType))
                            {
                                // jsonDecode(json['userIds']).cast<String>();
                                sb.AppendLine($"    {field.Name} = (jsonDecode(json['{field.Name}'] as String) as List<dynamic>).cast<{dartType}>(),");
                            }
                            else
                            {
                                sb.AppendLine($"    {field.Name} = List<{dartType}>.from((json['{field.Name}'] as List<Map<String, dynamic>>).map({dartType}.fromJson)),");
                            }
                        }
                        else
                        {
                            sb.AppendLine($"    {field.Name} = json['{field.Name}'] as {FixupType(context, field.DataType, field.IsArray, field.Nullable)},");
                        }
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

                sb.AppendLine("}");
                var classPath = Path.Combine(modelsDir, item.Name.CamelCaseToSnakeCase() + ".dart");
                await File.WriteAllTextAsync(classPath, sb.ToString());
            }
        }    
    }

    private bool IsJsonConvertType(string dataType)
    {
        return dataType.ToLowerInvariant() is "string" or "uuid";
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
               + $"/// (c) {project.CopyrightHolder}\n"
               + "///\n"
               + "/// For the full copyright and license information, please view the LICENSE\n"
               + "/// file that was distributed with this source code.\n"
               + "///\n"
               + $"/// @author     {project.AuthorName} <{project.AuthorEmail}>\n"
               + $"/// @copyright  {project.CopyrightHolder}\n"
               + $"/// @link       {project.Dart.GithubUrl}\n"
               + "///\n"
               + "library;\n";    
    }

    public string LanguageName()
    {
        return "Dart";
    }
}