using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ProjectKMP.Presentation;

namespace ProjectKMP.UI
{
    /// <summary>
    /// 遊び方を1枚で見せる。
    ///
    /// 展示では来場者が説明を読まずに触る。係の人が毎回口で説明するより、
    /// 1枚の絵として置いておくほうが早い。
    ///
    /// 操作の書き方はいま触っている機器に合わせる。
    /// パッドを握っている人に『Rキー』と出しても意味がない。
    ///
    /// 表示に必要なものは自分で組み立てるので、シーンへの事前配置は要らない。
    /// </summary>
    public class HowToPlayPanel : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>他のUIより手前に出す</summary>
        private const int SORTING_ORDER = 900;

        /// <summary>1行ぶんの高さ(px)</summary>
        private const float ROW_HEIGHT = 78.0f;

        // ---- 型 ------------------------------------------

        /// <summary>説明する操作1つぶん</summary>
        private class Row
        {
            public string Name;
            public GameAction Action;
            public bool HasAction;
            public string TouchText;
            public string KeyboardText;
            public string GamepadText;
            public bool Hold;

            public TMP_Text Label;
        }

        // ---- 設定 ----------------------------------------

        [SerializeField, Tooltip("操作の割り当て表。未設定なら書き分けができない")]
        private InputGlyphTable _table;

        [SerializeField, Tooltip("文字に使うフォント")]
        private TMP_FontAsset _font;

        [SerializeField, Tooltip("ボタンの絵。タイトルのボタンと同じものを入れると角の丸みが揃う")]
        private Sprite _buttonSprite;

        // ---- 内部状態 ------------------------------------

        private readonly List<Row> _rows = new List<Row>();

        private GameObject _root;
        private TMP_Text _hint;
        private Selectable _closeButton;
        private GameObject _selectionBeforeOpen;

        // ---- 公開API -------------------------------------

        /// <summary>
        /// 開いているか。後ろの画面が操作を受け取らないよう、
        /// 上下の送りや選択の面倒を見ている側がこれを見る。
        /// </summary>
        public static bool IsOpen { get; private set; }

        /// <summary>開く。ボタンから呼ぶ</summary>
        public void Open()
        {
            if (_root == null || IsOpen) return;

            IsOpen = true;
            TitleOverlay.Push();

            _root.SetActive(true);
            Refresh(InputModeTracker.Current);

            // 閉じるボタンを選んでおく。パッドではここが唯一の出口になる
            if (EventSystem.current != null)
            {
                _selectionBeforeOpen = EventSystem.current.currentSelectedGameObject;

                if (_closeButton != null) EventSystem.current.SetSelectedGameObject(_closeButton.gameObject);
            }
        }

        /// <summary>閉じる</summary>
        public void Close()
        {
            if (_root == null || !IsOpen) return;

            IsOpen = false;
            TitleOverlay.Pop();

            // 閉じ方が3つ(ボタン・B・Esc)あるので、音はここでまとめて鳴らす。
            // ボタンに任せると、BやEscで閉じたときだけ無音になる
            if (UiSoundPlayer.Instance != null) UiSoundPlayer.Instance.Play(UiSoundPlayer.SoundKind.Cancel);

            _root.SetActive(false);

            // 開く前に選んでいたものへ戻す。戻さないと選択が消えて動かせなくなる
            if (EventSystem.current == null || _selectionBeforeOpen == null) return;

            EventSystem.current.SetSelectedGameObject(_selectionBeforeOpen);
        }

