using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator.Languages;

public class RubySdk : ILanguageSdk
{
    private string FileHeader(ProjectSchema project)
    {
        return "#\n"
               + $"# {project.ProjectName} for Ruby\n"
               + "#\n"
               + $"# (c) {project.CopyrightHolder}\n"
               + "#\n"
               + "# For the full copyright and license information, please view the LICENSE\n"
               + "# file that was distributed with this source code.\n"
               + "#\n"
               + $"# @author     {project.AuthorName} <{project.AuthorEmail}>\n"
               + $"# @copyright  {project.CopyrightHolder}\n"
               + $"# @link       {project.Ruby.GithubUrl}\n"
               + "#\n\n";
    }

    private async Task ExportSchemas(GeneratorContext context)
    {
        var modelsDir = context.MakePath(context.Project.Ruby.Folder, "lib", context.Project.Ruby.Namespace, "models");
        Directory.CreateDirectory(modelsDir);
        foreach (var modelFile in Directory.EnumerateFiles(modelsDir, "*.rb"))
        {
            File.Delete(modelFile);
        }

        foreach (var item in context.Api.Schemas)
        {
            if (item.Fields != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine(FileHeader(context.Project));
                sb.AppendLine("require 'json'");
                sb.AppendLine();
                sb.AppendLine($"module {context.Project.Ruby.ModuleName}");
                sb.AppendLine();

                // Ruby likes to have comments padded to the length of the longest field
                sb.Append(MakeRubyDoc(item.DescriptionMarkdown, 4, null));
                sb.AppendLine($"    class {item.Name.ToProperCase()}");
                sb.AppendLine();
                sb.AppendLine("        ##");
                sb.AppendLine($"        # Initialize the {item.Name.ToProperCase()} using the provided prototype");
                sb.AppendLine("        def initialize(params = {})");
                foreach (var f in item.Fields)
                {
                    sb.AppendLine(
                        $"            @{f.Name.ProperCaseToSnakeCase()} = params.dig(:{f.Name.ProperCaseToSnakeCase()})");
                }

                sb.AppendLine("        end");
                sb.AppendLine();
                foreach (var f in item.Fields)
                {
                    sb.AppendLine("        ##");
                    sb.AppendLine(
                        $"        # @return [{f.DataType.ToProperCase()}] {f.DescriptionMarkdown.ToSingleLineMarkdown()}");
                    sb.AppendLine($"        attr_accessor :{f.Name.ProperCaseToSnakeCase()}");
                    sb.AppendLine();
                }

                sb.AppendLine("        ##");
                sb.AppendLine("        # @return [object] This object as a JSON key-value structure");
                sb.AppendLine("        def as_json(options={})");
                sb.AppendLine("            {");
                foreach (var f in item.Fields)
                {
                    sb.AppendLine($"                '{f.Name}' => @{f.Name.ProperCaseToSnakeCase()},");
                }

                sb.AppendLine("            }");
                sb.AppendLine("        end");
                sb.AppendLine();
                sb.AppendLine("        ##");
                sb.AppendLine("        # @return [String] This object converted to a JSON string");
                sb.AppendLine("        def to_json(*options)");
                sb.AppendLine("            \"[#{as_json(*options).to_json(*options)}]\"");
                sb.AppendLine("        end");
                sb.AppendLine("    end");
                sb.AppendLine("end");
                var modelPath = Path.Combine(modelsDir, item.Name.ProperCaseToSnakeCase() + ".rb");
                await File.WriteAllTextAsync(modelPath, sb.ToString());
            }
        }
    }

