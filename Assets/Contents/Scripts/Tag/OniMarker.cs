using UnityEngine;

namespace ProjectKMP.Tag
{
    /// <summary>
    /// 鬼の頭上に出すマーカー。どの角度から見ても読めるようカメラの方を向き続ける。
    /// </summary>
    public class OniMarker : MonoBehaviour
    {
        // ---- 調整パラメータ ------------------------------
        [SerializeField] private float _bobHeight   = 0.10f;
        [SerializeField] private float _bobSpeed    = 3.0f;
        [SerializeField] private float _spinSpeedDeg;

        // ---- 内部状態 ------------------------------------
        private Vector3 _baseLocalPosition;
        private Camera _camera;

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _baseLocalPosition = transform.localPosition;
        }

        private void OnEnable()
        {
            // 表示するたびに同じ高さから始めたい
            transform.localPosition = _baseLocalPosition;
        }

        private void LateUpdate()
        {
            if (_camera == null) _camera = Camera.main;

            float offset = Mathf.Sin(Time.time * _bobSpeed) * _bobHeight;
            transform.localPosition = _baseLocalPosition + Vector3.up * offset;

            if (_camera == null) return;

            float roll = _spinSpeedDeg * Time.time;
            transform.rotation = _camera.transform.rotation * Quaternion.Euler(0.0f, 0.0f, roll);
        }
    }
}