        private void Update()
        {
            if (!IsOpen) return;

            // パッドのBと Esc でも閉じられるようにする。
            // 閉じるボタンを押しに行かせるだけだと、出口が1つしかなくて詰まりやすい
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null && gamepad.buttonEast.wasPressedThisFrame) { Close(); return; }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Close();
        }

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            BuildRows();
            Build();

            _root.SetActive(false);
        }

        private void OnEnable()
        {
            InputModeTracker.Ensure();
            InputModeTracker.Changed += Refresh;
        }

        private void OnDisable()
        {
            InputModeTracker.Changed -= Refresh;
            IsOpen = false;
        }

        /// <summary>説明する操作を並べる。上から覚えてほしい順</summary>
        private void BuildRows()
        {
            _rows.Add(new Row
            {
                Name = "うごく",
                TouchText = "ひだりの スティック",
                KeyboardText = "W A S D",
                GamepadText = "ひだり スティック",
            });

            _rows.Add(new Row { Name = "がぶっ", Action = GameAction.Attack, HasAction = true });
            _rows.Add(new Row { Name = "ビーム", Action = GameAction.Beam, HasAction = true, Hold = true });
            _rows.Add(new Row { Name = "ひっさつわざ", Action = GameAction.EnergyBall, HasAction = true, Hold = true });
            _rows.Add(new Row { Name = "とびこみ", Action = GameAction.Dive, HasAction = true });
            _rows.Add(new Row { Name = "ねらう", Action = GameAction.TargetCamera, HasAction = true });

            _rows.Add(new Row
            {
                Name = "カメラ",
                TouchText = "がめんを なぞる",
                KeyboardText = "ひだり みぎ キー",
                GamepadText = "みぎ スティック",
            });
        }

        /// <summary>いまの機器に合わせて書き直す</summary>
        private void Refresh(InputMode mode)
        {
            foreach (Row row in _rows)
            {
                if (row.Label == null) continue;

                row.Label.text = ResolveText(row, mode);
            }

            if (_hint == null) return;

            // 閉じ方も機器で変わる。パッドはBで戻れる
            _hint.text = mode == InputMode.Gamepad ? "Bボタンで とじる" : "とじる を おす";
        }

        private string ResolveText(Row row, InputMode mode)
        {
            if (!row.HasAction)
            {
                if (mode == InputMode.Touch) return row.TouchText;
                if (mode == InputMode.Gamepad) return row.GamepadText;

                return row.KeyboardText;
            }

            // 指で触るときは、画面のボタンそのものが答えになる
            if (mode == InputMode.Touch) return "がめんの ボタン";

            InputGlyphTable.Entry entry = _table != null ? _table.Find(row.Action, mode) : null;
            if (entry == null || string.IsNullOrEmpty(entry.Label)) return "-";

            return row.Hold || entry.Hold ? entry.Label + "  ながおし" : entry.Label;
        }

        // ---- 組み立て ------------------------------------

        private void Build()
        {
            _root = new GameObject("HowToPlayPanel", typeof(RectTransform), typeof(Image));
            _root.transform.SetParent(transform, false);

            var canvas = _root.AddComponent<Canvas>();
            canvas.overrideSorting = true;

            // 決め打ちの値だと、親のほうが上だったときに後ろへ回ってしまう。
            // 親より必ず上になるよう、親の値を見てから決める
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            int baseOrder = parentCanvas != null ? parentCanvas.sortingOrder : 0;
            canvas.sortingOrder = Mathf.Max(SORTING_ORDER, baseOrder + 10);
            _root.AddComponent<GraphicRaycaster>();

            var rootRect = _root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var backdrop = _root.GetComponent<Image>();
            backdrop.color = new Color(0.05f, 0.08f, 0.12f, 0.93f);
            backdrop.raycastTarget = true;

            CreateText(rootRect, "Heading", "あそびかた", 64,
                new Vector2(0.0f, 380.0f), new Vector2(900.0f, 90.0f), TextAlignmentOptions.Center);

            float top = 270.0f;
            for (int i = 0; i < _rows.Count; i++)
            {
                float y = top - i * ROW_HEIGHT;

                CreateText(rootRect, "Name" + i, _rows[i].Name, 40,
                    new Vector2(-200.0f, y), new Vector2(420.0f, ROW_HEIGHT), TextAlignmentOptions.Right);

                _rows[i].Label = CreateText(rootRect, "Key" + i, string.Empty, 40,
                    new Vector2(260.0f, y), new Vector2(480.0f, ROW_HEIGHT), TextAlignmentOptions.Left);

                _rows[i].Label.color = new Color(1.0f, 0.86f, 0.35f, 1.0f);
            }

            _hint = CreateText(rootRect, "Hint", string.Empty, 32,
                new Vector2(0.0f, -300.0f), new Vector2(900.0f, 60.0f), TextAlignmentOptions.Center);

            _hint.color = new Color(0.8f, 0.82f, 0.85f, 1.0f);

            BuildCloseButton(rootRect);
        }

        private void BuildCloseButton(RectTransform parent)
        {
            var go = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0.0f, -390.0f);
            rect.sizeDelta = new Vector2(320.0f, 96.0f);

            var image = go.GetComponent<Image>();
            image.color = new Color(0.95f, 0.95f, 0.95f, 1.0f);

            // タイトルのボタンと同じ絵を使えば、角の丸みがぴたりと揃う。
            // 入っていなければ、その場で描いた角丸で代用する
            image.sprite = _buttonSprite != null ? _buttonSprite : UiShapeSprites.RoundedBox();
            image.type = Image.Type.Sliced;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(Close);

            _closeButton = button;

            // 音は閉じる処理の側で鳴らすので、ボタン共通の音は付けさせない
            go.AddComponent<UiButtonSoundKind>().SetKind(UiSoundPlayer.SoundKind.None);

            TMP_Text label = CreateText(rect, "Label", "とじる", 40,
                Vector2.zero, new Vector2(320.0f, 96.0f), TextAlignmentOptions.Center);

            label.color = new Color(0.12f, 0.14f, 0.18f, 1.0f);
        }

        private TMP_Text CreateText(
            RectTransform parent, string name, string content, int fontSize,
            Vector2 position, Vector2 size, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            if (_font != null) text.font = _font;

            return text;
        }
    }
}
