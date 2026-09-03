using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace Match3Game.Juice {
    /// <summary>
    /// Hidden MonoBehaviour singleton that owns AudioSource + ParticleSystem prefab references
    /// and hosts coroutines for <see cref="MatchJuice"/>. Created on demand via
    /// <see cref="EnsureExists"/>; survives scene loads via DontDestroyOnLoad.
    ///
    /// Serialized clip / prefab fields are intentionally null-in-code — the user drags assets
    /// in the Unity Inspector on the auto-spawned "[MatchJuiceRuntime]" GameObject (or on a
    /// pre-placed one in the scene, which will be preferred over an auto-spawned instance).
    /// </summary>
    [DisallowMultipleComponent]
    public class MatchJuiceRuntime : MonoBehaviour {

        // -------- Serialized wiring (set in Inspector) --------
        [Header("Audio — set in Inspector")]
        [Tooltip("Pool of pop clips; index 0 is base pitch, cascade pitch = 1 + 0.08*depth.")]
        [SerializeField] AudioClip[] matchPopClips;
        [SerializeField] AudioClip powerUpChimeClip;
        [SerializeField] AudioClip coinFlyClip;

        [Header("Particles — set in Inspector")]
        [Tooltip("Prefab containing a ParticleSystem — Burst 6-12 short-lived sparkles.")]
        [SerializeField] GameObject sparkleParticlePrefab;

        // -------- Runtime state --------
        static MatchJuiceRuntime _instance;
        AudioSource _audio;
        ScreenShaker _shaker;
        HitstopController _hitstop;

        // Warn-once flags per subsystem so a missing wiring does not spam the console.
        bool _warnedNoPopClips, _warnedNoPowerUpClip, _warnedNoCoinClip, _warnedNoSparklePrefab, _warnedNoCamera;

        /// <summary>Return the singleton, creating it on first access.</summary>
        public static MatchJuiceRuntime EnsureExists() {
            if (_instance != null) return _instance;

            // If someone already dropped one in a scene, take it.
            var existing = FindObjectOfType<MatchJuiceRuntime>();
            if (existing != null) {
                _instance = existing;
                _instance.Init();
                return _instance;
            }

            var go = new GameObject("[MatchJuiceRuntime]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MatchJuiceRuntime>();
            _instance.Init();
            return _instance;
        }

        void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(this);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            Init();
        }

        void Init() {
            if (_audio == null) {
                _audio = GetComponent<AudioSource>();
                if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.loop = false;
            }
            if (_hitstop == null) _hitstop = new HitstopController(this);
        }

        // -------- API surface called by MatchJuice --------

        internal void DoHitstop(int ms) {
            _hitstop.Begin(ms);
        }

        internal void DoShake(float amp, float dur) {
            var shaker = GetOrCreateShaker();
            if (shaker == null) return; // camera missing; already warned once
            shaker.Shake(amp, dur);
        }

        internal void DoPitchedPop(int cascadeDepth) {
            if (matchPopClips == null || matchPopClips.Length == 0) {
                WarnOnce(ref _warnedNoPopClips, "MatchJuiceRuntime.matchPopClips is empty; PitchedPop is a no-op.");
                return;
            }
            // Pick a clip — first slot is the "canonical" pop; extras (if wired) rotate for variety.
            var clip = matchPopClips[Mathf.Clamp(cascadeDepth, 0, matchPopClips.Length - 1)];
            if (clip == null) return;
            float pitch = 1f + 0.08f * Mathf.Max(0, cascadeDepth);
            PlayOneShotPitched(clip, pitch);
        }

        internal void DoBurst(Vector3 worldPos, Color color) {
            if (sparkleParticlePrefab == null) {
                WarnOnce(ref _warnedNoSparklePrefab, "MatchJuiceRuntime.sparkleParticlePrefab is null; BurstAt is a no-op.");
                return;
            }
            var go = Instantiate(sparkleParticlePrefab, worldPos, Quaternion.identity);
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) {
                // Not fatal — still schedule destroy.
                Destroy(go, 2f);
                return;
            }
            var main = ps.main;
            main.startColor = color;
            var em = ps.emission;
            // Burst count 6-12 as spec'd. Overwrites bursts on the prefab so scripters don't have to
            // hand-tune each spawn.
            short count = (short)Random.Range(6, 13); // 6..12 inclusive
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
            ps.Play();
            Destroy(go, main.duration + main.startLifetime.constantMax + 0.25f);
        }

        internal void DoPunchScale(Transform t, float amount, float dur) {
            if (t == null) return;
            // Kill any prior punch on this transform so overlapping calls don't compound to insanity.
            t.DOKill(complete: true);
            t.DOPunchScale(Vector3.one * amount, dur, vibrato: 6, elasticity: 0.6f);
        }

        internal void DoCoinFlyCue() {
            if (coinFlyClip == null) {
                WarnOnce(ref _warnedNoCoinClip, "MatchJuiceRuntime.coinFlyClip is null; CoinFlyCue is a no-op.");
                return;
            }
            PlayOneShotPitched(coinFlyClip, 1f);
        }

        internal void DoStarFlyCue() {
            if (coinFlyClip == null) {
                WarnOnce(ref _warnedNoCoinClip, "MatchJuiceRuntime.coinFlyClip is null; StarFlyCue is a no-op.");
                return;
            }
            // Same clip as coins, pitched up ~a fifth so stars read as "brighter".
            PlayOneShotPitched(coinFlyClip, 1.5f);
        }

        internal void DoPowerUpChime() {
            if (powerUpChimeClip == null) {
                WarnOnce(ref _warnedNoPowerUpClip, "MatchJuiceRuntime.powerUpChimeClip is null; PowerUpChime is a no-op.");
                return;
            }
            PlayOneShotPitched(powerUpChimeClip, 1f);
        }

        // -------- Internals --------

        void PlayOneShotPitched(AudioClip clip, float pitch) {
            if (_audio == null || clip == null) return;
            _audio.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
            _audio.PlayOneShot(clip);
            // No need to reset — PlayOneShot samples pitch at call-time only for the launched voice.
        }

        ScreenShaker GetOrCreateShaker() {
            if (_shaker != null) return _shaker;
            var cam = Camera.main;
            if (cam == null) {
                WarnOnce(ref _warnedNoCamera, "Camera.main is null; MatchJuice.Shake will no-op until a camera is tagged MainCamera.");
                return null;
            }
            _shaker = cam.GetComponent<ScreenShaker>();
            if (_shaker == null) _shaker = cam.gameObject.AddComponent<ScreenShaker>();
            return _shaker;
        }

        static void WarnOnce(ref bool flag, string msg) {
            if (flag) return;
            flag = true;
            Debug.LogWarning(msg);
        }
    }
}
