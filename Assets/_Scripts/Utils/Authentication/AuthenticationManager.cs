using System;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using System.Threading.Tasks;

namespace StroTheGoat
{
    public class AuthenticationManager : MonoBehaviour
    {
        private IPlatformSignIn _platformSignIn;

        public void Init(IPlatformSignIn platformSignIn)
        {
            _platformSignIn = platformSignIn;
        }

        public async Task SignInAsync()
        {
            if (_platformSignIn == null)
            {
                Debug.LogError("PlatformSignIn implementation not set! Call Init() first.");
                return;
            }

            try
            {
                await UnityServices.InitializeAsync();
                await _platformSignIn.SignInAsync();
                await UnityAuthenticationHelper.SignInUnityAuthIfNeeded();
                Debug.Log("🎉 Full authentication complete!");
            }
            catch (Exception ex)
            {
                Debug.LogError("❌ Authentication failed: " + ex.Message);
            }
        }

        public static class UnityAuthenticationHelper
        {
            /// <summary>
            /// Ensures Unity Authentication Service sign-in.
            /// </summary>
            public static async Task SignInUnityAuthIfNeeded()
            {
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    Debug.Log("✅ Signed in to Unity Auth as: " + AuthenticationService.Instance.PlayerId);
                }
            }
        }
    }
}
