using System.Collections.Generic;
using ProjectKMP.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectKMP.UI
{
    /// <summary>
    /// その日の記録を上位3件だけ見せる。
    ///
    /// タイトルに常時出すと絵を隠すので、見たい人だけが開く形にする。
    /// 上位3件に絞るのは、4件目以降は誰も自分と比べないため。
    ///
    /// 順位はメダルの色で伝える。数字を読まなくても、
    /// 金・銀・銅の並びだけで誰が一番かが分かる。
    /// </summary>
    public class RecordsPanel : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        private const int SORTING_ORDER = 900;
        private const int SHOW_COUNT = 3;
        private const float ROW_HEIGHT = 108.0f;

        private static readonly Color[] MEDAL_COLORS =
        {
            new Color(0.949f, 0.780f, 0.267f, 1.0f),
            new Color(0.788f, 0.804f, 0.831f, 1.0f),
            new Color(0.800f, 0.545f, 0.329f, 1.0f),
        };

        // ---- 設定 ----------------------------------------

        [SerializeField, Tooltip("文字に使うフォント")]
        private TMP_FontAsset _font;

        [SerializeField, Tooltip("ボタンの絵。タイトルのボタンと同じものを入れると角の丸みが揃う")]
        private Sprite _buttonSprite;

        // ---- 内部状態 ------------------------------------

        private readonly List<TMP_Text> _names = new List<TMP_Text>();
        private readonly List<TMP_Text> _times = new List<TMP_Text>();
        private readonly List<GameObject> _rows = new List<GameObject>();

        private GameObject _root;
        private TMP_Text _empty;
        private Selectable _closeButton;
        private GameObject _selectionBeforeOpen;
        private bool _isOpen;

        // ---- 公開API -------------------------------------

        /// <summary>開く。ボタンから呼ぶ</summary>
        public void Open()
        {
            if (_root == null || _isOpen) return;

            _isOpen = true;
            TitleOverlay.Push();

            _root.SetActive(true);
            Refresh();

            if (EventSystem.current == null) return;

            _selectionBeforeOpen = EventSystem.current.currentSelectedGameObject;
            if (_closeButton != null) EventSystem.current.SetSelectedGameObject(_closeButton.gameObject);
        }

        /// <summary>閉じる</summary>
        public void Close()
        {
            if (_root == null || !_isOpen) return;

            _isOpen = false;
            TitleOverlay.Pop();

            // 閉じ方が3つ(ボタン・B・Esc)あるので、音はここでまとめて鳴らす。
            // ボタンに任せると、BやEscで閉じたときだけ無音になる
            if (UiSoundPlayer.Instance != null) UiSoundPlayer.Instance.Play(UiSoundPlayer.SoundKind.Cancel);

            _root.SetActive(false);

            if (EventSystem.current == null || _selectionBeforeOpen == null) return;

            EventSystem.current.SetSelectedGameObject(_selectionBeforeOpen);
        }

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            Build();
            _root.SetActive(false);
        }

        private void OnDisable()
        {
            if (_isOpen) Close();
        }

        private void Update()
        {
            if (!_isOpen) return;

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null && gamepad.buttonEast.wasPressedThisFrame) { Close(); return; }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Close();
        }

        /// <summary>いまの記録を書き込む</summary>
        private void Refresh()
        {
            List<BestTimeBoard.Entry> entries = BestTimeBoard.Load();

            if (_empty != null) _empty.gameObject.SetActive(entries.Count == 0);

            for (int i = 0; i < SHOW_COUNT; i++)
            {
                bool has = i < entries.Count;

                if (_rows.Count > i && _rows[i] != null) _rows[i].SetActive(has);
                if (!has) continue;

                if (_names.Count > i && _names[i] != null) _names[i].text = entries[i].Name;
                if (_times.Count > i && _times[i] != null) _times[i].text = ClearTime.Format(entries[i].Seconds);
            }
        }

        // ---- 組み立て ------------------------------------

        private void Build()
        {
            _root = new GameObject("RecordsPanel", typeof(RectTransform), typeof(Image));
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

            BuildHeading(rootRect);

            for (int i = 0; i < SHOW_COUNT; i++) BuildRow(rootRect, i);

            _empty = CreateText(rootRect, "Empty", "まだ きろくが ないよ", 40,
                new Vector2(0.0f, 40.0f), new Vector2(900.0f, 80.0f), TextAlignmentOptions.Center);

            _empty.color = new Color(0.75f, 0.77f, 0.8f, 1.0f);

            BuildCloseButton(rootRect);
        }

        /// <summary>
        /// 見出しを組み立てる。
        ///
        /// 文字を大きくするだけでは表題に見えない。
        /// 濃い縁で文字を立たせ、左右に線を引いて『ここが表題』と分かる形にする。
        /// </summary>
        private void BuildHeading(RectTransform parent)
        {
            TMP_Text heading = CreateText(parent, "Heading", "きょうの きろく", 80,
                new Vector2(0.0f, 300.0f), new Vector2(900.0f, 110.0f), TextAlignmentOptions.Center);

            heading.color = new Color(1.0f, 0.88f, 0.42f, 1.0f);
            heading.fontStyle = FontStyles.Bold;

            // 濃い縁を付けて、金色でも背景に沈まないようにする
            heading.fontMaterial.EnableKeyword("OUTLINE_ON");
            heading.outlineWidth = 0.22f;
            heading.outlineColor = new Color32(40, 26, 8, 255);

            // 文字の幅より外へ置く。近いと文字に食い込んで、線が矢印のように見える
            CreateRule(parent, new Vector2(-500.0f, 300.0f));
            CreateRule(parent, new Vector2(500.0f, 300.0f));
        }

        /// <summary>見出しの左右に引く飾りの線</summary>
        private static void CreateRule(RectTransform parent, Vector2 position)
        {
            var go = new GameObject("Rule", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(150.0f, 6.0f);

            var image = go.GetComponent<Image>();
            image.color = new Color(1.0f, 0.88f, 0.42f, 0.65f);
            image.raycastTarget = false;
            image.sprite = UiShapeSprites.RoundedBox();
            image.type = Image.Type.Sliced;
        }

        private void BuildRow(RectTransform parent, int index)
        {
            float y = 150.0f - index * ROW_HEIGHT;

            var row = new GameObject("Row" + index, typeof(RectTransform));
            row.transform.SetParent(parent, false);

            var rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0.5f, 0.5f);
            rowRect.anchorMax = new Vector2(0.5f, 0.5f);
            rowRect.pivot = new Vector2(0.5f, 0.5f);
            rowRect.anchoredPosition = new Vector2(0.0f, y);
            rowRect.sizeDelta = new Vector2(760.0f, ROW_HEIGHT);

            // メダル。順位はここの色で伝える
            var medal = new GameObject("Medal", typeof(RectTransform), typeof(Image));
            medal.transform.SetParent(rowRect, false);

            var medalRect = medal.GetComponent<RectTransform>();
            medalRect.anchorMin = new Vector2(0.0f, 0.5f);
            medalRect.anchorMax = new Vector2(0.0f, 0.5f);
            medalRect.pivot = new Vector2(0.5f, 0.5f);
            medalRect.anchoredPosition = new Vector2(50.0f, 0.0f);
            medalRect.sizeDelta = new Vector2(72.0f, 72.0f);

            var image = medal.GetComponent<Image>();
            image.color = MEDAL_COLORS[Mathf.Clamp(index, 0, MEDAL_COLORS.Length - 1)];
            image.raycastTarget = false;

            // 何も貼らないと四角い板のまま。丸い絵を貼ってメダルらしくする
            image.sprite = UiShapeSprites.Circle();

            TMP_Text rank = CreateText(medalRect, "Rank", (index + 1).ToString(), 40,
                Vector2.zero, new Vector2(72.0f, 72.0f), TextAlignmentOptions.Center);

            rank.color = new Color(0.16f, 0.12f, 0.04f, 1.0f);

            // メダルの右から中ほどまでが名前、右端がタイム。
            // 場所を分けておかないと、名前が長いときにタイムと重なる
            TMP_Text name = CreateText(rowRect, "Name", string.Empty, 44,
                new Vector2(-95.0f, 0.0f), new Vector2(350.0f, ROW_HEIGHT), TextAlignmentOptions.Left);

            // 長い名前は切らずに縮めて収める。切ると誰の記録か分からなくなる
            name.enableWordWrapping = false;
            name.enableAutoSizing = true;
            name.fontSizeMin = 24.0f;
            name.fontSizeMax = 44.0f;

            TMP_Text time = CreateText(rowRect, "Time", string.Empty, 44,
                new Vector2(230.0f, 0.0f), new Vector2(260.0f, ROW_HEIGHT), TextAlignmentOptions.Right);

            _rows.Add(row);
            _names.Add(name);
            _times.Add(time);
        }

        private void BuildCloseButton(RectTransform parent)
        {
            var go = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0.0f, -300.0f);
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
