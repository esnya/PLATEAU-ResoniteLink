using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendQueuedCityObject(
    ResoniteConstructionCityObject CityObject,
    Task<ResoniteSharedSlotIndex.ObjectSlotHierarchy> ObjectHierarchyTask,
    AsyncWeightedGate.Lease MemoryLease);
