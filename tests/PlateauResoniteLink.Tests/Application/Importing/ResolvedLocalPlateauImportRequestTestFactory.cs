using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Application.Importing;

internal static class ResolvedLocalPlateauImportRequestTestFactory
{
    public static ResolvedLocalPlateauImportRequest Create(
        string cityGmlLocalSourcePath = "C:\\tmp\\plateau",
        string dataset = "tokyo23ku",
        string meshCode = "53394525",
        IReadOnlyList<string>? packageNames = null,
        string? demTextureLocalSourcePath = null)
    {
        ValidatedLocalDatasetLocation? demTextureSource = demTextureLocalSourcePath is null
            ? null
            : new ValidatedLocalDatasetLocation(demTextureLocalSourcePath);
        ValidatedPlateauImportRequest request = new(
            dataset,
            meshCode,
            new Regex($@"\A(?:{Regex.Escape(meshCode)})\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
            new ValidatedLocalDatasetLocation(cityGmlLocalSourcePath),
            demTextureSource,
            packageNames);
        return ResolvedLocalPlateauImportRequest.Create(
            request,
            new ValidatedLocalDatasetLocation(cityGmlLocalSourcePath),
            demTextureSource);
    }
}
