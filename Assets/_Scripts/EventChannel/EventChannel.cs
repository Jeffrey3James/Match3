using System.Collections.Generic;
using UnityEngine;

public abstract class EventChannel<J> : ScriptableObject {
  readonly HashSet<EventListener<J>> observers = new();

  // Reused scratch buffer so Invoke() doesn't allocate a new list every call.
  private readonly List<EventListener<J>> _invokeBuffer = new();

  /// <summary>
  /// ScriptableObjects are project assets, not scene objects — they are NOT
  /// automatically cleared between Play Mode sessions, especially with
  /// "Enter Play Mode Options" set to skip domain reload. Without this,
  /// any listener that registered but never cleanly deregistered (e.g. a
  /// scene object destroyed without unsubscribing) stays referenced here
  /// forever, across every subsequent play session. OnDisable fires when
  /// exiting Play Mode / on domain reload, so this guarantees a clean slate.
  /// </summary>
  private void OnDisable() {
    observers.Clear();
    }

  public void Invoke(J value) {
    // Snapshot into a reused buffer before iterating: if any observer's
    // Raise() call triggers a Deregister (e.g. "this event destroys me,
    // which unsubscribes me" — a very common pattern), mutating
    // `observers` mid-foreach would throw InvalidOperationException.
    _invokeBuffer.Clear();
    _invokeBuffer.AddRange(observers);

    foreach (var observer in _invokeBuffer) {
      // Guard against Unity's "fake null" — a listener whose GameObject
      // was destroyed without ever calling Deregister (the exact bug
      // class we just fixed in Match3.OnDestroy). Without this check,
      // calling .Raise() on it throws MissingReferenceException.
      if (observer == null) {
        observers.Remove(observer);
        continue;
        }

      observer.Raise(value);
      }
    }

  public void Register(EventListener<J> observer) => observers.Add(observer);

  public void Deregister(EventListener<J> observer) => observers.Remove(observer);
  }

public readonly struct Empty { }