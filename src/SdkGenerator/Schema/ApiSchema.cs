using System;
using System.Collections.Generic;
using System.Linq;

namespace SdkGenerator.Schema;

public class SchemaRef
{
    public string DataType { get; set; } = string.Empty;
    public string DataTypeRef { get; set; } = string.Empty;
    public bool IsArray { get; set; }
}

public class SchemaField
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string DataTypeRef { get; set; } = string.Empty;
    public string DescriptionMarkdown { get; set; } = string.Empty;
    public bool IsArray { get; set; }
    public bool Nullable { get; set; }
    public bool ReadOnly { get; set; }
    public bool Deprecated { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
}

public class SchemaItem
{
    public string Name { get; set; } = string.Empty;
    public string DescriptionMarkdown { get; set; } = string.Empty;
    public List<SchemaField> Fields { get; set; } = new();
}

public class EnumItem
{
    public string Name { get; set; } = string.Empty;
    public string DescriptionMarkdown { get; set; } = string.Empty;
    public string EnumType { get; set; } = string.Empty;
    public Dictionary<string, object> Values { get; set; } = new();
}

public class ParameterField : SchemaField
{
    public string Location { get; set; } = string.Empty;
    public bool Required { get; set; }
}

public class EndpointItem
{
    public string Name { get; set; } = string.Empty;
    public string DescriptionMarkdown { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public List<ParameterField> Parameters { get; set; } = new();
    public SchemaRef ReturnDataType { get; set; } = null!;
    public bool Deprecated { get; set; }
}

public class ApiSchema
{
    public string Semver2 { get; set; } = string.Empty;
    public string Semver3 { get; set; } = string.Empty;
    public string Semver4 { get; set; } = string.Empty;
    public List<EndpointItem> Endpoints { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public List<SchemaItem> Schemas { get; set; } = new();
    public List<EnumItem> Enums { get; set; } = new();

    public SchemaItem? FindSchema(string typeName)
    {
        return Schemas.FirstOrDefault(s => string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase));
    }

    public EnumItem? FindEnum(string typeName)
    {
        return Enums.FirstOrDefault(e => string.Equals(e.Name, typeName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Replaces enums with their underlying types; allows you to treat enums as constants
    /// </summary>
    /// <param name="typeName"></param>
    /// <returns></returns>
    public string ReplaceEnumWithType(string typeName)
    {
        var enumItem = FindEnum(typeName); 
        if (enumItem != null)
        {
            return enumItem.EnumType;
        }

        return typeName ?? string.Empty;
    }
}