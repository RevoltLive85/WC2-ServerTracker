namespace WC2.Shared.Pooling;

/// <summary>Trivial non-thread-safe pool for the game thread; used for
/// per-tick scratch objects (StringBuilders, damage records) to keep GC quiet on 64p servers.</summary>
public sealed class ObjectPool<T> where T : class
{
    private readonly Stack<T> _items = new();
    private readonly Func<T> _factory;
    private readonly Action<T>? _reset;

    public ObjectPool(Func<T> factory, Action<T>? reset = null, int prewarm = 0)
    {
        _factory = factory; _reset = reset;
        for (var i = 0; i < prewarm; i++) _items.Push(factory());
    }

    public T Rent() => _items.Count > 0 ? _items.Pop() : _factory();
    public void Return(T item) { _reset?.Invoke(item); _items.Push(item); }
}
