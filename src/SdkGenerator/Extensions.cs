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
    /// Make this Swagger parameter a safe variable name
    ///
    /// Examples:
    /// * $top -> top
    /// * name -> name
    /// * some name -> somename
    /// * [reserved keyword] -> _reservedkeyword
    /// </summary>
    /// <param name="swaggerParameterName">A swagger parameter name</param>
    /// <param name="keywords">A list of reserved keywords to avoid</param>
    /// <returns></returns>
    public static string ToVariableName(this string swaggerParameterName, List<string> keywords = null)
    {
        var sb = new StringBuilder();
        foreach (var c in swaggerParameterName)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }

        var newName = sb.ToString();
        if (keywords != null && keywords.Contains(newName, StringComparer.OrdinalIgnoreCase))
        {
            return "_" + newName;
        }

        return newName;
    }
    
    /// <summary>
    /// camelCase: First character lowercase, all other word segments start with a capital letter
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public static string ToCamelCase(this string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return "unknownName";
        }
        return $"{char.ToLower(s[0])}{s[1..].Replace(" ", "")}";
    }

    /// <summary>
    /// ProperCase: All word segments start with a capital letter
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public static string ToProperCase(this string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return "UnknownName";
        }
        return $"{char.ToUpper(s[0])}{s[1..].Replace(" ", "")}";
    }

    /// <summary>
    /// snake_case: All lowercase, all word segments separated with an underscore (_)
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public static string WordsToSnakeCase(this string s)
    {
        return s.ToLower().Replace(" ", "_");
    }

    public static string CamelCaseToSnakeCase(this string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsUpper(c))
            {
                if (sb.Length != 0)
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLower(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
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
        // First replace double newline with periods and spaces
        var s2 = s.Replace("\n\n", ". ")
            .Replace("..", ".");
        
        // Now replace all multi-whitespace with a single space
        return Regex.Replace(s2, "\\s+", " ");
    }

    
    public static string ToDartDoc(this string description, int indent, List<ParameterField> parameters = null)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return "";
        }

        var sb = new StringBuilder();
        var prefix = "".PadLeft(indent) + "///";

        // Add summary section
        foreach (var line in description.Split("\n"))
        {
            if (line.StartsWith("###"))
            {
                break;
            }

            sb.AppendLine($"{prefix} {line}".TrimEnd());
        }
        
        return sb.ToString();
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
                    ? $"{prefix} * @param {p.Name.ToVariableName()} {cleansedMarkdown}"
                    : $"{prefix} * @param {p.Name.ToVariableName()} Documentation pending");
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
    /// Take markdown text in which a single newline or crlf is considered a "flow point" rather than a new line, and
    /// a double newline or double crlf is considered a double newline or crlf.
    ///
    /// This function cleanses text that is captured from XMLDOC which necessarily has single newlines in it
    /// to be readable within the original source code.
    ///
    /// Example:
    /// * "This is\na test" -> "This is a test"
    /// * "This is a test.\n\nNext paragraph" -> unchanged 
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    public static string ReflowMarkdown(this string text)
    {
        var sb = new StringBuilder();
        foreach (var line in text.Split("\n".ToCharArray()))
        {
            // Whenever we get a fully blank or whitespace line, trim and add a real markdown line break
            if (string.IsNullOrWhiteSpace(line))
            {
                sb.TrimEnd();
                sb.Append("\n\n");
            }
            else
            {
                sb.Append(line.Trim());
                sb.Append(" ");
            }
        }

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
    
    public static bool IsValidName(this string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return false;
        }
        
        // Ensure that all characters within this name are safe characters
        foreach (var c in itemName)
        {
            if (!IsSafeChar(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSafeChar(this char c)
    {
        return char.IsAsciiLetterOrDigit(c) || c == '_' || c == ' ';
    }
}