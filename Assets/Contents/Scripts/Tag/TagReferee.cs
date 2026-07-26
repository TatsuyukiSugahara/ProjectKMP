using Photon.Pun;
using ProjectKMP.Battle;
using UnityEngine;

namespace ProjectKMP.Tag
{
    /// <summary>
    /// 鬼と逃げる側の接触を判定して鬼を交代させる。MasterClient でのみ動作する。
    /// 判定を1台に集約することで、両者が同時に相手をタッチしたと主張する状態を避ける。
    /// </summary>
    public class TagReferee : MonoBehaviour
    {
        // ---- 調整パラメータ ------------------------------
        [SerializeField] private float _tagRadius = 1.2f;

        // ---- Unityイベント -------------------------------

        private void Update()
        {
            if (!PhotonNetwork.IsMasterClient) return;
            if (!PhotonNetwork.InRoom) return;
            if (!BattleClock.IsRunning) return;

            TagPlayer oni = FindOni();
            if (oni == null)
            {
                // 鬼が退室した場合などに、鬼不在のまま進まないようにする
                TagState.ChooseRandomOni();
                return;
            }

            if (!TagState.IsTagReady()) return;

            TryTag(oni);
        }

        // ---- 内部処理 ------------------------------------

        private static TagPlayer FindOni()
        {
            int oniActorNumber = TagState.GetOniActorNumber();
            if (oniActorNumber < 0) return null;

            foreach (TagPlayer player in TagPlayer.All)
            {
                if (player != null && player.ActorNumber == oniActorNumber) return player;
            }

            return null;
        }

        private void TryTag(TagPlayer oni)
        {
            float sqrRadius = _tagRadius * _tagRadius;
            Vector3 oniPosition = oni.transform.position;

            foreach (TagPlayer player in TagPlayer.All)
            {
                if (player == null || player == oni) continue;

                if ((player.transform.position - oniPosition).sqrMagnitude > sqrRadius) continue;

                TagState.SetOni(player.ActorNumber);
                TagScore.AddOniCount(player.photonView.Owner);
                Debug.Log($"[Tag] 鬼交代 Actor{oni.ActorNumber} -> Actor{player.ActorNumber}");
                return;
            }
        }
    }
}
