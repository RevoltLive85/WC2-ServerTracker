using Microsoft.Extensions.Logging;
using WC2.API.Interfaces;

namespace WC2.Shared;

/// <summary>
/// Allocation-light synchronous event bus. Handler lists are copied on write
/// (subscriptions are rare, publishes are hot), so Publish iterates a stable
/// array with zero locking and zero LINQ.
/// </summary>
public sealed class Wc2EventBus : IWc2EventBus
{
    private readonly Dictionary<Type, Delegate[]> _handlers = new();
    private readonly ILogger _logger;

    public Wc2EventBus(ILogger logger) => _logger = logger;

    public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        lock (_handlers)
        {
            _handlers.TryGetValue(typeof(TEvent), out var existing);
            var next = existing is null ? new Delegate[1] : new Delegate[existing.Length + 1];
            existing?.CopyTo(next, 0);
            next[^1] = handler;
            _handlers[typeof(TEvent)] = next;
        }
    }

    public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        lock (_handlers)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var existing)) return;
            var list = new List<Delegate>(existing);
            list.Remove(handler);
            _handlers[typeof(TEvent)] = list.ToArray();
        }
    }

    public void Publish<TEvent>(TEvent evt) where TEvent : class
    {
        Delegate[]? snapshot;
        lock (_handlers) _handlers.TryGetValue(typeof(TEvent), out snapshot);
        if (snapshot is null) return;

        for (var i = 0; i < snapshot.Length; i++)
        {
            try { ((Action<TEvent>)snapshot[i]).Invoke(evt); }
            catch (Exception ex)
            {
                // One faulty subscriber must never break the chain for others.
                _logger.LogError(ex, "[WC2] Event handler for {Event} threw", typeof(TEvent).Name);
            }
        }
    }
}
