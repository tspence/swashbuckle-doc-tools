using SdkGenerator.Schema;

namespace SdkGenerator.Links;

public interface ILinkGenerator
{
    public string MakeLink(EndpointItem endpoint);
}