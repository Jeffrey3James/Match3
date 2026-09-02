using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Timeline;
using Random = UnityEngine.Random;
using StroTheGoat;



namespace Match3Game
{
    public class Match3 : MonoBehaviour
    {
        // ------------------------------------------------------------
        // Speed tuning — every timed animation in Match3.cs and the
        // power-up strategies reads one of these constants (multiplied
        // by animScale, tunable in the Inspector). See CommercialPolish
        // gap-analysis item 3.
        // ------------------------------------------------------------
        public const float SWAP_DURATION       = 0.15f; // per-swap tween cap
        public const float FALL_DURATION       = 0.18f; // per-cell fall tween cap
        public const float POP_DURATION        = 0.12f; // per-gem pop tween cap
        public const float CASCADE_WAIT        = 0.06f; // wait between resolve steps
        public const float RESHUFFLE_FADE      = 0.20f; // fade in/out on no-moves reshuffle

        [Header("Speed tuning")]
        [Tooltip("Multiplies every tween/wait constant above. 1 = shipping cadence, <1 = faster, >1 = slower for debugging.")]
        [SerializeField] private float animScale = 1f;

        // Scaled accessors — call these instead of the raw consts so animScale
        // reaches every tween in one place. Guarded against zero/negative so
        // designers can't accidentally hard-freeze the board.
        private float ScaledSwap    => SWAP_DURATION   * Mathf.Max(0.01f, animScale);
        private float ScaledFall    => FALL_DURATION   * Mathf.Max(0.01f, animScale);
        private float ScaledPop     => POP_DURATION    * Mathf.Max(0.01f, animScale);
        private float ScaledCascade => CASCADE_WAIT    * Mathf.Max(0.01f, animScale);
        private float ScaledFade    => RESHUFFLE_FADE  * Mathf.Max(0.01f, animScale);

        // Single-slot buffered swap: when the player attempts a swap while the
        // resolve/cascade coroutine is still running, we stash it here and
        // replay it as soon as the board is stable. Only ONE swap is buffered
        // at a time (a second attempt while _bufferedSwap is non-null is
        // dropped). This is what gives the concurrent-matching feel — see
        // gap-analysis item 2.
        private (Vector2Int a, Vector2Int b)? _bufferedSwap = null;

        // Lightweight singleton so power-up strategies can query the board
        // for objectives, MatchJuice callers can find the camera, etc.
        // Only one Match3 lives per scene (the Awake log already asserts this).
        public static Match3 Instance { get; private set; }

        [Header("Grid Settings")]
        [SerializeField] private int width = 8;
        [SerializeField] private int height = 8;
        [SerializeField] private float cellSize = 1f;
        [SerializeField] private Vector3 originPosition = Vector3.zero;
        [SerializeField] private bool debug = true;

        [Header("Prefabs")]
        [SerializeField] private Gem gemPrefab;
        [SerializeField] private ObstacleGem obstaclePrefab;
        [SerializeField] private PowerUp powerUpPrefab;
        [SerializeField] private NukeGem nukePrefab;

        [Header("Lists to Instantiate")]
        [SerializeField] private GemTypes[] gemTypes;
        [SerializeField] private GemTypes[] gemTypesForPowerups;

        [Header("Level Settings")]
        [SerializeField] private int movesLeft;
        [SerializeField] private int obstaclesToClear;
        [SerializeField] private int objectivesToClear;
        [SerializeField] private Level level;

        [Header("Gem Interaction Settings")]
        [SerializeField] private Ease ease = Ease.InQuad;
        [SerializeField] private GameObject explosion;
        
        [Header("UI Settings")]
        [SerializeField] private TextMeshProUGUI movesLeftText;

        private HashSet<Vector2Int> invalidPositions = new HashSet<Vector2Int>();
        private List<(Vector2Int position, int powerupType, GemTypes gemType)> powerupSpawns = new();
        private const int gemValue = 15;
        [SerializeField] private int scoreForThisLevel = 0;
        private bool isRunningGameLoop = false;
        private bool isGameOver = false;

        private InputReader inputReader;
        private GridSystem2D<GridObj> grid2;
        private Vector2Int selectedGem = Vector2Int.one * -1;

        #region Event Delegates
        private System.Action onSwapStartedAction;
        private System.Func<GemTypes, GemTypes> onGetGemTypeFunc;
        private System.Action onLevelCompletedAction;
        #endregion

        [Tooltip("Fallback level used when PlayerHandler has no current level (offline, editor play, catalog unreachable).")]
        [SerializeField] private Level fallbackLevel;

        private void Awake()
        {
      Debug.Log($"[Match3] Board instances alive: {FindObjectsByType<Match3>(FindObjectsSortMode.None).Length}, scene={gameObject.scene.name}");
      Instance = this;
      inputReader = GetComponent<InputReader>();
            ResolveLevel();
        }

        private void ResolveLevel()
        {
            // Prefer the level chosen by PlayerHandler (from the JadedBelles catalog + player progress).
            if (PlayerHandler.instance != null)
            {
                var playerLevel = PlayerHandler.instance.GetCurrentLevel();
                if (playerLevel != null)
                {
                    level = playerLevel;
                    WarnIfLevelIsEmpty("PlayerHandler");
                    return;
                }
            }

            // Pressing Play directly on GameScene skips the splash, so PlayerHandler never got a
            // level. Go to the catalog ourselves rather than dropping to the blank asset — that
            // asset has no objectives and no obstacles, so the board loads with an empty top panel
            // and it looks like the obstacle UI is broken when it's really just missing data.
            if (level == null && LevelHandler.instance != null && LevelHandler.instance.LevelsReady)
            {
                var catalog = LevelHandler.instance.GetAllLevels();
                if (catalog.Count > 0)
                {
                    level = catalog[0];
                    Debug.LogWarning("Match3: no level from PlayerHandler. Using catalog level 0 (" +
                                     level.GetLevelName() + "). This is normal when playing " +
                                     "GameScene directly.");
                    WarnIfLevelIsEmpty("catalog");
                    return;
                }
            }

            // Last resort: a designer-assigned Level asset so the scene still boots offline.
            if (level == null && fallbackLevel != null)
            {
                level = fallbackLevel;
                Debug.LogWarning("Match3: using fallbackLevel because PlayerHandler had no current " +
                                 "level and the catalog wasn't ready.");
                WarnIfLevelIsEmpty("fallbackLevel");
            }

            if (level == null)
                Debug.LogError("Match3: could not resolve a Level from PlayerHandler, the catalog, " +
                               "or fallbackLevel. The board cannot build.");
        }

