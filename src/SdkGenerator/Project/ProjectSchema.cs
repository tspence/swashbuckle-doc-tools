using System.Collections.Generic;

namespace SdkGenerator.Project;

public class ProjectSchema
{
    public string CompanyName { get; set; }
    public string AuthorName { get; set; }
    public string AuthorEmail { get; set; }
    public string DocumentationUrl { get; set; }
        
    public string ProjectName { get; set; }
    public string CopyrightHolder { get; set; }
    public int ProjectStartYear { get; set; }
    public string SwaggerUrl { get; set; }

    /// <summary>
    /// The major version mode for this application - e.g. "11.1" or "11.1.2023".
    /// * 2 - Use the first two segments of semver for public releases
    /// * 3 - Use the first three segments of semver for public releases
    /// </summary>
    public int? SemverMode { get; set; }
    
    /// <summary>
    /// Any class that ends with one of these generic suffixes is considered
    /// an object that doesn't need its own documentation; instead it is represented
    /// by the official version of that generic.
    /// </summary>
    public string[] GenericSuffixes { get; set; }
    public SwaggerParameterSchema[] IgnoredParameters { get; set; }
    public string[] IgnoredEndpoints { get; set; }
    public string[] IgnoredCategories { get; set; }
    
    public EnvironmentSchema[] Environments { get; set; }
    public string SwaggerSchemaFolder { get; set; }
    public bool GenerateMarkdownFiles { get; set; }
    public bool? BlankOutSecuritySchemesSection { get; set; }
    public string Keywords { get; set; }
    public string Description { get; set; }
    public string AuthenticationHelp { get; set; }
    public string ReleaseNotes { get; set; }

    /// <summary>
    /// If you use a readme site, provide this information
    /// </summary>
    public ReadmeSiteSchema Readme { get; set; }

    /// <summary>
    /// To determine the correct version number for your project, use this URL and regex
    /// </summary>
    public string VersionNumberUrl { get; set; }

    public string VersionNumberRegex { get; set; }

    /// <summary>
    /// Extra information about the various languages
    /// </summary>
    public LanguageSchema Csharp { get; set; }

    public LanguageSchema Java { get; set; }
    public LanguageSchema Python { get; set; }
    public LanguageSchema Ruby { get; set; }
    public LanguageSchema Typescript { get; set; }
    public LanguageSchema Workato { get; set; }
    public LanguageSchema Dart { get; set; }
}

public class SwaggerParameterSchema
{
    public string Name { get; set; }
    public string Location { get; set; }
}

/// <summary>
/// Represents a shortcut name you can use to identify an environment
/// </summary>
public class EnvironmentSchema
{
    public string Name { get; set; }
    public string Url { get; set; }
    public bool? Default { get; set; }
}

/// <summary>
/// If you use Readme.com, here's information about where to upload documentation
/// </summary>
public class ReadmeSiteSchema
{
    /// <summary>
    /// The base URL of data models
    /// </summary>
    public string DataModelUrl { get; set; }

    /// <summary>
    /// An API key to use to communicate with Readme
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// The Readme ID of the category where these models will be saved
    /// TODO: Replace this with a call to https://docs.readme.com/main/reference/getcategories
    /// and either create the category or add items to it.
    /// Should also allow us to specify a readme "version number"
    /// </summary>
    public string ModelCategory { get; set; }
    
    /// <summary>
    /// The x-readme-version to use when making API calls
    /// </summary>
    public string ReadmeVersionCode { get; set; }
    
    /// <summary>
    /// Set this value to avoid uploading API references multiple times
    /// </summary>
    public string ReadmeApiDefinitionId { get; set; }
    
    /// <summary>
    /// Defined formats: 'list' and 'table'
    /// </summary>
    public string Format { get; set; }
}

public class LanguageSchema
{
    public string ModuleName { get; set; }
    public string ExtraCredit { get; set; }
    public string Folder { get; set; }
    public string ClassName { get; set; }
    public string ResponseClass { get; set; }
    public string ResponseErrorClass { get; set; }
    public string Namespace { get; set; }
    public string GithubUrl { get; set; }
    public List<string> HandwrittenClasses { get; set; }
}