using System;
using System.Globalization;

namespace PlateauResoniteLink.Application.Importing;

public sealed class ContinuableImportException : Exception
{
    public ContinuableImportException(
        string phase,
        string packageName,
        string objectKey,
        string objectName,
        string actualMeshCode,
        string sourceFileRelativePath,
        string reason,
        Exception? innerException = null)
        : base(CreateMessage(phase, packageName, objectKey, objectName, actualMeshCode, sourceFileRelativePath, reason), innerException)
    {
        Phase = string.IsNullOrWhiteSpace(phase)
            ? throw new ArgumentException("Continuable import phase must be provided.", nameof(phase))
            : phase;
        PackageName = string.IsNullOrWhiteSpace(packageName)
            ? throw new ArgumentException("Continuable import package name must be provided.", nameof(packageName))
            : packageName;
        ObjectKey = string.IsNullOrWhiteSpace(objectKey)
            ? throw new ArgumentException("Continuable import object key must be provided.", nameof(objectKey))
            : objectKey;
        ObjectName = string.IsNullOrWhiteSpace(objectName)
            ? throw new ArgumentException("Continuable import object name must be provided.", nameof(objectName))
            : objectName;
        ActualMeshCode = string.IsNullOrWhiteSpace(actualMeshCode)
            ? throw new ArgumentException("Continuable import actual mesh code must be provided.", nameof(actualMeshCode))
            : actualMeshCode;
        SourceFileRelativePath = string.IsNullOrWhiteSpace(sourceFileRelativePath)
            ? throw new ArgumentException("Continuable import source file path must be provided.", nameof(sourceFileRelativePath))
            : sourceFileRelativePath;
        Reason = string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException("Continuable import reason must be provided.", nameof(reason))
            : reason;
    }

    public string Phase { get; }

    public string PackageName { get; }

    public string ObjectKey { get; }

    public string ObjectName { get; }

    public string ActualMeshCode { get; }

    public string SourceFileRelativePath { get; }

    public string Reason { get; }

    private static string CreateMessage(
        string phase,
        string packageName,
        string objectKey,
        string objectName,
        string actualMeshCode,
        string sourceFileRelativePath,
        string reason)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Continuable import failure phase='{phase}', package='{packageName}', "
            + $"object_key='{objectKey}', object_name='{objectName}', "
            + $"actual_mesh='{actualMeshCode}', source_file='{sourceFileRelativePath}': {reason}");
    }
}
