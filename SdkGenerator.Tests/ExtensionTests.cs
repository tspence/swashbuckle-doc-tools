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
        Assert.AreEqual("somename", "some name".ToVariableName(keywords));
        Assert.AreEqual("top", "$top".ToVariableName(keywords));
        Assert.AreEqual("unknownName", "".ToVariableName(keywords));
    }

    [TestMethod]
    public void TestWordsToSnakeCase()
    {
        Assert.AreEqual("test_words_case", "Test Words Case".WordsToSnakeCase());   
        Assert.AreEqual("testwordscase", "testwordscase".WordsToSnakeCase());   
        Assert.AreEqual("test_words_case", "test words case".WordsToSnakeCase());   
    }
    
    [TestMethod]
    public void TestCamelCaseToSnakeCase()
    {
        Assert.AreEqual("test_words_case", "Test Words Case".CamelCaseToSnakeCase());   
        Assert.AreEqual("testwordscase", "testwordscase".CamelCaseToSnakeCase());   
        Assert.AreEqual("test_words_case", "test words case".CamelCaseToSnakeCase());   
        Assert.AreEqual("test_words_case", "test    words case".CamelCaseToSnakeCase());   
        Assert.AreEqual("test_words_case", "test       Words   Case".CamelCaseToSnakeCase());   
    }
}