        /// <summary>
        /// A level with neither objectives nor obstacles is almost always a wiring or data fault,
        /// but it fails silently: the board builds, the top panel just stays empty. Say so.
        /// </summary>
        private void WarnIfLevelIsEmpty(string source)
        {
            if (level == null) return;

            int obstacles = level.GetObtacleConfigs() != null ? level.GetObtacleConfigs().Count : 0;
            int objectives = level.GetObjectives() != null ? level.GetObjectives().Count : 0;

            if (obstacles == 0 && objectives == 0)
            {
                Debug.LogWarning($"Match3: level '{level.GetLevelName()}' (from {source}) has no " +
                                 "objectives and no obstacles, so the top panel will render empty. " +
                                 "Check that levels.json has data for it and that every obstacle " +
                                 "name resolves in GemTypeRegistry.");
            }
        }

        private IEnumerator LevelComplete()
        {           
            List<Vector2Int> spotsToEpmty = new List<Vector2Int>();

            //Create new powerups to activate based on the amount of moves left in the level after completion
            while (spotsToEpmty.Count < movesLeft)
            {
                int randomX = Random.Range(0, width);
                int randomY = Random.Range(0, height);
                Vector2Int position = new Vector2Int(randomX, randomY);

                Debug.Log(position);
                if (spotsToEpmty.Contains(position))
                {
                    Debug.LogWarning("Already contains these coordinates skipping !!"); continue;
                }

                if (!IsValidPosition(position))
                {
                    Debug.LogWarning("InvalidPosition Get another one  !!"); continue;
                }

                spotsToEpmty.Add(position);

                int randomPowerUpType = Random.Range(0, gemTypesForPowerups.Length - 1);

                var gem = grid2.GetValue(position.x, position.y).GetGem();     
            }
            StartCoroutine(ExplodeGems(spotsToEpmty));
            
            GameEventsManager.instance.gameEvents.ScoreFinalized();
           yield return CoroutineUtils.AwaitTask(GameEventsManager.instance.gameEvents.PlayerDataSaved());
            Debug.Log(powerupSpawns.Count);

            yield return new WaitForSeconds(0.1f);
        }

        private void Start()
        {
            // Level may not be set yet if the JadedBelles catalog is still loading.
            // Re-resolve once, then bail out cleanly instead of NRE'ing on level.GetWidth().
            if (level == null) ResolveLevel();
            if (level == null)
            {
                Debug.LogError("Match3: no Level is available (PlayerHandler.playerCurrentLevel is null and fallbackLevel is unassigned). Aborting scene boot. Assign a Level asset to Match3.fallbackLevel or make sure the level catalog loads before entering this scene.");
                enabled = false;
                return;
            }

            var events = GameEventsManager.instance.gameEvents;

            width = level.GetWidth();
            height = level.GetHeight();
            InitializeGrid();
            inputReader.Fire += OnSelectGem;

            onLevelCompletedAction = () => StartCoroutine(LevelComplete());
            events.onLevelCompleted += onLevelCompletedAction;

            events.onObstacleCleared += UpdateObstacleToClear;
            events.onObjectiveProgressionChanged += UpdateObjectivesToClear;

            onGetGemTypeFunc = (inputType) => { return GetGemTypeFromMatch(inputType);};
            events.onGetGemType += onGetGemTypeFunc;

            onSwapStartedAction = () => { inputReader.enabled = false; };
            events.onSwapStarted += onSwapStartedAction;

            SetObjectiveAmount();
            SetMaxMoves();
            DeselectGem();
        }    

       private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            inputReader.Fire -= OnSelectGem;

            var events = GameEventsManager.instance.gameEvents;
            events.onObjectiveProgressionChanged -= UpdateObjectivesToClear;
            events.onSwapStarted -= onSwapStartedAction;
            events.onGetGemType -= onGetGemTypeFunc;
            events.onLevelCompleted -= onLevelCompletedAction;

            Debug.Log("Match3 Game Destroyed");
        }

        #region Main GameLoop

        private IEnumerator RunGameLoop(Vector2Int gridPosA, Vector2Int gridPosB)
        {
            // If the resolve loop is already spinning, capture this swap in
            // the single-slot buffer instead of dropping it. The tail of the
            // running loop (PostSettleTasks) will drain it. See item 2.
            if (isRunningGameLoop)
            {
                if (_bufferedSwap == null)
                {
                    _bufferedSwap = (gridPosA, gridPosB);
                    Debug.Log($"[Match3] Buffered swap {gridPosA} <-> {gridPosB} (resolve in progress)");
                }
                // A second attempt while one is already buffered is dropped
                // on the floor — the buffer is intentionally single-slot.
                yield break;
            }

            isRunningGameLoop = true;

            GameEventsManager.instance.gameEvents.SwapStarted();
            yield return StartCoroutine(SwapGems(gridPosA, gridPosB));
            List<Vector2Int> matches = FindMatches();

            if (matches.Count == 0)
            {
                yield return StartCoroutine(SwapGems(gridPosA, gridPosB)); // Swap back
                AudioManager.instance.PlayNoMatch();
                DeselectGem();
                isRunningGameLoop = false;
                Debug.Log("No matches found after swap, reverting swap.");
                yield break;
            }

            UpdateMovesLeft();
            int cascadeGuard = 0;
            do
            {
                // Safety valve: if a downstream exception ever prevents ExplodeGems from
                // clearing the matched cells, FindMatches would return the same match
                // forever and soft-lock the board (this happened on device when a null
                // AudioSource killed the explode coroutine). Bail out loudly instead.
                if (++cascadeGuard > 50)
                {
                    Debug.LogError("Cascade guard tripped: aborting match loop after 50 iterations. A coroutine is likely throwing before clearing matches.");
                    break;
                }

                yield return StartCoroutine(ExplodeGems(matches));
                SpawnPowerups();
                SpawnAct2Specials(); // ACT 2 HOOK #4 — see method doc comment below.
                yield return StartCoroutine(CheckAllAdjacentObstacles(new HashSet<Vector2Int>(matches)));
                
                yield return StartCoroutine(MakeGemsFall());
                yield return StartCoroutine(FillEmptySpots());
                matches = FindMatches();

            } while (matches.Count > 0);
            UpdateGemPOS();
            ValidateGridState();
            DeselectGem();
            GameOver();

            inputReader.enabled = true;
            isRunningGameLoop = false;

            // Board just settled. Trigger post-settle work: replay any
            // buffered swap first (item 2), then scan for a valid move and
            // reshuffle if none exists (item 1).
            StartCoroutine(PostSettleTasks());
        }

