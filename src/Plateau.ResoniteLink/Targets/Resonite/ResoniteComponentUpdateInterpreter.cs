using ResoniteLink;

namespace Plateau.ResoniteLink.Targets.Resonite.Execution;

internal sealed class ResoniteComponentUpdateInterpreter
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Keep component update emission behind an instance interpreter so the update boundary stays injectable and explicit.")]
    public Task ApplyAsync(
        IResoniteLinkClient client,
        string componentId,
        IReadOnlyDictionary<string, Member> members,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentNullException.ThrowIfNull(members);

        return client.UpdateComponentAsync(
            new UpdateComponent
            {
                Data = new Component
                {
                    ID = componentId,
                    Members = members.ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value,
                        StringComparer.Ordinal),
                },
            },
            cancellationToken);
    }
}
