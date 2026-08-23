namespace Match3Game {

    //for using a single type in the grid system
    public class GridObject<T> {
        GridSystem2D<GridObject<T>> grid;
        int x;
        int y;
        T gem;
        
        public GridObject(GridSystem2D<GridObject<T>> grid, int x, int y) {
            this.grid = grid;
            this.x = x;
            this.y = y;
        }

        public void SetValue(T gem) {
            this.gem = gem;
        }
        
        public T GetValue() => gem;
        public int GetX() => x;
        public int GetY() => y;
    }

    [System.Serializable]
    //For using multiple different types in the grid system
    public class GridObj
    {
        private GridSystem2D<GridObj> grid;
        private int x;
        private int y;

        private Gem gem;
        private LevelShaperComponent levelShaper;

        public GridObj(GridSystem2D<GridObj> grid, int x, int y)
        {
            this.grid = grid;
            this.x = x;
            this.y = y;
        }

        public void SetGem(Gem gem) => this.gem = gem;
        public Gem GetGem() => gem;

        public void SetXY(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public void SetLevelShaper(LevelShaperComponent levelShaper) => this.levelShaper = levelShaper;
        public LevelShaperComponent GetLevelShaper() => levelShaper;

        public int GetX() => x;
        public int GetY() => y;
    }
}