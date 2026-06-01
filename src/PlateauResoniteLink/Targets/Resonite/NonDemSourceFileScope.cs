using System;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record NonDemSourceFileScope
{
    public NonDemSourceFileScope(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        RelativePath = relativePath;
    }

    public string RelativePath { get; }

    public static NonDemSourceFileScopeResolution Resolve(ResoniteConstructionCityObject cityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        if (string.IsNullOrWhiteSpace(cityObject.SourceFileRelativePath))
        {
            return NonDemSourceFileScopeResolution.Missing.Instance;
        }

        return new NonDemSourceFileScopeResolution.Available(new NonDemSourceFileScope(cityObject.SourceFileRelativePath));
    }

    public override string ToString() => RelativePath;
}

internal abstract record NonDemSourceFileScopeResolution
{
    private NonDemSourceFileScopeResolution()
    {
    }

    public sealed record Available(NonDemSourceFileScope Scope) : NonDemSourceFileScopeResolution;

    public sealed record Missing : NonDemSourceFileScopeResolution
    {
        public static Missing Instance { get; } = new();

        private Missing()
        {
        }
    }
}
