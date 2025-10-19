using System;
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
                sb.AppendLine(
                    $"            hint: \"{RubySdk.MakeRubyMultilineString(field.DescriptionMarkdown, 14)}\",");
                sb.AppendLine($"            control_type: \"{WorkatoControlType(context, field)}\",");
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

            sb.AppendLine("        ]");
            sb.AppendLine("      end");
            sb.AppendLine("    },");
            sb.AppendLine();
        }

        // Next produce input and output definitions for each API endpoint
        foreach (var endpoint in context.Api.Endpoints.Where(endpoint => !endpoint.Deprecated))
        {
            sb.AppendLine();
            sb.AppendLine($"    {endpoint.Name.WordsToSnakeCase()}_input: {{");
            sb.AppendLine($"      fields: lambda do|_connection, config_fields, object_definitions|");
            sb.AppendLine($"        [");
            
            // Note that we need to unroll every field individually.
            foreach (var parameter in endpoint.Parameters.Where(p => !p.Deprecated))
            {
                EmitComplexInputParameter(context, sb, parameter);
            }
            sb.AppendLine($"        ]");
            sb.AppendLine($"      end");
            sb.AppendLine($"    }},");
            sb.AppendLine();
            sb.AppendLine($"    {endpoint.Name.WordsToSnakeCase()}_output: {{");
            sb.AppendLine($"      fields: lambda do|_connection, config_fields, object_definitions|");
            // Note that we need to unroll every field individually.
            // Since these fields are generic we need to expand the types
            var outputSchema = context.Api.FindSchema(endpoint.ReturnDataType.DataType);
            if (outputSchema != null)
            {
                EmitComplexOutputParameter(context, sb, outputSchema, []);
            }
            else
            {
                var items = context.ExtractGenerics(endpoint.ReturnDataType.DataType);
                var genericLevelOneSchema = context.Api.FindSchema(items[0]);
                if (genericLevelOneSchema != null)
                {
                    EmitComplexOutputParameter(context, sb, genericLevelOneSchema, items);
                }
            }

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
        
        // Here's where Workato wants us to inject our custom code
        var customMethods = await ScribanFunctions.ExecuteTemplateString(context, "SdkGenerator.Templates.workato.custom-methods.scriban", null);
        sb.AppendLine(customMethods);
        
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
            sb.AppendLine($"      hint: \"{RubySdk.MakeRubyMultilineString(endpoint.DescriptionMarkdown.GetFirstSentence(), 8)}\","); 
            
            // So far these two fields don't seem to be necessary; they seem to be duplicated from "help"
            //sb.AppendLine($"      subtitle: \"\",");
            sb.AppendLine($"      help: \"{RubySdk.MakeRubyMultilineString(endpoint.DescriptionMarkdown.GetSecondSentenceOnwards(), 8)}\",");
            sb.AppendLine($"      input_fields: lambda do |object_definitions|");
            sb.AppendLine($"        object_definitions['{endpoint.Name.WordsToSnakeCase()}_input']");
            sb.AppendLine($"      end,");
            sb.AppendLine($"      output_fields: lambda do |object_definitions|");
            sb.AppendLine($"        object_definitions['{endpoint.Name.WordsToSnakeCase()}_output']");
            sb.AppendLine($"      end,");
            sb.AppendLine($"      execute: lambda do |connection, input, extended_input_schema|");
            sb.AppendLine($"        call(:{endpoint.Name.WordsToSnakeCase()}_execute, connection, input, extended_input_schema)");
            sb.AppendLine($"      end,");
            sb.AppendLine($"    }},");
        }
        sb.AppendLine("  }");
        sb.AppendLine("}");

        // Write this category to a file
        var unifiedFilePath = context.MakePath(context.Project.Workato?.Folder, "connector.rb");
        await File.WriteAllTextAsync(unifiedFilePath, sb.ToString());
    }

    private void EmitComplexOutputParameter(GeneratorContext context, StringBuilder sb, SchemaItem schema, string[] nextGeneric)
    {
        bool isFirst = true;

        // First pass: Find all complex data types
        foreach (var field in schema.Fields.Where(f => !f.Deprecated && !IsBasicDataType(context, f)))
        {
            string line = $"object_definitions['{field.DataType.CamelCaseToSnakeCase()}'].map {{ |x| x.merge(name: '{field.Name}') }}";
            if (!isFirst)
            {
                line = $".concat({line})";
            }
            sb.AppendLine($"        {line}");
            isFirst = false;
        }
        
        // Insert the data field, if necessary
        if (string.Equals(schema.Name, context.Project.Workato?.ResponseClass) 
            && context.Project.Workato?.ResponseDataField != null 
            && nextGeneric.Length > 1)
        {
            string line = $"object_definitions['{nextGeneric[1].CamelCaseToSnakeCase()}'].map {{ |x| x.merge(name: '{context.Project.Workato?.ResponseDataField}') }}";
            if (!isFirst)
            {
                line = $".concat({line})";
            }
            sb.AppendLine($"        {line}");
            isFirst = false;
        }
        
        // Second pass: Simple data types
        var simpleDataTypes = schema.Fields.Where(f => !f.Deprecated && IsBasicDataType(context, f)).ToList();
        if (simpleDataTypes.Any())
        {
            if (!isFirst)
            {
                sb.AppendLine("        .concat(");
            }
            sb.AppendLine("          [");
            foreach (var field in simpleDataTypes)
            {
                sb.AppendLine($"            {{");
                sb.AppendLine($"              \"readOnly\" => true,");
                sb.AppendLine($"              \"name\" => \"{field.Name}\",");
                sb.AppendLine($"              \"hint\" => \"{RubySdk.MakeRubyMultilineString(field.DescriptionMarkdown, 20)}\",");
                sb.AppendLine($"              \"control_type\" => \"{WorkatoControlType(context, field)}\",");
                sb.AppendLine($"              \"type\" => \"{MakeWorkatoTypeName(field)}\",");
                sb.AppendLine($"            }},");
            }
            sb.AppendLine("          ]");
            if (!isFirst)
            {
                sb.AppendLine("        )");
            }
        }
    }

    private bool IsBasicDataType(GeneratorContext context, SchemaField field)
    {
        // Enums are always strings in Ruby
        if (context.Api.IsEnumType(field.DataType))
        {
            return true;
        }
        switch (field.DataType)
        {
            case "string":
            case "int32":
            case "integer":
            case "double":
            case "float":
            case "date":
            case "datetime":
            case "boolean":
                return true;
            default:
                return false;
        }
    }

    private void EmitComplexInputParameter(GeneratorContext context, StringBuilder sb, ParameterField parameter)
    {
        // For basic parameters, we can emit the value directly
        // For objects, we need to unroll them
        if (IsBasicDataType(context, parameter))
        {
            EmitOneParameter(context, sb, parameter, parameter.Location, parameter.Required);
        }
        else
        {
            var schema = context.Api.FindSchema(parameter.DataType);
            if (schema != null)
            {
                foreach (var field in schema.Fields)
                {
                    EmitOneParameter(context, sb, field, parameter.Location, parameter.Required);
                }
            }
        }
    }

    private void EmitOneParameter(GeneratorContext context, StringBuilder sb, SchemaField field, string location, bool required)
    {
        sb.AppendLine($"          {{");
        sb.AppendLine($"            name: \"{field.Name}_{location.ToLower()}\",");
        sb.AppendLine($"            hint: \"{RubySdk.MakeRubyMultilineString(field.DescriptionMarkdown, 16)}\",");
        if (required)
        {
            sb.AppendLine($"            optional: false,");
        }
        sb.AppendLine($"            control_type: \"{WorkatoControlType(context, field)}\",");
        sb.AppendLine($"            type: {MakeWorkatoType(field)},");
        sb.AppendLine($"            label: \"{field.Name}\",");
        sb.AppendLine($"            location: \"{location.ToLower()}\",");
        sb.AppendLine($"          }},");
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
            case "date":
                return "date";
            case "datetime":
                return "date_time";
            case "boolean":
                return "boolean";
            default:
                return "object";
        }    
    }

    private string WorkatoControlType(GeneratorContext context, SchemaField field)
    {
        // Enums are always strings in Ruby
        if (context.Api.IsEnumType(field.DataType)) 
        {
            return context.Api.ReplaceEnumWithType(field.DataType);
        }

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