    private async Task ExportEndpoints(GeneratorContext context)
    {
        var clientsDir = context.MakePath(context.Project.Ruby.Folder, "lib", context.Project.Ruby.Namespace, "clients");
        Directory.CreateDirectory(clientsDir);
        foreach (var clientsFile in Directory.EnumerateFiles(clientsDir, "*.rb"))
        {
            File.Delete(clientsFile);
        }

        // Gather a list of unique categories
        foreach (var cat in context.Api.Categories)
        {
            var sb = new StringBuilder();

            // Construct header
            sb.AppendLine(FileHeader(context.Project));
            sb.AppendLine("require 'awrence'");
            sb.AppendLine();
            sb.AppendLine($"class {cat.ToProperCase()}Client");
            sb.AppendLine();
            sb.AppendLine("    ##");
            sb.AppendLine($"    # Initialize the {cat.ToProperCase()}Client class with an API client instance.");
            sb.AppendLine($"    # @param connection [{context.Project.Ruby.ClassName}] The API client object for this connection");
            sb.AppendLine("    def initialize(connection)");
            sb.AppendLine("        @connection = connection");
            sb.AppendLine("    end");
            sb.AppendLine();

            // Run through all APIs
            foreach (var endpoint in context.Api.Endpoints.Where(endpoint => endpoint.Category == cat && !endpoint.Deprecated))
            {
                sb.AppendLine();
                sb.Append(MakeRubyDoc(endpoint.DescriptionMarkdown, 4, endpoint.Parameters));

                // Figure out the parameter list
                var body = (from p in endpoint.Parameters where p.Location == "body" select p).FirstOrDefault();
                var hasBody = body != null;
                var bodyParamStr = hasBody ? body.DataType == "object" ? "body.to_camelback_keys.to_json" : "body" : "nil";
                var hasQueryParams = (from p in endpoint.Parameters where p.Location == "query" select p).Any();
                var paramListStr = string.Join(", ", from p in endpoint.Parameters select $"{FixupVariableName(p.Name)}:");

                // Write the method
                sb.AppendLine($"    def {endpoint.Name.WordsToSnakeCase()}({paramListStr})");
                sb.AppendLine($"        path = \"{endpoint.Path.Replace("{", "#{")}\"");
                if (hasQueryParams)
                {
                    var paramObjStr = string.Join(", ",
                        from p in endpoint.Parameters
                        where p.Location == "query"
                        select $":{p.Name} => {FixupVariableName(p.Name)}");
                    sb.AppendLine($"        params = {{{paramObjStr}}}");
                }

                sb.AppendLine(
                    $"        @connection.request(:{endpoint.Method.ToLower()}, path, {bodyParamStr}, {(hasQueryParams ? "params" : "nil")})");
                sb.AppendLine("    end");
            }

            // Close out the class
            sb.AppendLine("end");

            // Write this category to a file
            var classPath = Path.Combine(clientsDir, cat.ProperCaseToSnakeCase() + "_client.rb");
            await File.WriteAllTextAsync(classPath, sb.ToString());
        }
    }

    /// <summary>
    /// Correct names that are keywords in ruby
    /// </summary>
    /// <param name="incomingName"></param>
    /// <returns></returns>
    private static string FixupVariableName(string incomingName)
    {
        if (incomingName == "include")
        {
            return "include_param";
        }

        return incomingName.ProperCaseToSnakeCase();
    }

    /// <summary>
    /// Convert a ruby type name to a data type hint
    /// </summary>
    /// <param name="dataType"></param>
    /// <returns></returns>
    public static string DataTypeHint(string dataType)
    {
        switch (dataType)
        {
            case "uuid":
            case "object":
            case "string":
            case "int":
            case "date":
            case "uri":
            case "email":
            case "int32":
            case "integer":
            case "double":
            case "float":
            case "boolean":
                return dataType;
            case "date-time":
                return "date_time";
            default:
                return dataType.ToProperCase();
        }
    }

    public static string MakeRubyDoc(string description, int indent, List<ParameterField> parameters)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return "";
        }

        var sb = new StringBuilder();
        var prefix = "".PadLeft(indent) + "#";

        // The first line has two hashtags alone, according to https://github.com/ruby/rdoc
        sb.AppendLine($"{prefix}#");

        // Add summary section
        foreach (var line in description.Split("\n"))
        {
            if (line.StartsWith("###"))
            {
                break;
            }

            sb.AppendLine($"{prefix} {line}".TrimEnd());
        }


        // Add documentation for parameters
        foreach (var p in parameters ?? Enumerable.Empty<ParameterField>())
        {
            sb.AppendLine(
                $"{prefix} @param {FixupVariableName(p.Name)} [{DataTypeHint(p.DataType)}] {p.DescriptionMarkdown.ToSingleLineMarkdown()}");
        }

        return sb.ToString();
    }

    public async Task Export(GeneratorContext context)
    {
        if (context.Project.Ruby == null)
        {
            return;
        }
        Console.WriteLine("Exporting Ruby...");

        await ExportSchemas(context);
        await ExportEndpoints(context);

        // Some paths we'll need
        var rubyModulePath = context.MakePath(context.Project.Ruby.Folder, "lib", context.Project.Ruby.Namespace);
        // var rubyGemspecPath = context.MakePath(context.Project.Ruby.Folder, context.Project.Ruby.ModuleName + ".gemspec");

        // Let's try using Scriban to populate these files
        await ScribanFunctions.ExecuteTemplate(context, 
            "SdkGenerator.Templates.ruby.ApiClient.rb.scriban",
            Path.Combine(rubyModulePath, context.Project.Ruby.ClassName.ProperCaseToSnakeCase() + ".rb"));
        // TODO - Need to update the ruby generator with ability to build these files
        // await Extensions.PatchFile(context, Path.Combine(rubyModulePath, "version.rb"),
        //     "VERSION = \"[\\d\\.]+\"",
        //     $"VERSION = \"{context.OfficialVersion}\"");
        // await Extensions.PatchFile(context, rubyGemspecPath,
        //     "s.version = '[\\d\\.]+'",
        //     $"s.version = '{context.OfficialVersion}'");
        // await Extensions.PatchFile(context, rubyGemspecPath,
        //     "s.date = '[\\d-]+'",
        //     $"s.date = '{DateTime.Today:yyyy-MM-dd}'");
        // await Extensions.PatchFile(context, Path.Combine(context.Project.Ruby.Folder, "Gemfile.lock"),
        //     $"{context.Project.Ruby.ModuleName} \\([\\d\\.]+\\)",
        //     $"{context.Project.Ruby.ModuleName} ({context.OfficialVersion})");
    }
    
    public string LanguageName()
    {
        return "Ruby";
    }
}