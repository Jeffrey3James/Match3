using System;
using System.Globalization;
using System.Threading.Tasks;
using JadedBelles.Networking;
using UnityEngine;

/// <summary>
/// Owns player progress persistence. PlayerPrefs is authoritative so local progress is
/// never gated by the network; generic game-save sync is best effort for signed-in players.
/// Replaces the old Unity Cloud Save manager; same scene object, same GUID.
/// </summary>
public class PlayerDataManager : MonoBehaviour
{
    public static PlayerDataManager instance { get; set; }

    private const string LocalSaveKey = "jb_player_data";
    private const string LocalUpdatedAtKey = "jb_player_data_updated_at";
    private const string RemoteRevisionKey = "jb_player_data_revision";
    private const int SaveSlot = 0;
    private const int SaveSchemaVersion = 1;

    private void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }
        DontDestroyOnLoad(gameObject);
    }

    // Start runs after every Awake in the scene, so PlayerHandler.instance and
    // GameEventsManager.instance are guaranteed to exist before we hand them data.
    private void Start()
    {
        if (instance != this) return;

        LoadLocalPlayerData();

        if (JadedBellesApiClient.Instance != null && JadedBellesApiClient.Instance.HasSession)
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
            catch (Exception) { data = null; }
        }

        if (data == null || string.IsNullOrEmpty(data.playerName))
        {
            Debug.Log("No local player data found, initializing new player data.");
            data = new PlayerData("Player", 0, 5, 100, 0);
        }

        if (PlayerHandler.instance == null)
        {
            Debug.LogWarning("PlayerDataManager: PlayerHandler.instance is null; can't hand off player data. Is PlayerHandler in this scene?");
            return;
        }
        PlayerHandler.instance.RecievePlayerDataFromCloud(data);

        if (GameEventsManager.instance != null && GameEventsManager.instance.gameEvents != null)
        {
            GameEventsManager.instance.gameEvents.PlayerDataLoaded();
        }
        else
        {
            Debug.LogWarning("PlayerDataManager: GameEventsManager.instance not ready; skipping PlayerDataLoaded event.");
        }
    }

    /// <summary>Fetches slot zero and applies it only when it is newer than local data.</summary>
    public void PullRemotePlayerData()
    {
        JadedBellesApiClient.Instance.GetSaves(
            JadedBellesApiClient.GameProductSlug,
            response =>
            {
                GameSaveData remote = FindSaveSlot(response.data, SaveSlot);
                if (remote == null)
                {
                    Debug.Log("No remote player save exists yet; uploading local progress.");
                    _ = UpdatePlayerData();
                    return;
                }

                PlayerData remoteData = DeserializePlayerData(remote.saveData);
                if (remoteData == null)
                {
                    Debug.LogWarning("The remote player save could not be read. Keeping local progress.");
                    return;
                }

                DateTime remoteUpdatedAt = ParseUpdatedAt(remote.updatedAt);
                if (remoteUpdatedAt > GetLocalUpdatedAt())
                {
                    ApplyRemoteData(remoteData, remote.revision, remote.updatedAt);
                    Debug.Log("Player data synced from the JadedBelles API.");
                }
                else
                {
                    RememberRevision(remote.revision);
                    Debug.Log("Local player data is newer than the remote save; uploading it.");
                    _ = UpdatePlayerData();
                }
            },
            error => Debug.LogWarning($"Could not fetch the remote save ({error}). Using local data."));
    }

    // ---------- Save ----------

    /// <summary>
    /// Persists current progress. Always saves locally; also pushes an opaque JSON blob to
    /// generic game-save slot zero when signed in. The task completes after the best-effort push.
    /// </summary>
    public Task UpdatePlayerData()
    {
        PlayerData playerData = PlayerHandler.instance.SendPlayerDataToCloud(PlayerHandler.instance.playerData);
        SaveLocal(playerData);

        if (!JadedBellesApiClient.Instance.HasSession)
            return Task.CompletedTask;

        var completion = new TaskCompletionSource<bool>();
        PushRemotePlayerData(playerData, GetLocalUpdatedAt(), GetLastKnownRevision(), completion, true);
        return completion.Task;
    }

    private void PushRemotePlayerData(
        PlayerData playerData,
        DateTime localUpdatedAt,
        int? baseRevision,
        TaskCompletionSource<bool> completion,
        bool allowConflictRetry)
    {
        string saveData = JsonUtility.ToJson(playerData);
        JadedBellesApiClient.Instance.PutSave(
            JadedBellesApiClient.GameProductSlug,
            SaveSlot,
            saveData,
            SaveSchemaVersion,
            null,
            baseRevision,
            response =>
            {
                if (response.data != null)
                {
                    RememberRevision(response.data.revision);
                    RememberLocalUpdatedAt(response.data.updatedAt);
                    PlayerPrefs.Save();
                }

                Debug.Log("Player data saved to the JadedBelles API.");
                completion.TrySetResult(true);
            },
            conflict => ResolveSaveConflict(playerData, localUpdatedAt, conflict, completion, allowConflictRetry),
            error =>
            {
                // Local save already succeeded, so a failed push should not break gameplay.
                Debug.LogWarning($"Could not push the save to the API ({error}).");
                completion.TrySetResult(false);
            });
    }

    private void ResolveSaveConflict(
        PlayerData localData,
        DateTime localUpdatedAt,
        ApiResponseGameSave conflict,
        TaskCompletionSource<bool> completion,
        bool allowConflictRetry)
    {
        GameSaveData serverSave = conflict.data;
        PlayerData remoteData = serverSave == null ? null : DeserializePlayerData(serverSave.saveData);
        if (serverSave == null || remoteData == null)
        {
            Debug.LogWarning("Save conflict response did not contain a readable server save; keeping local progress.");
            completion.TrySetResult(false);
            return;
        }

        DateTime remoteUpdatedAt = ParseUpdatedAt(serverSave.updatedAt);
        if (remoteUpdatedAt > localUpdatedAt)
        {
            ApplyRemoteData(remoteData, serverSave.revision, serverSave.updatedAt);
            Debug.LogWarning("Save conflict resolved with the newer server save; local progress was replaced with that newer save.");
            completion.TrySetResult(true);
            return;
        }

        RememberRevision(serverSave.revision);
        if (allowConflictRetry)
        {
            Debug.LogWarning("Save conflict resolved with the newer local save; retrying once against the server revision.");
            PushRemotePlayerData(localData, localUpdatedAt, serverSave.revision, completion, false);
            return;
        }

        Debug.LogWarning("Save conflict persisted after one retry; keeping the newer local save in PlayerPrefs.");
        completion.TrySetResult(false);
    }

    private static GameSaveData FindSaveSlot(GameSaveData[] saves, int slot)
    {
        if (saves == null) return null;

        for (int i = 0; i < saves.Length; i++)
        {
            if (saves[i] != null && saves[i].slot == slot)
                return saves[i];
        }

        return null;
    }

    private static PlayerData DeserializePlayerData(string saveData)
    {
        if (string.IsNullOrEmpty(saveData)) return null;

        try
        {
            PlayerData data = JsonUtility.FromJson<PlayerData>(saveData);
            return data != null && !string.IsNullOrEmpty(data.playerName) ? data : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void ApplyRemoteData(PlayerData data, int revision, string updatedAt)
    {
        PlayerHandler.instance.RecievePlayerDataFromCloud(data);
        SaveLocal(data, updatedAt);
        RememberRevision(revision);
        GameEventsManager.instance.gameEvents.PlayerDataLoaded();
    }

    private static void SaveLocal(PlayerData data, string updatedAt = null)
    {
        PlayerPrefs.SetString(LocalSaveKey, JsonUtility.ToJson(data));
        RememberLocalUpdatedAt(updatedAt ?? DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        PlayerPrefs.Save();
    }

    private static int? GetLastKnownRevision()
    {
        return PlayerPrefs.HasKey(RemoteRevisionKey) ? PlayerPrefs.GetInt(RemoteRevisionKey) : (int?)null;
    }

    private static void RememberRevision(int revision)
    {
        PlayerPrefs.SetInt(RemoteRevisionKey, revision);
        PlayerPrefs.Save();
    }

    private static DateTime GetLocalUpdatedAt()
    {
        return ParseUpdatedAt(PlayerPrefs.GetString(LocalUpdatedAtKey, string.Empty));
    }

    private static void RememberLocalUpdatedAt(string updatedAt)
    {
        PlayerPrefs.SetString(LocalUpdatedAtKey, updatedAt ?? string.Empty);
    }

    private static DateTime ParseUpdatedAt(string value)
    {
        DateTime parsed;
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out parsed)
            ? parsed.ToUniversalTime()
            : DateTime.MinValue;
    }
}