        private IEnumerator ExplodeGems(List<Vector2Int> matches)
        {
            GameEventsManager.instance.gameEvents.MatchMade();
            AudioManager.instance.PlayPop();
            foreach (var match in matches)
            {
                if (IsValidPosition(match))
                {
                    var gem = grid2.GetValue(match.x, match.y).GetGem();
                    grid2.SetValue(match.x, match.y, null);
                    ExplodeVFX(match);
                    gem.transform.DOPunchScale(Vector3.one * 0.1f, ScaledPop, 1, 0.5f);
                    if (isGameOver)
                    {
                        scoreForThisLevel += gemValue;
                        Debug.Log(scoreForThisLevel);
                        movesLeft--;
                        UpdateMovesText();
                        GameEventsManager.instance.gameEvents.ScoreChanged(scoreForThisLevel);
                        
                    }
                    gem.GetChannel().Invoke(-1); // Notify the gem's channel that it has been destroyed
                    yield return new WaitForSeconds(ScaledPop);
                    Destroy(gem.gameObject, ScaledPop);
                }
            }
            PlayerHandler.instance.AddCoins(scoreForThisLevel);

        }

        private IEnumerator CheckAllAdjacentObstacles(HashSet<Vector2Int> allMatches)
        {


            // Check adjacent obstacles around matched gems
            Vector2Int[] cardinalOffsets = new Vector2Int[]
            {
        new Vector2Int(0, 1),   // Up
        new Vector2Int(0, -1),  // Down
        new Vector2Int(1, 0),   // Right
        new Vector2Int(-1, 0)   // Left
            };

            HashSet<Vector2Int> activatedObstacles = new();

            foreach (var pos in allMatches)
            {
                foreach (var offset in cardinalOffsets)
                {
                    int nx = pos.x + offset.x;
                    int ny = pos.y + offset.y;

                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                    {
                        var neighborObj = grid2.GetValue(nx, ny);
                        var neighborGem = neighborObj?.GetGem();

                        if (neighborGem != null)
                        {
                            GemTypes type = neighborGem.GetGemType();
                            Vector2Int obstaclePos = new(nx, ny);

                            if (type is Obstacle obstacle && activatedObstacles.Add(obstaclePos))
                            {
                                obstacle.Activate(neighborGem, nx, ny, grid2);
                            }
                        }
                    }
                }
            }

            yield return new WaitForSeconds(ScaledCascade); // Small delay to allow animations or effects to play out
        }

