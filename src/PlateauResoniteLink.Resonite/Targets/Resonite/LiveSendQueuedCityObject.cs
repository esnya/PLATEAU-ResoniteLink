using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendQueuedCityObject(
    ResoniteConstructionCityObject CityObject,
    Task<ResoniteObjectSlotHierarchy> ObjectHierarchyTask,
    AsyncWeightedGate.Lease MemoryLease);
