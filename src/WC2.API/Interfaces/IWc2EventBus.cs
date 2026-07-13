namespace WC2.API.Interfaces;

/// <summary>
/// In-process pub/sub decoupling all WC2 modules. Handlers run synchronously
/// on the game thread (CS2 API is not thread-safe); publishers must never
/// assume any subscriber exists.
/// </summary>
public interface IWc2EventBus
{
    void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
    void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
    void Publish<TEvent>(TEvent evt) where TEvent : class;
}
