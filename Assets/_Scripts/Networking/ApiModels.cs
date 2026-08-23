using System;

namespace JadedBelles.Networking
{
    // ---------- Requests ----------

    [Serializable]
    public class LoginRequest
    {
        public string email;
        public string password;
    }

    [Serializable]
    public class RegisterRequest
    {
        public string email;
        public string password;
        public string displayName;
    }

    [Serializable]
    public class RefreshRequest
    {
        public string refreshToken;
    }

    [Serializable]
    public class Match3SaveRequest
    {
        public string playerName;
        public int playerLevel;
        public int playerLives;
        public int playerCoins;
        public long playerLifeCountdown;
    }

    // ---------- Response payloads ----------

    [Serializable]
    public class AuthData
    {
        public string accessToken;
        public string refreshToken;
        public UserData user;
    }

    [Serializable]
    public class RefreshData
    {
        public string accessToken;
        public string refreshToken;
    }

    [Serializable]
    public class UserData
    {
        public int id;
        public string email;
        public string displayName;
    }

    [Serializable]
    public class Match3SaveData
    {
        public string playerName;
        public int playerLevel;
        public int playerLives;
        public int playerCoins;
        public long playerLifeCountdown;
        public string updatedAt;
    }

    // ---------- Response envelopes (ApiResponse<T> from the JadedBelles API) ----------

    [Serializable]
    public class ApiResponsePlain
    {
        public bool success;
        public string message;
    }

    [Serializable]
    public class ApiResponseAuth
    {
        public bool success;
        public string message;
        public AuthData data;
    }

    [Serializable]
    public class ApiResponseRefresh
    {
        public bool success;
        public string message;
        public RefreshData data;
    }

    [Serializable]
    public class ApiResponseUser
    {
        public bool success;
        public string message;
        public UserData data;
    }

    [Serializable]
    public class ApiResponseMatch3Save
    {
        public bool success;
        public string message;
        public Match3SaveData data;
    }
}
