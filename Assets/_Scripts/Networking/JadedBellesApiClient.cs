using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace JadedBelles.Networking
{
    /// <summary>
    /// Coroutine-based client for the JadedBelles Central API.
    /// Self-bootstraps: access it anywhere through <see cref="Instance"/>.
    /// </summary>
    public sealed class JadedBellesApiClient : MonoBehaviour
    {
        public const string BaseUrl = "https://api.jadedbelles.com";

        private const int RequestTimeoutSeconds = 30;

        private static JadedBellesApiClient instance;

        public static JadedBellesApiClient Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("JadedBellesApiClient");
                    instance = go.AddComponent<JadedBellesApiClient>();
                }
                return instance;
            }
        }

        /// <summary>True when a stored session exists. It may still be expired; RefreshToken to confirm.</summary>
        public bool HasSession => TokenStore.HasSession();

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // ---------- Auth ----------

        public void Login(string email, string password, Action<ApiResponseAuth> onSuccess, Action<string> onError)
        {
            LoginRequest body = new LoginRequest { email = email, password = password };
            StartCoroutine(SendRequest<ApiResponseAuth>(
                UnityWebRequest.kHttpVerbPOST,
                "/api/v1/auth/login",
                JsonUtility.ToJson(body),
                false,
                false,
                response =>
                {
                    if (response.data != null)
                        TokenStore.SaveTokens(response.data.accessToken, response.data.refreshToken);
                    onSuccess?.Invoke(response);
                },
                onError));
        }

        public void Register(string email, string password, string displayName, Action<ApiResponseAuth> onSuccess, Action<string> onError)
        {
            RegisterRequest body = new RegisterRequest
            {
                email = email,
                password = password,
                displayName = displayName
            };

            StartCoroutine(SendRequest<ApiResponseAuth>(
                UnityWebRequest.kHttpVerbPOST,
                "/api/v1/auth/register",
                JsonUtility.ToJson(body),
                false,
                false,
                response =>
                {
                    if (response.data != null)
                        TokenStore.SaveTokens(response.data.accessToken, response.data.refreshToken);
                    onSuccess?.Invoke(response);
                },
                onError));
        }

        public void RefreshToken(Action<ApiResponseRefresh> onSuccess, Action<string> onError)
        {
            StartCoroutine(RefreshAccessTokenRoutine(onSuccess, onError));
        }

        public void Logout(Action<ApiResponsePlain> onSuccess, Action<string> onError)
        {
            StartCoroutine(SendRequest<ApiResponsePlain>(
                UnityWebRequest.kHttpVerbPOST,
                "/api/v1/auth/logout",
                null,
                true,
                true,
                response =>
                {
                    TokenStore.Clear();
                    onSuccess?.Invoke(response);
                },
                onError));
        }

        public void GetCurrentUser(Action<ApiResponseUser> onSuccess, Action<string> onError)
        {
            StartCoroutine(SendRequest<ApiResponseUser>(
                UnityWebRequest.kHttpVerbGET,
                "/api/v1/auth/me",
                null,
                true,
                true,
                onSuccess,
                onError));
        }

        // ---------- Match3 ----------

        /// <summary>Fetch the raw level catalog JSON (anonymous endpoint, no envelope).</summary>
        public void GetLevelCatalog(Action<string> onSuccess, Action<string> onError)
        {
            StartCoroutine(GetRawRoutine("/api/v1/match3/levels", onSuccess, onError));
        }

        /// <summary>Fetch (or create on first call) the signed-in player's match3 save.</summary>
        public void GetMatch3Save(Action<ApiResponseMatch3Save> onSuccess, Action<string> onError)
        {
            StartCoroutine(SendRequest<ApiResponseMatch3Save>(
                UnityWebRequest.kHttpVerbGET,
                "/api/v1/match3/me/save",
                null,
                true,
                true,
                onSuccess,
                onError));
        }

        /// <summary>Persist the signed-in player's match3 save.</summary>
        public void PutMatch3Save(Match3SaveRequest body, Action<ApiResponseMatch3Save> onSuccess, Action<string> onError)
        {
            StartCoroutine(SendRequest<ApiResponseMatch3Save>(
                UnityWebRequest.kHttpVerbPUT,
                "/api/v1/match3/me/save",
                JsonUtility.ToJson(body),
                true,
                true,
                onSuccess,
                onError));
        }

        // ---------- Plumbing ----------

        private IEnumerator GetRawRoutine(string endpoint, Action<string> onSuccess, Action<string> onError)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(BaseUrl + endpoint))
            {
                request.timeout = RequestTimeoutSeconds;
                request.SetRequestHeader("Accept", "application/json");
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(ExtractErrorMessage(
                        request.downloadHandler != null ? request.downloadHandler.text : string.Empty,
                        request.responseCode,
                        request.error));
                    yield break;
                }

                onSuccess?.Invoke(request.downloadHandler.text);
            }
        }

        private IEnumerator RefreshAccessTokenRoutine(Action<ApiResponseRefresh> onSuccess, Action<string> onError)
        {
            string refreshToken = TokenStore.GetRefreshToken();
            if (string.IsNullOrEmpty(refreshToken))
            {
                onError?.Invoke("Your session has expired. Please sign in again.");
                yield break;
            }

            RefreshRequest body = new RefreshRequest { refreshToken = refreshToken };
            yield return SendRequest<ApiResponseRefresh>(
                UnityWebRequest.kHttpVerbPOST,
                "/api/v1/auth/refresh",
                JsonUtility.ToJson(body),
                false,
                false,
                response =>
                {
                    if (response.data == null || string.IsNullOrEmpty(response.data.accessToken))
                    {
                        onError?.Invoke("The server returned an incomplete session refresh.");
                        return;
                    }

                    string nextRefreshToken = string.IsNullOrEmpty(response.data.refreshToken)
                        ? refreshToken
                        : response.data.refreshToken;
                    TokenStore.SaveTokens(response.data.accessToken, nextRefreshToken);
                    onSuccess?.Invoke(response);
                },
                onError);
        }

        private IEnumerator SendRequest<T>(
            string method,
            string endpoint,
            string jsonBody,
            bool requiresAuthentication,
            bool retryUnauthorized,
            Action<T> onSuccess,
            Action<string> onError)
        {
            if (requiresAuthentication && string.IsNullOrEmpty(TokenStore.GetAccessToken()))
            {
                onError?.Invoke("No active session is available. Please sign in again.");
                yield break;
            }

            long responseCode;
            string responseText;
            string transportError;
            bool completedSuccessfully;

            using (UnityWebRequest request = CreateRequest(method, endpoint, jsonBody, requiresAuthentication))
            {
                yield return request.SendWebRequest();
                responseCode = request.responseCode;
                responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                transportError = request.error;
                completedSuccessfully = request.result == UnityWebRequest.Result.Success;
            }

            if (requiresAuthentication && responseCode == 401 && retryUnauthorized)
            {
                bool didRefresh = false;
                string refreshError = null;

                yield return RefreshAccessTokenRoutine(
                    _ => didRefresh = true,
                    error => refreshError = error);

                if (didRefresh)
                {
                    // The retry flag is false, which guarantees every original request retries at most once.
                    StartCoroutine(SendRequest<T>(
                        method,
                        endpoint,
                        jsonBody,
                        true,
                        false,
                        onSuccess,
                        onError));
                    yield break;
                }

                onError?.Invoke(string.IsNullOrEmpty(refreshError)
                    ? "Your session has expired. Please sign in again."
                    : refreshError);
                yield break;
            }

            if (!completedSuccessfully)
            {
                onError?.Invoke(ExtractErrorMessage(responseText, responseCode, transportError));
                yield break;
            }

            T response = JsonUtility.FromJson<T>(responseText);
            if (response == null)
            {
                onError?.Invoke("The server returned an unreadable response.");
                yield break;
            }

            if (!WasApiSuccessful(response))
            {
                onError?.Invoke(ExtractApiMessage(response) ?? "The request was not accepted by the server.");
                yield break;
            }

            onSuccess?.Invoke(response);
        }

        private static UnityWebRequest CreateRequest(string method, string endpoint, string jsonBody, bool requiresAuthentication)
        {
            UnityWebRequest request = new UnityWebRequest(BaseUrl + endpoint, method);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = RequestTimeoutSeconds;
            request.SetRequestHeader("Accept", "application/json");

            if (jsonBody != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            if (requiresAuthentication)
                request.SetRequestHeader("Authorization", "Bearer " + TokenStore.GetAccessToken());

            return request;
        }

        private static bool WasApiSuccessful<T>(T response)
        {
            if (response is ApiResponsePlain plain) return plain.success;
            if (response is ApiResponseAuth auth) return auth.success;
            if (response is ApiResponseRefresh refresh) return refresh.success;
            if (response is ApiResponseUser user) return user.success;
            if (response is ApiResponseMatch3Save save) return save.success;
            return false;
        }

        private static string ExtractApiMessage<T>(T response)
        {
            if (response is ApiResponsePlain plain) return plain.message;
            if (response is ApiResponseAuth auth) return auth.message;
            if (response is ApiResponseRefresh refresh) return refresh.message;
            if (response is ApiResponseUser user) return user.message;
            if (response is ApiResponseMatch3Save save) return save.message;
            return null;
        }

        private static string ExtractErrorMessage(string responseText, long responseCode, string transportError)
        {
            if (!string.IsNullOrEmpty(responseText))
            {
                ApiResponsePlain apiError = null;
                try { apiError = JsonUtility.FromJson<ApiResponsePlain>(responseText); }
                catch (Exception) { /* Non-JSON error bodies fall through to the transport error. */ }

                if (apiError != null && !string.IsNullOrEmpty(apiError.message))
                    return apiError.message;
            }

            if (!string.IsNullOrEmpty(transportError))
                return transportError;

            return responseCode > 0
                ? "The server returned HTTP " + responseCode + "."
                : "The request could not reach the server.";
        }
    }
}
