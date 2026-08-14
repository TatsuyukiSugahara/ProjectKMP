using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectKMP.UI
{
    /// <summary>
    /// BGMを鳴らす役。始まりは静かに立ち上げ、シーンを抜けるときは消しながら去る。
    /// いきなり鳴り始めたり途中でぶつ切りになったりすると、場面の切り替わりが乱暴に感じられる。
    ///
    /// 消すきっかけは SceneLoader が握っている。読み込みには時間がかかるので、
    /// 読み込みを始める時点で消し始めれば、切り替わるころには鳴り終わっている。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class BgmPlayer : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Range(0.0f, 1.0f), Tooltip("鳴らしきったときの音量")]
        private float _volume = 0.45f;

        [SerializeField, Min(0.0f), Tooltip("鳴り始めてから音量が上がりきるまでの秒数")]
        private float _fadeInSec = 1.5f;

        [SerializeField, Min(0.05f), Tooltip("消えるまでの秒数。シーンを抜けるときに使う")]
        private float _fadeOutSec = 0.8f;

        // ---- 内部状態 ------------------------------------

        private static BgmPlayer _current;

        private AudioSource _source;
        private CancellationTokenSource _cts;

        /// <summary>絞ったまま保つ残り時間</summary>
        private float _duckRemainSec;
        private bool _ducking;

        // ---- 公開API -------------------------------------

        /// <summary>いま鳴っているBGM。無ければ null</summary>
        public static BgmPlayer Current => _current;

        /// <summary>
        /// 一瞬だけBGMを絞る。大きい音の直前に呼ぶと、その音だけが前に出る。
        ///
        /// 音量を上げずに音が大きく聞こえるのは、周りが引くから。
        /// 打撃の手応えは音量よりも『間』で決まる。
        /// </summary>
        public static void Duck(float amount = 0.35f, float holdSec = 0.12f, float recoverSec = 0.35f)
        {
            if (_current == null) return;

            _current.DuckAsync(amount, holdSec, recoverSec).Forget();
        }

        /// <summary>鳴っているBGMを消していく。シーンを抜けるときに呼ぶ</summary>
        public static void FadeOutCurrent()
        {
            if (_current == null) return;

            _current.FadeOut();
        }

        /// <summary>音量を下げきって止める</summary>
        public void FadeOut()
        {
            Restart();
            FadeAsync(0.0f, _fadeOutSec, true, _cts.Token).Forget();
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _current = this;

            _source = GetComponent<AudioSource>();
            _source.loop = true;
            _source.playOnAwake = false;

            // BGMは距離で小さくならないよう2Dで鳴らす
            _source.spatialBlend = 0.0f;
            _source.volume = 0.0f;
        }

        private void Start()
        {
            if (_source.clip == null) return;

            _source.Play();

            Restart();
            FadeAsync(_volume, _fadeInSec, false, _cts.Token).Forget();
        }

        private void OnDestroy()
        {
            CancelFade();
            if (_current == this) _current = null;
        }

        // ---- 内部処理 ------------------------------------

        private void Restart()
        {
            CancelFade();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        }

        private void CancelFade()
        {
            if (_cts == null) return;

            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        /// <summary>
        /// 一瞬だけ絞ってから戻す。
        /// 落とすのは一気に、戻すのはゆっくり。逆にすると絞ったことが目立ってしまう。
        /// </summary>
        private async UniTaskVoid DuckAsync(float amount, float holdSec, float recoverSec)
        {
            if (_source == null) return;

            // 絞っている最中にもう一度呼ばれたら、より深いほうを採る
            float target = _volume * Mathf.Clamp01(1.0f - amount);
            if (_source.volume > target) _source.volume = target;

            _duckRemainSec = Mathf.Max(_duckRemainSec, holdSec);
            if (_ducking) return;

            _ducking = true;

            while (_duckRemainSec > 0.0f)
            {
                _duckRemainSec -= Time.unscaledDeltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }

            float start = _source.volume;
            float elapsed = 0.0f;

            while (elapsed < recoverSec)
            {
                elapsed += Time.unscaledDeltaTime;
                _source.volume = Mathf.Lerp(start, _volume, elapsed / recoverSec);
                await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            }

            _source.volume = _volume;
            _ducking = false;
        }

        /// <summary>
        /// 音量を目標へ動かす。ヒットストップなどで時間が止まっていても進めたいので実時間で数える。
        /// </summary>
        private async UniTaskVoid FadeAsync(float target, float durationSec, bool stopAtEnd, CancellationToken ct)
        {
            try
            {
                float from = _source.volume;
                float elapsed = 0.0f;

                while (elapsed < durationSec)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    elapsed += Time.unscaledDeltaTime;
                    _source.volume = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / durationSec));
                }

                _source.volume = target;
                if (stopAtEnd) _source.Stop();
            }
            catch (OperationCanceledException)
            {
                // シーンを抜けただけなので何もしない
            }
        }
    }
}
