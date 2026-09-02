using System;
using System.Collections.Generic;
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

        /// <summary>Editor-authored key→clip mapping used by <see cref="PlayNamed"/>. Callers
        /// reference sounds by string so systems (juice, HUD, tutorial) don't have to be recompiled
        /// when the sound designer swaps a clip.</summary>
        [Serializable]
        public struct NamedClip {
            public string key;
            public AudioClip clip;
        }

        [Header("Named Clips (key -> AudioClip)")]
        [SerializeField] List<NamedClip> namedClips = new List<NamedClip>();

        AudioSource audioSource;
        // Lazy lookup so we don't rebuild a Dictionary every call; nulled on OnValidate so
        // inspector edits at edit-time re-index next play.
        Dictionary<string, AudioClip> _namedLookup;

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
            _namedLookup = null; // force rebuild after inspector edits
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

        /// <summary>Play a named clip at the requested pitch. Silent no-op (with one warning)
        /// if the key isn't registered or the clip slot is empty — audio must never crash gameplay.</summary>
        public void PlayNamed(string key, float pitch = 1f)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (audioSource == null) return;

            var map = GetNamedLookup();
            if (!map.TryGetValue(key, out var clip) || clip == null) {
                Debug.LogWarning($"AudioManager.PlayNamed: no clip registered for key '{key}'.");
                return;
            }
            audioSource.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
            audioSource.PlayOneShot(clip);
            audioSource.pitch = 1f;
        }

        Dictionary<string, AudioClip> GetNamedLookup()
        {
            if (_namedLookup != null) return _namedLookup;
            _namedLookup = new Dictionary<string, AudioClip>(namedClips.Count);
            for (int i = 0; i < namedClips.Count; i++) {
                var nc = namedClips[i];
                if (string.IsNullOrEmpty(nc.key)) continue;
                // Last-write-wins is fine — designers dupe keys during iteration and expect the
                // later row to override the earlier one.
                _namedLookup[nc.key] = nc.clip;
            }
            return _namedLookup;
        }

        void PlayRandomPitch(AudioClip audioClip) 
        {
            // Audio must NEVER be able to crash gameplay: an exception thrown here
            // propagates into the board coroutines that call it (ExplodeGems etc.)
            // and kills the cascade mid-flight. Missing source/clip = silence, not a throw.
            if (audioSource == null || audioClip == null) return;
            audioSource.pitch = UnityEngine.Random.Range(0.9f, 1.1f);
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
