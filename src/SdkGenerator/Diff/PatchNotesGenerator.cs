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
                    diff.SchemaChanges[item.Name] = changes;
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
        
        // Handle duplicate names, a common error in manually named APIs
        var nameToEndpoint = new Dictionary<string, EndpointItem>();
        var pathToName = new Dictionary<string, string>();
        foreach (var item in previous.Api.Endpoints)
        {
            var name = MakeApiName(item);
            if (nameToEndpoint.ContainsKey(name))
            {
                current.LogError($"Duplicate API name in previous version of API: {name}");
            }

            nameToEndpoint[name] = item;
            pathToName[item.Path + ":" + item.Method] = name;
        }

        // Search for new or modified endpoints
        foreach (var item in current.Api.Endpoints)
        {
            EndpointItem prevItem = null;
            var name = MakeApiName(item);
            
            // First check if the API was simply renamed
            if (pathToName.TryGetValue(item.Path + ":" + item.Method, out var prevName) && name != prevName)
            {
                compared.Add(prevName);
                diff.Renames.Add($"Renamed '{prevName}' to '{name}'");
                if (nameToEndpoint.TryGetValue(prevName, out prevItem))
                {
                    diff.EndpointChanges[name] = GetEndpointChanges(item, prevItem);
                }
            }
            else
            {
                nameToEndpoint.TryGetValue(name, out prevItem);
                if (prevItem == null)
                {
                    if (diff.NewEndpoints.ContainsKey(name))
                    {
                        current.LogError($"Duplicate API name in current version of API: {name}");
                    }

                    diff.NewEndpoints[name] = item;
                }
                else
                {
                    var changes = GetEndpointChanges(item, prevItem);
                    if (changes.Any())
                    {
                        diff.EndpointChanges[name] = changes;
                    }
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