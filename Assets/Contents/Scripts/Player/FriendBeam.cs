using System.Collections.Generic;
using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 友達ビーム(何人かで合わせて撃つと強くなる)の成立を見張る。
    ///
    /// 誰かが狙いに入ると『合わせろ』の合図が立ち、受付時間のうちに別の人も撃てば成立する。
    /// 判定の材料(狙いに入った・撃った)はもともと全員へ届いているので、
    /// 各クライアントが同じ結論を独立に出せる。合体そのものに通信は要らない。
    ///
    /// 1人目は通常のビームで撃ち始め、2人目が撃った時点で両方が強化される。
    /// 『あとから合わさる』形にしたのは、合わせ損ねても普通に撃てて損をしないようにするため。
    /// </summary>
    public static class FriendBeam
    {
        // ---- 定数 ----------------------------------------

        /// <summary>誰かが撃ってから、あとの人が合わせられる時間(秒)</summary>
        public const float JOIN_WINDOW_SEC = 1.5f;

        /// <summary>合わせられる最大人数。これ以上は倍率を伸ばさない</summary>
        public const int MAX_MEMBERS = 4;

        /// <summary>同じ合体で何度も合図を出さないための間隔(秒)</summary>
        private const float ANNOUNCE_GUARD_SEC = 0.4f;

        // ---- 内部状態 ------------------------------------

        private struct Shot
        {
            public PlayerBeamSkill Skill;
            public float Time;
        }

        private static readonly List<Shot> _shots = new List<Shot>();
        private static readonly List<PlayerBeamSkill> _aiming = new List<PlayerBeamSkill>();
        private static readonly List<PlayerBeamSkill> _partners = new List<PlayerBeamSkill>();

        private static float _lastAnnounceTime = -999f;
        private static int _lastAnnounceMembers;

        // ---- 公開API -------------------------------------

        /// <summary>狙いに入ったことを知らせる。ここから合図が立つ</summary>
        public static void BeginAim(PlayerBeamSkill skill)
        {
            if (skill == null || _aiming.Contains(skill)) return;

            _aiming.Add(skill);
        }

        /// <summary>狙いを抜けた(撃った・やめた)ことを知らせる</summary>
        public static void EndAim(PlayerBeamSkill skill)
        {
            _aiming.Remove(skill);
        }

        /// <summary>
        /// 撃ったことを記録し、合わせられる相手を返す。
        /// 返るのは『受付時間のうちに撃って、まだ照射中の他の人』だけ。
        /// 返すリストは使い回しなので、次に呼ぶまでの間に読み切ること。
        /// </summary>
        public static IReadOnlyList<PlayerBeamSkill> RegisterShot(PlayerBeamSkill skill)
        {
            Prune();

            _partners.Clear();
            if (skill == null) return _partners;

            foreach (Shot shot in _shots)
            {
                if (shot.Skill == null || shot.Skill == skill) continue;

                _partners.Add(shot.Skill);
            }

            _shots.Add(new Shot { Skill = skill, Time = Time.unscaledTime });
            return _partners;
        }

        /// <summary>
        /// 合図を向ける相手を返す。呼びかけている本人には出さないので viewer は除く。
        /// 誰も呼びかけていなければ null。
        /// </summary>
        public static Transform GetCallTarget(PlayerBeamSkill viewer)
        {
            Prune();

            foreach (PlayerBeamSkill skill in _aiming)
            {
                if (skill == null || skill == viewer) continue;

                return skill.transform;
            }

            // 撃ったあとも受付が続く間は合図を残す。跳び上がっている最中に消えると追いつけない
            foreach (Shot shot in _shots)
            {
                if (shot.Skill == null || shot.Skill == viewer) continue;

                return shot.Skill.transform;
            }

            return null;
        }

        /// <summary>
        /// 合体の合図(カットインなど)を出してよいかを返す。
        /// 合体は同じ瞬間に何人ぶんも成立するので、重ねて出さないようここで間引く。
        /// ただし直後に人数が増えたときは、より派手な合図に出し直す。
        /// </summary>
        public static bool TryAnnounce(int members)
        {
            float now = Time.unscaledTime;

            if (now - _lastAnnounceTime < ANNOUNCE_GUARD_SEC && members <= _lastAnnounceMembers) return false;

            _lastAnnounceTime = now;
            _lastAnnounceMembers = members;
            return true;
        }

        /// <summary>覚えていることを全部忘れる</summary>
        public static void Clear()
        {
            _shots.Clear();
            _aiming.Clear();
            _partners.Clear();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>受付を過ぎたもの・撃ち終わったもの・消えたものを落とす</summary>
        private static void Prune()
        {
            float now = Time.unscaledTime;

            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                Shot shot = _shots[i];
                bool expired = now - shot.Time > JOIN_WINDOW_SEC;

                if (shot.Skill == null || expired || !shot.Skill.IsFiring) _shots.RemoveAt(i);
            }

            for (int i = _aiming.Count - 1; i >= 0; i--)
            {
                PlayerBeamSkill skill = _aiming[i];

                if (skill == null || !skill.IsBusy) _aiming.RemoveAt(i);
            }
        }
    }
}
