using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectKMP.Core
{
    /// <summary>
    /// 同じ物を作り直さず、使い終わったら返してもらって次へ回す。
    ///
    /// 砂埃や擬音のように何度も出る物は、そのたびに作って捨てると
    /// 片付けの処理が積み上がり、時々画面が引っかかる原因になる。
    /// 20人が走り回る場では、1秒あたり数百個の作り捨てになる。
    ///
    /// 借りる側は Rent と Return を呼ぶだけ。
    /// 足りなければ勝手に増えるので、数を見積もる必要はない。
    /// </summary>
    public class GameObjectPool
    {
        // ---- 内部状態 ------------------------------------

        private readonly Func<GameObject> _factory;
        private readonly Stack<GameObject> _idle = new Stack<GameObject>();

        // ---- 公開API -------------------------------------

        /// <summary>これまでに作った総数。使い回しが効いているかを見るために持つ</summary>
        public int CreatedCount { get; private set; }

        /// <summary>いま貸し出している数</summary>
        public int RentedCount { get; private set; }

        /// <summary>手元で待機している数</summary>
        public int IdleCount => _idle.Count;

        /// <summary>
        /// 作り方を渡して用意する。
        /// prewarm を入れておくと、最初の1回で引っかかるのを防げる。
        /// </summary>
        public GameObjectPool(Func<GameObject> factory, int prewarm = 0)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));

            for (int i = 0; i < prewarm; i++)
            {
                GameObject go = Create();
                go.SetActive(false);

                _idle.Push(go);
            }
        }

        /// <summary>1つ借りる。手元に無ければ作る</summary>
        public GameObject Rent()
        {
            GameObject go = null;

            // 場面の切り替えなどで消えていることがあるので、生きている物が出るまで捨てる
            while (_idle.Count > 0 && go == null) go = _idle.Pop();

            if (go == null) go = Create();

            go.SetActive(true);
            RentedCount++;

            return go;
        }

        /// <summary>使い終わった物を返す</summary>
        public void Return(GameObject go)
        {
            if (go == null) return;

            go.SetActive(false);
            _idle.Push(go);

            if (RentedCount > 0) RentedCount--;
        }

        /// <summary>手元の待機分を片付ける。場面を抜けるときに呼ぶ</summary>
        public void Clear()
        {
            while (_idle.Count > 0)
            {
                GameObject go = _idle.Pop();
                if (go == null) continue;

                UnityEngine.Object.Destroy(go);
            }
        }

        // ---- 内部処理 ------------------------------------

        private GameObject Create()
        {
            CreatedCount++;

            return _factory();
        }
    }
}
