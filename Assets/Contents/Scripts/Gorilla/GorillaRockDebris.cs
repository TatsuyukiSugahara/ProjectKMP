using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 砕けた岩のかけら。投げた岩が地面に当たった瞬間に何個か飛び散らせる。
    ///
    /// 1つ1つが自分で飛んで、跳ねて、消えるところまで面倒を見る。
    /// 出した側(岩投げステート)は Burst() を1行呼ぶだけでよく、
    /// ステートが終わった後もかけらは残って落ち切る。
    ///
    /// 見た目だけの演出なので、エフェクトと同じく全クライアントで動く処理から出せば
    /// 追加の通信なしで全員の画面で砕ける(ネットワーク同期は不要)。
    /// </summary>
    public class GorillaRockDebris : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>落下の加速度(m/秒^2)</summary>
        private const float GRAVITY = -22.0f;

        /// <summary>地面で跳ねるときに残る勢いの割合。1で減らない</summary>
        private const float BOUNCE_DAMPING = 0.42f;

        /// <summary>これより遅くなったら跳ねるのをやめて転がり終わったことにする</summary>
        private const float BOUNCE_STOP_SPEED = 1.2f;

        /// <summary>消えるときに縮む時間(秒)</summary>
        private const float SHRINK_SEC = 0.35f;

        // ---- 内部状態 ------------------------------------

        private Vector3 _velocity;
        private Vector3 _spinAxis;
        private float _spinSpeedDeg;
        private float _groundY;
        private float _lifetimeSec;
        private float _elapsedTime;
        private Vector3 _baseScale;

        // ---- 公開API -------------------------------------

        /// <summary>
        /// かけらを一斉に飛び散らせる。
        /// </summary>
        /// <param name="piecePrefab">かけらに使う見た目。投げた岩と同じプレハブでよい</param>
        /// <param name="center">砕けた位置(着弾地点)</param>
        /// <param name="count">飛ばす個数</param>
        /// <param name="pieceScale">かけら1個の大きさ</param>
        /// <param name="spreadSpeed">横へ散る速さ(m/秒)</param>
        /// <param name="upSpeed">上へ跳ね上がる速さ(m/秒)</param>
        /// <param name="lifetimeSec">消えるまでの時間(秒)</param>
        public static void Burst(
            GameObject piecePrefab, Vector3 center, int count, float pieceScale,
            float spreadSpeed = 6.0f, float upSpeed = 6.0f, float lifetimeSec = 2.5f)
        {
            if (piecePrefab == null || count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                // 円周に沿って均等に配りつつ、少しずらして機械的な並びに見えないようにする
                float angle = (360.0f / count) * i + Random.Range(-18.0f, 18.0f);
                Vector3 direction = Quaternion.Euler(0.0f, angle, 0.0f) * Vector3.forward;

                var instance = Object.Instantiate(piecePrefab, center, Random.rotation);
                instance.name = piecePrefab.name + "_Debris";

                // かけらは大きさをばらけさせる。全部同じ大きさだと砕けたように見えない
                float scale = pieceScale * Random.Range(0.55f, 1.0f);
                instance.transform.localScale = Vector3.one * scale;

                var debris = instance.AddComponent<GorillaRockDebris>();
                debris.Launch(
                    direction * (spreadSpeed * Random.Range(0.6f, 1.3f)) + Vector3.up * (upSpeed * Random.Range(0.7f, 1.3f)),
                    center.y,
                    lifetimeSec * Random.Range(0.8f, 1.2f));
            }
        }

        // ---- Unityイベント -------------------------------

        private void Update()
        {
            _elapsedTime += Time.deltaTime;

            UpdateFlight();

            // 終わりぎわだけ縮めて消す。パッと消えると存在が嘘くさく見える
            float shrinkStart = _lifetimeSec - SHRINK_SEC;
            if (_elapsedTime >= shrinkStart)
            {
                float rate = Mathf.Clamp01((_elapsedTime - shrinkStart) / SHRINK_SEC);
                transform.localScale = _baseScale * (1.0f - rate);

                if (_elapsedTime >= _lifetimeSec) Destroy(gameObject);
            }
        }

        // ---- 内部処理 ------------------------------------

        private void Launch(Vector3 velocity, float groundY, float lifetimeSec)
        {
            _velocity = velocity;
            _groundY = groundY;
            _lifetimeSec = Mathf.Max(SHRINK_SEC + 0.1f, lifetimeSec);
            _baseScale = transform.localScale;

            _spinAxis = Random.onUnitSphere;
            _spinSpeedDeg = Random.Range(180.0f, 520.0f);
        }

        /// <summary>落ちながら回り、地面に着いたら跳ねる</summary>
        private void UpdateFlight()
        {
            float delta = Time.deltaTime;

            _velocity.y += GRAVITY * delta;
            transform.position += _velocity * delta;
            transform.Rotate(_spinAxis, _spinSpeedDeg * delta, Space.World);

            if (transform.position.y > _groundY) return;

            // 地面にめり込ませない
            Vector3 position = transform.position;
            position.y = _groundY;
            transform.position = position;

            if (Mathf.Abs(_velocity.y) < BOUNCE_STOP_SPEED)
            {
                // 跳ねきったのでその場に転がったままにする
                _velocity = Vector3.zero;
                _spinSpeedDeg *= 0.5f;
                return;
            }

            // 跳ね返る。横の勢いも一緒に落として、だんだん止まるようにする
            _velocity.y = -_velocity.y * BOUNCE_DAMPING;
            _velocity.x *= BOUNCE_DAMPING;
            _velocity.z *= BOUNCE_DAMPING;
            _spinSpeedDeg *= BOUNCE_DAMPING;
        }
    }
}
