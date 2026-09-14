using JadedBelles.Util.GridSystem;

namespace Match3Game
{
    /// <summary>
    /// Compatibility shim over <see cref="GridCell{TValue, TShape}"/> from
    /// <c>com.jadedbelles.util</c>. Preserves the legacy <c>SetGem</c>/<c>GetGem</c>/
    /// <c>SetLevelShaper</c>/<c>GetLevelShaper</c> method names so existing Match3 call sites
    /// keep compiling without changes.
    ///
    /// Migration path (future PR): rename SetGem→SetValue, GetGem→GetValue,
    /// SetLevelShaper→SetShape, GetLevelShaper→GetShape, then delete this shim.
    /// </summary>
    [System.Serializable]
    public class GridObj : GridCell<Gem, LevelShaperComponent>
    {
        // We accept the original GridSystem2D<GridObj> signature but pass null to the base
        // since neither the pre-shim GridObj nor the packaged GridCell surfaces this
        // reference through any code path Match3 exercises. If a caller ever needs
        // GetGrid() to return non-null, expose a covariant getter here.
        public GridObj(GridSystem2D<GridObj> grid, int x, int y) : base(null, x, y) { }

        public void SetGem(Gem gem) => SetValue(gem);
        public Gem GetGem() => GetValue();

        public void SetLevelShaper(LevelShaperComponent levelShaper) => SetShape(levelShaper);
        public LevelShaperComponent GetLevelShaper() => GetShape();
    }

    /// <summary>
    /// Legacy single-value cell wrapper — retained for parity with older Match3 code.
    /// Callers should prefer <see cref="GridCell{TValue, TShape}"/> or the packaged
    /// <see cref="JadedBelles.Util.GridSystem.GridObject{T}"/> directly.
    /// </summary>
    public class GridObject<T> : JadedBelles.Util.GridSystem.GridObject<T>
    {
        public GridObject(GridSystem2D<GridObject<T>> grid, int x, int y) : base(null, x, y) { }
    }
}