        private IEnumerator MakeGemsFall()
        {
            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    if (grid2.GetValue(x, y) == null)
                    {
                        for (var i = y + 1; i < height; i++)
                        {
                            var fallingGridObj = grid2.GetValue(x, i);
                            if (fallingGridObj != null)
                            {
                                var gem = fallingGridObj.GetGem();
                                if (gem != null)
                                {                               
                                    fallingGridObj.SetXY(x, y);

                                    grid2.SetValue(x, y, fallingGridObj);
                                    grid2.SetValue(x, i, null);

                                   gem.SetXY(x, y, grid2);

                                    gem.transform
                                        .DOLocalMove(grid2.GetWorldPositionCenter(x, y), ScaledFall)
                                        .SetEase(ease)
                                        .WaitForCompletion();

                                    yield return new WaitForSeconds(ScaledCascade);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        private IEnumerator FillEmptySpots()
        {
            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    if (grid2.GetValue(x, y) == null)
                    {
                        CreateGem(x, y);
                        AudioManager.instance.PlayPop();
                        yield return new WaitForSeconds(ScaledCascade);
                    }
                }
            }
        }

        private List<Vector2Int> FindMatches()
        {
            HashSet<Vector2Int> allMatches = new();
            List<List<Vector2Int>> matchGroups = new();
            // ACT 2 ADDITION: horizontal and vertical runs kept separate (not just
            // merged into matchGroups) so ShapeMatchDetector can look for L/T
            // intersections between them below. Act 1's own logic below still only
            // ever reads matchGroups (the concatenation of both), unchanged.
            List<List<Vector2Int>> horizontalGroups = new();
            List<List<Vector2Int>> verticalGroups = new();
            powerupSpawns.Clear();

            // Horizontal
            for (int y = 0; y < height; y++)
            {
                int matchStart = 0;
                while (matchStart < width)
                {
                    List<Vector2Int> temp = new();

                    var baseGem = grid2.GetValue(matchStart, y).GetGem();
                    if (baseGem == null) { matchStart++; continue; }

                    GemTypes baseGemType = baseGem.GetGemType();
                    temp.Add(new Vector2Int(matchStart, y));
                    int next = matchStart + 1;

                    while (next < width)
                    {
                        var nextGem = grid2.GetValue(next, y).GetGem();
                        if (nextGem == null || nextGem.type != baseGem.type) break;
                        temp.Add(new Vector2Int(next, y));
                        next++;
                    }
                    if (temp.Count >= 3)
                    {
                        foreach (var pos in temp) allMatches.Add(pos);
                        matchGroups.Add(new(temp));
                        horizontalGroups.Add(new(temp));
                    }
                    matchStart = next;
                }
            }

            // Vertical
            for (int x = 0; x < width; x++)
            {
                int matchStart = 0;
                while (matchStart < height)
                {
                    List<Vector2Int> temp = new();
                    var baseGem = grid2.GetValue(x, matchStart).GetGem();
                    if (baseGem == null) { matchStart++; continue; }
                    temp.Add(new Vector2Int(x, matchStart));
                    int next = matchStart + 1;
                    while (next < height)
                    {
                        var nextGem = grid2.GetValue(x, next).GetGem();
                        if (nextGem == null || nextGem.type != baseGem.type) break;
                        temp.Add(new Vector2Int(x, next));
                        next++;
                    }
                    if (temp.Count >= 3)
                    {
                        foreach (var pos in temp) allMatches.Add(pos);
                        matchGroups.Add(new(temp));
                        verticalGroups.Add(new(temp));
                    }
                    matchStart = next;
                }
            }

            if(isGameOver)
            {
                Debug.Log("Game Over, no matches found");
                return new List<Vector2Int>();
            }

            // ============================================================
            // ACT 2 HOOK #1 — L/T-shape (wrapped tile) detection.
            // Gated behind player level 251+ via Act2SpecialTileManager.IsActive().
            // Calls OUT to the standalone Act2.ShapeMatchDetector (Assets/_Scripts/
            // Act2/ShapeMatchDetector.cs) rather than implementing shape detection
            // inline here — FindMatches() itself still only does straight-line runs.
            // Below level 251 this whole block is skipped and behavior is identical
            // to before Act 2 existed.
            // ============================================================
            if (Act2.Act2SpecialTileManager.IsActive() && Act2.Act2SpecialTileManager.instance != null)
            {
                var shapeMatches = Act2.ShapeMatchDetector.FindShapeMatches(horizontalGroups, verticalGroups);
                foreach (var shapeMatch in shapeMatches)
                {
                    Act2.Act2SpecialTileManager.instance.QueueWrapped(shapeMatch.intersection);
                }
            }

            foreach (var group in matchGroups)
            {
                var gem = grid2.GetValue(group[0].x, group[0].y)?.GetGem();
                if (gem != null)
                {
                    GemTypes gemType = gem.GetGemType();
                    GemTypes returnedType = GameEventsManager.instance.gameEvents.RequestGemType(gemType);                    
                    switch (group.Count)
                    {                        
                        case 3:
                            break;
                        case 4:
                            // ACT 2 HOOK #2 — striped-tile override. Below level 251,
                            // TryOverridePowerupSpawn always returns false and this is a
                            // no-op, so the Act 1 Bomb/Flower spawn below fires exactly
                            // as it always has. At/above level 251, Act 2 queues a
                            // Striped tile INSTEAD and we skip the Act 1 add. See
                            // Act2SpecialTileManager's coexistence doc comment for why.
                            if (Act2.Act2SpecialTileManager.instance != null &&
                                Act2.Act2SpecialTileManager.instance.TryOverridePowerupSpawn(group, gemType))
                            {
                                break;
                            }
                            //Bomb --- Flower 
                            powerupSpawns.Add((group[0], 0, null));
                            break;
                        case 5:
                            // ACT 2 HOOK #3 — color-bomb override (same pattern as case 4,
                            // see comment above). Below level 251 this is a no-op and the
                            // Act 1 Rocket spawn below fires exactly as it always has.
                            if (Act2.Act2SpecialTileManager.instance != null &&
                                Act2.Act2SpecialTileManager.instance.TryOverridePowerupSpawn(group, gemType))
                            {
                                break;
                            }
                            //Rockets -- Vine
                            int[] rocketPowerups = { 1, 2 };
                            int randomRocket = rocketPowerups[Random.Range(0, rocketPowerups.Length)];
                            Debug.Log(randomRocket);
                            powerupSpawns.Add((group[0], randomRocket, null));
                            break;
                            //Hammer -- Acorn
                        case 6:
                            powerupSpawns.Add((group[0], 3, null));
                            break;
                            //Missile -- Crossbow
                        case 7:
                            powerupSpawns.Add((group[0], 4, null));
                            break;
                            //Nuke -- Forest Emblem
                        case 8:
                            powerupSpawns.Add((group[0], 5, returnedType));
                            break;
                        default:
                            break;
                    }
                }
            }

            // Remove non-normal gems from match list
            allMatches.RemoveWhere(pos =>
            {
                var gridObj = grid2.GetValue(pos.x, pos.y);
                var gem = gridObj?.GetGem();
                return gem == null || gem.GetGemType().gemCategory != GemTypes.GemCategory.Normal;
            });
            return new List<Vector2Int>(allMatches);
        }

        private IEnumerator SwapGems(Vector2Int gridPosA, Vector2Int gridPosB)
        {
            var gemA = grid2.GetValue(gridPosA.x, gridPosA.y);
            var gemB = grid2.GetValue(gridPosB.x, gridPosB.y);

            if (gemA == null || gemB == null)
            {
                Debug.LogWarning("One of the gems to swap is null.");
                yield break;
            }

            // Update internal coordinates BEFORE moving
            gemA.SetXY(gridPosB.x, gridPosB.y);
            gemB.SetXY(gridPosA.x, gridPosA.y);

            // Update gem positions
            gemA.GetGem().SetXY(gridPosB.x, gridPosB.y, grid2);
            gemB.GetGem().SetXY(gridPosA.x, gridPosA.y, grid2);

            gemA.GetGem().Deselect();
            gemB.GetGem().Deselect();

            // Animate the visual movement — duration comes from SWAP_DURATION
            // (item 3 in the gap doc) so all swaps stay in the 0.15s target.
            gemA.GetGem().transform
                .DOLocalMove(grid2.GetWorldPositionCenter(gridPosB.x, gridPosB.y), ScaledSwap)
                .SetEase(ease)
                .WaitForCompletion();
            gemB.GetGem().transform
                .DOLocalMove(grid2.GetWorldPositionCenter(gridPosA.x, gridPosA.y), ScaledSwap)
                .SetEase(ease)
                .WaitForCompletion();

            // Update grid references
            grid2.SetValue(gridPosA.x, gridPosA.y, gemB);
            grid2.SetValue(gridPosB.x, gridPosB.y, gemA);


            yield return new WaitForSeconds(ScaledSwap);
        }

        private void OnSelectGem()
        {
            if (isGameOver) return;
            var gridPos = grid2.GetXY(Camera.main.ScreenToWorldPoint(inputReader.Selected));
            if (!IsValidPosition(gridPos) || IsEmptyPosition(gridPos) || IsObstacle(gridPos)) return;
            
            //get teh grid Object at that position, Get the gem that is there
   
            Gem gem = grid2.GetValue(gridPos.x, gridPos.y).GetGem();

            if (selectedGem == gridPos)
            {
                DeselectGem();
                gem.Deselect();
                Debug.Log("deselected GEm");
                AudioManager.instance.PlayDeselect();
            }
            else if (selectedGem == Vector2Int.one * -1)
            {
                SelectGem(gridPos);
                gem.Select();
                AudioManager.instance.PlayClick();
            }
            else if (IsAdjacent(selectedGem, gridPos))
            {
                // RunGameLoop handles the isRunningGameLoop == true case by
                // stashing into _bufferedSwap, so calling it unconditionally
                // gives us buffered-swap behavior (item 2) for free.
                StartCoroutine(RunGameLoop(selectedGem, gridPos));
            }
            else
            {
                // Second click is not orthogonally adjacent to the currently selected gem.
                // Treat it as re-selecting the new gem rather than an illegal long-distance swap.
                var previousGem = grid2.GetValue(selectedGem.x, selectedGem.y)?.GetGem();
                if (previousGem != null) previousGem.Deselect();

                SelectGem(gridPos);
                gem.Select();
                AudioManager.instance.PlayClick();
            }
        }

        // True only when b is directly left, right, above, or below a (Manhattan distance == 1).
        // Diagonals and same-cell are rejected.
        private static bool IsAdjacent(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x);
            int dy = Mathf.Abs(a.y - b.y);
            return (dx == 1 && dy == 0) || (dx == 0 && dy == 1);
        }

        #endregion

        #region PowerUp GameLoop

        private IEnumerator FindMatchesAfterPowerUp()
        {
            if (isRunningGameLoop)
                yield break;

            isRunningGameLoop = true;

            GameEventsManager.instance.gameEvents.SwapStarted();

            List<Vector2Int> newMatches = FindMatches();
            int cascadeGuard = 0;
            do
            {
                if (++cascadeGuard > 50)
                {
                    Debug.LogError("Cascade guard tripped in powerup loop: aborting after 50 iterations.");
                    break;
                }
                yield return StartCoroutine(ExplodeGems(newMatches));
                SpawnPowerups();
                SpawnAct2Specials(); // ACT 2 HOOK #4 — see method doc comment below.
                yield return StartCoroutine(MakeGemsFall());
                yield return StartCoroutine(FillEmptySpots());
                newMatches = FindMatches();
            } while (newMatches.Count > 0);

            UpdateGemPOS();
            DeselectGem();
            inputReader.enabled = true; // Re-enable input after processing matches
            yield return new WaitForSeconds(ScaledCascade);

            inputReader.enabled = true;
            isRunningGameLoop = false;

            // Board is stable now — reshuffle if no valid swap exists, then
            // drain any input that arrived while we were resolving.
            StartCoroutine(PostSettleTasks());
        }

        private void SpawnPowerups()
        {
            foreach (var (pos, type, gemType) in powerupSpawns)
            {
                if (grid2.GetValue(pos.x, pos.y) == null)
                {
                    CreatePowerUpGem(pos.x, pos.y, type, gemType); // Pass the stored gem type
                }
            }
            powerupSpawns.Clear();
        }

        // ACT 2 ADDITION — minimal hook only. Mirrors SpawnPowerups() immediately
        // above: called right after it from both game-loop coroutines. Delegates
        // all actual instantiation to Act2SpecialTileManager.SpawnQueuedSpecials()
        // (Assets/_Scripts/Act2/Act2SpecialTileManager.cs), which owns its own
        // prefab/asset references and grid-write logic — nothing Act2-specific
        // lives in this method body beyond the null/no-op guard. If no manager
        // instance exists (Act 2 not present in the scene) or the level gate is
        // below 251, the pending-spawn list managed by Act2SpecialTileManager is
        // simply empty and this is a harmless no-op, identical to pre-Act2 behavior.
        private void SpawnAct2Specials()
        {
            if (Act2.Act2SpecialTileManager.instance == null) return;
            Act2.Act2SpecialTileManager.instance.SpawnQueuedSpecials(grid2, transform);
        }

        private void CreatePowerUpGem(int x, int y, int powerupType, GemTypes types)
        {
            Gem gemInstance = null;

            // Spawn correct prefab
            if (powerupType == 5) // Nuke powerup (assuming 4 is nuke)
            {
                var nuke = Instantiate(nukePrefab, grid2.GetWorldPositionCenter(x, y), Quaternion.identity, transform);
                gemInstance = nuke;
                gemInstance.SetType(gemTypesForPowerups[powerupType]);

                if (gemInstance is NukeGem nukeGem)
                {
                    nukeGem.SetTargetGemType(types);  
                }
            }
            else 
            {
                gemInstance = Instantiate(powerUpPrefab, grid2.GetWorldPositionCenter(x, y), Quaternion.identity, transform);
                gemInstance.SetType(gemTypesForPowerups[powerupType]);

                if (gemInstance is PowerUp powerUpGem && powerupType == 5)
                {
                    powerUpGem.SetTypeToDestroy(types);  // Use passed-in type here too!
                }
            }

            // Register the gem in the grid
            var gridObj = new GridObj(grid2, x, y);
            gridObj.SetGem(gemInstance);
            grid2.SetValue(x, y, gridObj);
        }

        private IEnumerator CheckForPowerup(int x, int y, GridObj gridObject, GemTypes type)
        {
            if (type.gemCategory != GemTypes.GemCategory.Normal)
            {
                if (type is PowerUpGems powerups)
                {
                    UpdateMovesLeft();
                    type.Activate(gridObject.GetGem(), x, y, grid2);
                    yield return StartCoroutine(MakeGemsFall());
                    yield return StartCoroutine(FillEmptySpots());
                    yield return StartCoroutine(FindMatchesAfterPowerUp());
                }
            }
        }

        #endregion

        #region Utilities

        private GemTypes GetGemTypeFromVector2Int(Vector2Int gridPos, out Gem gem)
        {
            var gridObj = grid2.GetValue(gridPos.x, gridPos.y);
            if (gridObj != null)
            {
                gem = gridObj.GetGem();
                var gemType = gem.GetGemType();
                Debug.Log(gemType);
                return gemType;
            }
            else
            {
                gem = null;
            }
            return null;
        }

        bool IsEmptyPosition(Vector2Int pos) => grid2.GetValue(pos.x, pos.y) == null;
        bool IsObstacle(Vector2Int pos) => grid2.GetValue(pos.x, pos.y).GetGem().GetGemType().IsObstacle();
 
        private bool IsValidPosition(Vector2Int pos)
        {
            bool inBounds = pos.x >= 0 && pos.x < width && pos.y >= 0 && pos.y < height;
            return inBounds && !invalidPositions.Contains(pos);
        }

        private void MarkInvalidPosition(Vector2Int pos)
        {
            invalidPositions.Add(pos);
        }

        private void ValidateGridState()
        {
            Dictionary<Vector2Int, List<string>> positionGems = new Dictionary<Vector2Int, List<string>>();

            foreach (var T in grid2.GridArray)
            {
                if (T?.GetGem() != null)
                {
                    var pos = new Vector2Int(T.GetX(), T.GetY());
                    var gemName = T.GetGem().name;

                    if (!positionGems.ContainsKey(pos))
                        positionGems[pos] = new List<string>();

                    positionGems[pos].Add(gemName);
                }
            }

            // Also check actual GameObjects in scene
            var allGems = FindObjectsByType<Gem>(FindObjectsSortMode.None);
            Dictionary<Vector3, List<string>> worldPositions = new Dictionary<Vector3, List<string>>();

            foreach (var gem in allGems)
            {
                var worldPos = gem.transform.position;
                var roundedPos = new Vector3(
                    Mathf.Round(worldPos.x * 10f) / 10f,
                    Mathf.Round(worldPos.y * 10f) / 10f,
                    worldPos.z
                );

                if (!worldPositions.ContainsKey(roundedPos))
                    worldPositions[roundedPos] = new List<string>();

                worldPositions[roundedPos].Add(gem.name);
            }

            // Log duplicates
            foreach (var kvp in positionGems.Where(x => x.Value.Count > 1))
            {
                Debug.LogError($"GRID: Multiple gems at position {kvp.Key}: {string.Join(", ", kvp.Value)}");
            }

            foreach (var kvp in worldPositions.Where(x => x.Value.Count > 1))
            {
                Debug.LogError($"WORLD: Multiple gems at world position {kvp.Key}: {string.Join(", ", kvp.Value)}");
            }
        }

        private GemTypes GetGemTypeFromMatch(GemTypes types)
        {
            return types;
        }

        private List<GridObj> GetTheWholeGrid()
        {
            List<GridObj> gridObjects = new List<GridObj>();
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var gridObj = grid2.GetValue(x, y);
                    if (gridObj != null)
                    {
                        gridObjects.Add(gridObj);
                    }
                }
            }
            return gridObjects;
        }
        #endregion

