using WC2.API.Interfaces;
using WC2.API.Models;

namespace WC2.World.Services;

public sealed class RegionService : IRegionService
{
    private RegionsFileConfig _config;
    public RegionDefinition? CurrentRegion { get; private set; }

    public RegionService(RegionsFileConfig config) => _config = config;
    public void ApplyConfig(RegionsFileConfig config) => _config = config;

    public IReadOnlyList<RegionDefinition> GetRegions() => _config.Regions;
    public void ReloadDefinitions() { }

    public RegionDefinition? GetRegionForMap(string mapName)
    {
        foreach (var r in _config.Regions)
            foreach (var m in r.Maps)
                if (string.Equals(m, mapName, StringComparison.OrdinalIgnoreCase))
                    return r;
        return null;
    }

    /// <summary>Called by Plugin.cs on map start; returns the region if it changed.</summary>
    public RegionDefinition? EnterMap(string mapName)
    {
        var region = GetRegionForMap(mapName);
        CurrentRegion = region;
        return region;
    }
}
