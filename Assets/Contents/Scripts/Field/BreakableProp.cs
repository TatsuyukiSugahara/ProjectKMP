using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectKMP.Attack;
using ProjectKMP.Battle;
using UnityEngine;

namespace ProjectKMP.Field
{
    /// <summary>
    /// 岩・切り株・箱など、モデルを問わず使えるフィールド破壊部品。
    /// コライダーを攻撃判定へ混ぜず、登録リストとの距離だけでビームと範囲攻撃を判定する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BreakableProp : MonoBehaviour
    {
        public enum BreakMotion { Tumble, Pop }

        private const int MAX_ACTIVE_MOTIONS = 32;
        private const int MAX_EFFECTS_PER_SECOND = 12;
        private const int MAX_CHAIN_GENERATION = 3;

        private static readonly List<BreakableProp> PROPS = new List<BreakableProp>();
        private static int _activeMotions;
        private static int _effectCount;
        private static float _effectWindowStart;

        [Header("当たり判定")]
        [SerializeField, Min(0.05f), Tooltip("物体を上から見た半径。Transformのスケールが掛かる")]
        private float _hitRadius = 0.8f;

        [SerializeField, Min(0.05f), Tooltip("物体の高さ。Transformのスケールが掛かる")]
        private float _hitHeight = 1.5f;

        [Header("壊れ方")]
        [SerializeField] private BreakMotion _motion = BreakMotion.Tumble;
        [SerializeField, Min(0.05f)] private float _motionDurationSec = 0.65f;
        [SerializeField, Min(0.0f), Tooltip("Pop時の跳び上がる高さ、Tumble時の横へ飛ぶ距離")]
        private float _moveDistance = 2.0f;
        [SerializeField, Min(0.0f)] private float _lingerSec = 0.8f;
        [SerializeField, Min(0.05f)] private float _sinkDurationSec = 0.7f;
        [SerializeField, Min(0.0f)] private float _sinkDepth = 2.0f;
        [SerializeField, Min(0.0f), Tooltip("0なら復活しない")]
        private float _regrowDelaySec;

        [Header("連鎖")]
        [SerializeField, Tooltip("近くの破壊から連鎖して壊れる")]
        private bool _receiveChain = true;
        [SerializeField, Min(0.0f), Tooltip("この距離以内の破壊を受け取る")]
        private float _chainReceiveRadius = 3.5f;
        [SerializeField, Min(0.0f), Tooltip("壊れたとき、周囲へ連鎖を広げる距離。0で広げない")]
        private float _chainSpreadRadius = 3.0f;

        [Header("演出（任意）")]
        [SerializeField] private GameObject _breakEffectPrefab;
        [SerializeField, Min(0.01f)] private float _breakEffectScale = 1.0f;
        [SerializeField, Min(0.05f)] private float _breakEffectLifeSec = 1.5f;
        [SerializeField] private AttackDecal _breakDecalPrefab;
        [SerializeField, Min(0.1f)] private float _decalDiameter = 2.0f;
        [SerializeField] private Color _shockwaveColor = new Color(1.0f, 0.72f, 0.2f, 1.0f);

        [Header("音")]
        [SerializeField, Tooltip("壊れたときの音。未設定なら鳴らさない")]
        private AudioClip _breakClip;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("音量")]
        private float _breakVolume = 0.7f;

        private bool _isBroken;
        private bool _isReserved;
        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private Vector3 _homeScale;

        public bool IsStanding => !_isBroken && !_isReserved;

        public static void BreakAlongBeam(Vector3 origin, Vector3 direction, float length, float beamRadius)
        {
            if (PROPS.Count == 0 || length <= 0.0f || direction.sqrMagnitude < 0.0001f) return;
            Vector3 forward = direction.normalized;

            for (int i = PROPS.Count - 1; i >= 0; i--)
            {
                BreakableProp prop = PROPS[i];
                if (prop == null || !prop.IsStanding) continue;

                Vector3 basePosition = prop.transform.position;
                float along = Mathf.Clamp(Vector3.Dot(basePosition - origin, forward), 0.0f, length);
                Vector3 closest = origin + forward * along;
                if (!prop.ContainsPoint(closest, beamRadius)) continue;

                prop.ReserveBreak(closest, 0.0f, 0);
            }
        }

        public static void BreakInSphere(Vector3 center, float radius)
        {
            ScheduleInSphere(center, radius, 0, false);
        }

