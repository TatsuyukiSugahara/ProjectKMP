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

        [SerializeField, Min(1.0f), Tooltip("ボーナス中のダメージ倍率")]
        private float _multiplier = 1.5f;

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

        // ---- 公開API -------------------------------------

        /// <summary>いまボーナスが乗る状態か(UIの表示などに使える)</summary>
        public static bool IsActive =>
            INSTANCE != null && INSTANCE.HasRecentHitFromOthers(LocalActorNumber);

        /// <summary>
        /// 攻撃を送り出す直前に通す。他の人が直前に当てていれば倍率を掛けたダメージを返す。
        /// 置かれていない(このシーンで使わない)ときは、そのままの値を返す。
        /// </summary>
        public static int Apply(int damage)
        {
            if (damage <= 0 || INSTANCE == null) return damage;
            if (!INSTANCE.HasRecentHitFromOthers(LocalActorNumber)) return damage;

            return Mathf.Max(damage, Mathf.RoundToInt(damage * INSTANCE._multiplier));
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
            if (Time.unscaledTime < _nextFeedbackTime) return;

            _nextFeedbackTime = Time.unscaledTime + _feedbackIntervalSec;

            if (_comboClip != null && UiSoundPlayer.Instance != null)
            {
                UiSoundPlayer.Instance.PlayOneShot(_comboClip, _comboVolume);
            }


            if (_logCombo) Debug.Log("[Battle] 同時ヒットボーナス", this);
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
