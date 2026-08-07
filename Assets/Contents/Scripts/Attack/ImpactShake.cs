using UnityEngine;

namespace ProjectKMP.Attack
{
    /// <summary>
    /// 爆発の周りにある背景オブジェクト(木など)を一時的に揺らす。
    /// 対象に自動で付いて、揺れ終わると元の姿勢に戻して自分を消す。見た目だけの演出。
    /// </summary>
    public class ImpactShake : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        private const int OVERLAP_BUFFER_SIZE = 32;

        /// <summary>地面のような巨大なものは揺らさない。その判定に使う大きさ(m)</summary>
        private const float MAX_OBJECT_SIZE = 25f;

        private const float SHAKE_FREQUENCY = 9f;

        private static readonly Collider[] OVERLAP_BUFFER = new Collider[OVERLAP_BUFFER_SIZE];

        // ---- 内部状態 ------------------------------------

        private Quaternion _originalRotation;
        private Vector3 _localAxis = Vector3.right;
        private float _amplitudeDeg;
        private float _durationSec;
        private float _elapsedSec;

        // ---- 公開API -------------------------------------

        /// <summary>中心から radius 以内の対象を揺らす。近いものほど大きく揺れる</summary>
        public static void ShakeAround(
            Vector3 center, float radius, LayerMask layers, float amplitudeDeg, float durationSec, int maxCount)
        {
            if (radius <= 0f || amplitudeDeg <= 0f || durationSec <= 0f || maxCount <= 0) return;

            int count = Physics.OverlapSphereNonAlloc(
                center, radius, OVERLAP_BUFFER, layers, QueryTriggerInteraction.Ignore);

            int shaken = 0;
            for (int i = 0; i < count && shaken < maxCount; i++)
            {
                Collider collider = OVERLAP_BUFFER[i];
                if (collider == null || !IsShakable(collider)) continue;

                Transform target = collider.transform;

                Vector3 away = target.position - center;
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f) away = Vector3.forward;

                // 爆心から遠いほど弱く揺らす
                float falloff = 1f - Mathf.Clamp01(away.magnitude / radius);

                ImpactShake shake = target.GetComponent<ImpactShake>();
                if (shake == null) shake = target.gameObject.AddComponent<ImpactShake>();
                shake.Begin(away.normalized, amplitudeDeg * falloff, durationSec);
                shaken++;
            }
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _originalRotation = transform.localRotation;
            _durationSec = 0f;
            _elapsedSec = 0f;
        }

        private void Update()
        {
            if (_elapsedSec >= _durationSec)
            {
                transform.localRotation = _originalRotation;
                Destroy(this);
                return;
            }

            _elapsedSec += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsedSec / _durationSec);

            // だんだん揺れ幅が小さくなる減衰振動
            float angle = _amplitudeDeg * (1f - t) * Mathf.Sin(_elapsedSec * SHAKE_FREQUENCY * Mathf.PI * 2f);
            transform.localRotation = _originalRotation * Quaternion.AngleAxis(angle, _localAxis);
        }

        // ---- 内部処理 ------------------------------------

        private static bool IsShakable(Collider collider)
        {
            // キャラクターは自分で動くので触らない
            if (collider.GetComponentInParent<HitTarget>() != null) return false;
            if (collider.GetComponentInParent<CharacterController>() != null) return false;

            // 地面のような巨大なものを動かすと世界ごと揺れてしまう
            return collider.bounds.size.magnitude <= MAX_OBJECT_SIZE;
        }

        private void Begin(Vector3 awayDirection, float amplitudeDeg, float durationSec)
        {
            // 揺れの途中で呼び直されても、元の姿勢を揺れた姿勢で上書きしない
            if (_elapsedSec >= _durationSec) _originalRotation = transform.localRotation;

            // 爆心から離れる方へ倒れるよう、その向きに直交する軸で回す
            Vector3 axis = Vector3.Cross(Vector3.up, awayDirection);
            _localAxis = transform.InverseTransformDirection(axis).normalized;
            if (_localAxis.sqrMagnitude < 0.0001f) _localAxis = Vector3.right;

            _amplitudeDeg = amplitudeDeg;
            _durationSec = Mathf.Max(0.05f, durationSec);
            _elapsedSec = 0f;
        }
    }
}
