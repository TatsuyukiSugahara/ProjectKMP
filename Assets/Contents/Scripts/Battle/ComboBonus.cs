using System;
using System.Collections.Generic;
using Photon.Pun;
using ProjectKMP.Attack;
using ProjectKMP.Monster;
using ProjectKMP.UI;
using R3;
using UnityEngine;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// 短い時間のうちに複数のプレイヤーがボスへ当てたとき、ダメージに倍率をかける同時ヒットボーナス。
    /// ひとりで殴り続けるより、タイミングを合わせたほうが強くなる。
    ///
    /// ヒットの通知(HitTarget.Hit)は全クライアントで流れるので、誰がいつ当てたかは各自が同じ情報を持てる。
    /// 倍率は攻撃した本人のクライアントで掛け、掛けたあとのダメージをRPCで配るため、
    /// 全員が同じ数字を見ることになる(追加の通信は不要)。
    ///
    /// ボス側のスクリプトには手を入れず、ヒットの通知を外から見ているだけ。
    /// </summary>
    public class ComboBonus : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [SerializeField, Min(0.05f), Tooltip("他の人のヒットからこの秒数以内に当てるとボーナス")]
        private float _windowSec = 1.5f;

        [SerializeField, Tooltip("連携が続くほど上がる倍率。左から順に上がり、いちばん右で頭打ち")]
        private float[] _chainMultipliers = new float[] { 1.5f, 2.0f, 2.5f };

        [SerializeField, Min(0.0f), Tooltip("連鎖が1段上がるまでの最短間隔(秒)。持続ダメージで一気に上がるのを防ぐ")]
        private float _chainStepIntervalSec = 0.3f;

        [SerializeField, Tooltip("見張るボスの HitTarget。未設定ならシーンの BossHealth から探す")]
        private HitTarget _hitTarget;

        [Header("演出")]
        [SerializeField, Tooltip("ボーナスが成立したときの音")]
        private AudioClip _comboClip;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("音量")]
        private float _comboVolume = 0.7f;


        [SerializeField, Min(0.0f), Tooltip("音が鳴る最短間隔(秒)。当たるたびに鳴るとうるさいので間引く")]
        private float _feedbackIntervalSec = 0.6f;

        [SerializeField, Tooltip("ボーナスの成立をコンソールに出す")]
        private bool _logCombo;

        [Header("動作確認")]
        [SerializeField, Tooltip("ひとりでも必ずボーナスが出るようにする。見た目の確認用なので、確認が済んだら必ず切ること")]
        private bool _debugAlwaysCombo;

        // ---- 内部状態 ------------------------------------

        private static ComboBonus INSTANCE;

        /// <summary>誰がいつ最後に当てたか。ActorNumber をキーにする</summary>
        private readonly Dictionary<int, float> _lastHitTime = new Dictionary<int, float>();

        private IDisposable _hitSubscription;
        private float _nextFeedbackTime;
        private int _chainStep;
        private int _lastChainActor = int.MinValue;
        private float _lastChainTime;

        // ---- 公開API -------------------------------------

        /// <summary>いまボーナスが乗る状態か(合図の表示などに使える)</summary>
        public static bool IsActive =>
            INSTANCE != null && INSTANCE.HasRecentHitFromOthers(LocalActorNumber);

        /// <summary>いま乗る倍率。表示に使う</summary>
        public static float CurrentMultiplier => INSTANCE != null ? INSTANCE.ResolveMultiplier() : 1.0f;

        /// <summary>連鎖の段数。0が1段目</summary>
        public static int ChainStep => INSTANCE != null ? INSTANCE._chainStep : 0;

        /// <summary>
        /// 攻撃を送り出す直前に通す。他の人が直前に当てていれば倍率を掛けたダメージを返す。
        /// 置かれていない(このシーンで使わない)ときは、そのままの値を返す。
        /// </summary>
        public static int Apply(int damage)
        {
            if (damage <= 0 || INSTANCE == null) return damage;
            if (!INSTANCE.HasRecentHitFromOthers(LocalActorNumber)) return damage;

            return Mathf.Max(damage, Mathf.RoundToInt(damage * INSTANCE.ResolveMultiplier()));
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            INSTANCE = this;
        }

        private void Start()
        {
            if (_hitTarget == null)
            {
                BossHealth boss = FindAnyObjectByType<BossHealth>(FindObjectsInactive.Include);
                if (boss != null) _hitTarget = boss.GetComponent<HitTarget>();
            }

            if (_hitTarget == null)
            {
                Debug.LogWarning("[Battle] ボスの HitTarget が見つからないため、同時ヒットボーナスは動きません", this);
                return;
            }

            _hitSubscription = _hitTarget.Hit.Subscribe(info => OnHit(info));

            // 切り忘れると、ひとりで当てただけでボーナスが乗り続けてしまう
            if (_debugAlwaysCombo)
            {
                Debug.LogWarning("[Battle] 同時ヒットボーナスが確認用の常時ONになっています", this);
            }
        }

        /// <summary>誰も当てない時間が続いたら連鎖を切る</summary>
        private void Update()
        {
            if (_chainStep <= 0) return;
            if (HasAnyRecentHit()) return;

            _chainStep = 0;
            _lastChainActor = int.MinValue;
        }

        private void OnDestroy()
        {
            _hitSubscription?.Dispose();
            if (INSTANCE == this) INSTANCE = null;
        }

        // ---- 内部処理 ------------------------------------

        private static int LocalActorNumber =>
            PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;

        /// <summary>ヒットは全クライアントで流れるので、どの画面でも同じ履歴になる</summary>
        private void OnHit(HitTarget.HitInfo info)
        {
            // モンスターなど、プレイヤー以外が出したダメージは数えない
            if (info.AttackerActorNumber < 0) return;

            bool isCombo = HasRecentHitFromOthers(info.AttackerActorNumber);
            _lastHitTime[info.AttackerActorNumber] = Time.unscaledTime;

            if (!isCombo) return;

            AdvanceChain(info.AttackerActorNumber);
            if (Time.unscaledTime < _nextFeedbackTime) return;

            _nextFeedbackTime = Time.unscaledTime + _feedbackIntervalSec;

            if (_comboClip != null && UiSoundPlayer.Instance != null)
            {
                UiSoundPlayer.Instance.PlayOneShot(_comboClip, _comboVolume);
            }


            if (_logCombo) Debug.Log("[Battle] 同時ヒットボーナス", this);
        }

        /// <summary>
        /// 連携が続くほど倍率を上げる。ビームや元気玉は短い間隔で当たり続けるため、
        /// 「直前とは別の人が当てた」かつ「一定の間隔が空いた」ときだけ上げる。
        /// そうしないと2人が撃ち合った瞬間に頭打ちまで到達してしまう。
        /// </summary>
        private void AdvanceChain(int actorNumber)
        {
            if (_chainMultipliers == null || _chainMultipliers.Length == 0) return;
            if (_chainStep >= _chainMultipliers.Length - 1) return;
            if (actorNumber == _lastChainActor) return;
            if (Time.unscaledTime - _lastChainTime < _chainStepIntervalSec) return;

            _chainStep++;
            _lastChainActor = actorNumber;
            _lastChainTime = Time.unscaledTime;
        }

        private float ResolveMultiplier()
        {
            if (_chainMultipliers == null || _chainMultipliers.Length == 0) return 1.0f;

            return _chainMultipliers[Mathf.Clamp(_chainStep, 0, _chainMultipliers.Length - 1)];
        }

        /// <summary>誰かが直近に当てているか。連鎖を切るかどうかの判定に使う</summary>
        private bool HasAnyRecentHit()
        {
            float now = Time.unscaledTime;
            foreach (KeyValuePair<int, float> pair in _lastHitTime)
            {
                if (now - pair.Value <= _windowSec) return true;
            }

            return false;
        }

        /// <summary>自分以外の誰かが、直近この秒数のうちに当てているか</summary>
        private bool HasRecentHitFromOthers(int actorNumber)
        {
            // 確認用。ひとりで遊んでいても常に成立させる
            if (_debugAlwaysCombo) return true;

            // ヒットストップで時間が止まる場面があるため、実時間で測る
            float now = Time.unscaledTime;

            foreach (KeyValuePair<int, float> pair in _lastHitTime)
            {
                if (pair.Key == actorNumber) continue;
                if (now - pair.Value <= _windowSec) return true;
            }

            return false;
        }
    }
}
