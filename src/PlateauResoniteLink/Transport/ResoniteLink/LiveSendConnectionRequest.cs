namespace PlateauResoniteLink.Transport.ResoniteLink;

internal sealed record LiveSendConnectionRequest(
    string Dataset,
    string MeshCode);
