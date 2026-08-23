using UnityEngine;
using Match3Game;

[RequireComponent(typeof(SpriteRenderer))]

public class LevelShaperComponent : MonoBehaviour
{
    private int x;
    private int y;
    private GridSystem2D<GridObj> grid;

    public void Initialize(int x, int y, GridSystem2D<GridObj> grid)
    {
        this.x = x;
        this.y = y;
        this.grid = grid;
    }
}

