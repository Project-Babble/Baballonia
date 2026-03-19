using System;
using System.Collections.Generic;
using System.Linq;

namespace Baballonia.Services;

public interface IEventBus
{
    void Subscribe<T>(Action<T> callback);

    void Unsubscribe<T>(Action<T> callback);

    void Publish<T>(T data);
}

public interface IFacePipelineEventBus : IEventBus
{
}

public interface IEyePipelineEventBus : IEventBus
{
}

public class FacePipelineEventBus : GenericEventBus, IFacePipelineEventBus
{
}

public class EyePipelineEventBus : GenericEventBus, IEyePipelineEventBus
{
}

public class GenericEventBus : IEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _subscribers = new();
    private object _lock = new();

    public void Subscribe<T>(Action<T> callback)
    {
        lock (_lock)
        {
            if (!_subscribers.TryGetValue(typeof(T), out var list))
            {
                list = new List<Delegate>();
                _subscribers[typeof(T)] = list;
            }

            list.Add(callback);
        }
    }

    public void Unsubscribe<T>(Action<T> callback)
    {
        lock (_lock)
        {
            if (_subscribers.TryGetValue(typeof(T), out var list))
            {
                list.Remove(callback);
                if (list.Count == 0)
                    _subscribers.Remove(typeof(T));
            }
        }
    }

    public void Publish<T>(T data)
    {
        lock (_lock)
        {
            if (_subscribers.TryGetValue(typeof(T), out var list))
            {
                foreach (var callback in list.Cast<Action<T>>())
                    callback(data);
            }
        }
    }
}
