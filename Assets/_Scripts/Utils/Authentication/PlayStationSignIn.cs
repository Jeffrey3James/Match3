using UnityEngine;
using System.Threading.Tasks;

namespace StroTheGoat
{
        public class PlayStationSignIn : IPlatformSignIn
        {
            public async Task SignInAsync()
            {
                // Your PlayStation Network sign-in logic here
                Debug.Log("✅ Signed in with PlayStation Network");
            }
        }

}
