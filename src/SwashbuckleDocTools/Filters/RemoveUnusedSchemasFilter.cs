using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SwashbuckleDocTools.Filters;

/// <summary>
/// RemoveUnusedSchemasFilter - A garbage collector for your Swagger (aka OpenAPI) definition.
/// 
/// This filter analyzes your Swagger file and removes any schemas that are not referenced
/// in any endpoints or other schemas.  If Swashbuckle or some other program accidentally
/// captures a bunch of schema classes and things that you have to filter out, and it doesn't
/// filter them out perfectly, you can use this class to remove all schemas that were
/// not needed by the final swagger file.
/// </summary>
public class RemoveUnusedSchemasFilter : IDocumentFilter
{
    private Dictionary<string, int> _countByDefinitionRef = new ();

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        bool aDefinitionWasRemoved;

        do
        {
            aDefinitionWasRemoved = RemoveUnused(swaggerDoc, context);
        } while (aDefinitionWasRemoved);
    }

    private bool RemoveUnused(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        // Assume everything is not referenced by default, and first check references in endpoints
        _countByDefinitionRef = new();
        foreach (var pathItem in swaggerDoc.Paths.Values)
        {
            CountParameterRefs(pathItem.Parameters);
            foreach (var op in pathItem.Operations)
            {
                CountOperationRefs(op.Value);
            }
        }

        // Now, count references from schemas that we know are referenced by those endpoints
        CountSchemaTree(_countByDefinitionRef.Keys.ToList(), context);
        var aDefinitionWasRemoved = false;
        foreach (var key in context.SchemaRepository.Schemas.Keys)
        {
            bool found = _countByDefinitionRef.TryGetValue(key, out var countByRef);
            if (!found || countByRef == 0)
            {
                aDefinitionWasRemoved = aDefinitionWasRemoved || context.SchemaRepository.Schemas.Remove(key);
            }
        }

        return aDefinitionWasRemoved;
    }

    private void CountSchemaTree(List<string> initialIds, DocumentFilterContext context)
    {
        Queue<string> schemaIds = new Queue<string>(initialIds);
        while (schemaIds.Count > 0)
        {
            var id = schemaIds.Dequeue();
            var schema = context.SchemaRepository.Schemas.FirstOrDefault(s => s.Key == id);
            CountSchemaRefs(schema.Value);
            CountSchemaRefs(schema.Value.Items);
            foreach (var prop in schema.Value.Properties)
            {
                EnqueueSchema(prop.Value, schemaIds);
                EnqueueSchema(prop.Value.Items, schemaIds);
            }
        }
    }

    private void EnqueueSchema(OpenApiSchema propValue, Queue<string> schemaIds)
    {
        if (propValue?.Reference?.Id != null)
        {
            schemaIds.Enqueue(propValue.Reference.Id);
        }

        if (propValue?.Items?.Reference?.Id != null)
        {
            schemaIds.Enqueue(propValue.Items.Reference.Id);
        }

        if (propValue?.AllOf != null)
        {
            foreach (var item in propValue.AllOf)
            {
                schemaIds.Enqueue(item.Reference.Id);
            }
        }
        if (propValue?.AnyOf != null)
        {
            foreach (var item in propValue.AnyOf)
            {
                schemaIds.Enqueue(item.Reference.Id);
            }
        }
    }

    private void CountOperationRefs(OpenApiOperation operation)
    {
        if (operation.RequestBody != null)
        {
            foreach (var content in operation.RequestBody.Content.Values)
            {
                if (content != null)
                {
                    CountSchemaRefs(content.Schema);
                }
            }
        }

        CountParameterRefs(operation.Parameters);
        CountResponseRefs(operation.Responses);
    }

    private void CountResponseRefs(IDictionary<string, OpenApiResponse> responsesByHttpStatus)
    {
        foreach (var response in responsesByHttpStatus.Values)
        {
            IncrementReference(response.Reference);
            foreach (var content in response.Content.Values)
            {
                IncrementReference(content?.Schema?.Reference);
            }
        }
    }

    private void CountParameterRefs(IList<OpenApiParameter> parameters)
    {
        foreach (var param in parameters)
        {
            CountSchemaRefs(param.Schema);
        }
    }

    private void CountSchemaRefs(OpenApiSchema? schema)
    {
        if (schema == null)
        {
            return;
        }
        IncrementReference(schema.Reference);
        IncrementReference(schema.Items?.Reference);
        CountSchemaArrayRefs(schema.AllOf);
        CountSchemaArrayRefs(schema.AnyOf);

        if (schema.Properties != null)
        {
            foreach (var s in schema.Properties.Values)
            {
                CountSchemaRefs(s);
            }
        }
    }

    private void CountSchemaArrayRefs(IList<OpenApiSchema> schemaArray)
    {
        foreach (var item in schemaArray)
        {
            CountSchemaRefs(item);
        }
    }

    private void IncrementReference(OpenApiReference? reference)
    {
        if (reference?.Id != null)
        {
            var idString = reference.Id;
            if (!_countByDefinitionRef.ContainsKey(idString))
            {
                _countByDefinitionRef.Add(idString, 1);
            }
            else
            {
                _countByDefinitionRef[idString]++;
            }
        }
    }
}

