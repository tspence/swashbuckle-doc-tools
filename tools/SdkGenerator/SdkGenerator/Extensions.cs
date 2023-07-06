using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator;

public static class Extensions
{
    /// <summary>
    /// camelCase: First character lowercase, all other word segments start with a capital letter
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public static string ToCamelCase(this string s)
    {
        return $"{char.ToLower(s[0])}{s[1..].Replace(" ", "")}";
    }

    /// <summary>
    /// ProperCase: All word segments start with a capital letter
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public static string ToProperCase(this string s)
    {
        return $"{char.ToUpper(s[0])}{s[1..].Replace(" ", "")}";
    }

    /// <summary>
    /// snake_case: All lowercase, all word segments separated with an underscore (_)
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public static string ToSnakeCase(this string s)
    {
        return s.ToLower().Replace(" ", "_");
    }

    /// <summary>
    /// Convert ProperCase to proper_case, assuming that any capital character represents the beginning of a
    /// word segment
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public static string ProperCaseToSnakeCase(this string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return "";
        }

        var sb = new StringBuilder();
        var withinWordSegment = true;
        foreach (var c in s)
        {
            if (char.IsUpper(c))
            {
                if (!withinWordSegment)
                {
                    sb.Append('_');
                }

                withinWordSegment = true;
            }
            else
            {
                withinWordSegment = false;
            }

            sb.Append(char.ToLower(c));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Take a markdown string and remove all newlines
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public static string ToSingleLineMarkdown(this string s)
    {
        return Regex.Replace(s, "\\s+", " ");
    }

    public static string ToJavaDoc(this string markdown, int indent, string returnType = null, List<ParameterField> parameterList = null)
    {
        if (string.IsNullOrWhiteSpace(markdown) && parameterList == null && string.IsNullOrWhiteSpace(returnType))
        {
            return "";
        }

        var sb = new StringBuilder();
        var prefix = "".PadLeft(indent);
        sb.AppendLine($"{prefix}/**");

        // Break markdown into something readable
        var lastLineBlank = false;
        if (!string.IsNullOrWhiteSpace(markdown))
        {
            foreach (var line in markdown.Replace(" & ", " and ").Split("\n"))
            {
                if (line.StartsWith("###"))
                {
                    break;
                }

                var nextLine = $"{prefix} * {line}".TrimEnd();
                if (nextLine.Length == indent + 2)
                {
                    if (!lastLineBlank)
                    {
                        sb.AppendLine(nextLine);
                    }

                    lastLineBlank = true;
                }
                else
                {
                    sb.AppendLine(nextLine);
                    lastLineBlank = false;
                }
            }
        }

        // Separation between header and parameters
        if (!lastLineBlank && (parameterList != null || returnType != null))
        {
            sb.AppendLine($"{prefix} *");
        }

        // Add documentation for parameters
        if (parameterList != null)
        {
            foreach (var p in parameterList)
            {
                var cleansedMarkdown = Regex.Replace(p.DescriptionMarkdown, "\\s+", " ").TrimEnd();
                sb.AppendLine(!string.IsNullOrWhiteSpace(cleansedMarkdown)
                    ? $"{prefix} * @param {p.Name} {cleansedMarkdown}"
                    : $"{prefix} * @param {p.Name} Documentation pending");
            }
        }

        // Do they want to describe a return type?
        if (!string.IsNullOrWhiteSpace(returnType))
        {
            sb.AppendLine($"{prefix} * @return {returnType.ToSingleLineMarkdown()}".TrimEnd());
        }

        // End the javadoc
        sb.AppendLine($"{prefix} */");
        return sb.ToString();
    }

    /// <summary>
    /// Wraps text as best as possible taking into consideration markdown behavior.
    ///
    /// Slightly modified from source: https://www.programmingnotes.org/7392/cs-word-wrap-how-to-split-a-string-text-into-lines-with-maximum-length-using-cs/
    /// </summary>
    /// <param name="text"></param>
    /// <param name="maxCharactersPerLine"></param>
    /// <param name="prefix"></param>
    /// <returns></returns>
    public static string WrapMarkdown(this string text, int maxCharactersPerLine, string prefix)
    {
        const string token = "@@@PARAGRAPH@@@";

        // Get a list of words
        var words = text
            .Replace("\r\n\r\n", $" {token} ")
            .Replace("\n\n", $" {token} ")
            .ToSingleLineMarkdown()
            .Trim()
            .Split(" ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return "";
        }

        // Construct a clean bit of text
        var sb = new StringBuilder();
        sb.Append(prefix);
        var position = prefix.Length;
        foreach (var word in words)
        {
            if (word == token)
            {
                sb.TrimEnd();
                sb.AppendLine();
                sb.AppendLine();
                sb.Append(prefix);
                position = prefix.Length;
            }

            // Trimming a line after one word seems awkward, so we only trim the line
            // when we reach the halfway point of a line.  This often happens when we
            // have some markdown text, like a URL, which is nearly or greater than 70
            // characters by itself.
            else if (position > maxCharactersPerLine / 2 && position + word.Length > maxCharactersPerLine)
            {
                sb.TrimEnd();
                sb.AppendLine();
                sb.Append(prefix);
                sb.Append(word);
                sb.Append(" ");
                position = prefix.Length + word.Length + 1;
            }
            else
            {
                sb.Append(word);
                sb.Append(" ");
                position += word.Length + 1;
            }

            if (position > maxCharactersPerLine)
            {
                sb.TrimEnd();
                sb.AppendLine();
                sb.Append(prefix);
                position = prefix.Length;
            }
        }

        return sb.ToString().TrimEnd();
    }

    public static async Task PatchFile(GeneratorContext context, string filename, string regex, string replacement)
    {
        if (!File.Exists(filename))
        {
            context.Log($"Unable to find file {filename}");
            return;
        }

        var text = await File.ReadAllTextAsync(filename);
        var match = Regex.Match(text, regex);
        if (match.Success)
        {
            var newText = text.Replace(match.ToString(), replacement);
            await File.WriteAllTextAsync(filename, newText);
        }
        else
        {
            context.Log($"Failed to patch file {filename} - no match found.");
        }
    }

    public static void TrimEnd(this StringBuilder sb)
    {
        while (sb.Length > 0 && sb[^1] == ' ')
        {
            sb.Length--;
        }
    }

#nullable enable
    public static IEnumerable<T> EmptyIfNull<T>(this IEnumerable<T>? source)
    {
        return source ?? Enumerable.Empty<T>();
    }
#nullable disable
}