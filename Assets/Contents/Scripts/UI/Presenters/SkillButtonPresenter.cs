using ProjectKMP.Core;
using R3;
using UnityEngine;

namespace ProjectKMP.UI.Presenters
{
    /// <summary>
    /// 状態と技のボタンをつなぐ。
    ///
    /// ボタンは渡された値で見た目を作るだけにしてある。
    /// どの技の値を渡すかをここで決めるので、同じボタンを別の用途にも使い回せる。
    ///
    /// つなぐ相手が変わっても、ボタンの側は触らなくてよい。
    /// </summary>
    public class SkillButtonPresenter : MonoBehaviour
    {
        // ---- 型 ------------------------------------------

        /// <summary>どの技の待ち時間を見せるか</summary>
        public enum Source
        {
            Beam,
            EnergyBall,
            Dive,
        }

        // ---- 設定 ----------------------------------------

        [SerializeField, Tooltip("値を渡す先のボタン。未設定なら同じ物から探す")]
        private SkillButton _button;

        [SerializeField, Tooltip("どの技の待ち時間を見せるか")]
        private Source _source = Source.Beam;

        // ---- 内部状態 ------------------------------------

        private System.IDisposable _subscription;

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            if (_button == null) _button = GetComponent<SkillButton>();
        }

        private void OnEnable()
        {
            if (_button == null) return;

            _subscription = ResolveSource().Subscribe(ratio => _button.SetCooldownRatio(ratio));
        }

        private void OnDisable()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private ReadOnlyReactiveProperty<float> ResolveSource()
        {
            PlayerStatus status = PlayerStatusHub.Local;

            switch (_source)
            {
                case Source.EnergyBall: return status.EnergyBallCooldown01;
                case Source.Dive: return status.DiveCooldown01;
                default: return status.BeamCooldown01;
            }
        }
    }
}
