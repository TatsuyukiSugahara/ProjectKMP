using R3;
using UnityEngine;

namespace ProjectKMP.Attack
{
    /// <summary>
    /// 攻撃を受けたときに出す共通のヒットエフェクト。
    /// 同じオブジェクトにあるダメージ通知(プレイヤーの PlayerHealth / 敵の HitTarget)を自動で購読するので、
    /// 敵・味方どちらに付けても動く。プレハブ・大きさ・表示位置・間引きはインスペクタでキャラごとに変えられる。
    /// どちらの通知も全クライアントで流れるため、追加の通信なしで全員の画面に出る。
    /// 注意: プレイヤーの噛みつき攻撃は AttackData 側にもヒットエフェクトの仕組みがあるため、
    /// そちらでエフェクトが出る相手(HitTarget)に二重で付けると2つ出る。どちらか一方を使うこと。
    /// </summary>
    public class DamageHitEffect : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("被弾時に出すエフェクト")]
        private GameObject _effectPrefab;

        [SerializeField, Min(0.01f), Tooltip("エフェクトの大きさ倍率")]
        private float _effectScale = 1.0f;

        [SerializeField, Tooltip("エフェクトを出す高さ(足元からのオフセット、メートル)。当たった位置が分かる通知ではそちらを優先する")]
        private float _effectHeight = 0.8f;

        [SerializeField, Min(0.0f), Tooltip("エフェクトを自動で消すまでの秒数。0ならプレハブ側の設定に任せる")]
        private float _effectLifeSec = 2.0f;

        [SerializeField, Min(0.0f), Tooltip("連続ヒット時にエフェクトを間引く間隔(秒)。ビームの継続ダメージなどで出過ぎるのを防ぐ")]
        private float _minIntervalSec = 0.15f;

        // ---- 内部状態 ------------------------------------

        private System.IDisposable _hitSubscription;
        private System.IDisposable _damagedSubscription;
        private float _lastSpawnTime = float.NegativeInfinity;

        // ---- Unityイベント -------------------------------

        private void Start()
        {
            // 敵側: HitTarget の被弾通知(当たった位置つき)
            var hitTarget = GetComponent<HitTarget>();
            if (hitTarget != null)
            {
                _hitSubscription = hitTarget.Hit.Subscribe(info => Spawn(hitTarget.GetEffectPosition(info.Position)));
            }

            // プレイヤー側: PlayerHealth の被弾通知(位置情報が無いので体の高さに出す)
            var health = GetComponent<Player.PlayerHealth>();
            if (health != null)
            {
                _damagedSubscription = health.Damaged.Subscribe(_ => Spawn(transform.position + Vector3.up * _effectHeight));
            }

            if (hitTarget == null && health == null)
            {
                Debug.LogWarning("[Attack] DamageHitEffect: 購読できるダメージ通知(HitTarget / PlayerHealth)が見つかりません", this);
            }
        }

        private void OnDestroy()
        {
            _hitSubscription?.Dispose();
            _damagedSubscription?.Dispose();
        }

        // ---- 内部処理 ------------------------------------

        private void Spawn(Vector3 position)
        {
            if (_effectPrefab == null) return;

            // 連続ヒットの間引き
            if (Time.time - _lastSpawnTime < _minIntervalSec) return;
            _lastSpawnTime = Time.time;

            AttackEffect.Spawn(_effectPrefab, position, Quaternion.identity, _effectScale, _effectLifeSec);
        }
    }
}