        #region Game / Level Logic

        private void UpdateMovesLeft()
        {
            if (isGameOver) return;
            movesLeft--;
            UpdateMovesText();
        }

        private void SetObjectiveAmount()
        {
            int totalObjectives = level.GetObjectives().Sum(obj => obj.GetObjectiveConfigAmountToClear());
            objectivesToClear = totalObjectives;
        }

        private void GameOver()
        {
            if (obstaclesToClear <= 0  && objectivesToClear <= 0)
            {
                isGameOver = true;
                inputReader.enabled = false;
                GameEventsManager.instance.gameEvents.LevelCompleted();
            }
            else if (movesLeft <= 0)
            {
                isGameOver = true;
                inputReader.enabled = false;
                Debug.Log("Level Failed! No moves left.");
                GameEventsManager.instance.gameEvents.LevelFailed();
            }

            float percentage = ((float)movesLeft / level.GetMaxMoves()) * 100f;
            Debug.Log(percentage);
        }

        /// <summary>
        /// Rewarded-ad rescue: resumes a board that ended ONLY because the player
        /// ran out of moves. Grants <paramref name="amount"/> extra moves, clears
        /// the game-over state, and re-enables input. Returns false (and changes
        /// nothing) if the board isn't in a moves-exhausted fail state — callers
        /// must only hide the fail panel when this returns true.
        /// </summary>
        public bool TryResumeWithExtraMoves(int amount)
        {
            if (amount <= 0 || !isGameOver) return false;
            if (obstaclesToClear <= 0 && objectivesToClear <= 0) return false; // level was won, not failed
            if (movesLeft > 0) return false; // failed for some other reason

            movesLeft += amount;
            isGameOver = false;
            inputReader.enabled = true;
            UpdateMovesText();
            Debug.Log($"Resumed with {amount} extra moves after rewarded ad.");
            return true;
        }

