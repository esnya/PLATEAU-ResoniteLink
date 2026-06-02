using System;

namespace PlateauResoniteLink.Application.Importing;

public readonly record struct TextureImportSourceIdentity
{
    public TextureImportSourceIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
