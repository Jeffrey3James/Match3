using UnityEngine;
using StroTheGoat;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NUnit.Framework;

public class Test : MonoBehaviour
{

    public List<int> _diceResults = new List<int>();

    private void Start()
    {
        Dice.AddAllDiceOfDifferentTypes(
            new List<(int numberOfDice, int sides)>
            {
                (2, 6), 
                (3, 4), 
                (1, 20) 
            },
            _diceResults
        );
    }
}
