using System;
using System.Collections.Generic;
using Photon.Pun;
using ProjectKMP.Attack;
using ProjectKMP.Gorilla;
using ProjectKMP.Monster;
using ProjectKMP.Player;
using ProjectKMP.UI;
using ProjectKMP.UI.InGame;
using R3;
using UnityEngine;
using ProjectKMP.Presentation;

namespace ProjectKMP.Battle
{
    /// <summary>
    /// 全員の攻撃をひとつのゲージへ集め、満タン時に合体必殺を進行する。
    /// 判定は MasterClient、表示は全クライアントで行う。
    /// BossHealth が同じ GameObject へ自動追加するため、シーン側の設定は不要。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TeamPowerDirector : MonoBehaviourPun
    {
        private enum Phase { Charging, JoinWindow, Bursting }

        private const float REQUIRED_HP_RATIO = 0.38f;
        private const float UNIQUE_PLAYER_BONUS_RATIO = 0.0075f;
        // 2回目がフィニッシュになりやすい値。38%ぶん攻撃→18%必殺を2周すると決着する。
        private const float BURST_DAMAGE_RATIO = 0.18f;
        private const float JOIN_WINDOW_SEC = 2.2f;
        private const float BURST_RECOVER_SEC = 1.25f;
        private const int MIN_REQUIRED_POWER = 50;

        private static readonly Color POWER_COLOR = new Color(1.0f, 0.78f, 0.18f, 1.0f);

        private readonly HashSet<int> _contributors = new HashSet<int>();
        private readonly HashSet<int> _participants = new HashSet<int>();

        private BossHealth _boss;
        private HitTarget _hitTarget;
        private GorillaAI _gorilla;
        private TeamPowerHud _hud;
        private IDisposable _hitSubscription;

        private Phase _phase;
        private float _power;
        private float _requiredPower = MIN_REQUIRED_POWER;
        private double _phaseEndTime;
        private int _sequence;
        private bool _localJoined;

        public static TeamPowerDirector Active { get; private set; }

        public bool IsAcceptingJoin => _phase == Phase.JoinWindow;
        public bool HasLocalJoined => _localJoined;
        public float PowerRatio01 => Mathf.Clamp01(_power / Mathf.Max(1.0f, _requiredPower));

        /// <summary>木などの連鎖破壊をご褒美としてゲージへ加える。割合には連鎖側で上限を掛ける。</summary>
        public void AddDestructionPower(float requiredPowerRatio)
        {
            if (!HasAuthority || _phase != Phase.Charging || _boss == null || _boss.IsDefeated) return;
            if (requiredPowerRatio <= 0.0f) return;

            _power = Mathf.Min(_requiredPower, _power + _requiredPower * requiredPowerRatio);
            BroadcastPower();
            if (_power >= _requiredPower) BeginJoinWindow();
        }

        /// <summary>
        /// PlayerAttack が読み取った攻撃入力を、合体必殺の受付中だけこちらで消費する。
        /// すでに参加済みでも通常攻撃へ漏らさず、演出中の誤操作を防ぐ。
        /// </summary>
        public static bool TryConsumeJoinInput(bool pressed)
        {
            TeamPowerDirector active = Active;
            if (active == null || !active.IsAcceptingJoin) return false;

            if (pressed && !active._localJoined) active.RequestLocalJoin();
            return pressed;
        }

        private void Awake()
        {
            Active = this;
            _boss = GetComponent<BossHealth>();
            _hitTarget = GetComponent<HitTarget>();
            _gorilla = GetComponent<GorillaAI>();
        }

        private void Start()
        {
            if (_boss == null || _hitTarget == null)
            {
                Debug.LogWarning("[TeamPower] ボスのHPまたはHitTargetが無いため無効化します", this);
                enabled = false;
                return;
            }

            _requiredPower = Mathf.Max(MIN_REQUIRED_POWER, _boss.MaxHp * REQUIRED_HP_RATIO);
            _hitSubscription = _hitTarget.Hit.Subscribe(OnBossHit);
            _hud = TeamPowerHud.Ensure();
            _hud.SetPower(0.0f);
        }

        private void OnDestroy()
        {
            _hitSubscription?.Dispose();
            if (Active == this) Active = null;
        }

        private void Update()
        {
            if (_phase == Phase.JoinWindow && HasAuthority && NetworkTime >= _phaseEndTime)
            {
                FinishJoinWindow();
            }

            if (_phase == Phase.Bursting && NetworkTime >= _phaseEndTime)
            {
                _phase = Phase.Charging;
                _boss.SetTeamPowerLock(false);
                _hud?.HideEvent();
            }
        }

        private bool HasAuthority => !PhotonNetwork.IsConnected || PhotonNetwork.IsMasterClient;

        private static double NetworkTime => PhotonNetwork.IsConnected
            ? PhotonNetwork.Time
            : Time.unscaledTimeAsDouble;

        private void OnBossHit(HitTarget.HitInfo info)
        {
            if (!HasAuthority || _phase != Phase.Charging || _boss.IsDefeated) return;
            if (info.Damage <= 0 || info.AttackerActorNumber < 0) return;

            float gain = info.Damage;
            if (_contributors.Add(info.AttackerActorNumber))
            {
                gain += _requiredPower * UNIQUE_PLAYER_BONUS_RATIO;
            }

            _power = Mathf.Min(_requiredPower, _power + gain);
            BroadcastPower();

            if (_power >= _requiredPower) BeginJoinWindow();
        }

        private void BroadcastPower()
        {
            float ratio = PowerRatio01;
            if (PhotonNetwork.IsConnected)
            {
                photonView.RPC(nameof(RpcSetPower), RpcTarget.All, ratio);
            }
            else
            {
                RpcSetPower(ratio);
            }
        }

        [PunRPC]
        private void RpcSetPower(float ratio)
        {
            _power = Mathf.Clamp01(ratio) * _requiredPower;
            _hud ??= TeamPowerHud.Ensure();
            _hud.SetPower(ratio);
        }

        private void BeginJoinWindow()
        {
            if (!HasAuthority || _phase != Phase.Charging) return;

            _phase = Phase.JoinWindow;
            _sequence++;
            _participants.Clear();
            _contributors.Clear();
            _phaseEndTime = NetworkTime + JOIN_WINDOW_SEC;

            if (PhotonNetwork.IsConnected)
            {
                photonView.RPC(nameof(RpcBeginJoin), RpcTarget.All, _sequence, _phaseEndTime);
            }
            else
            {
                RpcBeginJoin(_sequence, _phaseEndTime);
            }
        }

        [PunRPC]
        private void RpcBeginJoin(int sequence, double endTime)
        {
            if (sequence < _sequence) return;

            _sequence = sequence;
            _phase = Phase.JoinWindow;
            _phaseEndTime = endTime;
            _localJoined = false;
            _participants.Clear();
            _boss.SetTeamPowerLock(true);
            _gorilla?.BeginTeamPowerStun((float)Math.Max(0.1, endTime - NetworkTime) + BURST_RECOVER_SEC);

            _hud ??= TeamPowerHud.Ensure();
            _hud.ShowJoin(0, GetPlayerCount());
            Onomatopoeia.Play(transform.position + Vector3.up * 3.2f, "パワーまんたん！", POWER_COLOR, 2.0f, 1.0f);
            ShockwaveRing.Play(transform.position, POWER_COLOR, 12.0f, 0.65f, 1.0f);
            ScreenFlash.Play(new Color(1.0f, 0.85f, 0.25f, 0.22f), 0.3f);
        }

        private void RequestLocalJoin()
        {
            if (_phase != Phase.JoinWindow || _localJoined) return;

            _localJoined = true;
            _hud?.SetLocalJoined(true);
            TeamPlayScore.AddLocalBurstJoin();

            int actorNumber = PhotonNetwork.LocalPlayer != null
                ? PhotonNetwork.LocalPlayer.ActorNumber
                : 1;

            if (PhotonNetwork.IsConnected)
            {
                photonView.RPC(nameof(RpcRequestJoin), RpcTarget.MasterClient, _sequence, actorNumber);
            }
            else
            {
                AcceptParticipant(_sequence, actorNumber);
            }
        }

        [PunRPC]
        private void RpcRequestJoin(int sequence, int claimedActorNumber, PhotonMessageInfo info)
        {
            if (!HasAuthority) return;
            int actorNumber = info.Sender != null ? info.Sender.ActorNumber : claimedActorNumber;
            AcceptParticipant(sequence, actorNumber);
        }

        private void AcceptParticipant(int sequence, int actorNumber)
        {
            if (_phase != Phase.JoinWindow || sequence != _sequence || actorNumber < 0) return;
            if (!_participants.Add(actorNumber)) return;

            if (PhotonNetwork.IsConnected)
            {
                photonView.RPC(nameof(RpcParticipantCount), RpcTarget.All, sequence, _participants.Count);
            }
            else
            {
                RpcParticipantCount(sequence, _participants.Count);
            }
        }

        [PunRPC]
        private void RpcParticipantCount(int sequence, int count)
        {
            if (sequence != _sequence || _phase != Phase.JoinWindow) return;
            _hud?.ShowJoin(count, GetPlayerCount());
        }

        private void FinishJoinWindow()
        {
            if (!HasAuthority || _phase != Phase.JoinWindow) return;

            int count = _participants.Count;
            bool isFinish = _boss.CurrentHp <= Mathf.CeilToInt(_boss.MaxHp * BURST_DAMAGE_RATIO);
            double recoverTime = NetworkTime + BURST_RECOVER_SEC;

            if (PhotonNetwork.IsConnected)
            {
                photonView.RPC(nameof(RpcFireBurst), RpcTarget.All, _sequence, count, isFinish, recoverTime);
            }
            else
            {
                RpcFireBurst(_sequence, count, isFinish, recoverTime);
            }
        }

        [PunRPC]
        private void RpcFireBurst(int sequence, int participantCount, bool isFinish, double recoverTime)
        {
            if (sequence != _sequence || _phase == Phase.Bursting) return;

            _phase = Phase.Bursting;
            _phaseEndTime = recoverTime;
            _power = 0.0f;
            _hud ??= TeamPowerHud.Ensure();
            _hud.SetPower(0.0f);
            _hud.PlayBurst(participantCount, isFinish);

            float scale = Mathf.Clamp01(participantCount / (float)Mathf.Max(1, GetPlayerCount()));
            float radius = Mathf.Lerp(12.0f, 20.0f, scale);
            Color color = isFinish ? new Color(1.0f, 0.45f, 0.16f, 1.0f) : POWER_COLOR;

            HitStop.Play(isFinish ? 0.18f : 0.11f, 0.04f, 0.22f);
            ImpactFrame.PlayWhite(isFinish ? 0.06f : 0.045f, isFinish ? 0.92f : 0.7f);
            ScreenFlash.Play(new Color(color.r, color.g, color.b, 0.42f), 0.5f);
            ShockwaveRing.Play(transform.position, color, radius, 0.75f, 1.4f);
            ShockwaveRing.Play(transform.position, Color.white, radius * 0.7f, 0.5f, 0.8f);
            Onomatopoeia.Play(transform.position + Vector3.up * 3.5f,
                isFinish ? "みんなで ドッカーン！" : "わんぱくバースト！",
                color, isFinish ? 2.8f : 2.2f, 1.1f);

            ThirdPersonCamera camera = FindAnyObjectByType<ThirdPersonCamera>();
            camera?.Shake(isFinish ? 0.55f : 0.35f, isFinish ? 0.8f : 0.55f);
            HitFlash.PlayWhite(transform, isFinish ? 0.35f : 0.2f);

            if (HasAuthority) _boss.ApplyTeamPowerDamage(BURST_DAMAGE_RATIO);
        }

        private int GetPlayerCount()
        {
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
            {
                return Mathf.Max(1, PhotonNetwork.CurrentRoom.PlayerCount);
            }
            return 1;
        }
    }
}
