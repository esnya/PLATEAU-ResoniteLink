using System;

namespace PlateauResoniteLink.Application.Importing;

public sealed record SceneImportExecutionPlan
{
    private SceneImportExecutionPlan(
        SceneImportRequest sceneImportRequest)
    {
        ArgumentNullException.ThrowIfNull(sceneImportRequest);

        SceneImportRequest = sceneImportRequest;
    }

    public SceneImportRequest SceneImportRequest { get; }

    public static SceneImportExecutionPlan Create(
        ResolvedLocalPlateauImportRequest resolvedRequest,
        ImportedSceneMetadata metadata,
        string workRoot,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(resolvedRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);
        ArgumentNullException.ThrowIfNull(commonMaterials);

        return new SceneImportExecutionPlan(
            new SceneImportRequest(
                metadata with
                {
                    Request = resolvedRequest.ToImportRequest(),
                },
                workRoot,
                commonMaterials));
    }
}
