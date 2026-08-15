using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectKMP.Presentation
{
    /// <summary>
    /// Time.timeScale をまとめて預かる係。
    ///
    /// 通常攻撃のヒットストップと必殺技の溜めスローが別々に Time.timeScale を書くと、
    /// 片方が終わるときに 1.0 へ戻して相手の演出を消してしまう。書き込む場所をここ1か所に絞る。
    ///
    /// 掛かるのは自分のクライアントだけ。手応えを返すための演出なので通信はしない
    /// (他人の画面まで止めると、位置同期の補間がガタつく)。
    /// </summary>
    public static class HitStop
    {
        // ---- 内部状態 ------------------------------------

        /// <summary>続けて掛けたい遅さの申請。いちばん遅いものを採用する</summary>
        private static readonly Dictionary<object, float> SLOW_REQUESTS = new Dictionary<object, float>();

        private static float _stopRemainSec;
        private static float _stopTimeScale = 0.05f;
        private static float _recoverRemainSec;
        private static float _recoverSec;
        private static Runner _runner;

        // ---- 公開API -------------------------------------

        /// <summary>いま止めている最中か</summary>
        public static bool IsStopping => _stopRemainSec > 0.0f;

        /// <summary>
        /// 一瞬だけ時間を止めて、当たった手応えを出す。
        /// 止め終わったあとは一気に戻さず、戻る時間をかけることで衝撃を長く感じさせる。
        /// </summary>
        public static void Play(float durationSec, float timeScale = 0.05f, float recoverSec = 0.12f)
        {
            if (durationSec <= 0.0f) return;

            EnsureRunner();

            // 連続で当たったときは長いほうを残し、途中で戻ってしまわないようにする
            _stopRemainSec = Mathf.Max(_stopRemainSec, durationSec);
            _stopTimeScale = timeScale;
            _recoverSec = recoverSec;
            _recoverRemainSec = 0.0f;

            Apply();
        }

        /// <summary>溜め中のスローなど、続けて掛けたい遅さを申請する</summary>
        public static void SetSlow(object owner, float timeScale)
        {
            if (owner == null) return;

            EnsureRunner();
            SLOW_REQUESTS[owner] = Mathf.Clamp(timeScale, 0.01f, 1.0f);
            Apply();
        }

        /// <summary>申請を取り下げる。掛けた側は必ず呼ぶこと(呼び忘れると遅いままになる)</summary>
        public static void ClearSlow(object owner)
        {
            if (owner == null) return;
            if (!SLOW_REQUESTS.Remove(owner)) return;

            Apply();
        }

        /// <summary>すべて取り消して通常の速さへ戻す</summary>
        public static void ResetAll()
        {
            SLOW_REQUESTS.Clear();
            _stopRemainSec = 0.0f;
            _recoverRemainSec = 0.0f;
            Time.timeScale = 1.0f;
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>止めている間も進める必要があるので、実時間で数える</summary>
        private static void Tick()
        {
            if (_stopRemainSec > 0.0f)
            {
                _stopRemainSec -= Time.unscaledDeltaTime;
                if (_stopRemainSec <= 0.0f) _recoverRemainSec = _recoverSec;

                Apply();
                return;
            }

            if (_recoverRemainSec <= 0.0f) return;

            _recoverRemainSec -= Time.unscaledDeltaTime;
            Apply();
        }

        private static void Apply()
        {
            float baseScale = ResolveBaseScale();

            if (_stopRemainSec > 0.0f)
            {
                Time.timeScale = _stopTimeScale;
                return;
            }

            if (_recoverRemainSec > 0.0f && _recoverSec > 0.0f)
            {
                float t = 1.0f - Mathf.Clamp01(_recoverRemainSec / _recoverSec);
                Time.timeScale = Mathf.Lerp(_stopTimeScale, baseScale, t);
                return;
            }

            Time.timeScale = baseScale;
        }

        private static float ResolveBaseScale()
        {
            float scale = 1.0f;
            foreach (KeyValuePair<object, float> pair in SLOW_REQUESTS)
            {
                if (pair.Value < scale) scale = pair.Value;
            }

            return scale;
        }

        /// <summary>更新を回す入れ物を用意する。シーンには置かず、必要になったときに自分で作る</summary>
        private static void EnsureRunner()
        {
            if (_runner != null) return;

            var go = new GameObject("HitStop") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
        }

        private class Runner : MonoBehaviour
        {
            private void OnEnable()
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
            }

            private void OnDisable()
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }

            private void Update()
            {
                Tick();
            }

            /// <summary>掛けた本人が消えたまま遅さが残らないよう、シーンが変わったら白紙に戻す</summary>
            private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
            {
                ResetAll();
            }
        }
    }
}
