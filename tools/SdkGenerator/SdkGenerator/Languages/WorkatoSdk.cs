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
                    sb.AppendLine($"        {{name: \"{field.Name}\", label: \"{field.Name}\", control_type: \"{RubySdk.DataTypeHint(field.DataType)}\", type: {MakeWorkatoType(field)} }},");
                }

                sb.AppendLine("      ],");
                sb.AppendLine("    },");
                sb.AppendLine();
            }
        }

        var schemasPath = Path.Combine(context.Project.Workato.Folder, "schemas.rb");
        await File.WriteAllTextAsync(schemasPath, sb.ToString());
    }

    private static string MakeWorkatoType(SchemaField field)
    {
        if (field.IsArray)
        {
            return $":array, of: \"object\", properties: object_definitions[\"{field.DataType.CamelCaseToSnakeCase()}\"]";
        }
        switch (field.DataType)
        {
            case "int32":
            case "integer":
            case "double":
            case "float":
                return ":number";
            default:
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
            sb.AppendLine($"          object_definitions['{endpoint.ReturnDataType.DataType.CamelCaseToSnakeCase()}']");
            sb.AppendLine($"        end,");
            sb.AppendLine($"        execute: lambda do |connection, input|");
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