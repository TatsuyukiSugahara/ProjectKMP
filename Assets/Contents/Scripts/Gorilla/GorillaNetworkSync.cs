using System;
using Photon.Pun;
using R3;
using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// ゴリラの移動と攻撃開始をオンラインで揃えるコンポーネント。
    ///
    /// 役割分担は BossHealth と同じ考え方で、
    ///   ・MasterClient … AI を動かし、その結果(位置・向き・ステート)を毎フレーム配る
    ///   ・ゲスト        … 配られた位置へ滑らかに寄せ、配られたステートをそのまま再生する
    /// とする。ゲストが自分で行き先や攻撃の種類を決めないので、全員の画面で
    /// 「同じ場所にいるゴリラが、同じタイミングで同じ攻撃を始める」状態になる。
    ///
    /// 攻撃の当たり判定は各ステート側が「自分が操作しているプレイヤーだけ」を対象に取るため、
    /// ステート開始さえ揃っていれば多重ダメージにはならない。
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    [RequireComponent(typeof(GorillaAI))]
    public class GorillaNetworkSync : MonoBehaviourPun
    {
        // ---- インスペクタ設定 ------------------------------

        [Header("参照")]
        [SerializeField, Tooltip("同期対象のゴリラAI。未設定なら同じ GameObject から探す")]
        private GorillaAI _ai;

        [SerializeField, Tooltip("位置・ステートの同期。未設定なら同じ GameObject から探す")]
        private GorillaSyncObject _sync;

        [Header("ゲスト側の補間")]
        [SerializeField, Min(0.0f), Tooltip("配られた位置へ寄せる速さ。大きいほど本家に忠実だが、通信のガタつきも見えやすくなる")]
        private float _positionLerpSpeed = 12.0f;

        [SerializeField, Min(0.0f), Tooltip("配られた向きへ回す速さ(度/秒)")]
        private float _rotationLerpSpeedDeg = 720.0f;

        [SerializeField, Min(0.0f), Tooltip("配られた位置がこの距離以上離れていたら、補間せず一気に飛ばす(ジャンプ攻撃や復帰時のズレ対策)")]
        private float _teleportDistance = 5.0f;

        [Header("動作確認")]
        [SerializeField, Tooltip("ステートを配った/受け取ったタイミングをコンソールに出す")]
        private bool _logStateChange = false;

        // ---- 内部状態 ------------------------------------

        private IDisposable _syncSubscription;

        /// <summary>ゲストが寄せていく目標の位置・向き</summary>
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;

        /// <summary>まだ一度も値を受け取っていない間は補間しない(原点へ吸い寄せられるのを防ぐ)</summary>
        private bool _hasReceived;

        /// <summary>最後に再生したステートの通し番号。同じ番号を二重に再生しないための目印</summary>
        private int _lastAppliedSequence = -1;

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            if (_ai == null) _ai = GetComponent<GorillaAI>();
            if (_sync == null) _sync = GetComponent<GorillaSyncObject>();

            if (_sync == null)
            {
                Debug.LogWarning("[Gorilla] GorillaSyncObject が無いため、ゴリラはこのクライアント限りで動きます", this);
            }
        }

        private void Start()
        {
            if (_sync == null) return;

            // 自分がマスターでも同じ経路を通すが、配った値をそのまま自分に適用し返さないよう
            // 受信側の処理でゲストだけに絞る
            _syncSubscription = _sync.Value.Subscribe(OnSyncValueChanged);
        }

        private void OnDestroy()
        {
            _syncSubscription?.Dispose();
            _syncSubscription = null;
        }

        /// <summary>
        /// 送受信は LateUpdate で行う。ステートの Update() が位置を書き換えた後に
        /// 「マスターは書き終わった結果を送る / ゲストは配られた結果で上書きする」という順番にするため。
        /// </summary>
        private void LateUpdate()
        {
            if (_sync == null || _ai == null) return;

            // 部屋に入っていないとき(ひとりでの動作確認、モデル確認シーンなど)は同期しない
            if (!GorillaAI.IsPhotonReady) return;

            if (_ai.HasAuthority)
            {
                SendCurrentState();
                return;
            }

            ApplyReceivedTransform();
        }

        // ---- 内部処理(MasterClient側) ---------------------

        /// <summary>いまの位置・向き・ステートを全員に配る</summary>
        private void SendCurrentState()
        {
            _sync.SetValue(data =>
            {
                data.Position = transform.position;
                data.YawDeg = transform.eulerAngles.y;
                data.State = _ai.CurrentStateKind;
                data.StateSequence = _ai.StateSequence;
                data.GrabbedActorNumber = _ai.GrabbedActorNumber;
            });
        }

        // ---- 内部処理(ゲスト側) ---------------------------

        /// <summary>配られた値を受け取る。ステートの切り替えだけはここで即座に反映する</summary>
        private void OnSyncValueChanged(GorillaSyncData data)
        {
            if (data == null) return;

            // 自分で配った値が自分に返ってきただけなので何もしない
            if (_ai == null || _ai.HasAuthority) return;

            // MasterClient がまだ一度も配っていない初期値。
            // ここで位置(0,0,0)を適用してしまうとゴリラが原点へ飛ぶので無視する
            if (data.State == GorillaStateKind.None) return;

            // 誰が掴まれているかは、ステートの切り替えとは別に毎回反映する。
            // 掴まれた本人はこれを見て自分のキャラを手の位置へ運ぶ
            _ai.ApplyNetworkGrabbedActorNumber(data.GrabbedActorNumber);

            _targetPosition = data.Position;
            _targetRotation = Quaternion.Euler(0.0f, data.YawDeg, 0.0f);

            // 受信の1回目は補間せず、その場に置く
            if (!_hasReceived)
            {
                _hasReceived = true;
                if (IsAiControllable) transform.SetPositionAndRotation(_targetPosition, _targetRotation);
            }

            if (data.StateSequence == _lastAppliedSequence) return;
            _lastAppliedSequence = data.StateSequence;

            if (_logStateChange) Debug.Log($"[Gorilla] ステート受信 {data.State} (seq {data.StateSequence})", this);

            _ai.ApplyNetworkState(data.State);
        }

        /// <summary>配られた位置・向きへ毎フレーム少しずつ寄せる</summary>
        private void ApplyReceivedTransform()
        {
            if (!_hasReceived || !IsAiControllable) return;

            float distance = Vector3.Distance(transform.position, _targetPosition);
            if (_teleportDistance > 0.0f && distance > _teleportDistance)
            {
                // ジャンプ攻撃の着地や、演出でワープしたときに引きずられ続けないよう一気に合わせる
                transform.SetPositionAndRotation(_targetPosition, _targetRotation);
                return;
            }

            transform.position = Vector3.Lerp(transform.position, _targetPosition, 1.0f - Mathf.Exp(-_positionLerpSpeed * Time.deltaTime));
            transform.rotation = Quaternion.RotateTowards(transform.rotation, _targetRotation, _rotationLerpSpeedDeg * Time.deltaTime);
        }

        // ---- 掴みからの脱出 ------------------------------

        /// <summary>
        /// 掴まれた本人(ゲスト)が「抜け出した」ことを MasterClient へ伝える。
        ///
        /// このプロジェクトでは状態の変更を SyncObject(マスター → 全員)で流すのが基本だが、
        /// この1件だけは向きが逆(ゲスト → マスター)で、SyncObject では表せない。
        /// 送るのは「抜け出したい」という合図だけで、実際に離すかどうかは
        /// 受け取った MasterClient が決めるため、ゲストがゲーム状態を直接触ることにはならない。
        /// </summary>
        public void SendGrabEscapeRequest()
        {
            if (photonView == null || !GorillaAI.IsPhotonReady) return;

            photonView.RPC(nameof(RpcRequestGrabEscape), RpcTarget.MasterClient);
        }

        [PunRPC]
        private void RpcRequestGrabEscape()
        {
            if (_ai == null) return;
            _ai.AcceptGrabEscapeRequest();
        }

        /// <summary>
        /// いま位置をこちらで書き換えてよいか。
        /// 開幕のカットシーン中は BattleIntroDirector が GorillaAI を無効にしたうえで
        /// 全クライアントが同じ軌道でゴリラを動かしているため、その間は手を出さない。
        /// </summary>
        private bool IsAiControllable => _ai != null && _ai.enabled;
    }
}
