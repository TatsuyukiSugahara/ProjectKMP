using ProjectKMP.Player;
using UnityEngine;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// 弾。全クライアントがローカルに生成して同じ速度で直進させるため、位置は同期しない。
    /// 当たり判定は撃った本人のクライアントだけが行い、命中したら RPC で全員に伝える。
    /// </summary>
    public class Bullet : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------
        private const int MAX_HIT_BUFFER = 8;

        // ---- 調整パラメータ ------------------------------
        [SerializeField] private float _speed       = 7.0f;
        [SerializeField] private float _lifeTimeSec = 4.0f;
        [SerializeField] private float _hitRadius   = 0.45f;

        // ---- 内部状態 ------------------------------------
        private readonly Collider[] _hitBuffer = new Collider[MAX_HIT_BUFFER];
        private int _shooterActorNumber = -1;
        private bool _isOwnerSide;
        private float _elapsed;

        // ---- 公開API -------------------------------------

        /// <summary>生成直後に呼ぶ。isOwnerSide が true のクライアントだけが当たり判定を行う</summary>
        public void Initialize(int shooterActorNumber, bool isOwnerSide)
        {
            _shooterActorNumber = shooterActorNumber;
            _isOwnerSide = isOwnerSide;
        }

        // ---- Unityイベント -------------------------------

        private void Update()
        {
            transform.position += transform.forward * (_speed * Time.deltaTime);

            _elapsed += Time.deltaTime;
            if (_elapsed >= _lifeTimeSec)
            {
                Destroy(gameObject);
                return;
            }

            if (_isOwnerSide) CheckHit();
        }

        // ---- 内部処理 ------------------------------------

        private void CheckHit()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, _hitRadius, _hitBuffer);

            for (int i = 0; i < count; i++)
            {
                Collider hit = _hitBuffer[i];
                if (hit == null) continue;

                var health = hit.GetComponentInParent<PlayerHealth>();
                if (health == null)
                {
                    // プレイヤー以外(障害物)に当たったら消える
                    if (!hit.isTrigger) { Destroy(gameObject); return; }
                    continue;
                }

                // 自分の弾で自分は死なない。既に死んでいる相手も無視する
                if (health.OwnerActorNumber == _shooterActorNumber) continue;
                if (health.IsDead) continue;

                health.ApplyKill(_shooterActorNumber);
                Destroy(gameObject);
                return;
            }
        }
    }
}
