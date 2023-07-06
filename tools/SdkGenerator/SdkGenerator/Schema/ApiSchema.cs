using System;
using System.Collections.Generic;
using System.Linq;

namespace SdkGenerator.Schema;

public class SchemaRef
{
    public string DataType { get; set; }
    public string DataTypeRef { get; set; }
    public bool IsArray { get; set; }
}

public class SchemaField
{
    public string Name { get; set; }
    public string DataType { get; set; }
    public string DataTypeRef { get; set; }
    public string DescriptionMarkdown { get; set; }
    public bool IsArray { get; set; }
    public bool Nullable { get; set; }
    public bool ReadOnly { get; set; }
    public bool Deprecated { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
}

public class SchemaItem
{
    public string Name { get; set; }
    public string DescriptionMarkdown { get; set; }
    public string EnumType { get; set; }
    public List<SchemaField> Fields { get; set; }
    public List<int> Enums { get; set; }
}

public class ParameterField
{
    public string Name { get; set; }
    public string Location { get; set; }
    public string DataType { get; set; }
    public string DataTypeRef { get; set; }
    public string DescriptionMarkdown { get; set; }
    public bool IsArray { get; set; }
    public bool Nullable { get; set; }
    public bool ReadOnly { get; set; }
    public bool Deprecated { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public bool Required { get; set; }
}

public class EndpointItem
{
    public string Name { get; set; }
    public string DescriptionMarkdown { get; set; }
    public string Category { get; set; }
    public string Path { get; set; }
    public string Method { get; set; }
    public List<ParameterField> Parameters { get; set; }
    public SchemaRef ReturnDataType { get; set; }
    public bool Deprecated { get; set; }
}

public class ApiSchema
{
    public string Semver2 { get; set; }
    public string Semver3 { get; set; }
    public string Semver4 { get; set; }
    public List<EndpointItem> Endpoints { get; set; }
    public List<string> Categories { get; set; }
    public List<SchemaItem> Schemas { get; set; }

    public SchemaItem FindSchema(string typeName)
    {
        return (from s in Schemas
            where string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase)
            select s).FirstOrDefault();
    }

    public bool IsEnum(string typeName)
    {
        return (from s in Schemas
            where string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase) && s.EnumType != null
            select s).Any();
    }
}