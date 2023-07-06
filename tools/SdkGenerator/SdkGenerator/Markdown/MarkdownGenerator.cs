using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Polly;
using RestSharp;
using SdkGenerator.Project;
using SdkGenerator.Readme;
using SdkGenerator.Schema;

namespace SdkGenerator.Markdown;

public static class MarkdownGenerator
{
    public static async Task UploadSchemas(GeneratorContext context, string format)
    {
        var order = 1;
        
        if (!string.IsNullOrWhiteSpace(context.Project?.Readme?.ApiKey))
        {
            // Find the category
            var categories = await ReadmeTools.GetCategories(context);
            var cat = categories.FirstOrDefault(item => String.Equals(item.Title, context.Project.Readme.ModelCategory, StringComparison.OrdinalIgnoreCase));
            if (cat == null)
            {
                cat = await ReadmeTools.CreateCategory(context, context.Project.Readme.ModelCategory);
            }
        
            // Upload each API as a guide within that category
            foreach (var schema in context.Api.Schemas.Where(schema => schema.Fields != null))
            {
                try
                {
                    var markdownText = format switch
                    {
                        "table" => MakeMarkdownTable(schema, context.Api),
                        "list" => MakeMarkdownBulletList(context, schema),
                        _ => ""
                    };

                    await ReadmeTools.UploadGuideToReadme(context, cat.Id, schema.Name, order++, markdownText);
                }
                catch (Exception e)
                {
                    context.Log($"Exception while parsing model for {schema.Name}: {e}");
                }
            }
        }
    }
    
    public static async Task WriteMarkdownFiles(GeneratorContext context, string format)
    {
        foreach (var schema in context.Api.Schemas.Where(schema => schema.Fields != null))
        {
            try
            {
                var markdownText = format switch
                {
                    "table" => MakeMarkdownTable(schema, context.Api),
                    "list" => MakeMarkdownBulletList(context, schema),
                    _ => ""
                };

                var filename = Path.Combine(context.Project.SwaggerSchemaFolder, schema.Name.ToLower() + ".md");
                await File.WriteAllTextAsync(filename, markdownText);
            }
            catch (Exception e)
            {
                context.Log($"Exception while parsing model for {schema.Name}: {e}");
            }
        }
    }

    /// <summary>
    /// This is the older table-style data definition.  It was harder to read when the description of each field
    /// became more complex.  We switched away to using the bullet-list version.
    /// </summary>
    /// <param name="item"></param>
    /// <param name="api"></param>
    /// <returns></returns>
    private static string MakeMarkdownTable(SchemaItem item, ApiSchema api)
    {
        var sb = new StringBuilder();
        sb.AppendLine(item.DescriptionMarkdown);
        sb.AppendLine();

        // Link all the API endpoints that work with this model
        foreach (var endpoint in api.Endpoints.Where(endpoint => endpoint.ReturnDataType.DataType == item.Name))
        {
            sb.AppendLine($"* [{endpoint.Name}](/reference/test)");
        }

        // Provide definitions for all the fields
        sb.AppendLine("# Fields");
        sb.AppendLine("| Field | Type | Notes |");
        sb.AppendLine("|--|--|--|");
        foreach (var field in item.Fields)
        {
            var modifiers = "";
            if (field.Nullable)
            {
                modifiers += "(nullable) ";
            }

            if (field.ReadOnly)
            {
                modifiers += "(read-only) ";
            }

            if (field.Deprecated)
            {
                modifiers += "(deprecated) ";
            }

            if (field.MaxLength.HasValue && field.MinLength.HasValue)
            {
                modifiers += $"(between {field.MinLength} and {field.MaxLength} characters) ";
            }
            else if (field.MinLength.HasValue)
            {
                modifiers += $"(minimum {field.MinLength} characters) ";
            }
            else if (field.MaxLength.HasValue)
            {
                modifiers += $"(maximum {field.MaxLength} characters) ";
            }

            sb.AppendLine($"| **{field.Name}** | {field.DataType} {modifiers} | {FixupLines(field.DescriptionMarkdown)} |");
        }

        return sb.ToString();
    }

    private static string FixupLines(string str)
    {
        return (str ?? "").Replace(Environment.NewLine, "<br />");
    }

