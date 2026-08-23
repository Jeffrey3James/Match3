using UnityEngine;
using System.Threading.Tasks;

namespace StroTheGoat
{
        public class GoogleSignIn : IPlatformSignIn
        {
            public async Task SignInAsync()
            {
                // Your Google Play sign-in logic here
                // e.g. await GoogleSignInPlugin.SignInAsync();
                Debug.Log("✅ Signed in with Google");
            }
        }
}
