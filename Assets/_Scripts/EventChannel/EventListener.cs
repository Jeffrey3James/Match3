using UnityEngine;
using UnityEngine.Events;

public abstract class EventListener<J> : MonoBehaviour 
{
    [SerializeField] EventChannel<J> eventChannel;
    [SerializeField] UnityEvent<J> unityEvent;

    protected void Awake()
    {
        if(eventChannel == null)
        {
            return;
        }
        eventChannel.Register(this);
    }

    protected void OnDestroy()
    {
        eventChannel.Deregister(this);
    }

    public void Raise(J value)
    {
        unityEvent?.Invoke(value);
    }

    public EventChannel<J> GetEventChannel()
    {
        return eventChannel;
    }

    public void SetEventChannel(EventChannel<J> channel)
    {
        if (eventChannel != null)
        {
            eventChannel.Deregister(this);
        }

        eventChannel = channel;
        if (eventChannel != null)
        {
            eventChannel.Register(this);
        }
    }
}
public class EventListener : EventListener<Empty> { }
