using CounterStrikeSharp.API.Core.Capabilities;

namespace WC2.API.Capabilities;

/// <summary>
/// CSSharp's PluginCapability&lt;T&gt;.Get() throws KeyNotFoundException when no
/// provider has registered yet (e.g. during load order races). The WC2 design
/// requires "absent module = null, degrade gracefully", so all framework code
/// must use GetOrNull() instead of Get().
/// </summary>
public static class Wc2CapabilityExtensions
{
    public static T? GetOrNull<T>(this PluginCapability<T> capability) where T : class
    {
        try { return capability.Get(); }
        catch { return null; }
    }
}
