using System;
using System.Collections.Generic;
using Semver;

namespace SdkGenerator.Project;

public class ContextSorter : IComparer<GeneratorContext>
{
    public int Compare(GeneratorContext a, GeneratorContext b)
    {
        var validVersionA = Semver.SemVersion.TryParse(a?.OfficialVersion ?? string.Empty, SemVersionStyles.Any, out var semverA);
        var validVersionB = Semver.SemVersion.TryParse(b?.OfficialVersion ?? string.Empty, SemVersionStyles.Any, out var semverB);
        if (validVersionA && validVersionB)
        {
            return semverA.CompareSortOrderTo(semverB);
        }

        return 0;
    }
}