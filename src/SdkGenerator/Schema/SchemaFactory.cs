using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SdkGenerator.Project;

namespace SdkGenerator.Schema;

public static class SchemaFactory
{
    public static object MakeSchema(GeneratorContext context, JsonProperty jsonSchema)
    {
        // Is this an OData schema?  If so, ignore it
        if (jsonSchema.Name.StartsWith("IEdm") || jsonSchema.Name.StartsWith("Edm"))
        {
            return null;
        }
        
        if (jsonSchema.Value.TryGetProperty("properties", out var schemaPropertiesElement))
        {
            // Basic schema
            var item = new SchemaItem
            {
                Name = jsonSchema.Name,
                // Handle fields
                Fields = new List<SchemaField>()
            };
            item.DescriptionMarkdown = SafeGetPropString(context, jsonSchema.Value, "description");

            foreach (var prop in schemaPropertiesElement.EnumerateObject())
            {
                var field = new SchemaField
                {
                    Name = prop.Name
                };

                // Let's parse and cleanse the data type in more detail
                var typeRef = GetTypeRef(context, prop);
                field.DataType = typeRef.DataType;
                field.DataTypeRef = typeRef.DataTypeRef;
                field.IsArray = typeRef.IsArray;

                // Attributes about the field
                field.Nullable = GetBooleanElement(prop.Value, "nullable");
                field.Deprecated = GetBooleanElement(prop.Value, "deprecated");
                field.ReadOnly = GetBooleanElement(prop.Value, "readOnly");
                field.MinLength = GetIntElement(prop.Value, "minLength");
                field.MaxLength = GetIntElement(prop.Value, "maxLength");
                field.DescriptionMarkdown = SafeGetPropString(context, prop.Value, "description");
                item.Fields.Add(field);
            }

            if (IsValidModel(context, item))
            {
                return item;
            }
        }
        else if (jsonSchema.Value.TryGetProperty("enum", out var enumPropertiesElement))
        {
            // Basic schema
            var item = new EnumItem
            {
                Name = jsonSchema.Name,
                Values = new Dictionary<string, object>(),
            };
            
            // If somehow this is HttpStatusCode, skip it
            if (item.Name == "HttpStatusCode")
            {
                return null;
            }
            item.DescriptionMarkdown = SafeGetPropString(context, jsonSchema.Value, "description");
            item.EnumType = SafeGetPropString(context, jsonSchema.Value, "type");
            foreach (var value in enumPropertiesElement.EnumerateArray())
            {
                var name = value.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    if (value.ValueKind == JsonValueKind.Number)
                    {
                        item.Values[name] = value.GetInt32();
                    }
                    else
                    {
                        item.Values[name] = name;
                    }
                }
            }
            
            return item;
        }

