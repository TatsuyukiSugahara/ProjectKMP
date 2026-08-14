using UnityEngine;

namespace ProjectKMP.Player
{
    /// <summary>
    /// 戦いの場から出てしまった人を、場内へ戻す。
    ///
    /// 壁ですり抜けを防いでいても、跳ぶ技や吹き飛ばしが重なると外へ出ることがある。
    /// 出た本人は戻り方が分からず、展示ではそのまま遊べなくなってしまう。
    ///
    /// 直すべきは出る原因のほうだが、原因を全部塞ぐのは難しい。
    /// 出てしまっても数秒で戻れる道を用意しておけば、遊びは止まらない。
    ///
    /// 場の広さは壁から測る。壁を動かしても、ここを直す必要がない。
    /// </summary>
    public class ArenaKeeper : MonoBehaviour
    {
        // ---- 設定 ----------------------------------------

        [SerializeField, Tooltip("場の広さを測る目印。未設定なら Walls という名前を探す")]
        private Transform _wallsRoot;

        [SerializeField, Min(0.0f), Tooltip("この距離だけ場の外へ出たら戻す(メートル)")]
        private float _outMargin = 1.5f;

        [SerializeField, Min(0.0f), Tooltip("この高さより下へ落ちたら戻す(メートル)")]
        private float _fallLimitY = -12.0f;

        [SerializeField, Min(0.0f), Tooltip("外に出てから戻すまでの猶予(秒)。跳んでいる最中に戻さないための待ち")]
        private float _graceSec = 1.2f;

        [SerializeField, Min(0.0f), Tooltip("戻す位置を場の中心からどれだけ内側にするか(メートル)")]
        private float _returnInset = 4.0f;

        // ---- 内部状態 ------------------------------------

        private CharacterController _controller;
        private Bounds _arena;
        private bool _hasArena;
        private float _outElapsed;

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            ResolveArena();
        }

        /// <summary>壁の広がりから、場の内側を求める</summary>
        private void ResolveArena()
        {
            Transform root = _wallsRoot;
            if (root == null)
            {
                GameObject found = GameObject.Find("Walls");
                if (found != null) root = found.transform;
            }

            if (root == null) return;

            bool first = true;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (first) { _arena = collider.bounds; first = false; continue; }

                _arena.Encapsulate(collider.bounds);
            }

            _hasArena = !first;
        }

        private void Update()
        {
            if (!_hasArena) return;

            if (!IsOutside()) { _outElapsed = 0.0f; return; }

            // 跳んでいる最中に一瞬はみ出すことがある。すぐ戻すと技が中断されてしまう
            _outElapsed += Time.deltaTime;
            if (_outElapsed < _graceSec) return;

            _outElapsed = 0.0f;
            ReturnToArena();
        }

        private bool IsOutside()
        {
            Vector3 position = transform.position;

            if (position.y < _fallLimitY) return true;

            // 上下は見ない。跳んでいる最中に戻されると技が壊れる
            if (position.x < _arena.min.x - _outMargin || position.x > _arena.max.x + _outMargin) return true;
            if (position.z < _arena.min.z - _outMargin || position.z > _arena.max.z + _outMargin) return true;

            return false;
        }

        /// <summary>場の内側へ引き戻す。中心へ向かって少し入った所へ置く</summary>
        private void ReturnToArena()
        {
            Vector3 center = _arena.center;
            Vector3 position = transform.position;

            // 出た方向から入り直す。反対側へ飛ばされると、何が起きたか分からなくなる
            Vector3 toCenter = new Vector3(center.x - position.x, 0.0f, center.z - position.z);
            if (toCenter.sqrMagnitude < 0.01f) toCenter = Vector3.forward;

            toCenter.Normalize();

            float halfX = Mathf.Max(1.0f, _arena.extents.x - _returnInset);
            float halfZ = Mathf.Max(1.0f, _arena.extents.z - _returnInset);

            Vector3 target = new Vector3(
                Mathf.Clamp(position.x, center.x - halfX, center.x + halfX),
                0.0f,
                Mathf.Clamp(position.z, center.z - halfZ, center.z + halfZ));

            // 落ちた場合は横の位置も当てにならないので、中心寄りへ入れ直す
            if (position.y < _fallLimitY) target = center + toCenter * -_returnInset;

            target.y = ResolveGroundY(target, center.y) + 0.5f;

            // 動かす前に当たり判定を切る。切らないと壁に挟まって戻れない
            bool wasEnabled = _controller != null && _controller.enabled;
            if (_controller != null) _controller.enabled = false;

            transform.position = target;

            if (_controller != null) _controller.enabled = wasEnabled;
        }

        private static float ResolveGroundY(Vector3 position, float fallbackY)
        {
            Vector3 origin = new Vector3(position.x, 100.0f, position.z);

            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 300.0f, ~0, QueryTriggerInteraction.Ignore))
            {
                return fallbackY;
            }

            return hit.point.y;
        }
    }
}
