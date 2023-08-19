using System;
using System.Collections.Generic;
using System.Linq;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator.Diff;

public class PatchNotesGenerator
{
    /// <summary>
    /// Compare two swagger files and determine real-world differences
    /// </summary>
    /// <param name="previous"></param>
    /// <param name="current"></param>
    /// <returns></returns>
    public static SwaggerDiff Compare(GeneratorContext previous, GeneratorContext current)
    {
        var diff = new SwaggerDiff
        {
            OldVersion = previous.OfficialVersion,
            NewVersion = current.OfficialVersion
        };

        CompareEndpoints(previous, current, diff);
        CompareSchemas(previous, current, diff);

        return diff;
    }

    private static void CompareSchemas(GeneratorContext previous, GeneratorContext current, SwaggerDiff diff)
    {
        var compared = new HashSet<string>();
        var dict = previous.Api.Schemas.ToDictionary(s => s.Name);

        // Search for new or modified endpoints
        foreach (var item in current.Api.Schemas)
        {
            if (current.IsGenericSchema(item.Name))
            {
                continue;
            }
            dict.TryGetValue(item.Name, out var prevItem);
            if (prevItem == null)
            {
                if (!string.IsNullOrWhiteSpace(item.Name))
                {
                    diff.NewSchemas.Add(item.Name);
                }
            }
            else
            {
                var changes = GetSchemaChanges(item, prevItem);
                if (changes.Any())
                {
                    diff.SchemaChanges.Add(item.Name, changes);
                }
            }

            compared.Add(item.Name);
        }

        // Search for deprecated endpoints
        foreach (var oldItem in previous.Api.Schemas)
        {
            if (current.IsGenericSchema(oldItem.Name))
            {
                continue;
            }
            if (!compared.Contains(oldItem.Name))
            {
                diff.DeprecatedSchemas.Add(oldItem.Name);
            }
        }
    }

    private static List<string> GetSchemaChanges(SchemaItem item, SchemaItem prevItem)
    {
        return new List<string>();
    }

    private static void CompareEndpoints(GeneratorContext previous, GeneratorContext current, SwaggerDiff diff)
    {
        var compared = new HashSet<string>();
        var dict = previous.Api.Endpoints.ToDictionary(api => MakeApiName(api));

        // Search for new or modified endpoints
        foreach (var item in current.Api.Endpoints)
        {
            var name = MakeApiName(item);
            dict.TryGetValue(name, out var prevItem);
            if (prevItem == null)
            {
                diff.NewEndpoints.Add(name, item);
            }
            else
            {
                var changes = GetEndpointChanges(item, prevItem);
                if (changes.Any())
                {
                    diff.EndpointChanges.Add(name, changes);
                }
            }

            compared.Add(name);
        }

        // Search for deprecated endpoints
        foreach (var oldItem in previous.Api.Endpoints)
        {
            var name = MakeApiName(oldItem);
            if (!compared.Contains(name))
            {
                diff.DeprecatedEndpoints.Add(name);
            }
        }
    }

    private static string MakeApiName(EndpointItem item)
    {
        return $"{item.Category}.{item.Name.ToProperCase()}";
    }

    private static List<string> GetEndpointChanges(EndpointItem item, EndpointItem prevItem)
    {
        return new List<string>();
    }
}