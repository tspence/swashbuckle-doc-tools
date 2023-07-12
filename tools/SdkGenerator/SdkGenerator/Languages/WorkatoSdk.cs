using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator.Languages;

public class WorkatoSdk
{
    public static async Task Export(GeneratorContext context)
    {
        if (context.Project.Workato == null)
        {
            return;
        }

        await ExportSchemas(context);
        await ExportEndpoints(context);
    }

    private static async Task ExportSchemas(GeneratorContext context)
    {
        var sb = new StringBuilder();
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
                sb.Append(RubySdk.MakeRubyDoc(item.DescriptionMarkdown, 2, null));
                sb.AppendLine($"  {item.Name.CamelCaseToSnakeCase()}: {{");
                sb.AppendLine("    fields: lambda do|_connection, config_fields, object_definitions|");
                sb.AppendLine("      [");
                foreach (var field in item.Fields.Where(field => !field.Deprecated))
                {
                    sb.AppendLine($"        {{name: \"{field.Name}\", label: \"{field.Name}\", control_type: \"{WorkatoControlType(field)}\", type: {MakeWorkatoType(field)} }},");
                }

                sb.AppendLine("      ],");
                sb.AppendLine("    end");
                sb.AppendLine("  },");
                sb.AppendLine();
            }
        }

        var schemasPath = Path.Combine(context.Project.Workato.Folder, "schemas.rb");
        await File.WriteAllTextAsync(schemasPath, sb.ToString());
    }

    private static string WorkatoControlType(SchemaField field)
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

    private static string MakeWorkatoType(SchemaField field)
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

    private static async Task ExportEndpoints(GeneratorContext context)
    {
        var sb = new StringBuilder();
        int displayPriority = 1;

        // Run through all APIs
        foreach (var endpoint in context.Api.Endpoints.Where(endpoint => !endpoint.Deprecated))
        {
            sb.AppendLine();
            sb.Append(RubySdk.MakeRubyDoc(endpoint.DescriptionMarkdown, 6, endpoint.Parameters));
            sb.AppendLine($"      {endpoint.Name.WordsToSnakeCase()}: {{");
            sb.AppendLine($"        title: \"{endpoint.Name}\",");
            sb.AppendLine($"        subtitle: \"{endpoint.DescriptionMarkdown.Split(Environment.NewLine).FirstOrDefault()}\",");
            sb.AppendLine($"        display_priority: \"{displayPriority++}\",");
            sb.AppendLine($"        input_fields: lambda do |object_definitions|");
            
            // Add input parameters
            foreach (var parameter in endpoint.Parameters)
            {
                sb.AppendLine($"          {{ name: \"{parameter.Name}\", label: \"{parameter.Name}\", control_type: \"{WorkatoControlType(parameter)}\", type: {MakeWorkatoType(parameter)} }},");
            }

            // Ruby code to send the web request
            sb.AppendLine($"        end,");
            sb.AppendLine($"        execute: lambda do |connection, input|");
            foreach (var parameter in endpoint.Parameters)
            {
                sb.AppendLine($"          {parameter.Name} = input[\"{parameter.Name}\"]");
            }
            var method = endpoint.Method.ToLower();
            var url = endpoint.Path.Replace("{", "#{");
            sb.AppendLine($"          result = {method}(\"{url}\", params).after_response do |code, body, headers|");
            sb.AppendLine($"          end");
            
            sb.AppendLine($"        end,");
            sb.AppendLine($"        output_fields: lambda do |object_definitions|");
            sb.AppendLine($"          object_definitions['{endpoint.ReturnDataType.DataType.CamelCaseToSnakeCase()}']");
            sb.AppendLine($"        end,");
            sb.AppendLine($"      }},");
        }

        // Write this category to a file
        var schemasPath = Path.Combine(context.Project.Workato.Folder, "endpoints.rb");
        await File.WriteAllTextAsync(schemasPath, sb.ToString());
    }
}