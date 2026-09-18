using UnityEngine;

namespace Match3Game.Juice {
    /// <summary>
    /// Perlin-noise camera shake on transform.localPosition. Attach to a Camera (usually via
    /// <see cref="MatchJuiceRuntime"/> the first time Shake is called).
    ///
    /// Overlapping calls do NOT stack additively — they merge to the max amplitude and max
    /// remaining duration, so a small aftershock during a big shake never dampens the big one.
    /// When time expires the camera returns exactly to its captured rest position.
    /// </summary>
    [DisallowMultipleComponent]
    public class ScreenShaker : MonoBehaviour {

        Vector3 _restPos;
        bool _hasRest;
        float _amp;      // current effective amplitude in world units
        float _remaining; // seconds left
        float _duration;  // original duration of the currently-active shake window
        float _seed;      // Perlin sample offset so successive shakes don't repeat trajectory

        public void Shake(float amp, float dur) {
            if (amp <= 0f || dur <= 0f) return;

            if (!_hasRest) {
                _restPos = transform.localPosition;
                _hasRest = true;
            }

            // Merge with any in-flight shake: pick the larger amplitude, extend remaining time
            // to max of current-remaining and new-dur so a bigger request never gets truncated.
            _amp = Mathf.Max(_amp, amp);
            _remaining = Mathf.Max(_remaining, dur);
            _duration  = Mathf.Max(_duration, dur);
            _seed = Random.value * 100f;
            enabled = true;
        }

        void OnDisable() {
            // If disabled mid-shake (scene unload etc.) snap camera back so we don't leave it
            // hanging at a weird offset when the next scene reuses this transform.
            if (_hasRest) transform.localPosition = _restPos;
            _amp = 0f;
            _remaining = 0f;
        }

        void LateUpdate() {
            if (_remaining <= 0f) {
                if (_hasRest) transform.localPosition = _restPos;
                enabled = false;
                return;
            }

            _remaining -= Time.unscaledDeltaTime; // survive hitstop (Time.timeScale = 0)

            // Linear decay: full amp at start of the window, zero at end.
            float t = _duration > 0f ? Mathf.Clamp01(_remaining / _duration) : 0f;
            float currentAmp = _amp * t;

            // Perlin gives smooth 1-D noise per axis; offsetting the sample per axis decorrelates
            // X and Y so the shake feels 2D, not a diagonal shimmy.
            float time = Time.unscaledTime * 25f + _seed;
            float ox = (Mathf.PerlinNoise(time, 0f) - 0.5f) * 2f;
            float oy = (Mathf.PerlinNoise(0f, time) - 0.5f) * 2f;

            transform.localPosition = _restPos + new Vector3(ox * currentAmp, oy * currentAmp, 0f);

            if (_remaining <= 0f) {
                transform.localPosition = _restPos;
                _amp = 0f;
                enabled = false;
            }
        }
    }
}
