using System;
using System.Collections.Generic;
using System.Linq;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator.Diff;

public class ApiSchemaDiff
{
    /// <summary>
    /// Compare two swagger files and determine real-world differences
    /// </summary>
    /// <param name="prev"></param>
    /// <param name="current"></param>
    /// <returns></returns>
    public static List<string> CompareSchema(GeneratorContext previous, GeneratorContext current)
    {
        // Keep track of items we have compared
        var endpointsCompared = new List<string>();
        var schemaCompared = new List<string>();
        
        // Generate dictionaries for hashing
        var currentEndpoints = current.Api.Endpoints.ToDictionary(s => s.Name);
        var previousEndpoints = previous.Api.Endpoints.ToDictionary(s => s.Name);
        
        // Search for new or modified endpoints
        foreach (var currentEndpoint in current.Api.Endpoints)
        {
            previousEndpoints.TryGetValue(currentEndpoint.Name, out var previousEndpoint);
            if (previousEndpoint == null)
            {
                Console.WriteLine($"New API: {currentEndpoint.Name}");
            }

            endpointsCompared.Add(currentEndpoint.Name);
        }

        // Search for deprecated endpoints
        foreach (var previousEndpoint in previous.Api.Endpoints)
        {
            if (!endpointsCompared.Contains(previousEndpoint.Name))
            {
                Console.WriteLine($"Deprecated API: {previousEndpoint.Name}");
            }
        }

        return null;
    }
}