        /// <summary>
        /// Public hook for the in-run hammer booster. Destroys a single gem at
        /// (x, y) as if it were a match, then runs the normal cascade / fill /
        /// match-follow-up cycle so any resulting matches resolve automatically.
        /// Does NOT consume a move — hammer is free-cost, matching Royal Match.
        /// No-op if the coordinates are out of range, the cell is empty, or the
        /// board is in game-over state.
        /// </summary>
        public void RemoveGemAt(int x, int y)
        {
            var pos = new Vector2Int(x, y);
            if (!IsValidPosition(pos)) return;
            if (grid2.GetValue(x, y) == null) return;
            if (isGameOver) return;

            StartCoroutine(RemoveGemRoutine(pos));
        }

        private IEnumerator RemoveGemRoutine(Vector2Int pos)
        {
            var single = new List<Vector2Int> { pos };
            yield return StartCoroutine(ExplodeGems(single));
            yield return StartCoroutine(CheckAllAdjacentObstacles(new HashSet<Vector2Int>(single)));
            yield return StartCoroutine(MakeGemsFall());
            yield return StartCoroutine(FillEmptySpots());
            yield return StartCoroutine(FindMatchesAfterPowerUp());
        }

        private void SetMaxMoves()
        {
            movesLeft = level.GetMaxMoves();
            UpdateMovesText();
        }

        private void InitializeGrid()
        {

            grid2 = GridSystem2D<GridObj>.VerticalGrid(width, height, cellSize, originPosition, debug);

            // Exclude positions that are not valid for gem placement

            level.GetExcludedPositions().ForEach(pos =>
            {
                if (IsValidPosition(pos))
                {
                    ShapeLevel(pos.x, pos.y);
                    MarkInvalidPosition(pos);
                }
            });

            foreach(var config in level.GetObtacleConfigs())
            {
                InstantiateObstacleConfigAt(config);
            }

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {

                    var p = new Vector2Int(x, y);
                    if (!invalidPositions.Contains(p))
                    {
                        if (grid2.GetValue(p.x, p.y) != null && grid2.GetValue(p.x, p.y).GetGem() != null)
                            continue;
                        CreateGem(x, y);
                    }
                }
            }

