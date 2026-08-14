using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectKMP.UI
{
    /// <summary>
    /// タイトルのメニューを、常に3つだけ見せる縦のリストにする。
    ///
    /// 項目が増えるたびにボタンを足していくと、画面がボタンで埋まって
    /// タイトルの絵が見えなくなる。見せる数を固定すれば、
    /// 項目がいくつ増えても画面の使い方は変わらない。
    ///
    /// 真ん中が選択中で、大きく濃く出す。上下は小さく薄く。
    /// どれが選ばれているかを、位置と大きさの両方で伝える。
    ///
    /// 指やマウスでは、見えている3つはどれも直接押せる。
    /// 選び直してから決定する手間を挟まない。
    /// </summary>
    public class TitleMenuList : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler
    {
        // ---- 定数 ----------------------------------------

        /// <summary>倒しっぱなしで送られ続けないための遊び</summary>
        private const float STICK_DEAD_ZONE = 0.5f;

        /// <summary>押しっぱなしで送り始めるまでの待ち(秒)</summary>
        private const float REPEAT_DELAY = 0.4f;

        /// <summary>送り続けるときの間隔(秒)</summary>
        private const float REPEAT_INTERVAL = 0.15f;

        /// <summary>1つ送るのに必要な指の移動量(px)。小さいと押したつもりで送られる</summary>
        private const float DRAG_THRESHOLD = 60.0f;

        // ---- 設定 ----------------------------------------

        [SerializeField, Tooltip("並べる項目。上から順に。3つより多くても構わない")]
        private List<RectTransform> _items = new List<RectTransform>();

        [SerializeField, Min(0.0f), Tooltip("上下の間隔(px)")]
        private float _spacing = 118.0f;

        [SerializeField, Min(0.1f), Tooltip("選択中の大きさ")]
        private float _selectedScale = 1.0f;

        [SerializeField, Min(0.1f), Tooltip("上下の項目の大きさ")]
        private float _sideScale = 0.74f;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("上下の項目の濃さ")]
        private float _sideAlpha = 0.5f;

        [SerializeField, Min(0.01f), Tooltip("動きの速さ。大きいほどキビキビ入れ替わる")]
        private float _moveSpeed = 12.0f;

        // ---- 内部状態 ------------------------------------

        private readonly List<CanvasGroup> _groups = new List<CanvasGroup>();

        private int _index;
        private float _repeatTimer;
        private int _lastDirection;
        private float _dragAmount;

        // ---- 指とホイール --------------------------------

        /// <summary>
        /// 指でなぞって送る。
        ///
        /// 見えていない項目には、指だけでは一生たどり着けない。
        /// ボタンの上から始めたなぞりもここへ届くので、どこを触っても送れる。
        /// </summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragAmount = 0.0f;
        }

        public void OnDrag(PointerEventData eventData)
        {
            _dragAmount += eventData.delta.y;

            while (Mathf.Abs(_dragAmount) >= DRAG_THRESHOLD)
            {
                // 上へなぞると、下から次の項目が上がってくる
                int step = _dragAmount > 0.0f ? 1 : -1;
                _dragAmount -= step * DRAG_THRESHOLD;

                Step(step);
            }
        }

        /// <summary>マウスのホイールでも送れるようにする</summary>
        public void OnScroll(PointerEventData eventData)
        {
            float scroll = eventData.scrollDelta.y;
            if (Mathf.Abs(scroll) < 0.01f) return;

            Step(scroll > 0.0f ? -1 : 1);
        }

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            // 配布の形によっては、始まる前に消えている項目がある。
            // 一覧に残すと、送ったときに空の位置が回ってきてしまう
            DropMissing();

            foreach (RectTransform item in _items)
            {
                if (item == null) { _groups.Add(null); continue; }

                var group = item.GetComponent<CanvasGroup>();
                if (group == null) group = item.gameObject.AddComponent<CanvasGroup>();

                _groups.Add(group);

                // 上下の送りはこちらで持つ。任せると選択が飛んで、真ん中と食い違う
                var selectable = item.GetComponent<Selectable>();
                if (selectable != null) selectable.navigation = new Navigation { mode = Navigation.Mode.None };
            }

            Apply(true);
        }

        // 無くなった項目を一覧から外す。
        //
        // 消される側と組み立てる側で、どちらが先に動くかは決まっていない。
        // 表示のたびに確かめれば、順番に関係なく正しく並ぶ。
        private void DropMissing()
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i] != null) continue;

                _items.RemoveAt(i);
                if (_groups.Count > i) _groups.RemoveAt(i);
            }
        }

        private void OnEnable()
        {
            DropMissing();
            _index = 0;
            Apply(true);
        }

        private void Update()
        {
            // 消される側と動く順番が決まっていないので、毎回確かめる。
            // 気づくのが遅れると、消えた場所が空席として並びに残り、
            // そのぶん他の項目が画面の外へ押し出される
            DropMissing();

            // 遊び方が開いている間は送らない。後ろで勝手に項目が動くと混乱する
            if (!TitleOverlay.IsOpen) ReadInput();
            Apply(false);
        }

        /// <summary>上下の入力で選ぶ項目を送る</summary>
        private void ReadInput()
        {
            int direction = 0;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed) direction += 1;
                if (keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed) direction -= 1;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.dpad.up.isPressed) direction += 1;
                if (gamepad.dpad.down.isPressed) direction -= 1;

                float stick = gamepad.leftStick.ReadValue().y;
                if (stick > STICK_DEAD_ZONE) direction += 1;
                if (stick < -STICK_DEAD_ZONE) direction -= 1;
            }

            direction = Mathf.Clamp(direction, -1, 1);

            if (direction == 0) { _lastDirection = 0; _repeatTimer = 0.0f; return; }

            // 倒した瞬間は必ず1つ送る。そのあとは間隔をあけて送り続ける
            if (direction != _lastDirection)
            {
                _lastDirection = direction;
                _repeatTimer = REPEAT_DELAY;
                Step(-direction);
                return;
            }

            _repeatTimer -= Time.unscaledDeltaTime;
            if (_repeatTimer > 0.0f) return;

            _repeatTimer = REPEAT_INTERVAL;
            Step(-direction);
        }

        private void Step(int delta)
        {
            int count = UsableCount();
            if (count <= 0) return;

            _index = ((_index + delta) % count + count) % count;

            SelectCenter();
        }

        /// <summary>真ん中を選択状態にする。パッドの決定がここへ届くようにする</summary>
        private void SelectCenter()
        {
            if (InputModeTracker.Current != InputMode.Gamepad) return;
            if (EventSystem.current == null) return;

            RectTransform center = ItemAt(_index);
            if (center == null) return;

            EventSystem.current.SetSelectedGameObject(center.gameObject);
        }

        /// <summary>並びを反映する。snap が true なら補間せず即座に置く</summary>
        private void Apply(bool snap)
        {
            int count = UsableCount();
            if (count <= 0) return;

            // 項目が減ったとき、選んでいた位置が範囲の外に残ることがある
            if (_index >= count) _index = 0;

            for (int i = 0; i < _items.Count; i++)
            {
                RectTransform item = _items[i];
                if (item == null) continue;

                // 選択中からいくつ離れているか。回り込ませて、端で途切れないようにする
                int offset = i - _index;
                if (offset > count / 2) offset -= count;
                if (offset < -count / 2) offset += count;

                bool visible = Mathf.Abs(offset) <= 1;
                item.gameObject.SetActive(visible);
                if (!visible) continue;

                Vector2 target = new Vector2(0.0f, -offset * _spacing);
                float scale = offset == 0 ? _selectedScale : _sideScale;
                float alpha = offset == 0 ? 1.0f : _sideAlpha;

                if (snap)
                {
                    item.anchoredPosition = target;
                    item.localScale = Vector3.one * scale;
                }
                else
                {
                    float k = Mathf.Clamp01(_moveSpeed * Time.unscaledDeltaTime);
                    item.anchoredPosition = Vector2.Lerp(item.anchoredPosition, target, k);
                    item.localScale = Vector3.Lerp(item.localScale, Vector3.one * scale, k);
                }

                CanvasGroup group = _groups.Count > i ? _groups[i] : null;
                if (group != null) group.alpha = snap ? alpha : Mathf.Lerp(group.alpha, alpha, Mathf.Clamp01(_moveSpeed * Time.unscaledDeltaTime));
            }
        }

        private int UsableCount()
        {
            int count = 0;
            foreach (RectTransform item in _items)
            {
                if (item != null) count++;
            }

            return count;
        }

        private RectTransform ItemAt(int index)
        {
            if (index < 0 || index >= _items.Count) return null;

            return _items[index];
        }
    }
}
