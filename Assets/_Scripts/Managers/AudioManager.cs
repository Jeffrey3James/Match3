using UnityEngine;

namespace Match3Game {
    [RequireComponent(typeof(AudioSource))]
    public class AudioManager : MonoBehaviour {
        public static AudioManager instance { get; private set; }
        [SerializeField] AudioClip click;
        [SerializeField] AudioClip deselect;
        [SerializeField] AudioClip match;
        [SerializeField] AudioClip noMatch;
        [SerializeField] AudioClip woosh;
        [SerializeField] AudioClip pop;

        [Header("Power Up Sounds")]
        [SerializeField] AudioClip bomb;
        [SerializeField] AudioClip rocket;
        [SerializeField] AudioClip hammer;
        [SerializeField] AudioClip missile;
        [SerializeField] AudioClip nuke;

        AudioSource audioSource;

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                // OnValidate only runs in the editor — a built player never calls it,
                // so this assignment MUST happen here or audioSource is null on device.
                audioSource = GetComponent<AudioSource>();
            }
            else
            {
                Debug.LogWarning("Multiple AudioManager instances found. Destroying the new one.");
                Destroy(gameObject);
                return;
            }
        }

        void OnValidate() 
        {          
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
        }

        public void PlayClick() => PlaySafe(click);
        public void PlayDeselect() => PlaySafe(deselect);
        public void PlayMatch() => PlaySafe(match);
        public void PlayNoMatch() => PlaySafe(noMatch);
        public void PlayWoosh() => PlayRandomPitch(woosh);
        public void PlayPop() => PlayRandomPitch(pop);
        
        public void PlayBomb() => PlaySafe(bomb);
        public void PlayRocket() => PlaySafe(rocket);
        public void PlayHammer() => PlaySafe(hammer);
        public void PlayMissile() => PlaySafe(missile);
        public void PlayNuke() => PlaySafe(nuke);

        void PlayRandomPitch(AudioClip audioClip) 
        {
            // Audio must NEVER be able to crash gameplay: an exception thrown here
            // propagates into the board coroutines that call it (ExplodeGems etc.)
            // and kills the cascade mid-flight. Missing source/clip = silence, not a throw.
            if (audioSource == null || audioClip == null) return;
            audioSource.pitch = Random.Range(0.9f, 1.1f);
            audioSource.PlayOneShot(audioClip);
            audioSource.pitch = 1f;
        }

        void PlaySafe(AudioClip audioClip)
        {
            if (audioSource == null || audioClip == null) return;
            audioSource.PlayOneShot(audioClip);
        }

        public void PlayAudio(AudioClip audioClip) => PlaySafe(audioClip);
    }
}