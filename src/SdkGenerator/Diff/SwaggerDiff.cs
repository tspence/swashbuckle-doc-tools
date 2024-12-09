using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SdkGenerator.Schema;

namespace SdkGenerator.Diff;

public class SwaggerDiff
{
    /// <summary>
    /// The version number of the older swagger file
    /// </summary>
    public string OldVersion { get; set; }
    
    /// <summary>
    /// The version number of the newer swagger file
    /// </summary>
    public string NewVersion { get; set; }

    /// <summary>
    /// New endpoints added
    /// </summary>
    public Dictionary<string, EndpointItem> NewEndpoints { get; set; } = new();
    
    /// <summary>
    /// Endpoints that were deprecated
    /// </summary>
    public List<string> DeprecatedEndpoints { get; set; } = new();
    
    /// <summary>
    /// For endpoints that changed, a description of the changes
    /// </summary>
    public Dictionary<string, List<string>> EndpointChanges { get; set; } = new();
    
    /// <summary>
    /// For endpoints that were renamed, a list of the renames
    /// </summary>
    public List<string> Renames { get; set; } = new();
    
    /// <summary>
    /// New schema definitions
    /// </summary>
    public List<string> NewSchemas { get; set; } = new();
    
    /// <summary>
    /// Schemas that were deprecated
    /// </summary>
    public List<string> DeprecatedSchemas { get; set; } = new();
    
    /// <summary>
    /// For schemas that changed, a description of the changes
    /// </summary>
    public Dictionary<string, List<string>> SchemaChanges { get; set; } = new();
    
    /// <summary>
    /// True if this change is trivial - e.g. no schemas or endpoints have changed
    /// </summary>
    public bool IsMinorChange { get; set; }

    /// <summary>
    /// Convert this diff into a shortened summary of patch notes in Markdown format 
    /// </summary>
    /// <returns></returns>
    public string ToSummaryMarkdown()
    {
        var sb = new StringBuilder();
        
        // Header for these patch notes
        sb.AppendLine($"# Patch notes for {NewVersion}");
        sb.AppendLine();
        sb.AppendLine($"These patch notes summarize the changes from version {OldVersion}.");
        sb.AppendLine();
        
        // Explain which APIs were added
        if (NewEndpoints.Count > 0)
        {
            sb.AppendLine($"Added {NewEndpoints.Count} new APIs:");
            foreach (var api in NewEndpoints)
            {
                sb.AppendLine($"* {api.Key} ({api.Value.Method.ToUpper()} {api.Value.Path})");
            }
            sb.AppendLine();
        }

        // List name changes
        if (Renames.Count > 0)
        {
            sb.AppendLine($"Renamed {Renames.Count} old APIs:");
            foreach (var rename in Renames)
            {
                sb.AppendLine($"* {rename}");
            }

            sb.AppendLine();
        }
        
        // APIs with changes
        if (EndpointChanges.Count > 0)
        {
            sb.AppendLine($"Changes to existing APIs:");
            foreach (var rename in EndpointChanges)
            {
                foreach (var change in rename.Value)
                {
                    sb.AppendLine($"* {change}");
                }
            }

            sb.AppendLine();
        }
        
        // Schemas with changes
        if (SchemaChanges.Count > 0)
        {
            sb.AppendLine($"Changes to data models:");
            foreach (var rename in SchemaChanges)
            {
                foreach (var change in rename.Value)
                {
                    sb.AppendLine($"* {change}");
                }
            }

            sb.AppendLine();
        }

        // Explain which APIs were removed
        if (DeprecatedEndpoints.Count > 0)
        {
            sb.AppendLine($"Deprecated {DeprecatedEndpoints.Count} old APIs:");
            foreach (var api in DeprecatedEndpoints)
            {
                sb.AppendLine($"* {api}");
            }
            sb.AppendLine();
        }
        
        // If no major changes, let people know this was a minor release
        if (DeprecatedEndpoints.Count + EndpointChanges.Count + Renames.Count + NewEndpoints.Count + SchemaChanges.Count == 0)
        {
            sb.AppendLine("* Minor documentation changes, bugfixes, and performance improvements.");
        }

        return sb.ToString();
    }
}