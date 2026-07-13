using WC2.API.Models;

namespace WC2.API.Interfaces;

public interface IRegionService
{
    RegionDefinition? CurrentRegion { get; }
    RegionDefinition? GetRegionForMap(string mapName);
    IReadOnlyList<RegionDefinition> GetRegions();
    void ReloadDefinitions();
}
