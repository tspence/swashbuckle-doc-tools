using SdkGenerator.Schema;

namespace SdkGenerator.Diff;

public class SwaggerRenameInfo
{
    public EndpointItem Endpoint { get; set; } = new();

    public string OldName { get; set; } = string.Empty;
}