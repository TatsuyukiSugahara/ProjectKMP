using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ProjectKMP.Attack
{
    /// <summary>
    /// 攻撃を当てられる相手につけるコンポーネント。
    /// AttackData 側の設定でこれが付いた相手だけに当たるようにできる。
    /// </summary>
    public class HitTarget : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Tooltip("しぼりこみ用のID。AttackData の対象IDに書くと、このIDの相手だけに当たる")]
        private string _targetId = string.Empty;

        [SerializeField, Tooltip("今このターゲットに当てられるか")]
        private bool _canBeHit = true;

        [SerializeField, Tooltip("この相手だけ別のヒットエフェクトにしたいときに指定する")]
        private GameObject _overrideHitEffectPrefab;

        [SerializeField, Tooltip("エフェクトを出す位置。未設定なら当たった位置に出る")]
        private Transform _hitEffectPoint;

        [SerializeField, Tooltip("マルチプレイで相手を見分けるID。自動で入るので触らなくてよい")]
        private int _networkId;

        [SerializeField, Tooltip("当たったときに実行したい処理")]
        private UnityEvent _onHit;

        // ---- 内部状態 ------------------------------------

        private static readonly Dictionary<int, HitTarget> REGISTRY = new Dictionary<int, HitTarget>();

        // ---- 公開API -------------------------------------

        /// <summary>しぼりこみ用のID</summary>
        public string TargetId => _targetId;

        /// <summary>今このターゲットに当てられるか</summary>
        public bool CanBeHit => _canBeHit && isActiveAndEnabled;

        /// <summary>この相手専用のヒットエフェクト。未設定なら null</summary>
        public GameObject OverrideHitEffectPrefab => _overrideHitEffectPrefab;

        /// <summary>マルチプレイで相手を見分けるID</summary>
        public int NetworkId => _networkId;

        /// <summary>当たり判定の対象にするかどうかを切り替える</summary>
        public void SetCanBeHit(bool value)
        {
            _canBeHit = value;
        }

        /// <summary>エフェクトを出す位置。専用の位置が無ければ当たった位置をそのまま返す</summary>
        public Vector3 GetEffectPosition(Vector3 fallback)
        {
            return _hitEffectPoint != null ? _hitEffectPoint.position : fallback;
        }

        /// <summary>IDから対象を探す。見つからなければ null</summary>
        public static HitTarget Find(int networkId)
        {
            if (networkId == 0) return null;
            return REGISTRY.TryGetValue(networkId, out HitTarget target) ? target : null;
        }

        /// <summary>当たった瞬間に全クライアントで呼ばれる</summary>
        public void NotifyHit(Vector3 hitPosition, int attackerActorNumber)
        {
            _onHit?.Invoke();
        }

        // ---- Unityイベント -------------------------------

        private void OnEnable()
        {
            if (_networkId != 0) REGISTRY[_networkId] = this;
        }

        private void OnDisable()
        {
            if (_networkId == 0) return;
            if (REGISTRY.TryGetValue(_networkId, out HitTarget target) && target == this)
            {
                REGISTRY.Remove(_networkId);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // シーンに保存されたIDは全クライアントで同じ値になるので、そのまま識別に使える
            if (_networkId != 0) return;

            _networkId = System.Guid.NewGuid().GetHashCode();
            if (_networkId == 0) _networkId = 1;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
