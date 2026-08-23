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

        public void PlayClick() => audioSource.PlayOneShot(click);
        public void PlayDeselect() => audioSource.PlayOneShot(deselect);
        public void PlayMatch() => audioSource.PlayOneShot(match);
        public void PlayNoMatch() => audioSource.PlayOneShot(noMatch);
        public void PlayWoosh() => PlayRandomPitch(woosh);
        public void PlayPop() => PlayRandomPitch(pop);
        
        public void PlayBomb() => audioSource.PlayOneShot(bomb);
        public void PlayRocket() => audioSource.PlayOneShot(rocket);
        public void PlayHammer() => audioSource.PlayOneShot(hammer);
        public void PlayMissile() => audioSource.PlayOneShot(missile);
        public void PlayNuke() => audioSource.PlayOneShot(nuke);

        void PlayRandomPitch(AudioClip audioClip) 
        {
            audioSource.pitch = Random.Range(0.9f, 1.1f);
            audioSource.PlayOneShot(audioClip);
            audioSource.pitch = 1f;
        }

        public void PlayAudio(AudioClip audioClip)
        {
            audioSource.PlayOneShot(audioClip);
        }
    }
}