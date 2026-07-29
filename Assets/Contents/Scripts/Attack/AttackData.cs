using UnityEngine;

namespace ProjectKMP.Attack
{
    /// <summary>
    /// 攻撃1種類ぶんの設定。ヒットエフェクト・当たり判定の大きさ・出るタイミングをまとめて持つ。
    /// 攻撃を増やしたいときはこのアセットを複製して中身を差し替える。
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Attack_New", menuName = "ProjectKMP/攻撃データ")]
    public class AttackData : ScriptableObject
    {
        // ---- 基本 ----------------------------------------

        [Header("基本")]
        [SerializeField, Tooltip("ログなどに出す攻撃の名前")]
        private string _displayName = "かみつき";

        [SerializeField, Min(0), Tooltip("この攻撃1回ぶんのダメージ。当たった相手はこのぶんHPが減る")]
        private int _attackPower = 10;

        // ---- エフェクト ----------------------------------

        [Header("エフェクト")]
        [SerializeField, Tooltip("当たったときに出すヒットエフェクト。攻撃ごとに差し替える")]
        private GameObject _hitEffectPrefab;

        [SerializeField, Tooltip("ヒットエフェクトの大きさ倍率"), Min(0.01f)]
        private float _hitEffectScale = 1f;

        [SerializeField, Tooltip("当たった位置からのずらし(ワールド座標)")]
        private Vector3 _hitEffectOffset = Vector3.zero;

        [SerializeField, Tooltip("エフェクトを消すまでの秒数。0以下なら自動で消さない")]
        private float _hitEffectLifeSec = 2f;

        [SerializeField, Tooltip("攻撃した瞬間に出すエフェクト(空振りでも出る)。不要なら未設定でよい")]
        private GameObject _swingEffectPrefab;

        [SerializeField, Tooltip("攻撃エフェクトを出す位置(プレイヤーからの相対)")]
        private Vector3 _swingEffectOffset = new Vector3(0f, 0.7f, 1f);

        // ---- ダメージ表示 --------------------------------

        [Header("ダメージ表示")]
        [SerializeField, Tooltip("当たった位置に出すダメージ数字のプレハブ。未設定なら数字を出さない")]
        private GameObject _damagePopupPrefab;

        [SerializeField, Tooltip("数字を出す位置のずらし(ワールド座標)")]
        private Vector3 _damagePopupOffset = new Vector3(0f, 0.3f, 0f);

        // ---- 当たり判定 ----------------------------------

        [Header("当たり判定(球)")]
        [SerializeField, Tooltip("判定の球の半径(m)"), Min(0.01f)]
        private float _hitRadius = 1f;

        [SerializeField, Tooltip("判定の球の中心(プレイヤーからの相対)")]
        private Vector3 _hitOffset = new Vector3(0f, 0.6f, 1f);

        // ---- タイミング ----------------------------------

        [Header("タイミング(秒)")]
        [SerializeField, Tooltip("攻撃してから判定が出るまでの時間"), Min(0f)]
        private float _hitStartSec = 0.08f;

        [SerializeField, Tooltip("判定が出ている時間"), Min(0.01f)]
        private float _hitDurationSec = 0.15f;

        [SerializeField, Tooltip("次に攻撃できるようになるまでの時間"), Min(0f)]
        private float _cooldownSec = 0.6f;

        // ---- 当てる相手のしぼりこみ ----------------------

        [Header("当てる相手")]
        [SerializeField, Tooltip("判定を取るレイヤー")]
        private LayerMask _targetLayers = ~0;

        [SerializeField, Tooltip("HitTarget が付いた相手だけに当てる")]
        private bool _requireHitTarget = true;

        [SerializeField, Tooltip("このタグの相手だけに当てる。空なら制限なし")]
        private string[] _targetTags = new string[0];

        [SerializeField, Tooltip("このID(HitTarget側で設定)の相手だけに当てる。空なら制限なし")]
        private string[] _targetIds = new string[0];

        [SerializeField, Tooltip("1回の攻撃で当てられる最大数"), Min(1)]
        private int _maxHitCount = 8;

