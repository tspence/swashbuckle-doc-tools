using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SdkGenerator.Links;
using SdkGenerator.Schema;

namespace SdkGenerator.Diff;

public class SwaggerDiff
{
    /// <summary>
    /// The version number of the older swagger file
    /// </summary>
    public string OldVersion { get; set; } = string.Empty;
    
    /// <summary>
    /// The version number of the newer swagger file
    /// </summary>
    public string NewVersion { get; set; } = string.Empty;

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
    public List<SwaggerRenameInfo> Renames { get; set; } = new();
    
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
    public bool IsMinorChange {
        get
        {
            return !(SchemaChanges.Any() 
                   || EndpointChanges.Any()
                   || DeprecatedEndpoints.Any() 
                   || DeprecatedSchemas.Any() 
                   || NewSchemas.Any() 
                   || NewEndpoints.Any() 
                   || Renames.Any());
        }
    }

    /// <summary>
    /// Convert this diff into a shortened summary of patch notes in Markdown format 
    /// </summary>
    /// <returns></returns>
    public string ToSummaryMarkdown(string? oldVersionName, string? newVersionName, ILinkGenerator? linkGenerator)
    {
        var sb = new StringBuilder();
        
        // Build old and new names
        var oldName = BuildVersionName(oldVersionName, OldVersion);
        var newName = BuildVersionName(newVersionName, NewVersion);
        
        // Header for these patch notes
        sb.AppendLine($"## Patch notes for {newName}");
        sb.AppendLine();
        sb.AppendLine($"These patch notes summarize the changes from version {oldName}.");
        sb.AppendLine();
        
        // Explain which APIs were added
        if (NewEndpoints.Count > 0)
        {
            sb.AppendLine($"Added {NewEndpoints.Count} new endpoints:");
            foreach (var api in NewEndpoints)
            {
                if (linkGenerator != null)
                {
                    var link = linkGenerator.MakeLink(api.Value);
                    sb.AppendLine($"* [{api.Key}]({link})");
                }
                else
                {
                    sb.AppendLine($"* {api.Key}");
                }
            }
            sb.AppendLine();
        }

        // List name changes
        if (Renames.Count > 0)
        {
            sb.AppendLine($"Renamed {Renames.Count} endpoints:");
            foreach (var rename in Renames)
            {
                if (linkGenerator != null)
                {
                    var link = linkGenerator.MakeLink(rename.Endpoint);
                    sb.AppendLine(
                        $"* Renamed {rename.OldName} to [{rename.Endpoint.Category.ToProperCase()}.{rename.Endpoint.Name.ToProperCase()}]({link})");
                }
                else
                {
                    sb.AppendLine(
                        $"* Renamed {rename.OldName} to {rename.Endpoint.Category.ToProperCase()}.{rename.Endpoint.Name.ToProperCase()}");
                }
            }

            sb.AppendLine();
        }
        
        // APIs with changes
        if (EndpointChanges.Count > 0)
        {
            sb.AppendLine($"Changes to existing endpoints:");
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
            sb.AppendLine($"Deprecated {DeprecatedEndpoints.Count} old endpoints:");
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

    private string BuildVersionName(string? versionName, string versionNumber)
    {
        if (string.IsNullOrWhiteSpace(versionName))
        {
            return versionNumber;
        }
        
        // Special case for URLs
        if (versionName.StartsWith("https://", StringComparison.CurrentCultureIgnoreCase))
        {
            var url = new Uri(versionName);
            return $"[{url.Host} ({versionNumber})]({versionName})";
        }
        
        // Basic case for file names
        return $"{versionName} ({versionNumber})";
    }
}