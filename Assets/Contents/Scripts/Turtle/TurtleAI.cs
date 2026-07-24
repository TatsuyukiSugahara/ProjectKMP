using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectKMP.Turtle
{
    /// <summary>
    /// カメの徘徊AI。演出用の生き物のため Photon 同期は行わない(各クライアントでローカルに動く)。
    /// 待機→初期位置周辺のランダムな地点へゆっくり移動、を繰り返す。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class TurtleAI : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------
        private const string ANIM_IDLE = "Idle_A";
        private const string ANIM_WALK = "Walk";
        private const float ARRIVE_DISTANCE = 0.1f;
        private const float ANIM_CROSSFADE = 0.2f;

        // ---- 調整パラメータ ------------------------------
        [SerializeField] private float _moveSpeed   = 0.5f;
        [SerializeField] private float _turnSpeedDeg = 90.0f;
        [SerializeField] private float _wanderRadius = 5.0f;
        [SerializeField] private float _idleTimeMin  = 2.0f;
        [SerializeField] private float _idleTimeMax  = 5.0f;

        // ---- 内部状態 ------------------------------------
        private Animator _animator;
        private Vector3 _homePosition;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void Start()
        {
            _homePosition = transform.position;
            WanderLoopAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>待機と移動を交互に繰り返す徘徊ループ</summary>
        private async UniTaskVoid WanderLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await IdleAsync(Random.Range(_idleTimeMin, _idleTimeMax), ct);
                await MoveToAsync(PickWanderTarget(), ct);
            }
        }

        /// <summary>待機アニメーションを再生してその場で指定秒待つ</summary>
        private async UniTask IdleAsync(float duration, CancellationToken ct)
        {
            _animator.CrossFade(ANIM_IDLE, ANIM_CROSSFADE);
            await UniTask.Delay(System.TimeSpan.FromSeconds(duration), cancellationToken: ct);
        }

        /// <summary>歩行アニメーションを再生しながら目標地点まで移動する</summary>
        private async UniTask MoveToAsync(Vector3 target, CancellationToken ct)
        {
            _animator.CrossFade(ANIM_WALK, ANIM_CROSSFADE);

            while (Vector3.Distance(transform.position, target) > ARRIVE_DISTANCE)
            {
                Vector3 direction = (target - transform.position).normalized;

                Quaternion look = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, look, _turnSpeedDeg * Time.deltaTime);

                transform.position = Vector3.MoveTowards(
                    transform.position, target, _moveSpeed * Time.deltaTime);

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        /// <summary>初期位置を中心とした半径内のランダムな地点を選ぶ</summary>
        private Vector3 PickWanderTarget()
        {
            Vector2 offset = Random.insideUnitCircle * _wanderRadius;
            return _homePosition + new Vector3(offset.x, 0.0f, offset.y);
        }
    }
}