            DebugGridArray();
        }

        private void DebugGridArray()
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var gridObj = grid2.GetValue(x, y);

                    if (gridObj == null)
                    {                      
                        continue;
                    }

                    var gem = gridObj.GetGem();
                    if (gem == null)
                    {
                        continue;
                    }

                    var type = gem.GetGemType();
                    string typeName = type != null ? type.name : "Unknown";
                }
            }
        }

        private void ExplodeVFX(Vector2Int match)
        {
            var fx = Instantiate(explosion, transform);
            fx.transform.position = grid2.GetWorldPositionCenter(match.x, match.y);
            Destroy(fx, 5f);
            if(isGameOver)
            {
                Debug.Log("ExplodeGem called after gameOver");
            }
        }

        private void CreateGem(int x, int y)
        {

            var gem = Instantiate(gemPrefab, grid2.GetWorldPositionCenter(x, y), Quaternion.identity, transform);
            gem.SetType(gemTypes[Random.Range(0, gemTypes.Length)]);

            var gridObj = new GridObj(grid2, x, y);
            gridObj.SetGem(gem);
            grid2.SetValue(x, y, gridObj);
            gem.Initialize(x, y, grid2);

            gem.SetChannel(level.GetOrCreateChannelObjConfig(gem.GetGemType())); // Set the channel for the gem
        }

        public static void CreateGemEditor(int x, int y, Match3 instance)
        {
            instance.CreateGem(x, y);
        }

        private void InstantiateObstacleConfigAt(ObstacleConfig obstacleConfig)
        {
            for (int i = 0; i < obstacleConfig.GetLocation().Count; i++)
            {
                var location = obstacleConfig.GetLocation()[i];

                if (!IsValidPosition(new Vector2Int(location.x, location.y)))
                {
                    Debug.LogWarning($"Invalid position for obstacle: {location.x}, {location.y}");
                    return;
                }

                var obstacleGem = Instantiate(obstaclePrefab, grid2.GetWorldPositionCenter(location.x, location.y), Quaternion.identity, transform);
                obstacleGem.SetType(obstacleConfig.obstacle); // obstacle is a GemType

                obstacleGem.SetHealth(obstacleConfig.GetHealth()); // Set the health of the obstacle gem


                var gridObj = new GridObj(grid2, location.x, location.y);
                gridObj.SetGem(obstacleGem);
                grid2.SetValue(location.x, location.y, gridObj);
                obstacleGem.Initialize(location.x, location.y, grid2);
                obstacleGem.SetXY(location.x, location.y, grid2); // Set the position of the gem

                obstacleGem.SetChannel(level.GetOrCreateChannel(obstacleConfig.obstacle)); // Set the channel for the obstacle gem
                obstaclesToClear++;
            }         
        }

        private void ShapeLevel(int x, int y)
        {
            var levelGO = Instantiate(level.GetLevelShaperPrefab(), grid2.GetWorldPositionCenter(x, y), Quaternion.identity, transform);
            var levelShaper = levelGO.GetComponent<LevelShaperComponent>();

            var gridObj = new GridObj(grid2, x, y);
            gridObj.SetLevelShaper(levelShaper);
            grid2.SetValue(x, y, gridObj);
            levelShaper.Initialize(x, y, grid2);
        }

        private void SelectGem(Vector2Int gridPos)
        {
            selectedGem = gridPos;
            int x = selectedGem.x;
            int y = selectedGem.y;

            var gridObject = grid2.GetValue(x, y);

            if (gridObject != null && gridObject.GetGem() != null)
            {
                GemTypes type = gridObject.GetGem().GetGemType();
                StartCoroutine(CheckForPowerup(x, y, gridObject, type));
            }
            else
            {
                Debug.LogWarning("Selected an empty or invalid grid position.");
            }
        }

        private void UpdateGemPOS()
        {
            foreach (var T in grid2.GridArray)
            {
                var pos = new Vector2Int(T.GetX(), T.GetY());

                // Skip if the position is invalid
                if (invalidPositions.Contains(pos))
                {
                    continue;
                }

                var gem = T.GetGem();
                if (gem == null)
                {
                    continue;
                }
                gem.SetXY(pos.x, pos.y, grid2);
            }
        }

        void DeselectGem() => selectedGem = new Vector2Int(-1, -1);

        private void UpdateObstacleToClear()
        {
            if (obstaclesToClear >= 0)
            {
                obstaclesToClear--;
            }
        }

        private void UpdateObjectivesToClear()
        {
            if (objectivesToClear >= 0)
            {
                objectivesToClear--;
            }
        }

        public Level GetLevel() => level;

        /// <summary>
        /// Objectives the level is asking the player to clear. Read-only view
        /// for external systems (rocket/missile seeking, HUD, etc.). Returns
        /// an empty list if no level is bound yet.
        /// </summary>
        public IReadOnlyList<ObjectiveConfig> GetActiveObjectives()
        {
            if (level == null) return System.Array.Empty<ObjectiveConfig>();
            return level.GetObjectives();
        }

        /// <summary>
        /// Grid positions that currently hold a gem matching one of the
        /// active objective gem types. Called by seeking rockets/missiles
        /// (item 6 in the gap doc) to bias their flight path toward the
        /// closest still-alive objective tile.
        /// </summary>
        public List<Vector2Int> GetActiveObjectivePositions()
        {
            var results = new List<Vector2Int>();
            if (level == null || grid2 == null) return results;

            var objectives = level.GetObjectives();
            if (objectives == null || objectives.Count == 0) return results;

            var objectiveTypes = new HashSet<GemTypes>();
            foreach (var o in objectives)
            {
                var t = o.GetObjectiveConfigGemType();
                if (t != null) objectiveTypes.Add(t);
            }
            if (objectiveTypes.Count == 0) return results;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var go = grid2.GetValue(x, y);
                    var gem = go?.GetGem();
                    if (gem == null) continue;
                    if (objectiveTypes.Contains(gem.GetGemType()))
                        results.Add(new Vector2Int(x, y));
                }
            }
            return results;
        }

        #endregion

        #region Post-Settle: Buffered Swap + No-Moves Reshuffle

        /// <summary>
        /// Runs every time the board settles (end of RunGameLoop /
        /// FindMatchesAfterPowerUp). Two responsibilities:
        ///   1. If the player queued a swap during the cascade, replay it now.
        ///   2. Otherwise, scan for any valid swap; if none, reshuffle the
        ///      board (fade out → randomize colors preserving obstacles →
        ///      fade in → toast). Reshuffle does NOT consume a move.
        /// Only fires when the board is actually idle; guards against the
        /// game-over state and against re-entering during a still-in-progress
        /// cascade.
        /// </summary>
        private IEnumerator PostSettleTasks()
        {
            if (isGameOver) yield break;
            if (isRunningGameLoop) yield break;

            // --- 1. Drain the buffered swap first ------------------------
            if (_bufferedSwap.HasValue)
            {
                var pending = _bufferedSwap.Value;
                _bufferedSwap = null;

                // Re-validate the coordinates. During a cascade the tiles
                // that were originally selected may have moved / been
                // destroyed. Only replay if both endpoints still hold a
                // swappable gem.
                if (IsValidPosition(pending.a) && IsValidPosition(pending.b) &&
                    !IsEmptyPosition(pending.a) && !IsEmptyPosition(pending.b) &&
                    !IsObstacle(pending.a) && !IsObstacle(pending.b) &&
                    IsAdjacent(pending.a, pending.b))
                {
                    Debug.Log($"[Match3] Replaying buffered swap {pending.a} <-> {pending.b}");
                    yield return StartCoroutine(RunGameLoop(pending.a, pending.b));
                    yield break; // RunGameLoop will re-trigger PostSettleTasks itself
                }
                else
                {
                    Debug.Log("[Match3] Dropped buffered swap — endpoints no longer valid.");
                }
            }

            // --- 2. No-moves reshuffle ----------------------------------
            if (!HasAnyValidSwap())
            {
                yield return StartCoroutine(ReshuffleBoard());
            }
        }

        /// <summary>
        /// Scans every cell for an adjacent swap that would produce a
        /// 3-in-a-row. Returns true on the first hit. Obstacles and empty
        /// cells are skipped (they cannot participate in a swap). This is
        /// O(width * height) and runs once per settle, so it is cheap.
        /// </summary>
        private bool HasAnyValidSwap()
        {
            if (grid2 == null) return true; // pre-init: don't reshuffle

            // Only check right/up offsets to avoid double-testing each pair.
            Vector2Int[] offsets = { new Vector2Int(1, 0), new Vector2Int(0, 1) };

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var a = new Vector2Int(x, y);
                    if (!IsValidPosition(a) || IsEmptyPosition(a)) continue;
                    var gemA = grid2.GetValue(a.x, a.y).GetGem();
                    if (gemA == null || gemA.GetGemType() == null) continue;
                    // Obstacles never swap; skip.
                    if (gemA.GetGemType().IsObstacle()) continue;

                    foreach (var off in offsets)
                    {
                        var b = a + off;
                        if (!IsValidPosition(b) || IsEmptyPosition(b)) continue;
                        var gemB = grid2.GetValue(b.x, b.y).GetGem();
                        if (gemB == null || gemB.GetGemType() == null) continue;
                        if (gemB.GetGemType().IsObstacle()) continue;

                        if (SwapWouldMatch(a, b)) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Predicts whether swapping a and b would form a 3-in-a-row without
        /// mutating the grid. Only considers Normal-category gems for match
        /// counts (obstacles and power-ups don't chain by color).
        /// </summary>
        private bool SwapWouldMatch(Vector2Int a, Vector2Int b)
        {
            var goA = grid2.GetValue(a.x, a.y);
            var goB = grid2.GetValue(b.x, b.y);
            if (goA == null || goB == null) return false;
            var gemA = goA.GetGem();
            var gemB = goB.GetGem();
            if (gemA == null || gemB == null) return false;

            // "type" is the same field the real FindMatches compares.
            return WouldFormLine(b, gemA.type) || WouldFormLine(a, gemB.type);
        }

        private bool WouldFormLine(Vector2Int pos, GemTypes type)
        {
            if (type == null) return false;

            // Count how many contiguous same-type neighbors exist in each
            // cardinal direction, excluding pos itself. If any pair adds up
            // to >= 2 (horizontal or vertical), pos completes a 3-run.
            int left  = CountRun(pos, -1, 0, type);
            int right = CountRun(pos,  1, 0, type);
            int down  = CountRun(pos,  0,-1, type);
            int up    = CountRun(pos,  0, 1, type);
            return (left + right) >= 2 || (up + down) >= 2;
        }

        private int CountRun(Vector2Int origin, int dx, int dy, GemTypes type)
        {
            int count = 0;
            int nx = origin.x + dx;
            int ny = origin.y + dy;
            while (nx >= 0 && nx < width && ny >= 0 && ny < height)
            {
                var go = grid2.GetValue(nx, ny);
                var g  = go?.GetGem();
                // Any gap or type change stops the run. We DON'T look at the
                // origin cell itself — the caller is asking "if this type
                // sat at origin, does it complete a line?"
                if (g == null || g.type != type) break;
                count++;
                nx += dx; ny += dy;
            }
            return count;
        }

        /// <summary>
        /// Whole-board reshuffle when no legal swap exists. Fades every
        /// normal gem out, randomizes their color (obstacles and power-ups
        /// are preserved in place — they never reshuffle), fades back in,
        /// and re-attempts up to 20 times. If still deadlocked, force a
        /// guaranteed match by planting three same-type gems in a row.
        /// Does not charge a move.
        /// </summary>
        private IEnumerator ReshuffleBoard()
        {
            Debug.Log("[Match3] No moves available — reshuffling board.");

            // TODO: wire toast — Match3UI has no ShowToast API yet, so we log.
            Debug.Log("No moves — shuffling!");

            // Lock input during the animation so the player can't queue a
            // swap into the fading gems.
            bool inputWasEnabled = inputReader.enabled;
            inputReader.enabled = false;

            var normalGems = CollectShufflableGems();

            // --- Fade out ---
            foreach (var g in normalGems)
            {
                var sr = g.GetComponent<SpriteRenderer>();
                if (sr != null) sr.DOFade(0f, ScaledFade);
            }
            yield return new WaitForSeconds(ScaledFade);

            // --- Randomize (retry up to 20x for a solvable board) ---
            const int MAX_RETRIES = 20;
            bool solvable = false;
            for (int attempt = 0; attempt < MAX_RETRIES && !solvable; attempt++)
            {
                RandomizeShufflableGems(normalGems);
                // Also nuke any accidental 3-runs the shuffle produced so the
                // player doesn't see phantom pops on fade-in.
                if (FindMatches().Count == 0 && HasAnyValidSwap())
                {
                    solvable = true;
                }
            }

            if (!solvable)
            {
                Debug.LogWarning("[Match3] Reshuffle failed after 20 tries — forcing guaranteed match.");
                ForceGuaranteedMatch(normalGems);
            }

            // --- Fade in ---
            foreach (var g in normalGems)
            {
                var sr = g.GetComponent<SpriteRenderer>();
                if (sr != null) sr.DOFade(1f, ScaledFade);
            }
            yield return new WaitForSeconds(ScaledFade);

            inputReader.enabled = inputWasEnabled;
        }

        private List<Gem> CollectShufflableGems()
        {
            var list = new List<Gem>();
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (!IsValidPosition(new Vector2Int(x, y))) continue;
                    var go = grid2.GetValue(x, y);
                    var g = go?.GetGem();
                    if (g == null) continue;
                    var t = g.GetGemType();
                    if (t == null) continue;
                    // Preserve obstacles and power-ups in place. Only normal
                    // color gems get their type randomized.
                    if (t.IsObstacle()) continue;
                    if (t.gemCategory != GemTypes.GemCategory.Normal) continue;
                    list.Add(g);
                }
            }
            return list;
        }

        private void RandomizeShufflableGems(List<Gem> gems)
        {
            if (gemTypes == null || gemTypes.Length == 0) return;
            foreach (var g in gems)
            {
                var newType = gemTypes[Random.Range(0, gemTypes.Length)];
                g.SetType(newType);
                g.SetChannel(level.GetOrCreateChannelObjConfig(newType));
            }
        }

        /// <summary>
        /// Last-resort deterministic fallback if 20 shuffle attempts couldn't
        /// produce a solvable board (extreme edge case, e.g. very small level
        /// with only one normal gem type). Plants two same-type gems adjacent
        /// to a third of the same type so a trivial swap wins.
        /// </summary>
        private void ForceGuaranteedMatch(List<Gem> gems)
        {
            if (gemTypes == null || gemTypes.Length == 0 || gems.Count < 3) return;
            var seed = gemTypes[0];

            // Find any horizontal triple of shufflable slots and stamp them.
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x <= width - 3; x++)
                {
                    var p0 = new Vector2Int(x,     y);
                    var p1 = new Vector2Int(x + 1, y);
                    var p2 = new Vector2Int(x + 2, y);
                    if (!IsShufflableCell(p0) || !IsShufflableCell(p1) || !IsShufflableCell(p2))
                        continue;

                    ApplyTypeAt(p0, seed);
                    ApplyTypeAt(p1, seed);
                    // Leave p2 as a different type but keep it adjacent so
                    // ONE trivial swap produces a match.
                    var alt = gemTypes.Length > 1 ? gemTypes[1] : seed;
                    ApplyTypeAt(p2, alt);
                    // Place a same-type gem next to p2 so swapping p2 with
                    // its neighbor creates the 3-in-a-row.
                    var p3 = new Vector2Int(x + 2, y + 1 < height ? y + 1 : y);
                    if (IsShufflableCell(p3)) ApplyTypeAt(p3, seed);
                    return;
                }
            }
        }

        private bool IsShufflableCell(Vector2Int p)
        {
            if (!IsValidPosition(p) || IsEmptyPosition(p)) return false;
            var g = grid2.GetValue(p.x, p.y).GetGem();
            if (g == null || g.GetGemType() == null) return false;
            if (g.GetGemType().IsObstacle()) return false;
            return g.GetGemType().gemCategory == GemTypes.GemCategory.Normal;
        }

        private void ApplyTypeAt(Vector2Int p, GemTypes t)
        {
            var g = grid2.GetValue(p.x, p.y).GetGem();
            if (g == null || t == null) return;
            g.SetType(t);
            g.SetChannel(level.GetOrCreateChannelObjConfig(t));
        }

        #endregion

        #region UI Things        

        private void UpdateMovesText()
        {            
            movesLeftText.text = movesLeft.ToString();
        }
        #endregion
    }
}
