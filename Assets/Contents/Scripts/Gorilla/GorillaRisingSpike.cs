using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 地割れに沿って地面から突き上がる岩。
    ///
    /// 地面の下から生えてきて、しばらく残ってからまた沈んで消える。
    /// 出した側(地割れステート)は Play() を1行呼ぶだけでよく、
    /// ステートが終わった後もこの岩は残って、裂けた跡として画面に残る。
    ///
    /// 見た目だけの演出なので、エフェクトと同じく全クライアントで動く処理から出せば
    /// 追加の通信なしで全員の画面に出る(ネットワーク同期は不要)。
    /// </summary>
    public class GorillaRisingSpike : MonoBehaviour
    {
        /// <summary>沈んで消えるのにかける時間(秒)</summary>
        private const float SINK_SEC = 0.6f;

        private enum Phase
        {
            /// <summary>地面から生えてくる</summary>
            Rising,
            /// <summary>そのまま残っている</summary>
            Staying,
            /// <summary>沈んで消える</summary>
            Sinking,
        }

        private Phase _phase;
        private float _elapsedTime;

        private float _riseSec;
        private float _staySec;
        private float _height;

        private Vector3 _groundPosition;

        /// <summary>
        /// 突き上がる動きを始める。
        /// </summary>
        /// <param name="height">地面から出る高さ(メートル)</param>
        /// <param name="riseSec">生えきるまでの時間(秒)</param>
        /// <param name="lifetimeSec">生えてから沈み始めるまでの時間(秒)</param>
        public void Play(float height, float riseSec, float lifetimeSec)
        {
            _phase = Phase.Rising;
            _elapsedTime = 0.0f;
            _height = Mathf.Max(0.05f, height);
            _riseSec = Mathf.Max(0.01f, riseSec);
            _staySec = Mathf.Max(0.0f, lifetimeSec);
            _groundPosition = transform.position;

            // 最初は完全に地面の下に隠しておく
            transform.position = _groundPosition - Vector3.up * _height;
        }

        private void Update()
        {
            _elapsedTime += Time.deltaTime;

            switch (_phase)
            {
                case Phase.Rising:  UpdateRising();  break;
                case Phase.Staying: UpdateStaying(); break;
                case Phase.Sinking: UpdateSinking(); break;
            }
        }

        /// <summary>勢いよく突き上がって、少し行き過ぎてから収まる</summary>
        private void UpdateRising()
        {
            float rate = Mathf.Clamp01(_elapsedTime / _riseSec);

            // 行き過ぎてから戻る動き。地面を突き破った勢いに見せる
            float overshoot = Mathf.Sin(rate * Mathf.PI) * 0.18f;
            float eased = 1.0f - (1.0f - rate) * (1.0f - rate); // ease-out

            transform.position = _groundPosition + Vector3.up * (_height * (eased - 1.0f + overshoot));

            if (_elapsedTime < _riseSec) return;

            transform.position = _groundPosition;
            _phase = Phase.Staying;
            _elapsedTime = 0.0f;
        }

        private void UpdateStaying()
        {
            if (_elapsedTime < _staySec) return;

            _phase = Phase.Sinking;
            _elapsedTime = 0.0f;
        }

        /// <summary>地面へ沈めて消す。パッと消えると岩が嘘くさく見える</summary>
        private void UpdateSinking()
        {
            float rate = Mathf.Clamp01(_elapsedTime / SINK_SEC);
            transform.position = _groundPosition - Vector3.up * (_height * rate);

            if (rate < 1.0f) return;

            Destroy(gameObject);
        }
    }
}
