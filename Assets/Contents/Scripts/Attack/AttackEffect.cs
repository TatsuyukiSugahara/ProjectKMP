using UnityEngine;
using ProjectKMP.Player;

namespace ProjectKMP.Attack
{
    /// <summary>
    /// 攻撃エフェクトの生成をまとめた入れ物。BiteVfx のような再生が必要なものは自動で再生する。
    /// </summary>
    public static class AttackEffect
    {
        /// <summary>エフェクトを1つ出す。prefab が null なら何もしない</summary>
        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float scale, float lifeSec)
        {
            if (prefab == null) return null;

            GameObject instance = Object.Instantiate(prefab, position, rotation);

            if (!Mathf.Approximately(scale, 1f))
            {
                instance.transform.localScale *= scale;
            }

            // 噛みつきエフェクトは再生を明示的に始める必要がある
            BiteVfx bite = instance.GetComponent<BiteVfx>();
            if (bite != null) bite.Play();

            if (lifeSec > 0f) Object.Destroy(instance, lifeSec);

            return instance;
        }
    }
}
