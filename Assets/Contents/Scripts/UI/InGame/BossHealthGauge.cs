using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI.InGame
{
    /// <summary>
    /// 画面上部に出すボスのHPゲージ。表示だけを受け持ち、HPの持ち主から
    /// SetHealth() を呼んでもらって更新する。まだボス側にHPが無いので、
    /// つなぎ込みは呼び出し1行で済むようにしてある。
    /// Image の Filled は切り口が角になるため、中身の幅そのものを伸ばして端を丸いまま保つ。
    /// </summary>
    public class BossHealthGauge : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [Header("表示")]
        [SerializeField, Tooltip("表示・非表示に使う CanvasGroup")]
        private CanvasGroup _group;

        [SerializeField, Tooltip("ボスの名前を出す RawImage")]
        private RawImage _nameImage;

        [Header("ゲージ")]
        [SerializeField, Tooltip("ゲージの溝。伸びる範囲の基準になる")]
        private RectTransform _trackRect;

        [SerializeField, Tooltip("伸び縮みする中身")]
        private RectTransform _fillRect;

        [SerializeField, Tooltip("溝と中身のすきま(ピクセル)")]
        private float _padding = 4.0f;

        [SerializeField, Min(0.0f), Tooltip("減ったぶんが追いつくまでの秒数。0なら即座に反映する")]
        private float _animationSeconds = 0.25f;

        [Header("確認用")]
        [Header("ゲージの本数")]
        [SerializeField, Min(1), Tooltip("HPを何本に分けるか。1本削り切ると、次の本が右から現れる")]
        private int _segmentCount = 4;

        [SerializeField, Tooltip("本ごとの色。削る順に並べる(先頭が最初に削る本、末尾が最後の1本)")]
        private Color[] _segmentColors =
        {
            new Color(0.45f, 0.82f, 0.32f, 1.0f),
            new Color(1.00f, 0.84f, 0.22f, 1.0f),
            new Color(1.00f, 0.55f, 0.14f, 1.0f),
            new Color(1.00f, 0.28f, 0.24f, 1.0f),
        };

        [SerializeField, Tooltip("ゲージの溝。次の本の色を薄く出すのに使う。未設定なら色を変えない")]
        private Graphic _trackGraphic;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("溝の薄さ。小さいほど暗く沈む")]
        private float _trackBrightness = 0.45f;

        [SerializeField, Tooltip("最後の1本を削っているときの溝の色。この先が無いことを見せる")]
        private Color _trackLastColor = new Color(0.16f, 0.06f, 0.14f, 1.0f);

        [SerializeField, Tooltip("残りの本数を出す印。左から順に、残っているぶんだけ灯る")]
        private Graphic[] _segmentPips;

        [SerializeField, Tooltip("残っている印の色")]
        private Color _pipOnColor = new Color(1.0f, 0.85f, 0.35f, 1.0f);

        [SerializeField, Tooltip("削り終わった印の色")]
        private Color _pipOffColor = new Color(0.25f, 0.12f, 0.22f, 0.7f);

        [Header("1本削り切ったとき")]
        [SerializeField, Tooltip("ゲージを1本削り切った瞬間に演出を出す")]
        private bool _enableBreakEffect = true;

        [SerializeField, Tooltip("そのときに出す言葉")]
        private string _breakLabel = "ブレイク！";

        [SerializeField, Tooltip("演出の色")]
        private Color _breakColor = new Color(1.0f, 0.9f, 0.45f, 1.0f);

        [SerializeField, Min(0.0f), Tooltip("止める時間(秒)。長いと次の攻撃が遅れて気持ち悪い")]
        private float _breakHitStopSec = 0.12f;

        [Header("削れた分の残像")]
        [SerializeField, Tooltip("本体より遅れて追いつく帯。未設定なら出さない")]
        private RectTransform _delayedRect;

        [SerializeField, Min(0.0f), Tooltip("追いつき始めるまでの待ち(秒)。ここが『どれだけ削ったか』を見せる時間")]
        private float _delayedHoldSeconds = 0.35f;

        [SerializeField, Min(0.01f), Tooltip("追いつくのにかける時間(秒)")]
        private float _delayedCatchSeconds = 0.45f;

        [Header("残りが少ないとき")]
        [SerializeField, Tooltip("色を変える中身の絵。未設定なら色を変えない")]
        private Graphic _fillGraphic;

        [SerializeField, Tooltip("ふだんの色")]
        private Color _normalColor = new Color(0.910f, 0.435f, 0.839f, 1.0f);

        [SerializeField, Tooltip("残りわずかのときの色")]
        private Color _lowColor = new Color(1.0f, 0.30f, 0.25f, 1.0f);

        [SerializeField, Range(0.0f, 1.0f), Tooltip("この割合を下回ったら色を変え始める")]
        private float _lowThreshold = 0.3f;

        [SerializeField, Min(0.0f), Tooltip("残りわずかのときの脈打ちの速さ(1秒あたりの回数)。0で脈打たない")]
        private float _lowPulseHz = 2.2f;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("エディタで見た目を確かめるための値。実行中は使わない")]
        private float _previewRatio = 1.0f;

        // ---- 内部状態 ------------------------------------

        private float _displayRatio = 1.0f;
        private float _targetRatio = 1.0f;

        /// <summary>前に見えていた残り本数。減った瞬間を捕まえるのに使う</summary>
        private int _lastSegment = -1;
        private Transform _bossTransform;

        private float _delayedRatio = 1.0f;
        private float _delayedHoldRemain;
        private CancellationTokenSource _animationCts;

        // ---- 公開API -------------------------------------

        /// <summary>いま表示している割合(0〜1)</summary>
        public float Ratio01 => _displayRatio;

        /// <summary>現在HPと最大HPを渡してゲージを更新する</summary>
        public void SetHealth(int current, int max)
        {
            SetRatio(max <= 0 ? 0.0f : current / (float)max);
        }

        /// <summary>割合(0〜1)でゲージを更新する。設定した秒数をかけて追従する</summary>
        public void SetRatio(float ratio01)
        {
            _targetRatio = Mathf.Clamp01(ratio01);

            if (_animationSeconds <= 0.0f || !isActiveAndEnabled)
            {
                SetRatioImmediate(_targetRatio);
                return;
            }

            RestartAnimation();
        }

        /// <summary>アニメーションせずに即座に反映する。戦闘開始時のリセットなどに使う</summary>
        public void SetRatioImmediate(float ratio01)
        {
            StopAnimation();
            _targetRatio = Mathf.Clamp01(ratio01);
            _displayRatio = _targetRatio;
            ApplyFill(_displayRatio);
        }

        /// <summary>ゲージ全体の表示・非表示を切り替える</summary>
        public void SetVisible(bool visible)
        {
            if (_group == null) return;
            _group.alpha = visible ? 1.0f : 0.0f;
        }

        /// <summary>ボスの名前画像を差し替える。ボスが増えたときに使う</summary>
        public void SetBossName(Texture nameTexture)
        {
            if (_nameImage != null) _nameImage.texture = nameTexture;
        }

        // ---- Unityイベント -------------------------------

        private void OnEnable()
        {
            // 残像を合わせずに出すと、置いたままの大きさで真ん中に白い塊が残る
            _delayedRatio = _displayRatio;

            ApplyFill(_displayRatio);
            ApplyWidth(_delayedRect, SegmentRatio(_delayedRatio));
            UpdatePips();
            UpdateTrackColor();
        }

        private void OnDisable()
        {
            StopAnimation();
        }

        private void Update()
        {
            UpdateDelayed();
            UpdateFillColor();
            UpdateTrackColor();
            CheckSegmentBreak();
            UpdatePips();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying) return;

            _displayRatio = _previewRatio;
            _targetRatio = _previewRatio;
            ApplyFill(_previewRatio);
        }
