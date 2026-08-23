using DG.Tweening;
using Match3Game;
using Unity.VisualScripting;
using UnityEngine;

namespace Match3Game
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class Gem : MonoBehaviour
    {
        public bool isBeingDestroyed = false;
        [SerializeField] protected IntEventChannel gemChannel;

        private Tween pulseTween;
        public bool isSelected;

        public GemTypes type;
        protected int x;
        protected int y;

        // Add a reference to the grid so we can pass it during activation
        protected GridSystem2D<GridObj> grid;

        // Initialize or assign grid reference
        public virtual void Initialize(int x, int y, GridSystem2D<GridObj> grid)
        {
            this.x = x;
            this.y = y;
            this.grid = grid;
        }

        public void Select()
        {
            isSelected = true;

            //Stop existing tweening
            pulseTween?.Kill();

            pulseTween = transform.DOScale(.85f, .3f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine);
        }

        public void Deselect()
        {
            isSelected = false;
            pulseTween?.Kill();
            transform.localScale = Vector3.one;
        }

        public void SetXY(int x, int y, GridSystem2D<GridObj> grid)
        {
            this.x = x;
            this.y = y;
            this.grid = grid;
            // Optional: update name or debug log
            var name = type.ToString();
            gameObject.name = $"{name} ({x},{y})";
        }

        public virtual void SetType(GemTypes newType)
        {
            type = newType;
            GetComponent<SpriteRenderer>().sprite = newType.sprite;
        }

        public GemTypes GetGemType() => type;

        public void Activate()
        {
            if (type != null && grid != null)
            {
                type.Activate(this, x, y, grid);
            }
            else
            {
                Debug.LogWarning("Cannot activate gem: Missing type or grid reference");
            }
        }

        public void ClearDestroyFlag()
        {
            isBeingDestroyed = false;
        }

        public GemTypes GetGemTypeAt(Vector2Int position)
        {
            var gridObj = grid.GetValue(position.x, position.y);
            if (gridObj == null || gridObj.GetGem() == null)
            {
                Debug.LogWarning($"No gem at position {position}");
                return null; // Or return a default GemType
            }

            return gridObj.GetGem().GetGemType();
        }



    #region Event Channel Setup
    public virtual void CreateEvent()
    {
        gemChannel = IntEventChannel.CreateInstance<IntEventChannel>();
        gemChannel.name = gameObject.name + "ObstacleEventChannel";
    }

    public virtual IntEventChannel GetChannel()
    {
        if (gemChannel == null)
        {
            CreateEvent();
        }
        return gemChannel;
    }

    public virtual void SetChannel(EventChannel<int> channel)
    {
        gemChannel = channel as IntEventChannel;
    }
        #endregion

    }
}


