using System;
using UnityEngine;
using UnityEngine.XR;

public class GameEventsManager : MonoBehaviour
{
    public static GameEventsManager instance { get; set; }

    public GameEvents gameEvents;
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
        DontDestroyOnLoad(gameObject);
        gameEvents = new GameEvents();
    }
}
