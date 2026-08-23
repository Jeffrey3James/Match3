using Newtonsoft.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using UnityEngine;
using StroTheGoat;

public class CloudSaveManager : MonoBehaviour
{
    public static CloudSaveManager instance { get; set; }

    private async void OnEnable()
    {
        if (instance == null) instance = this;
        else Destroy(gameObject);
        DontDestroyOnLoad(gameObject);

        await UnityServicesInitializer.InitializeAndSignInAsync();

        Debug.Log("Signed in as: " + AuthenticationService.Instance.PlayerId);

        while (string.IsNullOrEmpty(AuthenticationService.Instance.AccessToken))
        {
            Debug.Log("Waiting for access token...");
            await Task.Delay(100);
        }

        if (AuthenticationService.Instance.IsAuthorized)
        {
            Debug.Log("Access token is ready, safe to call Cloud Save.");
            if (await HasPlayerDataAsync())
            {
                LoadPlayerData();
            }
            else
            {
                Debug.Log("No player data found, initializing new player data.");
                string playerId = AuthenticationService.Instance.PlayerId;
                await CreatePlayerData();
            }
        }
        else
        {
            Debug.LogError("Authentication not authorized, cannot call Cloud Save.");
        }
    }

    #region Wrapper Methods
    // Save data to the cloud
    public async Task SaveDataAsync(string key, object value)
    {
        var data = new Dictionary<string, object> { { key, value } };
        await CloudSaveService.Instance.Data.Player.SaveAsync(data);
        Debug.Log($"Saved {key}: {value}");
    }

    // Load data from the cloud
    public async Task<T> LoadDataAsync<T>(string key)
    {
        var data = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { key });
        if (data.TryGetValue(key, out var value))
        {
            return JsonUtility.FromJson<T>(value.Value.ToString());
        }
        return default;
    }

    // Example for primitives (like int or string)
    public async Task<string> LoadStringAsync(string key)
    {
        var data = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { key });
        if (data.TryGetValue(key, out var value))
        {
            return value.Value.ToString();
        }
        return null;
    }

    #endregion

    public async Task CreatePlayerData()
    {
        string playerName = AuthenticationService.Instance.PlayerId;
      var playerData = new Dictionary<string, object>
        {
            { "playerName", playerName},
            { "playerLevel", 0},
            { "playerLives", 5 },
            { "playerCoins", 100 },
            { "playerLifeCountdown", 0 }
        };

        PlayerData playerDataObject = new PlayerData
        {
            playerName = playerName,
            playerLevel = 0,
            playerLives = 5,
            playerCoins = 100,
            playerLifeCountdown = 0
        };

        PlayerHandler.instance.RecievePlayerDataFromCloud(playerDataObject);

        await CloudSaveService.Instance.Data.Player.SaveAsync(playerData);
        Debug.Log($"Saved data {string.Join(',', playerData)}");
    }

    public async Task UpdatePlayerData()
    {
        PlayerData playerData = PlayerHandler.instance.SendPlayerDataToCloud(PlayerHandler.instance.playerData);
        var data = new Dictionary<string, object>
        {
            { "playerName", playerData.playerName },
            { "playerLevel", playerData.playerLevel },
            { "playerLives", playerData.playerLives },
            { "playerCoins", playerData.playerCoins },
            { "playerLifeCountdown", playerData.playerLifeCountdown}
        };
        await CloudSaveService.Instance.Data.Player.SaveAsync(data);
        Debug.Log($"Updated player data: {JsonConvert.SerializeObject(data)}");
    }

    public async void LoadPlayerData()
    {
        var playerHandlerData = PlayerHandler.instance.playerData;
        var playerData = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> {
            "playerName", 
            "playerLevel", 
            "playerLives", 
            "playerCoins", 
            "playerLifeCountdown"
        });

        if (playerData.TryGetValue("playerName", out var firstKey))
        {
            playerHandlerData.playerName = firstKey.Value.GetAs<string>();
            Debug.Log($"playerName value: {firstKey.Value.GetAs<string>()}");
        }

        if (playerData.TryGetValue("playerLevel", out var secondKey))
        {
            playerHandlerData.playerLevel = secondKey.Value.GetAs<int>();
            Debug.Log($"playerLevel value: {secondKey.Value.GetAs<int>()}");
        }

        if (playerData.TryGetValue("playerLives", out var thirdKey))
        {
            playerHandlerData.playerLives = thirdKey.Value.GetAs<int>();
            Debug.Log($"playerLives value: {thirdKey.Value.GetAs<int>()}");
        }

        if (playerData.TryGetValue("playerCoins", out var fourthKey))
        {
            playerHandlerData.playerCoins = fourthKey.Value.GetAs<int>();
            Debug.Log($"playerCoins value: {fourthKey.Value.GetAs<int>()}");
        }

        if (playerData.TryGetValue("playerLifeCountdown", out var fifthKey))
        {
            playerHandlerData.playerLifeCountdown = fifthKey.Value.GetAs<long>();
            Debug.Log($"playerLifeCountdown value: {fifthKey.Value.GetAs<long>()}");
        }

        GameEventsManager.instance.gameEvents.PlayerDataLoaded();
    }


    public async Task<bool> HasPlayerDataAsync()
    {
        var keys = new HashSet<string> { "playerName", "playerLevel", "playerLives", "playerCoins" };
        var result = await CloudSaveService.Instance.Data.Player.LoadAsync(keys);

        // You can check if ANY of them exist
        foreach (var key in keys)
        {
            if (result.ContainsKey(key))
            {
                Debug.Log("✅ Player data exists.");
                return true;
            }
        }

        Debug.Log("⚠️ No player data found.");
        return false;
    }
}

