using R3;

namespace ProjectKMP.Core
{
    /// <summary>
    /// 操作している人の状態。
    ///
    /// 画面はこれだけを見る。プレイヤーの中身を直接触らせないことで、
    /// 遊びの処理を作り変えても画面が壊れなくなる。
    ///
    /// 書き込めるのは持ち主だけ。読む側には変えられない形で渡す。
    /// 画面が状態を書き換えられると、どちらが正しいのか分からなくなるため。
    /// </summary>
    public class PlayerStatus
    {
        // ---- 書き込む側が持つ入れ物 ----------------------

        private readonly ReactiveProperty<int> _currentHp = new ReactiveProperty<int>(0);
        private readonly ReactiveProperty<int> _maxHp = new ReactiveProperty<int>(0);
        private readonly ReactiveProperty<bool> _isDead = new ReactiveProperty<bool>(false);

        private readonly ReactiveProperty<float> _beamCooldown = new ReactiveProperty<float>(0.0f);
        private readonly ReactiveProperty<float> _diveCooldown = new ReactiveProperty<float>(0.0f);
        private readonly ReactiveProperty<float> _energyBallCooldown = new ReactiveProperty<float>(0.0f);

        private readonly ReactiveProperty<bool> _isAimingBeam = new ReactiveProperty<bool>(false);

        private readonly ReactiveProperty<float> _attackCooldown = new ReactiveProperty<float>(0.0f);
        private readonly ReactiveProperty<bool> _isInJustWindow = new ReactiveProperty<bool>(false);

        private readonly ReactiveProperty<float> _respawnRemainingSec = new ReactiveProperty<float>(0.0f);
        private readonly ReactiveProperty<float> _respawnDelaySec = new ReactiveProperty<float>(0.0f);

        private readonly ReactiveProperty<UnityEngine.Transform> _lockTarget =
            new ReactiveProperty<UnityEngine.Transform>(null);

        private readonly ReactiveProperty<UnityEngine.Transform> _friendBeamCallTarget =
            new ReactiveProperty<UnityEngine.Transform>(null);

        // ---- 読む側へ渡すもの ----------------------------

        /// <summary>いまの体力</summary>
        public ReadOnlyReactiveProperty<int> CurrentHp => _currentHp;

        /// <summary>体力の上限</summary>
        public ReadOnlyReactiveProperty<int> MaxHp => _maxHp;

        /// <summary>倒れているか</summary>
        public ReadOnlyReactiveProperty<bool> IsDead => _isDead;

        /// <summary>ビームの待ち時間。0で使える、1で待ち始めたばかり</summary>
        public ReadOnlyReactiveProperty<float> BeamCooldown01 => _beamCooldown;

        /// <summary>通常攻撃の待ち時間</summary>
        public ReadOnlyReactiveProperty<float> AttackCooldown01 => _attackCooldown;

        /// <summary>押しどきの受付中か。ボタンの色を変えるのに使う</summary>
        public ReadOnlyReactiveProperty<bool> IsInJustWindow => _isInJustWindow;

        /// <summary>とびこみの待ち時間</summary>
        public ReadOnlyReactiveProperty<float> DiveCooldown01 => _diveCooldown;

        /// <summary>必殺技の待ち時間</summary>
        public ReadOnlyReactiveProperty<float> EnergyBallCooldown01 => _energyBallCooldown;

        /// <summary>ビームの狙いを付けているか。合体の合図に使う</summary>
        public ReadOnlyReactiveProperty<bool> IsAimingBeam => _isAimingBeam;

        /// <summary>復活までの残り秒数</summary>
        public ReadOnlyReactiveProperty<float> RespawnRemainingSec => _respawnRemainingSec;

        /// <summary>倒れてから復活するまでの長さ。数え始めの表示に使う</summary>
        public ReadOnlyReactiveProperty<float> RespawnDelaySec => _respawnDelaySec;

        /// <summary>
        /// ターゲットカメラで注目している相手。誰も見ていなければ空。
        ///
        /// 位置を数値で写し取ると、動く相手に追いつかない。
        /// 相手そのものを渡し、画面の側で位置を測る。
        /// Transform は Unity の型なので、これを持っても遊びの処理には縛られない。
        /// </summary>
        public ReadOnlyReactiveProperty<UnityEngine.Transform> LockTarget => _lockTarget;

        /// <summary>合体ビームで呼びかけている相手。呼びかけが無ければ空</summary>
        public ReadOnlyReactiveProperty<UnityEngine.Transform> FriendBeamCallTarget => _friendBeamCallTarget;

        /// <summary>体力の割合。0〜1。上限が0なら0を返す</summary>
        public float HpRatio01 => _maxHp.CurrentValue <= 0
            ? 0.0f
            : (float)_currentHp.CurrentValue / _maxHp.CurrentValue;

        // ---- 書き込み ------------------------------------

        /// <summary>体力を伝える。持ち主だけが呼ぶ</summary>
        public void SetHp(int current, int max)
        {
            _currentHp.Value = current;
            _maxHp.Value = max;
        }

        public void SetDead(bool dead)
        {
            _isDead.Value = dead;
        }

        public void SetBeamCooldown(float ratio01)
        {
            _beamCooldown.Value = ratio01;
        }

        public void SetAttack(float cooldown01, bool inJustWindow)
        {
            _attackCooldown.Value = cooldown01;
            _isInJustWindow.Value = inJustWindow;
        }

        public void SetDiveCooldown(float ratio01)
        {
            _diveCooldown.Value = ratio01;
        }

        public void SetEnergyBallCooldown(float ratio01)
        {
            _energyBallCooldown.Value = ratio01;
        }

        public void SetRespawn(float remainingSec, float delaySec)
        {
            _respawnRemainingSec.Value = remainingSec;
            _respawnDelaySec.Value = delaySec;
        }

        public void SetLockTarget(UnityEngine.Transform target)
        {
            _lockTarget.Value = target;
        }

        public void SetFriendBeamCallTarget(UnityEngine.Transform target)
        {
            _friendBeamCallTarget.Value = target;
        }

        public void SetAimingBeam(bool aiming)
        {
            _isAimingBeam.Value = aiming;
        }
    }
}
