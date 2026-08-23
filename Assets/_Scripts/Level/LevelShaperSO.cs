using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LevelShaperSO", menuName = "Match3/LevelShaperSO/LevelShapers")]
public class LevelShaperSO : ScriptableObject
{
    [SerializeField] private List<LevelShaperSOGroups> positionGroups = new();


    private void OnEnable()
    {
        GetPositionsToExclude();
    }

    public List<Vector2Int> GetPositionsToExclude()
    {
        var all = new List<Vector2Int>();

        // 1) Pull in every group's positions
        if (positionGroups != null)
        {
            foreach (var group in positionGroups)
            {
                if (group != null)
                    all.AddRange(group.GetPositions());
            }
        }

        return all;
    }

    public List<LevelShaperSOGroups> GetPositionGroups()
    {
        return positionGroups;
    }
}
