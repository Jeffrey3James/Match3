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
                    return;
                }
            }

            // Fall back to a designer-assigned Level asset so the scene still boots offline.
            if (level == null && fallbackLevel != null)
            {
                level = fallbackLevel;
                Debug.LogWarning("Match3: using fallbackLevel because PlayerHandler had no current level.");
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
            if (isRunningGameLoop)
                yield break;

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
            do
            {
                yield return StartCoroutine(ExplodeGems(matches));
                SpawnPowerups();
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
                    gem.transform.DOPunchScale(Vector3.one * 0.1f, 0.1f, 1, 0.5f);
                    if (isGameOver)
                    {
                        scoreForThisLevel += gemValue;
                        Debug.Log(scoreForThisLevel);
                        movesLeft--;
                        UpdateMovesText();
                        GameEventsManager.instance.gameEvents.ScoreChanged(scoreForThisLevel);
                        
                    }
                    gem.GetChannel().Invoke(-1); // Notify the gem's channel that it has been destroyed
                    yield return new WaitForSeconds(0.1f);
                    Destroy(gem.gameObject, 0.1f);
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

            yield return new WaitForSeconds(.1f); // Small delay to allow animations or effects to play out
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
                                        .DOLocalMove(grid2.GetWorldPositionCenter(x, y), 0.5f)
                                        .SetEase(ease)
                                        .WaitForCompletion();

                                    yield return new WaitForSeconds(0.1f);
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
                        yield return new WaitForSeconds(.1f);
                    }
                }
            }
        }

        private List<Vector2Int> FindMatches()
        {
            HashSet<Vector2Int> allMatches = new();
            List<List<Vector2Int>> matchGroups = new();
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
                    }
                    matchStart = next;
                }
            }

            if(isGameOver)
            {
                Debug.Log("Game Over, no matches found");
                return new List<Vector2Int>();
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
                            //Bomb --- Flower 
                            powerupSpawns.Add((group[0], 0, null));
                            break;
                        case 5:
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

            // Animate the visual movement
            gemA.GetGem().transform
                .DOLocalMove(grid2.GetWorldPositionCenter(gridPosB.x, gridPosB.y), 0.5f)
                .SetEase(ease)
                .WaitForCompletion();
            gemB.GetGem().transform
                .DOLocalMove(grid2.GetWorldPositionCenter(gridPosA.x, gridPosA.y), 0.5f)
                .SetEase(ease)
                .WaitForCompletion();

            // Update grid references
            grid2.SetValue(gridPosA.x, gridPosA.y, gemB);
            grid2.SetValue(gridPosB.x, gridPosB.y, gemA);


            yield return new WaitForSeconds(0.5f);
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
            else
            {
                StartCoroutine(RunGameLoop(selectedGem, gridPos));
            }
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
            do
            {
                yield return StartCoroutine(ExplodeGems(newMatches));
                SpawnPowerups();
                yield return StartCoroutine(MakeGemsFall());
                yield return StartCoroutine(FillEmptySpots());
                newMatches = FindMatches();
            } while (newMatches.Count > 0);

            UpdateGemPOS();
            DeselectGem();
            inputReader.enabled = true; // Re-enable input after processing matches
            yield return new WaitForSeconds(.1f);

            inputReader.enabled = true;
            isRunningGameLoop = false;
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

        #endregion

        #region UI Things        

        private void UpdateMovesText()
        {            
            movesLeftText.text = movesLeft.ToString();
        }
        #endregion
    }
}
