using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

public class LevelHandler : MonoBehaviour
{
    public static LevelHandler instance { get; private set; }

    [SerializeField] Level[] levels;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public List<Level> GetAllLevels()
    {
        return new List<Level>(levels);
    }
}