#endif

        // ---- 内部処理 ------------------------------------

        private void RestartAnimation()
        {
            StopAnimation();
            _animationCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            AnimateAsync(_animationCts.Token).Forget();
        }

        private void StopAnimation()
        {
            if (_animationCts == null) return;

            _animationCts.Cancel();
            _animationCts.Dispose();
            _animationCts = null;
        }

        /// <summary>今の表示値から目標値へ、決めた秒数で滑らかに寄せる</summary>
        private async UniTaskVoid AnimateAsync(CancellationToken ct)
        {
            try
            {
                float from = _displayRatio;
                float elapsed = 0.0f;

                while (elapsed < _animationSeconds)
                {
                    elapsed += Time.deltaTime;
                    _displayRatio = Mathf.Lerp(from, _targetRatio, Mathf.Clamp01(elapsed / _animationSeconds));
                    ApplyFill(_displayRatio);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }

                _displayRatio = _targetRatio;
                ApplyFill(_displayRatio);
            }
            catch (OperationCanceledException)
            {
                // 次の値が来て止めただけなので何もしない
            }
        }

        /// <summary>
        /// 削れた分を、少し待ってから追いつかせる。
        /// 本体と一緒に減らすと『どれだけ削ったか』が見えず、手応えが伝わらない。
        /// </summary>
        private void UpdateDelayed()
        {
            if (_delayedRect == null) return;

            // 増えたとき(戦闘開始のリセットなど)は待たずに合わせる
            if (_delayedRatio < _displayRatio) { _delayedRatio = _displayRatio; ApplyWidth(_delayedRect, SegmentRatio(_delayedRatio)); return; }
            if (Mathf.Approximately(_delayedRatio, _displayRatio)) return;

            // 本が変わったら残像は持ち越さない。前の本の残りが新しい本に見えてしまう
            if (SegmentIndex(_delayedRatio) != SegmentIndex(_displayRatio))
            {
                _delayedRatio = _displayRatio;
                ApplyWidth(_delayedRect, SegmentRatio(_delayedRatio));
                return;
            }

            if (_delayedHoldRemain > 0.0f) { _delayedHoldRemain -= Time.deltaTime; return; }

            float step = Time.deltaTime / Mathf.Max(0.01f, _delayedCatchSeconds);
            _delayedRatio = Mathf.MoveTowards(_delayedRatio, _displayRatio, step);

            ApplyWidth(_delayedRect, SegmentRatio(_delayedRatio));
        }

        /// <summary>
        /// 中身の色を、いま削っている本に合わせる。
        /// 本ごとに色が変わると、切り替わった瞬間が見た目でも分かる。
        /// </summary>
        private void UpdateFillColor()
        {
            if (_fillGraphic == null) return;

            Color color = SegmentColor(SegmentIndex(_displayRatio));

            // 全体の残りがわずかなときだけ脈打たせる。最後の1本の緊張感を出す
            if (_lowPulseHz > 0.0f && _lowThreshold > 0.0f && _displayRatio <= _lowThreshold)
            {
                float pulse = 0.80f + 0.20f * Mathf.Sin(Time.unscaledTime * _lowPulseHz * Mathf.PI * 2.0f);
                color = new Color(color.r * pulse, color.g * pulse, color.b * pulse, color.a);
            }

            _fillGraphic.color = color;
        }

        /// <summary>
        /// 溝の色を『次に現れる本』の色にする。
        /// いま削り切ったら何色が出てくるかが先に見えるので、
        /// 本の切り替わりが唐突に感じられなくなる。
        /// </summary>
        private void UpdateTrackColor()
        {
            if (_trackGraphic == null) return;

            int remaining = SegmentIndex(_displayRatio);

            // 最後の1本を削っている間は、次が無いので暗いままにする
            if (remaining <= 1) { _trackGraphic.color = _trackLastColor; return; }

            Color next = SegmentColor(remaining - 1);

            _trackGraphic.color = new Color(
                next.r * _trackBrightness,
                next.g * _trackBrightness,
                next.b * _trackBrightness,
                1.0f);
        }

        /// <summary>
        /// 1本削り切った瞬間を捕まえて演出を出す。
        ///
        /// 4人で削っているときの達成感がここに集まる。
        /// 『あと1本』が見えると、そこから一気に攻めたくなる。
        /// </summary>
        private void CheckSegmentBreak()
        {
            int remaining = SegmentIndex(_displayRatio);

            // 最初の1回は前の値が無いので、覚えるだけにする
            if (_lastSegment < 0) { _lastSegment = remaining; return; }
            if (remaining >= _lastSegment) { _lastSegment = remaining; return; }

            _lastSegment = remaining;

            if (!_enableBreakEffect) return;

            // 最後の1本を削り切ったときは撃破。そちらの演出に任せる
            if (remaining <= 0) return;

            PlayBreakEffect(remaining);
        }

        private void PlayBreakEffect(int remaining)
        {
            ProjectKMP.Battle.HitStop.Play(_breakHitStopSec, 0.06f, 0.18f);
            ImpactFrame.Play(_breakColor, 0.05f);
            BgmPlayer.Duck(0.45f, 0.15f, 0.45f);

            Transform boss = ResolveBossTransform();
            if (boss == null) return;

            // 残り1本になったら、世界の空気ごと切り替える。
            // 言葉だけでなく、色と音でも『ここからが最後』を伝える
            if (remaining == 1)
            {
                ProjectKMP.Battle.FinalPhaseDirector.Begin();
                return;
            }

            string label = _breakLabel;

            ProjectKMP.Battle.Onomatopoeia.Play(boss.position + Vector3.up * 3.0f, label, _breakColor, 1.8f, 0.9f);
            ProjectKMP.Battle.ShockwaveRing.Play(boss.position, _breakColor, 10.0f, 0.5f, 1.0f);
        }

        /// <summary>ボスを探す。一度見つけたら控えておき、毎回探し直さない</summary>
        private Transform ResolveBossTransform()
        {
            if (_bossTransform != null) return _bossTransform;

            Monster.BossHealth boss = FindAnyObjectByType<Monster.BossHealth>();
            _bossTransform = boss != null ? boss.transform : null;

            return _bossTransform;
        }

        /// <summary>残り本数から、その本の色を返す</summary>
        private Color SegmentColor(int remaining)
        {
            if (_segmentColors == null || _segmentColors.Length == 0) return _normalColor;

            // 残りが多いほど『削る順』では手前。残り4本なら先頭の色になる
            int index = Mathf.Clamp(_segmentCount - remaining, 0, _segmentColors.Length - 1);

            return _segmentColors[index];
        }

        /// <summary>中身の幅を割合に合わせて変える</summary>
        private void ApplyFill(float ratio01)
        {
            ApplyWidth(_fillRect, SegmentRatio(ratio01));

            // 減ったときだけ残像を残す。増えたときは UpdateDelayed 側ですぐ合わせる
            if (_delayedRect != null && ratio01 < _delayedRatio) _delayedHoldRemain = _delayedHoldSeconds;
        }

        /// <summary>
        /// いま何本目を削っているか(1が最後の1本)。
        /// 全体の割合を本数で割って、残っている本の数として数える。
        /// </summary>
        private int SegmentIndex(float total01)
        {
            // 計算は切り出した側にある。画面が無くても正しさを確かめられる
            return ProjectKMP.Battle.BossSegments.Remaining(total01, _segmentCount);
        }

        /// <summary>
        /// いま削っている1本ぶんの残り(0〜1)。
        /// 全体が減って本の切れ目をまたぐと0から1へ戻り、次の本が右から現れる。
        /// </summary>
        private float SegmentRatio(float total01)
        {
            return ProjectKMP.Battle.BossSegments.Ratio(total01, _segmentCount);
        }

        /// <summary>残りの本数を印に反映する</summary>
        private void UpdatePips()
        {
            if (_segmentPips == null) return;

            int remaining = SegmentIndex(_displayRatio);

            for (int i = 0; i < _segmentPips.Length; i++)
            {
                if (_segmentPips[i] == null) continue;

                // 左の印ほど後に削る本。帯と同じ色にして、どの印がどの本かを分からせる
                _segmentPips[i].color = i < remaining ? SegmentColor(i + 1) : _pipOffColor;
            }
        }

        /// <summary>指定した帯の幅を割合に合わせて変える</summary>
        private void ApplyWidth(RectTransform target, float ratio01)
        {
            if (_trackRect == null || target == null) return;

            float trackWidth = _trackRect.rect.width - _padding * 2.0f;
            float trackHeight = _trackRect.rect.height - _padding * 2.0f;

            // 残りわずかでも高さぶんの幅を残し、丸い端がつぶれないようにする。0のときだけ完全に消す
            float width = ratio01 <= 0.0f ? 0.0f : Mathf.Max(trackHeight, ratio01 * trackWidth);

            target.anchorMin = new Vector2(0.0f, 0.0f);
            target.anchorMax = new Vector2(0.0f, 1.0f);
            target.offsetMin = new Vector2(_padding, _padding);
            target.offsetMax = new Vector2(_padding + width, -_padding);
        }
    }
}
