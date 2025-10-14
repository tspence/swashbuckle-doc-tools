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
        
        sb.AppendLine("  #");
        sb.AppendLine("  # Object Definitions: Defined inputs and outputs for APIs");
        sb.AppendLine("  #");
        sb.AppendLine("  object_definitions: {");
        foreach (var item in context.Api.Schemas)
        {
            // Testing: Do not skip handwritten classes since Workato needs explicit definitions for everything
            // and there's no mechanism for handwriting classes outside of this single file
            // // Is this one of the handwritten schemas?  If so, skip it
            // var handwritten = (context.Project.Workato.HandwrittenClasses ?? Enumerable.Empty<string>()).ToList();
            // handwritten.Add(context.Project.Workato.ResponseClass);
            // if (handwritten.Contains(item.Name))
            // {
            //     continue;
            // }

            if (item.Fields != null)
            {
                sb.AppendLine($"    {item.Name.CamelCaseToSnakeCase()}: {{");
                sb.AppendLine("      fields: lambda do|_connection, config_fields, object_definitions|");
                sb.AppendLine("        [");
                foreach (var field in item.Fields.Where(field => !field.Deprecated))
                {
                    sb.AppendLine($"          {{");
                    if (!field.Nullable)
                    {
                        sb.AppendLine($"            optional: false,");
                    }
                    else
                    {
                        sb.AppendLine($"            nullable: true,");
                    }
                    sb.AppendLine($"            name: \"{field.Name}\",");
                    sb.AppendLine($"            hint: \"{RubySdk.MakeRubyMultilineString(field.DescriptionMarkdown, 14)}\",");
                    sb.AppendLine($"            control_type: \"{WorkatoControlType(field)}\",");
                    sb.AppendLine($"            type: \"{MakeWorkatoTypeName(field)}\",");
                    if (field.IsArray && field.DataType == "string")
                    {
                        sb.AppendLine($"            additionalProperties: {{");
                        sb.AppendLine($"              of: \"string\",");
                        sb.AppendLine($"              type: \"array\",");
                        sb.AppendLine($"            }},");
                    }
                    sb.AppendLine($"            label: \"{field.Name}\",");
                    sb.AppendLine($"          }},");
                }

                sb.AppendLine("        ],");
                sb.AppendLine("      end");
                sb.AppendLine("    },");
                sb.AppendLine();
            }
        }
        
        // Next produce input and output definitions for each API endpoint
        foreach (var endpoint in context.Api.Endpoints.Where(endpoint => !endpoint.Deprecated))
        {
            sb.AppendLine();
            sb.AppendLine($"    {endpoint.Name.WordsToSnakeCase()}_input: {{");
            sb.AppendLine($"      fields: lambda do|_connection, config_fields, object_definitions|");
            sb.AppendLine($"        [");
            foreach (var parameter in endpoint.Parameters.Where(p => !p.Deprecated))
            {
                sb.AppendLine($"          {{");
                sb.AppendLine($"            name: \"{parameter.Name}_{parameter.Location.ToLower()}\",");
                sb.AppendLine($"            hint: \"{RubySdk.MakeRubyMultilineString(parameter.DescriptionMarkdown, 16)}\",");
                if (!parameter.Nullable)
                {
                    sb.AppendLine($"            optional: false,");
                }
                sb.AppendLine($"            control_type: \"{WorkatoControlType(parameter)}\",");
                sb.AppendLine($"            type: {MakeWorkatoType(parameter)},");
                sb.AppendLine($"            label: \"{parameter.Name}\",");
                sb.AppendLine($"            location: \"{parameter.Location.ToLower()}\",");
                sb.AppendLine($"          }},");
            }
            sb.AppendLine($"        ],");
            sb.AppendLine($"      end");
            sb.AppendLine($"    }},");
            sb.AppendLine();
            sb.AppendLine($"    {endpoint.Name.WordsToSnakeCase()}_output: {{");
            sb.AppendLine($"      fields: lambda do|_connection, config_fields, object_definitions|");
            sb.AppendLine($"        [");
            sb.AppendLine($"        ],");
            sb.AppendLine($"      end");
            sb.AppendLine($"    }},");
            sb.AppendLine();
        }

        
        // Next in the definition file is a list of methods
        sb.AppendLine("  },");
        sb.AppendLine();
        sb.AppendLine("  #");
        sb.AppendLine("  # Methods: This is the ruby code that calls an API");
        sb.AppendLine("  #");
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
        sb.AppendLine();
        sb.AppendLine("  #");
        sb.AppendLine("  # Actions: These are the definitions of APIs as they appear in Workato's UX");
        sb.AppendLine("  #");
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

    private string MakeWorkatoTypeName(SchemaField field)
    {
        if (field.IsArray)
        {
            return "object";
        }
        switch (field.DataType)
        {
            case "string":
                return "string";
            case "int32":
            case "integer":
            case "double":
            case "float":
                return "number";
            default:
                return "object";
        }    
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
    
    public string LanguageName()
    {
        return "Workato";
    }
}