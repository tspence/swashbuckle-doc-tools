namespace SdkGenerator.Tests;

[TestClass]
public class ExtensionTests
{
    [TestMethod]
    public void TestVariableNameProtection()
    {
        // Some swagger files include variables with names that are reserved keywords in one language or another.
        // This function converts them to underscores.
        List<string> keywords = ["for", "foreach", "var"];
        Assert.AreEqual("test", "test".ToVariableName(keywords));
        Assert.AreEqual("_for", "for".ToVariableName(keywords));
        Assert.AreEqual("_foreach", "foreach".ToVariableName(keywords));
        Assert.AreEqual("unknownName", "".ToVariableName(keywords));
    }
}