        /// <summary>木が倒れた地点から、連鎖を受け取れる汎用物へ伝播させる。</summary>
        public static void PropagateFromTree(Vector3 center)
        {
            ScheduleInSphere(center, 0.0f, 1, true);
        }

        private static void ScheduleInSphere(Vector3 center, float attackRadius, int generation, bool chainOnly)
        {
            if (PROPS.Count == 0 || generation > MAX_CHAIN_GENERATION) return;

            // 連鎖は遅延実行され、別の連鎖と重なる可能性があるため作業リストは呼び出しごとに持つ。
            var candidates = new List<BreakableProp>();
            for (int i = PROPS.Count - 1; i >= 0; i--)
            {
                BreakableProp prop = PROPS[i];
                if (prop == null || !prop.IsStanding) continue;
                if (generation > 0 && !prop._receiveChain) continue;

                float reach = chainOnly
                    ? (prop._receiveChain ? prop._chainReceiveRadius : 0.0f)
                    : attackRadius + prop.ScaledRadius;
                if (reach <= 0.0f) continue;

                Vector3 delta = prop.transform.position - center;
                if (delta.sqrMagnitude > reach * reach) continue;
                candidates.Add(prop);
            }

            candidates.Sort((left, right) =>
                Vector3.SqrMagnitude(left.transform.position - center)
                    .CompareTo(Vector3.SqrMagnitude(right.transform.position - center)));

            for (int i = 0; i < candidates.Count; i++)
            {
                float delay = Mathf.Min(0.55f, i * 0.055f + generation * 0.08f);
                candidates[i].ReserveBreak(center, delay, generation);
            }
        }

        private void Awake()
        {
            _homePosition = transform.position;
            _homeRotation = transform.rotation;
            _homeScale = transform.localScale;
            PROPS.Add(this);
        }

        private void OnDestroy()
        {
            PROPS.Remove(this);
        }

