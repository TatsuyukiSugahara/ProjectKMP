using ProjectKMP.Monster;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// 「いま撃てば連携になる」ことを見せる合図。
    /// 誰かがボスに当てたあとの受付時間だけ、ボスの足元にリングを出し、画面の端をうっすら光らせる。
    ///
    /// 合図が無いと、連携ボーナスは当たったあとに音と絵が出るだけになり、
    /// 狙って合わせる遊びにならない。ここが「協力する理由」を作る部分。
    ///
    /// 判定は ComboBonus.IsActive をそのまま見る(自分が当てればボーナスが乗る状態か)。
    /// ボスは位置を読むだけで、スクリプトには触らない。
    /// </summary>
    public class ComboSignal : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [Header("足元のリング")]
        [SerializeField, Tooltip("ボスの足元に出すリング。普段は無効にしておく")]
        private GameObject _ring;

        [SerializeField, Tooltip("リングを合わせる相手。未設定ならシーンの BossHealth から探す")]
        private Transform _followTarget;

        [SerializeField, Tooltip("地面に埋まらないよう少し浮かせる高さ(メートル)")]
        private float _ringHeight = 0.05f;

        [SerializeField, Tooltip("リングの大きさ(直径・メートル)")]
        private float _ringDiameter = 6.0f;

        [SerializeField, Min(0.0f), Tooltip("リングの脈打ち幅。0で脈打たない")]
        private float _pulseScale = 0.08f;

        [SerializeField, Min(0.1f), Tooltip("1秒あたりの脈打ち回数")]
        private float _pulseSpeed = 2.5f;

        [Header("画面端の発光")]
        [SerializeField, Tooltip("画面全体に敷く発光の Image")]
        private Image _edgeGlow;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("発光のいちばん濃いときの不透明度")]
        private float _edgeGlowAlpha = 0.35f;

        [Header("連鎖ごとの色")]
        [SerializeField, Tooltip("連鎖の段が上がるごとに使う色。左から順に対応する")]
        private Color[] _chainColors = new Color[]
        {
            new Color(0.35f, 0.85f, 1.0f),
            new Color(1.0f, 0.85f, 0.30f),
            new Color(1.0f, 0.45f, 0.35f),
        };

        [Header("出入り")]
        [SerializeField, Min(0.05f), Tooltip("出るまで・消えるまでの速さ。大きいほど機敏")]
        private float _fadeSpeed = 8.0f;

        // ---- 内部状態 ------------------------------------

        private Image _ringImage;
        private float _visibility;

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            if (_ring != null) _ringImage = _ring.GetComponentInChildren<Image>(true);

            SetVisibility(0.0f);
        }

        private void Start()
        {
            if (_followTarget != null) return;

            BossHealth boss = FindAnyObjectByType<BossHealth>(FindObjectsInactive.Include);
            if (boss != null) _followTarget = boss.transform;
        }

        private void LateUpdate()
        {
            // 受付中かどうかは毎フレーム変わるので、目標へなめらかに寄せる
            float target = ComboBonus.IsActive ? 1.0f : 0.0f;
            _visibility = Mathf.MoveTowards(_visibility, target, _fadeSpeed * Time.unscaledDeltaTime);

            SetVisibility(_visibility);

            if (_visibility <= 0.001f) return;

            FollowTarget();
        }

        // ---- 内部処理 ------------------------------------

        private void FollowTarget()
        {
            if (_ring == null || _followTarget == null) return;

            Vector3 position = _followTarget.position;
            position.y += _ringHeight;
            _ring.transform.position = position;

            // 脈打たせると「時間制限がある」ことが伝わりやすい
            float pulse = _pulseScale <= 0.0f
                ? 1.0f
                : 1.0f + Mathf.Sin(Time.unscaledTime * _pulseSpeed * Mathf.PI * 2.0f) * _pulseScale;

            float diameter = _ringDiameter * pulse * Mathf.Lerp(0.7f, 1.0f, _visibility);
            _ring.transform.localScale = new Vector3(diameter, diameter, diameter);
        }

        private void SetVisibility(float visibility)
        {
            Color color = ResolveChainColor();

            if (_ring != null)
            {
                bool show = visibility > 0.001f;
                if (_ring.activeSelf != show) _ring.SetActive(show);

                if (_ringImage != null)
                {
                    color.a = visibility;
                    _ringImage.color = color;
                }
            }

            if (_edgeGlow == null) return;

            Color glow = color;
            glow.a = visibility * _edgeGlowAlpha;
            _edgeGlow.color = glow;
            _edgeGlow.enabled = glow.a > 0.001f;
        }

        /// <summary>連鎖が進むほど色を変えて、倍率が上がっていることを見せる</summary>
        private Color ResolveChainColor()
        {
            if (_chainColors == null || _chainColors.Length == 0) return Color.white;

            return _chainColors[Mathf.Clamp(ComboBonus.ChainStep, 0, _chainColors.Length - 1)];
        }
    }
}
