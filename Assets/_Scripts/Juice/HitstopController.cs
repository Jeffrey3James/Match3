using System.Collections;
using UnityEngine;

namespace Match3Game.Juice {
    /// <summary>
    /// Internal helper that runs the "freeze Time.timeScale to 0 then restore" coroutine on
    /// behalf of <see cref="MatchJuiceRuntime"/>. Kept as a plain object (not a MonoBehaviour)
    /// so we can own the coroutine handle and cancel/extend cleanly without adding another
    /// GameObject to the scene tree.
    ///
    /// Overlapping calls: the longer window wins. A short 40ms request that arrives while a
    /// 200ms freeze is already in flight is ignored (returning early would risk restoring
    /// timeScale to 1 too soon). If the newer request is LONGER we extend the existing freeze.
    /// </summary>
    internal class HitstopController {
        readonly MonoBehaviour _host;
        Coroutine _co;
        float _priorScale = 1f;
        float _resumeAtRealtime; // Time.realtimeSinceStartup at which timeScale must be restored

        public HitstopController(MonoBehaviour host) {
            _host = host;
        }

        public void Begin(int ms) {
            if (_host == null || ms <= 0) return;
            float dur = ms / 1000f;
            float desiredResume = Time.realtimeSinceStartup + dur;

            if (_co != null) {
                // Already frozen — just push the resume-time out if the new window is longer.
                if (desiredResume > _resumeAtRealtime) _resumeAtRealtime = desiredResume;
                return;
            }

            _priorScale = Time.timeScale;
            _resumeAtRealtime = desiredResume;
            _co = _host.StartCoroutine(Run());
        }

        IEnumerator Run() {
            Time.timeScale = 0f;
            // Loop so mid-freeze extensions work — always wait until the current resume target.
            while (Time.realtimeSinceStartup < _resumeAtRealtime) {
                yield return null; // WaitForSecondsRealtime would ignore extensions
            }
            Time.timeScale = _priorScale;
            _co = null;
        }
    }
}
