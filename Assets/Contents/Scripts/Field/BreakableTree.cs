using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectKMP.Field
{
    /// <summary>
    /// ビームや必殺技が当たると根元から倒れる木。
    /// 木はシーンに置かれた静的なオブジェクトで、全クライアントに同じものが同じ位置にある。
    /// 攻撃の演出は各クライアントで同じ位置・同じタイミングに走るので、通信なしで全員の画面の
    /// 同じ木が倒れる(草のなぎ倒しや着弾デカールと同じ方式)。
    ///
    /// 当たり判定は物理を使わず、自分で登録した一覧との距離で取る。
    /// 木にコライダーを付けるとビームの OverlapCapsule のバッファが木で埋まり、
    /// ボスへのヒットを取りこぼす恐れがあるため。
    /// </summary>
    public class BreakableTree : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [Header("当たり判定")]
        [SerializeField, Min(0.05f), Tooltip("幹の太さ(半径・メートル)。木のスケールが掛かる")]
        private float _trunkRadius = 0.6f;

        [SerializeField, Min(0.5f), Tooltip("幹の高さ(メートル)。この範囲を通った攻撃だけが当たる。木のスケールが掛かる")]
        private float _trunkHeight = 6.0f;

        [Header("倒れ方")]
        [SerializeField, Min(0.1f), Tooltip("倒れきるまでの時間(秒)")]
        private float _fallDurationSec = 1.1f;

        [SerializeField, Range(60.0f, 110.0f), Tooltip("倒れる角度(度)。90度で真横になる")]
        private float _fallAngleDeg = 92.0f;

        [SerializeField, Min(0.0f), Tooltip("倒れてから沈み始めるまでの余韻(秒)")]
        private float _lingerSec = 1.5f;

        [SerializeField, Min(0.1f), Tooltip("地面へ沈んで見えなくなるまでの時間(秒)")]
        private float _sinkDurationSec = 1.2f;

        [SerializeField, Min(0.0f), Tooltip("沈む深さ(メートル)")]
        private float _sinkDepth = 4.0f;

        [SerializeField, Min(0.0f), Tooltip("倒れたあと生え直すまでの秒数。0なら生え直さない")]
        private float _regrowDelaySec = 0.0f;

        [SerializeField, Tooltip("倒れた先の草をなぎ倒す")]
        private bool _flattenGrassOnFall = true;

        // ---- 内部状態 ------------------------------------

        /// <summary>物理を使わないので、距離判定の対象は自分たちで持つ</summary>
        private static readonly List<BreakableTree> TREES = new List<BreakableTree>();

        private bool _isBroken;
        private bool _isBreakReserved;
        private Vector3 _defaultPosition;
        private Quaternion _defaultRotation;

        // ---- 公開API -------------------------------------

        /// <summary>まだ立っているか</summary>
        public bool IsStanding => !_isBroken;

        /// <summary>
        /// ビームの線分に触れた木を倒す。照射中に毎フレーム呼んでよい
        /// (一度倒れた木は対象から外れるので、何度呼んでも二重には倒れない)。
        /// </summary>
        public static void BreakAlongBeam(Vector3 origin, Vector3 direction, float length, float radius)
        {
            if (TREES.Count == 0 || length <= 0.0f) return;
            if (direction.sqrMagnitude < 0.0001f) return;

            Vector3 forward = direction.normalized;

            for (int i = TREES.Count - 1; i >= 0; i--)
            {
                BreakableTree tree = TREES[i];
                if (tree == null || tree._isBroken) continue;

                Vector3 basePosition = tree.transform.position;
                float scale = tree.CurrentScale;

                // ビームの線分上で、木の根元にいちばん近い点を求める
                float along = Mathf.Clamp(Vector3.Dot(basePosition - origin, forward), 0.0f, length);
                Vector3 closest = origin + forward * along;

                // 幹の高さの範囲を通っていなければ当たらない(足元や梢の上を抜けた場合)
                float trunkTopY = basePosition.y + tree._trunkHeight * scale;
                if (closest.y < basePosition.y - radius || closest.y > trunkTopY + radius) continue;

                if (!tree.IsWithinTrunk(closest, radius)) continue;

                tree.Break(closest);
            }
        }

        /// <summary>爆発など、中心から一定範囲にある木をまとめて倒す</summary>
        public static void BreakInSphere(Vector3 center, float radius)
        {
            if (TREES.Count == 0 || radius <= 0.0f) return;

            // 爆心に近い順に少しずつ遅らせ、破壊が外へ走る「連鎖」に見せる。
            var candidates = new List<BreakableTree>();

            for (int i = TREES.Count - 1; i >= 0; i--)
            {
                BreakableTree tree = TREES[i];
                if (tree == null || tree._isBroken || tree._isBreakReserved) continue;

                Vector3 basePosition = tree.transform.position;

                // 爆心が幹の高さから大きく外れている場合は当たらない
                float trunkTopY = basePosition.y + tree._trunkHeight * tree.CurrentScale;
                if (center.y < basePosition.y - radius || center.y > trunkTopY + radius) continue;

                if (!tree.IsWithinTrunk(center, radius)) continue;

                candidates.Add(tree);
            }

            candidates.Sort((left, right) =>
                Vector3.SqrMagnitude(left.transform.position - center)
                    .CompareTo(Vector3.SqrMagnitude(right.transform.position - center)));

            for (int i = 0; i < candidates.Count; i++)
            {
                float delay = Mathf.Min(0.6f, i * 0.065f);
                candidates[i].ReserveBreak(center, delay);
            }
        }

        /// <summary>発生源から離れる方へ倒す。すでに倒れている木は何もしない</summary>
        public void Break(Vector3 sourcePosition)
        {
            if (_isBroken || _isBreakReserved) return;
            _isBreakReserved = true;
            BreakNow(sourcePosition);
        }

        private void ReserveBreak(Vector3 sourcePosition, float delaySec)
        {
            if (_isBroken || _isBreakReserved) return;
            _isBreakReserved = true;
            BreakAfterDelayAsync(sourcePosition, delaySec, destroyCancellationToken).Forget();
        }

        private async UniTaskVoid BreakAfterDelayAsync(Vector3 sourcePosition, float delaySec, CancellationToken ct)
        {
            try
            {
                if (delaySec > 0.0f)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(delaySec), DelayType.UnscaledDeltaTime, cancellationToken: ct);
                }
                BreakNow(sourcePosition);
            }
            catch (OperationCanceledException)
            {
                _isBreakReserved = false;
            }
        }

        private void BreakNow(Vector3 sourcePosition)
        {
            if (_isBroken) return;
            _isBroken = true;
            _isBreakReserved = false;

            Battle.DestructionChain.NotifyBreak(transform.position);
            BreakableProp.PropagateFromTree(transform.position);

            Vector3 away = transform.position - sourcePosition;
            away.y = 0.0f;

            // 真横から撃たれて方向が決まらないときは、適当な向きへ倒しておく
            if (away.sqrMagnitude < 0.0001f) away = transform.forward;

            FallAsync(away.normalized, destroyCancellationToken).Forget();
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _defaultPosition = transform.position;
            _defaultRotation = transform.rotation;
            TREES.Add(this);
        }

        private void OnDestroy()
        {
            TREES.Remove(this);
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>木はまとめて一様スケールで置かれるので、代表してX成分を見る</summary>
        private float CurrentScale => Mathf.Max(0.01f, transform.lossyScale.x);

        /// <summary>水平方向の距離だけで幹に触れているかを見る</summary>
        private bool IsWithinTrunk(Vector3 point, float radius)
        {
            Vector3 basePosition = transform.position;
            float flatX = point.x - basePosition.x;
            float flatZ = point.z - basePosition.z;
            float reach = radius + _trunkRadius * CurrentScale;
            return flatX * flatX + flatZ * flatZ <= reach * reach;
        }

        /// <summary>
        /// 根元を軸に倒れ、余韻のあと地面へ沈んで見えなくなる。
        /// 折れてから加速して倒れるように、角度の進みは ease-in にしている。
        /// </summary>
        private async UniTaskVoid FallAsync(Vector3 awayDirection, CancellationToken ct)
        {
            try
            {
                Vector3 pivot = transform.position;

                // 倒れる向きに直交する軸で回すと、発生源と反対側へ倒れる
                Vector3 axis = Vector3.Cross(Vector3.up, awayDirection);
                if (axis.sqrMagnitude < 0.0001f) axis = Vector3.right;
                axis.Normalize();

                float appliedAngle = 0.0f;
                float elapsed = 0.0f;
                while (elapsed < _fallDurationSec)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    elapsed += Time.deltaTime;

                    float t = Mathf.Clamp01(elapsed / _fallDurationSec);
                    float angle = _fallAngleDeg * t * t;
                    transform.RotateAround(pivot, axis, angle - appliedAngle);
                    appliedAngle = angle;
                }

                // 倒れた幹が乗るあたりの草を伏せる
                if (_flattenGrassOnFall)
                {
                    float scale = CurrentScale;
                    Vector3 fallen = pivot + awayDirection * (_trunkHeight * scale * 0.5f);
                    fallen.y = pivot.y;
                    GrassField.FlattenAt(fallen, _trunkHeight * scale * 0.5f);
                }

                if (_lingerSec > 0.0f)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(_lingerSec), cancellationToken: ct);
                }

                float sunk = 0.0f;
                elapsed = 0.0f;
                while (elapsed < _sinkDurationSec)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    elapsed += Time.deltaTime;

                    float t = Mathf.Clamp01(elapsed / _sinkDurationSec);
                    float depth = _sinkDepth * t * t;
                    transform.position += Vector3.down * (depth - sunk);
                    sunk = depth;
                }

                gameObject.SetActive(false);

                if (_regrowDelaySec <= 0.0f) return;

                // 生え直す設定のときだけ、元の姿勢に戻して復活させる
                await UniTask.Delay(TimeSpan.FromSeconds(_regrowDelaySec), cancellationToken: ct);

                transform.SetPositionAndRotation(_defaultPosition, _defaultRotation);
                gameObject.SetActive(true);
                _isBroken = false;
                _isBreakReserved = false;
            }
            catch (OperationCanceledException)
            {
                // シーンを抜けるなどで破棄されただけなので何もしない
            }
        }
    }
}
