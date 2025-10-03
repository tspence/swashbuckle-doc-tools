using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator.Languages;

public class WorkatoSdk : ILanguageSdk
{
    public async Task Export(GeneratorContext context)
    {
        if (context.Project.Workato == null)
        {
            return;
        }
        Console.WriteLine("Exporting Workato...");

        await ExportUnifiedFile(context);
    }

    private async Task ExportUnifiedFile(GeneratorContext context)
    {
        var sb = new StringBuilder();
        
        // Add the header
        var header = await ScribanFunctions.ExecuteTemplateString(context, "SdkGenerator.Templates.workato.header.scriban", null);
        sb.AppendLine(header);
        
        sb.AppendLine("  object_definitions: {");
        foreach (var item in context.Api.Schemas)
        {
            // Is this one of the handwritten schemas?  If so, skip it
            var handwritten = (context.Project.Workato.HandwrittenClasses ?? Enumerable.Empty<string>()).ToList();
            handwritten.Add(context.Project.Workato.ResponseClass);
            if (handwritten.Contains(item.Name))
            {
                continue;
            }

            if (item.Fields != null)
            {
                //sb.Append(RubySdk.MakeRubyDoc(item.DescriptionMarkdown, 4, null));
                sb.AppendLine($"    {item.Name.CamelCaseToSnakeCase()}: {{");
                sb.AppendLine("      fields: lambda do|_connection, config_fields, object_definitions|");
                sb.AppendLine("        [");
                foreach (var field in item.Fields.Where(field => !field.Deprecated))
                {
                    sb.AppendLine($"          {{");
                    sb.AppendLine($"            name: \"{field.Name}\",");
                    sb.AppendLine($"            hint: \"{RubySdk.MakeRubyMultilineString(field.DescriptionMarkdown, 14)}\",");
                    if (!field.Nullable)
                    {
                        sb.AppendLine($"            optional: false,");
                    }
                    sb.AppendLine($"            control_type: \"{WorkatoControlType(field)}\",");
                    sb.AppendLine($"            type: {MakeWorkatoType(field)},");
                    sb.AppendLine($"            label: \"{field.Name}\",");
                    sb.AppendLine($"          }},");
                }

                sb.AppendLine("        ],");
                sb.AppendLine("      end");
                sb.AppendLine("    },");
                sb.AppendLine();
            }
        }
        
        // Next in the definition file is a list of methods
        sb.AppendLine("  },");
        sb.AppendLine("  methods: {");
        // Run through all APIs and emit input definitions
        foreach (var endpoint in context.Api.Endpoints.Where(endpoint => !endpoint.Deprecated))
        {
            sb.AppendLine();
            sb.AppendLine(
                $"      {endpoint.Name.WordsToSnakeCase()}_execute: lambda do |_connection, input, extended_input_schema| ");
            sb.AppendLine($"        url = \"{endpoint.Path}\"");
            sb.AppendLine($"        request_payload = call('format_api_request', input, extended_input_schema)");
            sb.AppendLine($"        url = call('format_url_endpoint', request_payload, url)");
            sb.AppendLine($"        {endpoint.Method.ToLower()}(url).request_format_json");
            sb.AppendLine($"          .params(request_payload['query'] || {{}})");
            sb.AppendLine($"          .payload(request_payload['request_body'] || {{}})");
            sb.AppendLine($"          .headers(request_payload['header'] || {{}})");
            sb.AppendLine($"          .after_response do |_code, body, headers|");
            sb.AppendLine($"            {{");
            sb.AppendLine($"              payload: call('clear_name', body),");
            sb.AppendLine($"              headers: call('clear_name', headers)");
            sb.AppendLine($"            }}");
            sb.AppendLine($"        end");
            sb.AppendLine($"      end,");
        }

        sb.AppendLine("  },");
        sb.AppendLine("  actions: {");

        // Run through all APIs and emit input definitions
        foreach (var endpoint in context.Api.Endpoints.Where(endpoint => !endpoint.Deprecated))
        {
            sb.AppendLine();
            //sb.Append(RubySdk.MakeRubyDoc(endpoint.DescriptionMarkdown, 6, endpoint.Parameters));
            sb.AppendLine($"    {endpoint.Name.WordsToSnakeCase()}: {{");
            sb.AppendLine($"      title: \"{endpoint.Name}\",");
            sb.AppendLine($"      hint: \"\"");
            sb.AppendLine($"      subtitle: \"\"");
            sb.AppendLine($"      help: \"{RubySdk.MakeRubyMultilineString(endpoint.DescriptionMarkdown, 8)}\",");
            sb.AppendLine($"      input_fields: lambda do |object_definitions|");
            sb.AppendLine($"        object_definitions['{endpoint.Name.WordsToSnakeCase()}_input']");
            sb.AppendLine($"      end,");
            sb.AppendLine($"      output_fields: lambda do |object_definitions|");
            sb.AppendLine($"        object_definitions['{endpoint.Name.WordsToSnakeCase()}_output']");
            sb.AppendLine($"      end,");
            sb.AppendLine($"      execute: lambda do |object_definitions|");
            sb.AppendLine($"        call(:{endpoint.Name.WordsToSnakeCase()}_execute, connection, input, extended_input_schema)");
            sb.AppendLine($"      end,");
            sb.AppendLine($"    }},");
        }
        sb.AppendLine("  }");
        sb.AppendLine("}");

        // Write this category to a file
        var unifiedFilePath = context.MakePath(context.Project.Workato.Folder, "connector.rb");
        await File.WriteAllTextAsync(unifiedFilePath, sb.ToString());
    }

    private string WorkatoControlType(SchemaField field)
    {
        // Reference: https://docs.workato.com/developing-connectors/sdk/sdk-reference/schema.html#control-types
        // Control type is the UX displayed when someone fills in a field
        switch (field.DataType)
        {
            case "string":
                if (field.Name.Contains("Description", StringComparison.OrdinalIgnoreCase))
                {
                    return "text-area";
                }
                return "text";
            case "uuid":
                return "text";
            case "int32":
            case "integer":
                return "integer";
            case "double":
            case "float":
                return "number";
            case "date":
                return "date";
            case "datetime":
                return "date_time";
            case "boolean":
                return "checkbox";
            default:
                return $"{RubySdk.DataTypeHint(field.DataType)}";
        }
    }

    private string MakeWorkatoType(SchemaField field)
    {
        switch (field.DataType)
        {
            case "string":
                if (field.IsArray)
                {
                    return $":array, of: :string";
                }
                return ":string";
            case "int32":
            case "integer":
            case "double":
            case "float":
                if (field.IsArray)
                {
                    return $":array, of: :number";
                }
                return ":number";
            default:
                if (field.IsArray)
                {
                    return $":array, of: \"object\", properties: object_definitions[\"{field.DataType.CamelCaseToSnakeCase()}\"]";
                }
                return $"\"{RubySdk.DataTypeHint(field.DataType)}\"";
        }
    }

    private string MakeWorkatoQueryString(List<ParameterField> endpointParameters)
    {
        var queryParams = endpointParameters.Where(p => p.Location == "query").ToList();
        if (queryParams.Count == 0)
        {
            return string.Empty;
        }
        
        // Assemble query parameter string using ruby
        var sb = new StringBuilder();
        sb.AppendLine("          queryString = {");
        foreach (var qp in queryParams)
        {
            sb.AppendLine($"            {qp.Name}: input[\"{qp.Name}\"],");
        }
        sb.AppendLine("          }.to_query");
        return sb.ToString();
    }
    
    public string LanguageName()
    {
        return "Workato";
    }
}