using SdkGenerator.Diff;

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

    public class ComplexTypeOne
    {
    }

    public class ComplexTypeTwo
    {
    }

    [TestMethod]
    public void ChangeParameterType()
    {
        using var v1 = new ContextBuilder()
            .AddRetrieveEndpoint("test", "RetrieveTest")
            .AddParameter(typeof(Guid), "ID")
            .AddParameter(typeof(ComplexTypeOne), "body") 
            .Build();

        using var v2 = new ContextBuilder()
            .AddRetrieveEndpoint("test", "RetrieveTest")
            .AddParameter(typeof(String), "ID")
            // Note that body type parameters are harder to categorize
            // and should be properly handled by changes in schemas
            .AddParameter(typeof(ComplexTypeTwo), "body") 
            .Build();
        
        var diff = PatchNotesGenerator.Compare(v1, v2);
        Assert.AreEqual(0, diff.NewEndpoints.Count);
        Assert.AreEqual(0, diff.DeprecatedEndpoints.Count);
        Assert.AreEqual(1, diff.EndpointChanges.Count);
        var change = diff.EndpointChanges.FirstOrDefault();
        Assert.AreEqual("test.RetrieveTest", change.Key);
        Assert.AreEqual("test.RetrieveTest changed the data type of the parameter `ID` from `Guid` to `String`", change.Value.FirstOrDefault());
    }

    public class SchemaTypeOne
    {
        public Guid id { get; set; }
    }

    [TestMethod]
    public void SchemaChange()
    {
        using var v1 = new ContextBuilder()
            .AddSchema(typeof(SchemaTypeOne)) 
            .Build();
        using var v2 = new ContextBuilder()
            .AddSchema(typeof(SchemaTypeOne))
            .ChangeSchemaFieldType(typeof(SchemaTypeOne), "id", "String")
            .Build();
        
        var diff = PatchNotesGenerator.Compare(v1, v2);
        Assert.AreEqual(0, diff.NewEndpoints.Count);
        Assert.AreEqual(0, diff.DeprecatedEndpoints.Count);
        Assert.AreEqual(0, diff.EndpointChanges.Count);
        Assert.AreEqual(1, diff.SchemaChanges.Count);
        var change = diff.SchemaChanges.FirstOrDefault();
        Assert.AreEqual("SchemaTypeOne", change.Key);
        Assert.AreEqual("SchemaTypeOne: Changed the data type of the field `id` from `Guid` to `String`", change.Value.FirstOrDefault());
    }
    
    
    [TestMethod]
    public void EndpointNameChange()
    {
        using var v1 = new ContextBuilder()
            .AddApi("/test/api/do-something", HttpMethod.Post, "Test", "DoSomethingMethod") 
            .Build();
        using var v2 = new ContextBuilder()
            .AddApi("/test/api/do-something", HttpMethod.Post, "Test", "DoSomething") 
            .Build();
        
        var diff = PatchNotesGenerator.Compare(v1, v2);
        Assert.AreEqual(0, diff.NewEndpoints.Count);
        Assert.AreEqual(0, diff.DeprecatedEndpoints.Count);
        Assert.AreEqual(0, diff.EndpointChanges.Count);
        Assert.AreEqual(0, diff.SchemaChanges.Count);
        Assert.AreEqual(1, diff.Renames.Count);
        Assert.AreEqual("Test.DoSomethingMethod", diff.Renames[0].OldName);
        Assert.AreEqual("DoSomething", diff.Renames[0].Endpoint.Name);
    }

    [TestMethod]
    public void TestPathParameterExtraction()
    {
        var elements = "/api/{one}/test/{two}/action".PathBreakdown();
        Assert.AreEqual(5, elements.Count);
        Assert.AreEqual("/api/", elements[0]);
        Assert.AreEqual("{one}", elements[1]);
        Assert.AreEqual("/test/", elements[2]);
        Assert.AreEqual("{two}", elements[3]);
        Assert.AreEqual("/action", elements[4]);
    }
    
    [TestMethod]
    public void PathParameterNameChange()
    {
        using var v1 = new ContextBuilder()
            .AddApi("/test/api/{myId}/do-something", HttpMethod.Post, "Test", "DoSomethingMethod") 
            .Build();
        using var v2 = new ContextBuilder()
            .AddApi("/test/api/{newId}/do-something", HttpMethod.Post, "Test", "DoSomethingMethod") 
            .Build();
        
        var diff = PatchNotesGenerator.Compare(v1, v2);
        Assert.AreEqual(0, diff.NewEndpoints.Count);
        Assert.AreEqual(0, diff.DeprecatedEndpoints.Count);
        Assert.AreEqual(1, diff.EndpointChanges.Count);
        Assert.AreEqual(0, diff.SchemaChanges.Count);
        Assert.AreEqual(0, diff.Renames.Count);
        var change = diff.EndpointChanges.FirstOrDefault();
        Assert.AreEqual("Test.DoSomethingMethod", change.Key);
        Assert.AreEqual("Test.DoSomethingMethod changed the parameter name `{myId}` to `{newId}`", change.Value.FirstOrDefault());
    }
}