        // ---- デバッグ ------------------------------------

        [Header("デバッグ")]
        [SerializeField, Tooltip("シーンビューに判定の球を表示する")]
        private bool _drawGizmo = true;

        [SerializeField, Tooltip("判定の球の色")]
        private Color _gizmoColor = new Color(1f, 0.35f, 0.2f, 0.35f);

        // ---- 公開API -------------------------------------

        /// <summary>ログなどに出す攻撃の名前</summary>
        public string DisplayName => _displayName;

        /// <summary>この攻撃1回ぶんのダメージ</summary>
        public int AttackPower => _attackPower;

        /// <summary>当たったときに出すヒットエフェクト</summary>
        public GameObject HitEffectPrefab => _hitEffectPrefab;

        /// <summary>ヒットエフェクトの大きさ倍率</summary>
        public float HitEffectScale => _hitEffectScale;

        /// <summary>当たった位置からのずらし</summary>
        public Vector3 HitEffectOffset => _hitEffectOffset;

        /// <summary>エフェクトを消すまでの秒数</summary>
        public float HitEffectLifeSec => _hitEffectLifeSec;

        /// <summary>攻撃した瞬間に出すエフェクト</summary>
        public GameObject SwingEffectPrefab => _swingEffectPrefab;

        /// <summary>攻撃エフェクトを出す位置(プレイヤーからの相対)</summary>
        public Vector3 SwingEffectOffset => _swingEffectOffset;

        /// <summary>ダメージ数字のプレハブ</summary>
        public GameObject DamagePopupPrefab => _damagePopupPrefab;

        /// <summary>数字を出す位置のずらし</summary>
        public Vector3 DamagePopupOffset => _damagePopupOffset;

        /// <summary>判定の球の半径(m)</summary>
        public float HitRadius => _hitRadius;

        /// <summary>判定の球の中心(プレイヤーからの相対)</summary>
        public Vector3 HitOffset => _hitOffset;

        /// <summary>攻撃してから判定が出るまでの時間(秒)</summary>
        public float HitStartSec => _hitStartSec;

        /// <summary>判定が出ている時間(秒)</summary>
        public float HitDurationSec => _hitDurationSec;

        /// <summary>次に攻撃できるまでの時間(秒)</summary>
        public float CooldownSec => _cooldownSec;

        /// <summary>判定を取るレイヤー</summary>
        public LayerMask TargetLayers => _targetLayers;

        /// <summary>1回の攻撃で当てられる最大数</summary>
        public int MaxHitCount => _maxHitCount;

        /// <summary>シーンビューに判定の球を表示するか</summary>
        public bool DrawGizmo => _drawGizmo;

        /// <summary>判定の球の色</summary>
        public Color GizmoColor => _gizmoColor;

        /// <summary>この相手に当ててよいかどうかを判定する</summary>
        public bool CanHit(HitTarget target, GameObject targetObject)
        {
            if (_requireHitTarget && target == null) return false;
            if (target != null && !target.CanBeHit) return false;
            if (targetObject == null) return false;

            if (!IsEmpty(_targetTags))
            {
                bool matched = false;
                for (int i = 0; i < _targetTags.Length; i++)
                {
                    if (string.IsNullOrEmpty(_targetTags[i])) continue;
                    if (targetObject.CompareTag(_targetTags[i])) { matched = true; break; }
                }
                if (!matched) return false;
            }

            if (!IsEmpty(_targetIds))
            {
                string id = target != null ? target.TargetId : string.Empty;
                bool matched = false;
                for (int i = 0; i < _targetIds.Length; i++)
                {
                    if (string.IsNullOrEmpty(_targetIds[i])) continue;
                    if (_targetIds[i] == id) { matched = true; break; }
                }
                if (!matched) return false;
            }

            return true;
        }

        // ---- 内部処理 ------------------------------------

        private static bool IsEmpty(string[] values)
        {
            if (values == null || values.Length == 0) return true;
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrEmpty(values[i])) return false;
            }
            return true;
        }
    }
}
