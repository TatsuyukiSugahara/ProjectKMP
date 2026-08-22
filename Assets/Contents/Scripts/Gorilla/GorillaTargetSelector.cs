using System.Collections.Generic;
using ProjectKMP.Player;
using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// ゴリラが誰を狙うかを決める。
    ///
    /// 一番近い人だけを追うと、全員が同じくらいの距離にいるときにターゲットがふらついて
    /// 誰も攻撃されない膠着状態になる。そこで「近さ」に加えて「与えてきたダメージ(ヘイト)」を
    /// 足して評価し、殴ってきた人ほど狙われるようにする。役割分担が生まれ、
    /// 攻撃役が狙われている隙に別の人が回り込む、という遊び方につながる。
    ///
    /// 決めるのは MasterClient だけ。GorillaAI が HasAuthority のときだけ呼び出す。
    /// </summary>
    public class GorillaTargetSelector : MonoBehaviour
    {
        // ---- インスペクタ設定 ------------------------------

        [Header("狙う相手の決め方")]
        [SerializeField, Min(0.1f), Tooltip("狙う相手を選び直す間隔(秒)。短いほどコロコロ変わる")]
        private float _retargetIntervalSec = 3.0f;

        [SerializeField, Min(0.0f), Tooltip("近さの重み。1メートル近いごとに加算される点数")]
        private float _distanceWeight = 1.0f;

        [SerializeField, Min(0.0f), Tooltip("ヘイト(与えてきたダメージ)の重み。ダメージ1あたりの点数")]
        private float _hateWeight = 0.08f;

        [SerializeField, Min(0.0f), Tooltip("いま狙っている相手への加点。これが大きいほど途中で標的が変わりにくい")]
        private float _stickyBonus = 10.0f;

        [SerializeField, Min(0.1f), Tooltip("ヘイトが半分になるまでの時間(秒)。殴るのをやめれば狙われにくくなる")]
        private float _hateHalfLifeSec = 12.0f;

        [SerializeField, Min(1.0f), Tooltip("この距離より遠い相手は候補から外す(メートル)")]
        private float _maxTargetDistance = 40.0f;

        [Header("動作確認")]
        [SerializeField, Tooltip("狙う相手が変わったときにコンソールへ出す")]
        private bool _logRetarget = false;

        // ---- 内部状態 ------------------------------------

        /// <summary>ActorNumber ごとのヘイト。時間で減っていく</summary>
        private readonly Dictionary<int, float> _hateByActor = new Dictionary<int, float>();

        /// <summary>候補になるプレイヤー。増減があるので定期的に取り直す</summary>
        private readonly List<PlayerHealth> _players = new List<PlayerHealth>();

        private float _retargetTimer;
        private float _refreshTimer;

        /// <summary>プレイヤー一覧を取り直す間隔(秒)。参加・離脱に追従するためのもの</summary>
        private const float PLAYER_REFRESH_INTERVAL_SEC = 2.0f;

        // ---- 公開API -------------------------------------

        /// <summary>
        /// 攻撃を受けたことを伝え、その相手のヘイトを上げる。
        /// GorillaAI が HitTarget の通知から呼ぶ。
        /// </summary>
        public void AddHate(int actorNumber, int damage)
        {
            if (actorNumber < 0 || damage <= 0) return;

            _hateByActor.TryGetValue(actorNumber, out float current);
            _hateByActor[actorNumber] = current + damage;
        }

        /// <summary>
        /// いま狙うべき相手を返す。まだ選び直す時間でなければ current をそのまま返す。
        /// 候補がいなければ null。
        /// </summary>
        public Transform Evaluate(Vector3 fromPosition, Transform current, float deltaTime)
        {
            DecayHate(deltaTime);
            RefreshPlayers(deltaTime);

            // 今の相手が倒れた/居なくなったときは、間隔を待たずに選び直す
            bool currentIsGone = current == null || !IsAlive(current);

            _retargetTimer -= deltaTime;
            if (_retargetTimer > 0.0f && !currentIsGone) return current;
            _retargetTimer = _retargetIntervalSec;

            Transform best = PickBest(fromPosition, currentIsGone ? null : current);

            if (_logRetarget && best != current)
            {
                Debug.Log($"[Gorilla] 狙う相手を変更: {(current == null ? "なし" : current.name)} → {(best == null ? "なし" : best.name)}", this);
            }

            return best;
        }

        /// <summary>ヘイトをすべて忘れる。戦い直しのときに呼ぶ</summary>
        public void ResetHate()
        {
            _hateByActor.Clear();
        }

        // ---- 内部処理 ------------------------------------

        /// <summary>点数が一番高い相手を選ぶ。近いほど、ヘイトが高いほど高得点</summary>
        private Transform PickBest(Vector3 fromPosition, Transform current)
        {
            Transform best = null;
            float bestScore = float.MinValue;

            foreach (var player in _players)
            {
                if (player == null || player.IsDead) continue;

                float distance = Vector3.Distance(fromPosition, player.transform.position);
                if (distance > _maxTargetDistance) continue;

                _hateByActor.TryGetValue(player.OwnerActorNumber, out float hate);

                // 近さは「距離が小さいほど高得点」になるよう符号を反転して足す
                float score = -distance * _distanceWeight + hate * _hateWeight;
                if (player.transform == current) score += _stickyBonus;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = player.transform;
                }
            }

            return best;
        }

        /// <summary>ヘイトを時間で減らす。半減期で指数的に落とすので、殴るのをやめれば自然に外れる</summary>
        private void DecayHate(float deltaTime)
        {
            if (_hateByActor.Count == 0 || _hateHalfLifeSec <= 0.0f) return;

            float rate = Mathf.Pow(0.5f, deltaTime / _hateHalfLifeSec);

            // 列挙しながら書き換えられないので、キーを控えてから更新する
            var keys = new List<int>(_hateByActor.Keys);
            foreach (int key in keys)
            {
                float value = _hateByActor[key] * rate;
                if (value < 0.5f) _hateByActor.Remove(key);
                else _hateByActor[key] = value;
            }
        }

        /// <summary>プレイヤー一覧を取り直す。毎フレーム探すと重いので間隔を空ける</summary>
        private void RefreshPlayers(float deltaTime)
        {
            _refreshTimer -= deltaTime;
            if (_refreshTimer > 0.0f && _players.Count > 0) return;
            _refreshTimer = PLAYER_REFRESH_INTERVAL_SEC;

            _players.Clear();
            _players.AddRange(FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None));
        }

        /// <summary>その Transform が、まだ生きているプレイヤーのものか</summary>
        private bool IsAlive(Transform target)
        {
            foreach (var player in _players)
            {
                if (player == null || player.transform != target) continue;
                return !player.IsDead;
            }
            return false;
        }
    }
}
