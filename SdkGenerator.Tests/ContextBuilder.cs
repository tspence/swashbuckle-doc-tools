using System.Reflection.Metadata;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator.Tests;

public class ContextBuilder
{
    private ApiSchema api;
    private ProjectSchema project;

    public ContextBuilder()
    {
        api = new ApiSchema()
        {
            Endpoints = new(),
            Schemas = new(),
        };
        project = new ProjectSchema()
        {
            IgnoredParameters = [],
        };
    }
    
    public ContextBuilder AddRetrieveEndpoint(string category, string name)
    {
        api.Endpoints.Add(new EndpointItem()
        {
            Category = category,
            Name = name,
            DescriptionMarkdown = "Description",
            Method = "GET",
            Deprecated = false,
            Parameters = new(),
        });

        return this;
    }

    public ContextBuilder AddParameter(Type type, string name)
    {
        api.Endpoints[^1].Parameters.Add(new ParameterField()
        {
            Name = name,
            DataType = type.ToString(),
        });
        return this;
    }
    
    public GeneratorContext Build()
    {
        return GeneratorContext.FromApiSchema(api, project);
    }

    public ContextBuilder AddSchema(Type type)
    {
        var fields = new List<SchemaField>();
        foreach (var f in type.GetProperties())
        {
            fields.Add(new SchemaField()
            {
                Name = f.Name,
                DataType = f.PropertyType.ToString()
            });
        }
        api.Schemas.Add(new SchemaItem()
        {
            Name = type.Name,
            DescriptionMarkdown = "Description",
            Fields = fields,
        });
        return this;
    }

    public ContextBuilder ChangeSchemaFieldType(Type type, string fieldName, string newType)
    {
        var schema = api.Schemas.LastOrDefault();
        var field = schema.Fields.FirstOrDefault(f => f.Name == fieldName);
        field.DataType = newType;
        return this;
    }
}