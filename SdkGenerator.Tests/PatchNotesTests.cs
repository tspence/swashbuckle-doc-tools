using SdkGenerator.Diff;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator.Tests;

[TestClass]
public class PatchNotesTests
{
    [TestMethod]
    public void AddRemoveApis()
    {
        var v1 = new ContextBuilder()
            .Build();

        var v2 = new ContextBuilder()
            .AddRetrieveEndpoint("test", "RetrieveTest")
            .Build();
        
        var diff = PatchNotesGenerator.Compare(v1, v2);
        Assert.AreEqual(1, diff.NewEndpoints.Count);

        var diff2 = PatchNotesGenerator.Compare(v2, v1);
        Assert.AreEqual(1, diff2.DeprecatedEndpoints.Count);
    }
    
    [TestMethod]
    public void AddParameters()
    {
        var v1 = new ContextBuilder()
            .AddRetrieveEndpoint("test", "RetrieveTest")
            .AddParameter(typeof(Guid), "ID")
            .Build();

        var v2 = new ContextBuilder()
            .AddRetrieveEndpoint("test", "RetrieveTest")
            .AddParameter(typeof(Guid), "ID")
            .AddParameter(typeof(Boolean), "Flag")
            .Build();
        
        var diff = PatchNotesGenerator.Compare(v1, v2);
        Assert.AreEqual(0, diff.NewEndpoints.Count);
        Assert.AreEqual(0, diff.DeprecatedEndpoints.Count);
        Assert.AreEqual(1, diff.EndpointChanges.Count);
        Assert.AreEqual("test.RetrieveTest", diff.EndpointChanges.Keys.FirstOrDefault());
        Assert.AreEqual("test.RetrieveTest added  parameter `Flag`", diff.EndpointChanges["test.RetrieveTest"].FirstOrDefault());
    }
}