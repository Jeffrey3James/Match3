using System.Threading.Tasks;

namespace StroTheGoat
{

    public interface IPlatformSignIn
    {
        /// <summary>
        /// Performs platform-specific sign-in (Google, PlayStation, etc.)
        /// </summary>
        Task SignInAsync();
    }
}