        return null;
    }

    private static int? GetIntElement(JsonElement propValue, string propertyName)
    {
        if (propValue.TryGetProperty(propertyName, out var element))
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.GetInt32();
            }
        }

        return null;
    }

    private static bool GetBooleanElement(JsonElement propValue, string propertyName)
    {
        if (propValue.TryGetProperty(propertyName, out var element))
        {
            return element.GetBoolean();
        }

        return false;
    }

    private static string SafeGetPropString(GeneratorContext context, JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop))
        {
            return prop.GetString() ?? "";
        }

        return "";
    }

    private static string GetDescriptionMarkdown(GeneratorContext context, JsonElement element, string name)
    {
        var s = SafeGetPropString(context, element, name);
        if (!string.IsNullOrEmpty(s))
        {
            s = s.Replace("<br>", Environment.NewLine);
        }

        return s;
    }

    private static SchemaRef GetTypeRef(GeneratorContext context, JsonProperty prop)
    {
        // Is this a core type?
        if (prop.Value.TryGetProperty("type", out var typeElement))
        {
            var rawType = typeElement.GetString();
            if (string.Equals(rawType, "array"))
            {
                foreach (var innerType in prop.Value.EnumerateObject())
                {
                    if (innerType.NameEquals("items"))
                    {
                        var innerSchemaRef = GetTypeRef(context, innerType);
                        innerSchemaRef.IsArray = true;
                        return innerSchemaRef;
                    }
                }

                return null;
            }

            prop.Value.TryGetProperty("format", out var formatProp);
            if (formatProp.ValueKind == JsonValueKind.String)
            {
                rawType = formatProp.GetString();
            }

            return new SchemaRef
            {
                DataType = rawType
            };
        }

        if (prop.Value.TryGetProperty("$ref", out var refElement))
        {
            var refType = refElement.GetString();
            if (refType != null)
            {
                return MakeClassRef(refType, false);
            }
        }

        // Is this an "All Of" combined element?  If so, look through all children for a $ref
        if (prop.Value.TryGetProperty("allOf", out var allOfElement))
        {
            foreach (var subProp in allOfElement.EnumerateArray())
            {
                if (subProp.TryGetProperty("$ref", out var subRefElement))
                {
                    var refType = subRefElement.GetString();
                    if (refType != null)
                    {
                        return MakeClassRef(refType, false);
                    }
                }
            }
        }

        return new SchemaRef
        {
            DataType = "object",
        };
    }

    private static SchemaRef MakeClassRef(string refType, bool isArray)
    {
        var classname = refType.Substring(refType.LastIndexOf("/", StringComparison.Ordinal) + 1);
        return new SchemaRef
        {
            DataType = classname,
            DataTypeRef = $"/docs/{classname.ToLower()}",
            IsArray = isArray,
        };
    }

    public static List<EndpointItem> MakeEndpoint(GeneratorContext context, JsonProperty prop)
    {
        var items = new List<EndpointItem>();
        var path = prop.Name;
        foreach (var endpointProp in prop.Value.EnumerateObject())
        {
            EndpointItem item = null;
            try
            {
                item = new EndpointItem
                {
                    Parameters = new List<ParameterField>(),
                    Path = path,
                    Method = endpointProp.Name,
                    Name = SafeGetPropString(context, endpointProp.Value, "summary"),
                    DescriptionMarkdown = GetDescriptionMarkdown(context, endpointProp.Value, "description")
                };
                
                // Skip any endpoints that don't have a name!
                if (!item.Name.IsValidName())
                {
                    context.LogError($"Skipping endpoint {item.Path}; its name '{item.Name}' is invalid.");
                    continue;
                }

                // Is this an ignored endpoint?
                if (context.IsIgnoredEndpoint(item.Name, path))
                {
                    context.LogError($"Ignoring endpoint '{item.Name}'.");
                    continue;
                }
                
                // Determine category
                endpointProp.Value.TryGetProperty("tags", out var tags);
                item.Category = tags.ValueKind == JsonValueKind.Array
                    ? tags.EnumerateArray().FirstOrDefault().GetString()!.Replace("/", string.Empty)
                    : "Utility";

                // Determine if deprecated
                endpointProp.Value.TryGetProperty("deprecated", out var deprecatedProp);
                item.Deprecated = deprecatedProp.ValueKind == JsonValueKind.True;

                // Parse parameters
                endpointProp.Value.TryGetProperty("parameters", out var parameterListProp);
                if (parameterListProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var paramProp in parameterListProp.EnumerateArray())
                    {
                        var p = new ParameterField();
                        p.Name = SafeGetPropString(context, paramProp, "name");
                        p.Location = SafeGetPropString(context, paramProp, "in");
                        p.DescriptionMarkdown = GetDescriptionMarkdown(context, paramProp, "description");
                        
                        // Check if this is ignored - some parameters shouldn't be in the SDK
                        if (context.Project.IgnoredParameters != null &&
                            context.Project.IgnoredParameters.Any(ip => 
                                string.Equals(ip.Name, p.Name, StringComparison.OrdinalIgnoreCase) && 
                                string.Equals(ip.Location, p.Location, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        // Parse the field's required status
                        paramProp.TryGetProperty("required", out var requiredProp);
                        p.Required = requiredProp.ValueKind == JsonValueKind.True;

                        // Parse the field's schema
                        foreach (var paramSchemaProp in paramProp.EnumerateObject())
                        {
                            if (paramSchemaProp.NameEquals("schema"))
                            {
                                var schemaRef = GetTypeRef(context, paramSchemaProp);
                                p.DataType = schemaRef.DataType;
                                p.DataTypeRef = schemaRef.DataTypeRef;
                                p.IsArray = schemaRef.IsArray;
                            }
                        }
                        item.Parameters.Add(p);
                    }
                }

                // Parse the request body parameter
                endpointProp.Value.TryGetProperty("requestBody", out var requestBodyProp);
                if (requestBodyProp.ValueKind == JsonValueKind.Object)
                {
                    requestBodyProp.TryGetProperty("content", out var requestBodyContentProp);
                    foreach (var encodingProp in requestBodyContentProp.EnumerateObject())
                    {
                        if (encodingProp.Name == "application/json")
                        {
                            var p = new ParameterField
                            {
                                Name = "body",
                                Location = "body",
                                DescriptionMarkdown = GetDescriptionMarkdown(context, requestBodyProp, "description"),
                                Required = true,
                            };
                            item.Parameters.Add(p);
                            foreach (var innerSchemaProp in encodingProp.Value.EnumerateObject())
                            {
                                if (innerSchemaProp.NameEquals("schema"))
                                {
                                    var typeRef = GetTypeRef(context, innerSchemaProp);
                                    p.DataType = typeRef.DataType;
                                    p.DataTypeRef = typeRef.DataTypeRef;
                                    p.IsArray = typeRef.IsArray;
                                }
                            }
                        }
                        else if (encodingProp.Name == "multipart/form-data")
                        {
                            item.Parameters.Add(new ParameterField
                            {
                                Name = "fileName",
                                Location = "form",
                                DescriptionMarkdown = "The full path of a file to upload to the API",
                                Required = true,
                                DataType = "File",
                                DataTypeRef = "File",
                                IsArray = false,
                            });
                        }
                    }
                }

                // Parse the "success" response type
                endpointProp.Value.TryGetProperty("responses", out var responsesProp);
                foreach (var response in responsesProp.EnumerateObject())
                {
                    if (response.Name.StartsWith("2"))
                    {
                        try
                        {
                            response.Value.TryGetProperty("content", out var contentProp);
                            if (contentProp.ValueKind == JsonValueKind.Undefined)
                            {
                                item.ReturnDataType = new SchemaRef
                                {
                                    DataType = "byte[]"
                                };
                            }
                            else
                            {
                                contentProp.TryGetProperty("application/json", out var appJsonProp);
                                foreach (var responseSchemaProp in appJsonProp.EnumerateObject())
                                {
                                    item.ReturnDataType = GetTypeRef(context, responseSchemaProp);
                                    break;
                                }
                            }
                        }
                        // If this response fails, it's probably intended to be an octet stream
                        catch (Exception ex)
                        {
                            context.LogError($"Failed to process endpoint return data type - is it intended to be an octet-stream? {item?.Path ?? "Unknown Path"}: {ex.Message}");
                        }
                    }
                }
                items.Add(item);
            }
            catch (Exception ex)
            {
                context.LogError($"Failed to process endpoint {item?.Path ?? "Unknown Path"}: {ex.Message}");
            }
        }

        return items;
    }

    private static bool IsValidModel(GeneratorContext context, SchemaItem item)
    {
        var name = item.Name;
        
        // In the DotNet world, Swashbuckle will create unique models for each generic
        // of type MyGenericClass<MyObject> in the name format MyObjectMyGenericClass.
        // Therefore, we consider the names of generics as suffixes as items to ignore.
        // Important that we don't exclude the object itself though!
        if (context.Project.GenericSuffixes != null)
        {
            foreach (var suffix in context.Project.GenericSuffixes)
            {
                if (name.EndsWith(suffix, StringComparison.InvariantCultureIgnoreCase) && !name.Equals(suffix, StringComparison.InvariantCultureIgnoreCase))
                {
                    return false;
                }
            }    
        }
        
        return !name.EndsWith("Argument")
                   && !name.EndsWith("Attribute")
                   && !name.EndsWith("Base")
                   && !name.EndsWith("Exception")
                   && !name.EndsWith("Handle")
                   && !string.Equals(name, "Assembly")
                   && !string.Equals(name, "CustomAttributeData")
                   && !string.Equals(name, "Module")
                   && !string.Equals(name, "MemberBase")
                   && !string.Equals(name, "MethodBase")
                   && !string.Equals(name, "ProblemDetails")
                   && !string.Equals(name, "Type");
    }
}