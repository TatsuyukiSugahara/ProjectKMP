using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 見た目を潰したり伸ばしたりする。
    ///
    /// アニメの気持ちよさの正体はほぼこれ。跳ぶ前に縮み、跳んだら伸び、着地で潰れる。
    /// 体積が変わらないように、縦に伸ばしたら横を細くする。
    /// そうしないと、ただ大きくなったり小さくなったりして見える。
    ///
    /// 掛けるのは見た目だけ。当たり判定を持つ側に掛けると、
    /// 潰れた瞬間に地面をすり抜けたり、当たらなくなったりする。
    /// </summary>
    public class SquashStretch : MonoBehaviour
    {
        // ---- 設定 ----------------------------------------

        [SerializeField, Tooltip("形を変える対象。未設定なら自分自身")]
        private Transform _target;

        [SerializeField, Min(0.01f), Tooltip("元に戻る速さ。大きいほどキビキビ戻る")]
        private float _recoverSpeed = 9.0f;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("戻りぎわの跳ね返り。0で跳ねずに収まる")]
        private float _overshoot = 0.35f;

        // ---- 内部状態 ------------------------------------

        private Vector3 _baseScale = Vector3.one;

        /// <summary>いまの伸び具合。1で元どおり、1より大きいと縦に伸びている</summary>
        private float _stretch = 1.0f;

        private float _velocity;

        // ---- 公開API -------------------------------------

        /// <summary>
        /// 縦に伸ばす。amount は 0.3 で3割ぶん伸びる。
        /// 跳んだ瞬間や、前へ突っ込む瞬間に使う。
        /// </summary>
        public void Stretch(float amount)
        {
            _stretch = 1.0f + Mathf.Max(0.0f, amount);
            _velocity = 0.0f;
        }

        /// <summary>
        /// 縦に潰す。amount は 0.3 で3割ぶん潰れる。
        /// 着地や、殴られた瞬間に使う。
        /// </summary>
        public void Squash(float amount)
        {
            _stretch = 1.0f - Mathf.Clamp(amount, 0.0f, 0.8f);
            _velocity = 0.0f;
        }

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            if (_target == null) _target = transform;

            _baseScale = _target.localScale;
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            // 目標は常に『元どおり』。ばねのように戻すと、行きすぎて跳ね返る動きになる
            float difference = 1.0f - _stretch;
            _velocity += difference * _recoverSpeed * _recoverSpeed * Time.deltaTime;
            _velocity *= 1.0f - Mathf.Clamp01((1.0f - _overshoot) * _recoverSpeed * Time.deltaTime * 2.0f);

            _stretch += _velocity * Time.deltaTime;

            // ほぼ戻ったら止める。わずかな揺れが残り続けるのを防ぐ
            if (Mathf.Abs(_stretch - 1.0f) < 0.001f && Mathf.Abs(_velocity) < 0.01f)
            {
                _stretch = 1.0f;
                _velocity = 0.0f;
            }

            Apply();
        }

        /// <summary>縦に伸ばしたぶん横を細くする。かさを保たないと、ただの拡大縮小に見える</summary>
        private void Apply()
        {
            float vertical = _stretch;
            float horizontal = 1.0f / Mathf.Sqrt(Mathf.Max(0.01f, vertical));

            _target.localScale = new Vector3(
                _baseScale.x * horizontal,
                _baseScale.y * vertical,
                _baseScale.z * horizontal);
        }
    }
}
