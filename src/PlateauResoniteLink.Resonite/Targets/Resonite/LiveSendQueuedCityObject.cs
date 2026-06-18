using System.Threading.Tasks;

using PlateauResoniteLink.Resonite.Transport.ResoniteLink;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal sealed record LiveSendQueuedCityObject(
    ResoniteConstructionCityObject CityObject,
    Task<ResoniteObjectSlotHierarchy> ObjectHierarchyTask,
    AsyncWeightedGate.Lease MemoryLease);