        private float UniformScale => Mathf.Max(0.01f,
            Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)));
        private float ScaledRadius => _hitRadius * UniformScale;

        private bool ContainsPoint(Vector3 point, float extraRadius)
        {
            Vector3 basePosition = transform.position;
            float scale = UniformScale;
            if (point.y < basePosition.y - extraRadius || point.y > basePosition.y + _hitHeight * scale + extraRadius)
                return false;

            float x = point.x - basePosition.x;
            float z = point.z - basePosition.z;
            float reach = ScaledRadius + extraRadius;
            return x * x + z * z <= reach * reach;
        }

        private void ReserveBreak(Vector3 sourcePosition, float delaySec, int generation)
        {
            if (!IsStanding) return;
            _isReserved = true;
            BreakAfterDelayAsync(sourcePosition, delaySec, generation, destroyCancellationToken).Forget();
        }

        private async UniTaskVoid BreakAfterDelayAsync(
            Vector3 sourcePosition, float delaySec, int generation, CancellationToken ct)
        {
            try
            {
                if (delaySec > 0.0f)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(delaySec), DelayType.UnscaledDeltaTime, cancellationToken: ct);
                }
                BeginBreak(sourcePosition, generation);
            }
            catch (OperationCanceledException)
            {
                _isReserved = false;
            }
        }

        private void BeginBreak(Vector3 sourcePosition, int generation)
        {
            if (_isBroken) return;
            _isBroken = true;
            _isReserved = false;

            DestructionChain.NotifyBreak(transform.position);
            PlayBreakEffects();

            if (_chainSpreadRadius > 0.0f && generation < MAX_CHAIN_GENERATION)
            {
                ScheduleInSphere(transform.position, _chainSpreadRadius, generation + 1, false);
                BreakableTree.BreakInSphere(transform.position, _chainSpreadRadius);
            }

            Vector3 away = transform.position - sourcePosition;
            away.y = 0.0f;
            if (away.sqrMagnitude < 0.0001f) away = transform.forward;

            if (_activeMotions >= MAX_ACTIVE_MOTIONS)
            {
                gameObject.SetActive(false);
                if (_regrowDelaySec > 0.0f)
                    RestoreAfterOverflowAsync(destroyCancellationToken).Forget();
                return;
            }

            _activeMotions++;
            AnimateBreakAsync(away.normalized, destroyCancellationToken).Forget();
        }

        private async UniTaskVoid RestoreAfterOverflowAsync(CancellationToken ct)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(_regrowDelaySec), DelayType.UnscaledDeltaTime, cancellationToken: ct);
                Restore();
            }
            catch (OperationCanceledException)
            {
                // シーンを抜けただけ。
            }
        }

        // 壊れた場所から鳴らす。鳴らし役は使い回すので、
        // 連鎖で一度に何個も壊れても作り直しは起きない
        private void PlayBreakSound()
        {
            if (_breakClip == null) return;

            ProjectKMP.Core.OneShotSound.Play(
                _breakClip,
                transform.position + Vector3.up * (_hitHeight * 0.5f),
                _breakVolume);
        }

        private void PlayBreakEffects()
        {
            // 音は絵より軽いので、間引きの対象にしない。
            // 連鎖で絵が省かれても、音だけは全部鳴らすことで手応えが残る
            PlayBreakSound();

            if (!TryUseEffectBudget()) return;

            AttackEffect.Spawn(_breakEffectPrefab, transform.position + Vector3.up * (_hitHeight * 0.5f),
                Quaternion.identity, _breakEffectScale, _breakEffectLifeSec);
            AttackDecal.Spawn(_breakDecalPrefab, transform.position, _decalDiameter);
            ShockwaveRing.Play(transform.position, _shockwaveColor,
                Mathf.Max(2.0f, ScaledRadius * 3.0f), 0.38f, 0.35f);
        }

        private static bool TryUseEffectBudget()
        {
            float now = Time.unscaledTime;
            if (now - _effectWindowStart >= 1.0f)
            {
                _effectWindowStart = now;
                _effectCount = 0;
            }
            if (_effectCount >= MAX_EFFECTS_PER_SECOND) return false;
            _effectCount++;
            return true;
        }

        private async UniTaskVoid AnimateBreakAsync(Vector3 away, CancellationToken ct)
        {
            try
            {
                Vector3 startPosition = transform.position;
                Quaternion startRotation = transform.rotation;
                Vector3 axis = Vector3.Cross(Vector3.up, away).normalized;
                if (axis.sqrMagnitude < 0.0001f) axis = Vector3.right;

                float elapsed = 0.0f;
                while (elapsed < _motionDurationSec)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / _motionDurationSec);

                    if (_motion == BreakMotion.Tumble)
                    {
                        transform.position = startPosition + away * (_moveDistance * t);
                        transform.rotation = Quaternion.AngleAxis(110.0f * t, axis) * startRotation;
                    }
                    else
                    {
                        Vector3 position = startPosition + away * (_moveDistance * 0.45f * t);
                        position.y += _moveDistance * 4.0f * t * (1.0f - t);
                        transform.position = position;
                        transform.rotation = Quaternion.AngleAxis(360.0f * t, axis) * startRotation;
                    }
                }

                if (_lingerSec > 0.0f)
                    await UniTask.Delay(TimeSpan.FromSeconds(_lingerSec), cancellationToken: ct);

                Vector3 sinkStart = transform.position;
                elapsed = 0.0f;
                while (elapsed < _sinkDurationSec)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / _sinkDurationSec);
                    transform.position = sinkStart + Vector3.down * (_sinkDepth * t * t);
                    transform.localScale = Vector3.Lerp(_homeScale, _homeScale * 0.2f, t);
                }

                gameObject.SetActive(false);
                if (_regrowDelaySec <= 0.0f) return;

                await UniTask.Delay(TimeSpan.FromSeconds(_regrowDelaySec), DelayType.UnscaledDeltaTime, cancellationToken: ct);
                Restore();
            }
            catch (OperationCanceledException)
            {
                // シーンを抜けただけ。
            }
            finally
            {
                _activeMotions = Mathf.Max(0, _activeMotions - 1);
            }
        }

        private void Restore()
        {
            transform.SetPositionAndRotation(_homePosition, _homeRotation);
            transform.localScale = _homeScale;
            _isBroken = false;
            _isReserved = false;
            gameObject.SetActive(true);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1.0f, 0.65f, 0.15f, 0.8f);
            float scale = Application.isPlaying ? UniformScale : Mathf.Max(0.01f, transform.lossyScale.x);
            Vector3 center = transform.position + Vector3.up * (_hitHeight * scale * 0.5f);
            Gizmos.DrawWireCube(center, new Vector3(_hitRadius * scale * 2.0f, _hitHeight * scale, _hitRadius * scale * 2.0f));

            if (_chainSpreadRadius > 0.0f)
            {
                Gizmos.color = new Color(1.0f, 0.3f, 0.1f, 0.35f);
                UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, _chainSpreadRadius);
            }
        }
#endif
    }
}
