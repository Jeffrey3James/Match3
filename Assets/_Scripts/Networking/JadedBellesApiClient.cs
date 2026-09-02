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
        // The App Service default hostname ships with a valid Microsoft-managed
        // certificate on every platform (browser, Android, iOS, editor) — the
        // same base URL the jadedbelles.com website itself calls. The custom
        // domain api.jadedbelles.com currently serves a *.azurewebsites.net
        // cert and fails TLS validation, so don't switch back until an App
        // Service Managed Certificate is bound to it.
        public const string BaseUrl = "https://jadedbelles-api-ewatbnarexfqane6.centralus-01.azurewebsites.net";

        // Seeded match-3 product slug. Change this one value if Xandria Gem Jam uses a different production product.
        public const string GameProductSlug = "xandria-gem-jam";

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

        /// <summary>
        /// Logs in with a username OR an email address. The <paramref name="identifier"/>
        /// is sent as-is; the server decides how to look up the account. The legacy
        /// <c>email</c> field is also populated when the identifier looks like an
        /// email so older API deployments that only read <c>email</c> keep working.
        /// </summary>
        public void Login(string identifier, string password, Action<ApiResponseAuth> onSuccess, Action<string> onError)
        {
            string trimmed = identifier != null ? identifier.Trim() : string.Empty;
            LoginRequest body = new LoginRequest
            {
                identifier = trimmed,
                // Only send `email` when it actually looks like one, so a username
                // isn't accidentally interpreted as an email by legacy backends.
                email = trimmed.Contains("@") ? trimmed : null,
                password = password
            };
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

        /// <summary>
        /// Registers with either a username or an email. If <paramref name="identifier"/>
        /// contains '@' it's treated as the email; otherwise it's treated as a
        /// username-only signup and the account is created without an email.
        /// </summary>
        public void Register(string identifier, string password, string displayName, Action<ApiResponseAuth> onSuccess, Action<string> onError)
        {
            string trimmed = identifier != null ? identifier.Trim() : string.Empty;
            bool looksLikeEmail = trimmed.Contains("@");
            RegisterRequest body = new RegisterRequest
            {
                username = looksLikeEmail ? null : trimmed,
                email = looksLikeEmail ? trimmed : null,
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

        /// <summary>
        /// Kicks off the Google Play-compliant self-serve account deletion
        /// flow for the currently signed-in user's email (or any email the
        /// caller supplies). The API responds with the same generic
        /// "if that email exists, we've sent a link" message regardless of
        /// whether the address matched a real account, so the client
        /// treats every non-error response as success and tells the user
        /// to check their inbox. Completing the deletion requires the user
        /// to click the link in the email — nothing in the app can bypass
        /// that email round-trip. Unauthenticated on purpose so it works
        /// even if the app's saved token has expired.
        /// </summary>
        public void RequestAccountDeletion(string email, Action<ApiResponsePlain> onSuccess, Action<string> onError)
        {
            AccountDeletionRequestBody body = new AccountDeletionRequestBody { email = email };
            StartCoroutine(SendRequest<ApiResponsePlain>(
                UnityWebRequest.kHttpVerbPOST,
                "/api/accountDeletion/request",
                JsonUtility.ToJson(body),
                false,
                false,
                onSuccess,
                onError));
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

        // ---------- Generic game saves ----------

        public void GetSaves(string slug, Action<ApiResponseGameSaves> onSuccess, Action<string> onError)
        {
            StartCoroutine(SendRequest<ApiResponseGameSaves>(
                UnityWebRequest.kHttpVerbGET,
                "/api/v1/games/" + slug + "/saves",
                null,
                true,
                true,
                onSuccess,
                onError));
        }

        public void GetSave(string slug, int slot, Action<ApiResponseGameSave> onSuccess, Action<string> onError)
        {
            StartCoroutine(SendRequest<ApiResponseGameSave>(
                UnityWebRequest.kHttpVerbGET,
                "/api/v1/games/" + slug + "/saves/" + slot,
                null,
                true,
                true,
                onSuccess,
                onError));
        }

        /// <summary>
        /// Upserts a generic game save. A 409 conflict invokes onConflict with the server's
        /// current slot instead of being reported as a generic transport failure.
        /// </summary>
        public void PutSave(
            string slug,
            int slot,
            string saveData,
            int schemaVersion,
            string label,
            int? baseRevision,
            Action<ApiResponseGameSave> onSuccess,
            Action<ApiResponseGameSave> onConflict,
            Action<string> onError)
        {
            string jsonBody = baseRevision.HasValue
                ? JsonUtility.ToJson(new PutGameSaveWithRevisionRequest
                {
                    saveData = saveData,
                    schemaVersion = schemaVersion,
                    label = label,
                    baseRevision = baseRevision.Value
                })
                : JsonUtility.ToJson(new PutGameSaveRequest
                {
                    saveData = saveData,
                    schemaVersion = schemaVersion,
                    label = label
                });

            StartCoroutine(SendRequest<ApiResponseGameSave>(
                UnityWebRequest.kHttpVerbPUT,
                "/api/v1/games/" + slug + "/saves/" + slot,
                jsonBody,
                true,
                true,
                onSuccess,
                onError,
                onConflict));
        }

        public void DeleteSave(string slug, int slot, Action<ApiResponsePlain> onSuccess, Action<string> onError)
        {
            StartCoroutine(SendRequest<ApiResponsePlain>(
                UnityWebRequest.kHttpVerbDELETE,
                "/api/v1/games/" + slug + "/saves/" + slot,
                null,
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
                ApplyEditorCertBypass(request);
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
            Action<string> onError,
            Action<T> onConflict = null)
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
                        onError,
                        onConflict));
                    yield break;
                }

                onError?.Invoke(string.IsNullOrEmpty(refreshError)
                    ? "Your session has expired. Please sign in again."
                    : refreshError);
                yield break;
            }

            if (responseCode == 409 && onConflict != null)
            {
                T conflict = JsonUtility.FromJson<T>(responseText);
                if (conflict == null)
                {
                    onError?.Invoke("The server returned an unreadable save conflict.");
                    yield break;
                }

                onConflict?.Invoke(conflict);
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

            ApplyEditorCertBypass(request);
            return request;
        }

        // Editor-only escape hatch for local development when the api.jadedbelles.com
        // certificate is misconfigured (UnityTls error 7 / cert CN mismatch). NEVER
        // active in a shipped player build.
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private static void ApplyEditorCertBypass(UnityWebRequest request)
        {
#if UNITY_EDITOR
          request.certificateHandler = AcceptAllCertificatesHandler.Shared;
            request.disposeCertificateHandlerOnDispose = false;
      #endif
        }

        private sealed class AcceptAllCertificatesHandler : CertificateHandler
        {
            public static readonly AcceptAllCertificatesHandler Shared = new AcceptAllCertificatesHandler();
            protected override bool ValidateCertificate(byte[] certificateData) => true;
        }

        private static bool WasApiSuccessful<T>(T response)
        {
            if (response is ApiResponsePlain plain) return plain.success;
            if (response is ApiResponseAuth auth) return auth.success;
            if (response is ApiResponseRefresh refresh) return refresh.success;
            if (response is ApiResponseUser user) return user.success;
            if (response is ApiResponseGameSaves saves) return saves.success;
            if (response is ApiResponseGameSave save) return save.success;
            return false;
        }

        private static string ExtractApiMessage<T>(T response)
        {
            if (response is ApiResponsePlain plain) return plain.message;
            if (response is ApiResponseAuth auth) return auth.message;
            if (response is ApiResponseRefresh refresh) return refresh.message;
            if (response is ApiResponseUser user) return user.message;
            if (response is ApiResponseGameSaves saves) return saves.message;
            if (response is ApiResponseGameSave save) return save.message;
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