    private static string MakeMarkdownBulletList(GeneratorContext context, SchemaItem item)
    {
        var sb = new StringBuilder();
        sb.AppendLine(item.DescriptionMarkdown);
        sb.AppendLine();

        // Link all the API endpoints that work with this model
        var methods = new List<string>();
        foreach (var endpoint in context.Api.Endpoints)
        {
            var endpointDataType = endpoint.ReturnDataType.DataType;
            foreach (var genericName in context.Project.GenericSuffixes ?? Enumerable.Empty<string>())
            {
                endpointDataType = endpointDataType.Replace(genericName, "");   
            }
            if (endpointDataType == item.Name)
            {
                var fixedPath = endpoint.Path.Substring(1).ToLower().Replace('/', '-').Replace("{", "").Replace("}", "");
                methods.Add($"* [{endpoint.Name}](/reference/{endpoint.Method.ToLower()}_{fixedPath})");
            }
        }

        if (methods.Count > 0)
        {
            sb.AppendLine("## Methods");
            sb.AppendLine();
            sb.AppendLine("The following API methods use this data model.");
            sb.AppendLine();
            foreach (var line in methods)
            {
                sb.AppendLine(line);
            }
        }

        // Group fields into buckets
        List<SchemaField> requiredFields = new();
        List<SchemaField> optionalFields = new();
        List<SchemaField> deprecatedFields = new();
        List<SchemaField> collectionFields = new();
        List<SchemaField> readOnlyFields = new();
        foreach (var f in item.Fields)
        {
            if (f.Deprecated)
            {
                deprecatedFields.Add(f);
            }
            else if (f.ReadOnly && f.DescriptionMarkdown.Contains("To retrieve this collection, specify"))
            {
                collectionFields.Add(f);
            }
            else if (f.ReadOnly)
            {
                readOnlyFields.Add(f);
            }
            else if (!f.Nullable)
            {
                requiredFields.Add(f);
            }
            else
            {
                optionalFields.Add(f);
            }
        }

        // Read-only fields first
        if (readOnlyFields.Count > 0)
        {
            sb.AppendLine("## Read-Only Fields");
            sb.AppendLine("These fields are assigned by the API server and cannot be changed.");
            foreach (var field in readOnlyFields)
            {
                sb.AppendLine(FieldMarkdown(field));
            }
        }

        // Required fields first
        if (requiredFields.Count > 0)
        {
            sb.AppendLine("## Required Fields");
            foreach (var field in requiredFields)
            {
                sb.AppendLine(FieldMarkdown(field));
            }
        }

        // Optional fields second
        if (optionalFields.Count > 0)
        {
            sb.AppendLine("## Optional Fields");
            foreach (var field in optionalFields)
            {
                sb.AppendLine(FieldMarkdown(field));
            }
        }

        // Collections fields second
        if (collectionFields.Count > 0)
        {
            sb.AppendLine("## Included Collections");
            sb.AppendLine("These fields are available when using Retrieve or Query API calls if you specify the " +
                          "associated `Include` parameter.");
            foreach (var field in collectionFields)
            {
                sb.AppendLine(FieldMarkdown(field));
            }
        }

        // Deprecated fields last, if any
        if (deprecatedFields.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Deprecated Fields");
            sb.AppendLine();
            sb.AppendLine("Deprecated fields are maintained for backwards compatibility with previous versions of "
                          + "the API.  Deprecated fields may be removed in a future release of the API.");
            sb.AppendLine();
            foreach (var field in deprecatedFields)
            {
                sb.AppendLine(FieldMarkdown(field));
            }
        }

        return sb.ToString();
    }

    private static string FieldMarkdown(SchemaField field)
    {
        var sb = new StringBuilder();
        var modifiers = new List<string>
        {
            !string.IsNullOrWhiteSpace(field.DataTypeRef)
                ? $"[{field.DataType}]({field.DataTypeRef}){(field.IsArray ? "[]" : "")}"
                : $"{field.DataType}{(field.IsArray ? "[]" : "")}"
        };
        if (field.Nullable)
        {
            modifiers.Add("nullable");
        }

        if (field.ReadOnly)
        {
            modifiers.Add("read-only");
        }

        if (field.MaxLength.HasValue && field.MinLength.HasValue)
        {
            modifiers.Add($"{field.MinLength}-{field.MaxLength} characters");
        }
        else if (field.MinLength.HasValue)
        {
            modifiers.Add($"min {field.MinLength} characters");
        }
        else if (field.MaxLength.HasValue)
        {
            modifiers.Add($"max {field.MaxLength} characters");
        }

        // Append the field's basic information on one line
        sb.AppendLine($"### {field.Name}");
        sb.AppendLine();
        if (modifiers.Count > 0)
        {
            sb.AppendLine($"_{string.Join(", ", modifiers)}_");
            sb.AppendLine();
        }

        sb.AppendLine(field.DescriptionMarkdown);
        sb.AppendLine();
        return sb.ToString();
    }
}