using System;

namespace PlateauResoniteLink.Plateau.Application.Importing;

public interface IRemoteArchiveDistributionPolicy
{
    bool IsSupportedArchivePath(string path);
    string GetArchiveFileName(Uri archiveUri);
    string GetSourceArchivePath(string datasetRoot, Uri archiveUri, string archiveFileName);
    string GetSourceArchiveMetadataPath(string archivePath);
}
