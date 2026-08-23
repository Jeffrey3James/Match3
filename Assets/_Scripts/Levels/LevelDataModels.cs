using System;
using System.Collections.Generic;
using UnityEngine;

namespace Match3Game.Levels
{
    /// <summary>
    /// Plain serializable models for the data-driven level pipeline.
    /// Authored in Assets/Resources/Levels/levels.json and served remotely by the
    /// JadedBelles API at /api/v1/match3/levels. Parsed with JsonUtility, so every
    /// type here must stay a simple [Serializable] class (no nested arrays, no dictionaries).
    /// </summary>
    [Serializable]
    public class CellData
    {
        public int x;
        public int y;

        public CellData() { }

        public CellData(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public Vector2Int ToVector() => new Vector2Int(x, y);
    }

    [Serializable]
    public class ObjectiveData
    {
        /// <summary>Asset name of the GemTypes ScriptableObject to clear (e.g. "HeartGem").</summary>
        public string gemType;
        public int amount;
    }

    [Serializable]
    public class ObstacleData
    {
        /// <summary>Asset name of the Obstacle ScriptableObject (e.g. "Bubble", "Ice").</summary>
        public string type;
        public int health;
        public List<CellData> cells = new List<CellData>();
    }

    [Serializable]
    public class LevelData
    {
        public int id;
        public string name;
        public int maxMoves;
        public int width;
        public int height;
        public List<CellData> excludedCells = new List<CellData>();
        public List<ObjectiveData> objectives = new List<ObjectiveData>();
        public List<ObstacleData> obstacles = new List<ObstacleData>();
    }

    [Serializable]
    public class LevelCollection
    {
        public int version;
        public List<LevelData> levels = new List<LevelData>();
    }
}
