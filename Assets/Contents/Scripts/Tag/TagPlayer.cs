using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace ProjectKMP.Tag
{
    /// <summary>
    /// 鬼ごっこのプレイヤー。頭上マーカーの出し分けと、鬼判定用の一覧登録を行う。
    /// 誰が鬼になるかの決定は MasterClient の TagReferee が行う。
    /// </summary>
    public class TagPlayer : MonoBehaviourPunCallbacks
    {
        // ---- 参照 ----------------------------------------
        [SerializeField] private GameObject _oniMarker;
        [SerializeField] private OniGlow _oniGlow;

        // ---- 見た目設定 ----------------------------------
        [SerializeField] private bool _useMarker = true;
        [SerializeField] private bool _useGlow = true;

        // ---- 内部状態 ------------------------------------
        private static readonly List<TagPlayer> _all = new List<TagPlayer>();

        // ---- 公開API -------------------------------------

        /// <summary>シーン上に存在する全プレイヤー</summary>
        public static IReadOnlyList<TagPlayer> All => _all;

        /// <summary>このキャラを操作しているクライアントの ActorNumber</summary>
        public int ActorNumber => photonView.Owner != null ? photonView.Owner.ActorNumber : -1;

        /// <summary>このキャラが今の鬼かどうか</summary>
        public bool IsOni => ActorNumber >= 0 && ActorNumber == TagState.GetOniActorNumber();

        // ---- Unityイベント -------------------------------

        public override void OnEnable()
        {
            base.OnEnable();
            if (!_all.Contains(this)) _all.Add(this);
            RefreshMarker();
        }

        public override void OnDisable()
        {
            base.OnDisable();
            _all.Remove(this);
        }

        // ---- Photon コールバック --------------------------

        public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            // 鬼が変わったときだけ見た目を更新すればよい
            if (propertiesThatChanged.ContainsKey(TagState.KEY_ONI_ACTOR)) RefreshMarker();
        }

        // ---- 内部処理 ------------------------------------

        private void RefreshMarker()
        {
            bool isOni = IsOni;

            if (_oniMarker != null) _oniMarker.SetActive(isOni && _useMarker);
            if (_oniGlow != null) _oniGlow.SetGlow(isOni && _useGlow);
        }
    }
}
