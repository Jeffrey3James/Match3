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

    /// <summary>Generic save request without optimistic-concurrency metadata.</summary>
    [Serializable]
    public class PutGameSaveRequest
    {
        public string saveData;
        public int schemaVersion;
        public string label;
    }

    /// <summary>
    /// Separate type so JsonUtility omits baseRevision completely when no revision is known.
    /// JsonUtility cannot reliably represent nullable fields across all Unity targets.
    /// </summary>
    [Serializable]
    public class PutGameSaveWithRevisionRequest
    {
        public string saveData;
        public int schemaVersion;
        public string label;
        public int baseRevision;
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
        // JadedBelles user ids are Guids — keep the raw string form.
        public string id;
        public string email;
        public string displayName;
    }

    [Serializable]
    public class GameSaveData
    {
        public int slot;
        public string label;
        public int schemaVersion;
        public int revision;
        public string updatedAt;
        public string saveData;
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

    /// <summary>List response wrapper; JsonUtility requires arrays to be members of an object.</summary>
    [Serializable]
    public class ApiResponseGameSaves
    {
        public bool success;
        public string message;
        public GameSaveData[] data;
    }

    [Serializable]
    public class ApiResponseGameSave
    {
        public bool success;
        public string message;
        public GameSaveData data;
    }
}
