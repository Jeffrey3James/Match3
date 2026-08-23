using System.Threading.Tasks;
using JadedBelles.Networking;
using UnityEngine;

/// <summary>
/// Owns player progress persistence. Local-first: progress is always written to
/// PlayerPrefs so guests never lose anything, and it syncs with the JadedBelles API
/// (/api/v1/match3/me/save) whenever a session exists.
/// Replaces the old Unity Cloud Save manager; same scene object, same GUID.
/// </summary>
public class PlayerDataManager : MonoBehaviour
{
    public static PlayerDataManager instance { get; set; }

    private const string LocalSaveKey = "jb_player_data";

    private void OnEnable()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }
        DontDestroyOnLoad(gameObject);

        LoadLocalPlayerData();

        if (JadedBellesApiClient.Instance.HasSession)
        {
            PullRemotePlayerData();
        }
    }

    // ---------- Load ----------

    private void LoadLocalPlayerData()
    {
        string json = PlayerPrefs.GetString(LocalSaveKey, string.Empty);
        PlayerData data = null;

        if (!string.IsNullOrEmpty(json))
        {
            try { data = JsonUtility.FromJson<PlayerData>(json); }
            catch (System.Exception) { data = null; }
        }

        if (data == null || string.IsNullOrEmpty(data.playerName))
        {
            Debug.Log("No local player data found, initializing new player data.");
            data = new PlayerData("Player", 0, 5, 100, 0);
        }

        PlayerHandler.instance.RecievePlayerDataFromCloud(data);
        GameEventsManager.instance.gameEvents.PlayerDataLoaded();
    }

    /// <summary>Fetches the signed-in player's save and applies it when it is newer-looking.</summary>
    public void PullRemotePlayerData()
    {
        JadedBellesApiClient.Instance.GetMatch3Save(
            response =>
            {
                if (response.data == null) return;

                var local = PlayerHandler.instance.playerData;
                var remote = response.data;

                // The server save wins unless the local guest progress is further along.
                if (local == null || remote.playerLevel >= local.playerLevel)
                {
                    var data = new PlayerData(
                        string.IsNullOrEmpty(remote.playerName) ? "Player" : remote.playerName,
                        remote.playerLevel,
                        remote.playerLives,
                        remote.playerCoins,
                        remote.playerLifeCountdown);

                    PlayerHandler.instance.RecievePlayerDataFromCloud(data);
                    SaveLocal(data);
                    GameEventsManager.instance.gameEvents.PlayerDataLoaded();
                    Debug.Log("Player data synced from the JadedBelles API.");
                }
                else
                {
                    Debug.Log("Local progress is ahead of the server save; pushing it up.");
                    _ = UpdatePlayerData();
                }
            },
            error => Debug.LogWarning($"Could not fetch the remote save ({error}). Using local data."));
    }

    // ---------- Save ----------

    /// <summary>
    /// Persists current progress. Always saves locally; also pushes to the API when signed in.
    /// The task completes once the remote push finishes (or immediately for guests).
    /// </summary>
    public Task UpdatePlayerData()
    {
        PlayerData playerData = PlayerHandler.instance.SendPlayerDataToCloud(PlayerHandler.instance.playerData);
        SaveLocal(playerData);

        if (!JadedBellesApiClient.Instance.HasSession)
            return Task.CompletedTask;

        var completion = new TaskCompletionSource<bool>();

        var body = new Match3SaveRequest
        {
            playerName = playerData.playerName,
            playerLevel = playerData.playerLevel,
            playerLives = playerData.playerLives,
            playerCoins = playerData.playerCoins,
            playerLifeCountdown = playerData.playerLifeCountdown
        };

        JadedBellesApiClient.Instance.PutMatch3Save(
            body,
            _ =>
            {
                Debug.Log("Player data saved to the JadedBelles API.");
                completion.TrySetResult(true);
            },
            error =>
            {
                // Local save already succeeded, so a failed push should not break gameplay.
                Debug.LogWarning($"Could not push the save to the API ({error}).");
                completion.TrySetResult(false);
            });

        return completion.Task;
    }

    private static void SaveLocal(PlayerData data)
    {
        PlayerPrefs.SetString(LocalSaveKey, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
    }
}
