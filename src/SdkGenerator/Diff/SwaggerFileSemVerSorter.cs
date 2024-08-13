using System.Collections.Generic;
using System.IO;
using Semver;

namespace SdkGenerator.Diff;

public class SwaggerFileSemVerSorter : IComparer<string>
{
    int IComparer<string>.Compare(string x, string y)
    {
        var semverX = GetSemVerFor(x);
        var semverY = GetSemVerFor(y);
        
        // We want descending order, so Y compared to X
        return semverY.CompareSortOrderTo(semverX);
    }

    private SemVersion GetSemVerFor(string filename)
    {
        var fileName = Path.GetFileNameWithoutExtension(filename);
        var dashPos = fileName.IndexOf('-');
        if (dashPos > 0)
        {
            var versionString = fileName[(dashPos + 1)..];
            if (SemVersion.TryParse(versionString, SemVersionStyles.Any, out var semver))
            {
                return semver;
            }
        }

        // Treat unnamed files as lowest
        return new SemVersion(0, 0, 0);
    }
}