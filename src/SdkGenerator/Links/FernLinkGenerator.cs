using SdkGenerator.Schema;

namespace SdkGenerator.Links;

public class FernLinkGenerator(string Host) : ILinkGenerator
{
    public string MakeLink(EndpointItem endpoint)
    {
        return
            $"https://{Host}/api-reference/"
            + $"{endpoint.Category.CamelCaseToSnakeCase().Replace('_', '-')}/{endpoint.Name.CamelCaseToSnakeCase().Replace('_', '-')}";
    }
}