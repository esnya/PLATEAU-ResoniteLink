using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Domain.Importing;

/// <summary>
/// Manages LOD and pattern-based filtering for CityObjects during import.
/// Supports per-package LOD exclusion and pattern matching with optional Marking bypass.
/// </summary>
public sealed class LodFilteringStrategy
{
    private readonly IReadOnlySet<int>? _globalExcludeLodLevels;
    private readonly IReadOnlyDictionary<string, IReadOnlySet<int>>? _excludeLodByPackage;
    private readonly IReadOnlyDictionary<string, string>? _packagePatterns;
    private readonly bool _includeMarkingAlways;

    public LodFilteringStrategy(
        IReadOnlySet<int>? globalExcludeLodLevels = null,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? excludeLodByPackage = null,
        IReadOnlyDictionary<string, string>? packagePatterns = null,
        bool includeMarkingAlways = true)
    {
        _globalExcludeLodLevels = globalExcludeLodLevels;
        _excludeLodByPackage = excludeLodByPackage;
        _packagePatterns = packagePatterns;
        _includeMarkingAlways = includeMarkingAlways;
    }

    /// <summary>
    /// Determines whether an object with the given LOD level should be excluded.
    /// Marking objects always bypass exclusion when the includeMarkingAlways option is true.
    /// </summary>
    public bool ShouldExcludeLod(string packageName, int? lodLevel, bool isMarking)
    {
        // Marking objects bypass LOD exclusion if configured
        if (isMarking && _includeMarkingAlways)
        {
            return false;
        }

        if (!lodLevel.HasValue)
        {
            return false;
        }

        int lod = lodLevel.Value;

        // Check package-specific exclusion first
        if (_excludeLodByPackage?.TryGetValue(packageName, out IReadOnlySet<int>? packageLodExclusions) == true)
        {
            if (packageLodExclusions.Contains(lod))
            {
                return true;
            }
        }

        // Check global exclusion if package-specific doesn't apply
        if (_globalExcludeLodLevels?.Contains(lod) == true)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether an object matches the pattern filter for its package.
    /// Returns true if the object should be included (matches pattern, or no pattern is defined).
    /// </summary>
    public bool ShouldIncludeByPattern(string packageName, string objectId, bool isMarking)
    {
        if (_packagePatterns?.TryGetValue(packageName, out string? pattern) != true)
        {
            // No pattern defined for this package means include all
            return true;
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return true;
        }

        // Marking objects bypass pattern filter if configured
        if (isMarking && _includeMarkingAlways)
        {
            return true;
        }

        // Simple pattern matching: support *, prefix, suffix, and literal matching
        return MatchesPattern(objectId, pattern);
    }

    /// <summary>
    /// Simple pattern matching supporting wildcards:
    /// - "*suffix" matches strings ending with "suffix"
    /// - "prefix*" matches strings starting with "prefix"
    /// - "*middle*" matches strings containing "middle"
    /// - "exact" matches exact string (case-sensitive)
    /// </summary>
    private static bool MatchesPattern(string value, string pattern)
    {
        if (pattern.StartsWith('*') && pattern.EndsWith('*'))
        {
            // Contains: *middle*
            string middle = pattern[1..^1];
            return value.Contains(middle, StringComparison.Ordinal);
        }

        if (pattern.StartsWith('*'))
        {
            // Suffix: *suffix
            string suffix = pattern[1..];
            return value.EndsWith(suffix, StringComparison.Ordinal);
        }

        if (pattern.EndsWith('*'))
        {
            // Prefix: prefix*
            string prefix = pattern[..^1];
            return value.StartsWith(prefix, StringComparison.Ordinal);
        }

        // Exact match (case-sensitive)
        return value.Equals(pattern, StringComparison.Ordinal);
    }
}
