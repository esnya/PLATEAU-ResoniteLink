using System;
using System.Threading;
using System.Threading.Tasks;

using ResoniteLink;

namespace PlateauResoniteLink.Resonite.Transport.ResoniteLink;

internal interface IGeometryImportSource
{
    string Description { get; }

    int VertexCount { get; }

    int SubmeshCount { get; }

    long? EstimatedByteLength { get; }
}

internal interface IRawGeometryPayloadSource : IGeometryImportSource
{
    ValueTask<ImportMeshRawData> MaterializeRawAsync(CancellationToken cancellationToken);
}

internal static class GeometryImportSourceMaterializer
{
    public static ValueTask<ImportMeshRawData> MaterializeRawAsync(
        IGeometryImportSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source is not IRawGeometryPayloadSource rawSource)
        {
            throw new InvalidOperationException(
                $"Geometry import source '{source.GetType().Name}' cannot materialize a raw geometry payload.");
        }

        return rawSource.MaterializeRawAsync(cancellationToken);
    }
}
