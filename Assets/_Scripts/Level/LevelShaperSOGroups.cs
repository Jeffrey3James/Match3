using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[CreateAssetMenu(fileName = "LevelShaperSO", menuName = "Match3/LevelShaperSO/ShaperGroups")]
public class LevelShaperSOGroups : ScriptableObject
{
    [SerializeField] List<Vector2Int> levelShaperSOs;

    public List<Vector2Int> GetPositions()
    {
        if (levelShaperSOs == null)
        {
            Debug.LogWarning("levelShaperSOs is null");
            return new List<Vector2Int>();
        }
        return levelShaperSOs;
    }
}
