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
        using var v1 = new ContextBuilder()
            .Build();

        using var v2 = new ContextBuilder()
            .AddRetrieveEndpoint("test", "RetrieveTest")
            .Build();
        
        var diff = PatchNotesGenerator.Compare(v1, v2);
        Assert.AreEqual(1, diff.NewEndpoints.Count);

        var diff2 = PatchNotesGenerator.Compare(v2, v1);
        Assert.AreEqual(1, diff2.DeprecatedEndpoints.Count);
    }
    
    [TestMethod]
    public void AddRemoveParameters()
    {
        using var v1 = new ContextBuilder()
            .AddRetrieveEndpoint("test", "RetrieveTest")
            .AddParameter(typeof(Guid), "ID")
            .Build();

        using var v2 = new ContextBuilder()
            .AddRetrieveEndpoint("test", "RetrieveTest")
            .AddParameter(typeof(Guid), "ID")
            .AddParameter(typeof(Boolean), "Flag")
            .Build();
        
        var diff = PatchNotesGenerator.Compare(v1, v2);
        Assert.AreEqual(0, diff.NewEndpoints.Count);
        Assert.AreEqual(0, diff.DeprecatedEndpoints.Count);
        Assert.AreEqual(1, diff.EndpointChanges.Count);
        var change = diff.EndpointChanges.FirstOrDefault();
        Assert.AreEqual("test.RetrieveTest", change.Key);
        Assert.AreEqual("test.RetrieveTest added  parameter `Flag`", change.Value.FirstOrDefault());

        var diff2 = PatchNotesGenerator.Compare(v2, v1);
        Assert.AreEqual(0, diff2.NewEndpoints.Count);
        Assert.AreEqual(0, diff2.DeprecatedEndpoints.Count);
        Assert.AreEqual(1, diff2.EndpointChanges.Count);
        var change2 = diff2.EndpointChanges.FirstOrDefault();
        Assert.AreEqual("test.RetrieveTest", change2.Key);
        Assert.AreEqual("test.RetrieveTest removed  parameter `Flag`", change2.Value.FirstOrDefault());
    }
}