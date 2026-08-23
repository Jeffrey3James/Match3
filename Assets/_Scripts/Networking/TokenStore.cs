using System;
using System.Text;
using UnityEngine;

namespace JadedBelles.Networking
{
    /// <summary>
    /// Persists the API session between launches. Base64 is only light obfuscation and is not
    /// intended to secure a token against someone who can inspect PlayerPrefs.
    /// </summary>
    public static class TokenStore
    {
        private const string AccessTokenKey = "jb_access_token";
        private const string RefreshTokenKey = "jb_refresh_token";

        public static void SaveTokens(string access, string refresh)
        {
            if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh))
            {
                Clear();
                return;
            }

            PlayerPrefs.SetString(AccessTokenKey, Encode(access));
            PlayerPrefs.SetString(RefreshTokenKey, Encode(refresh));
            PlayerPrefs.Save();
        }

        public static string GetAccessToken()
        {
            return Decode(PlayerPrefs.GetString(AccessTokenKey, string.Empty));
        }

        public static string GetRefreshToken()
        {
            return Decode(PlayerPrefs.GetString(RefreshTokenKey, string.Empty));
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(AccessTokenKey);
            PlayerPrefs.DeleteKey(RefreshTokenKey);
            PlayerPrefs.Save();
        }

        public static bool HasSession()
        {
            return !string.IsNullOrEmpty(GetAccessToken()) && !string.IsNullOrEmpty(GetRefreshToken());
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string Decode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch (FormatException)
            {
                // Old/corrupt preferences should be treated as an expired session.
                return string.Empty;
            }
        }
    }
}
