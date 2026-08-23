using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public abstract class EventChannel<J> : ScriptableObject
{
    readonly HashSet<EventListener<J>> observers = new();

    public void Invoke(J value)
    {
        foreach (var observer in observers)
        {
            observer.Raise(value);
        }
    }

    public void Register(EventListener<J> observer) => observers.Add(observer);

    public void Deregister(EventListener<J> observer) => observers.Remove(observer);
}

public readonly struct Empty { }
