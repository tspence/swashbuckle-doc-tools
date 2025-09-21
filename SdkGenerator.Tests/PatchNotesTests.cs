using SdkGenerator.Diff;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator.Tests;

[TestClass]
public class PatchNotesTests
{
    public EndpointItem MakeRetrieveEndpoint(string category, string name)
    {
        return new EndpointItem()
        {
            Category = category,
            Name = name,
            DescriptionMarkdown = "Description",
            Method = "GET",
            Deprecated = false,
        };
    }

    [TestMethod]
    public void AddNewApi()
    {
        var v1 = GeneratorContext.FromApiSchema(new ApiSchema()
            { Endpoints = new(), Schemas = new()});

        var v2 = GeneratorContext.FromApiSchema(new ApiSchema()
        {
            Endpoints = [MakeRetrieveEndpoint("test", "RetrieveTest"),],
            Schemas = new()
        });
        var diff = PatchNotesGenerator.Compare(v1, v2);
        Assert.AreEqual(diff.NewEndpoints.Count, 1);
    }
}