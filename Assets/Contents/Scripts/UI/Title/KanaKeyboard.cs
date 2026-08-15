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
    /// パッドで名前を入れるための五十音パネル。
    ///
    /// パッドでは文字を打てないので、画面に文字を並べて選んでもらう。
    /// スマホは触れば端末のキーボードが出るし、PCはキーボードで打てるので、
    /// このパネルはパッドを使っているときにだけ開く。
    ///
    /// 勝手には開かない。入力欄を選んで決定したときに開く。
    /// 名前入力の画面に来ただけで全面が覆われると、他のボタンが押せず戸惑うため。
    ///
    /// 文字は元からある入力欄へ書き込み、決定と取り消しも元のボタンへ渡す。
    /// 名前の確定処理を二重に持たないための作り。
    /// </summary>
    public class KanaKeyboard : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        /// <summary>1列ぶん(あ段〜お段)。空白は文字が無いところ</summary>
        private static readonly string[] GYOU =
        {
            "あいうえお", "かきくけこ", "さしすせそ", "たちつてと", "なにぬねの",
            "はひふへほ", "まみむめも", "や ゆ よ", "らりるれろ", "わをん  ",
        };

        private const string DAKUTEN_FROM = "かきくけこさしすせそたちつてとはひふへほ";
        private const string DAKUTEN_TO = "がぎぐげござじずぜぞだぢづでどばびぶべぼ";

        private const string HANDAKU_FROM = "はひふへほ";
        private const string HANDAKU_TO = "ぱぴぷぺぽ";

        private const string SMALL_FROM = "あいうえおつやゆよわ";
        private const string SMALL_TO = "ぁぃぅぇぉっゃゅょゎ";

        private const int COLUMNS = 10;
        private const int ROWS = 5;

        /// <summary>押しっぱなしで消し続けるまでの待ち(秒)</summary>
        private const float REPEAT_DELAY = 0.4f;

        /// <summary>消し続けるときの間隔(秒)</summary>
        private const float REPEAT_INTERVAL = 0.06f;

        // ---- 設定 ----------------------------------------

        [Header("つなぐ先")]
        [SerializeField, Tooltip("書き込む入力欄")]
        private TMP_InputField _nameField;

        [SerializeField, Tooltip("名前入力のまとまり。ここが閉じたらパネルも閉じる")]
        private CanvasGroup _nameInputGroup;

        [SerializeField, Tooltip("決定。押されたことにする")]
        private Button _okButton;

        [SerializeField, Tooltip("取り消し。押されたことにする")]
        private Button _backButton;

        [SerializeField, Tooltip("文字に使うフォント。未設定なら入力欄から借りる")]
        private TMP_FontAsset _font;

        [Header("音")]
        [SerializeField, Tooltip("文字を入れたときの音")]
        private AudioClip _inputClip;

        [SerializeField, Tooltip("文字を消したときの音")]
        private AudioClip _deleteClip;

        [SerializeField, Range(0.0f, 1.0f), Tooltip("音量")]
        private float _soundVolume = 0.7f;

        [Header("調整")]
        [SerializeField, Min(1), Tooltip("入れられる文字数")]
        private int _maxLength = 8;

        [SerializeField, Min(20.0f), Tooltip("文字ボタン1つの大きさ(px)")]
        private float _keySize = 92.0f;

        [SerializeField, Min(0.0f), Tooltip("文字ボタンの間隔(px)")]
        private float _keySpacing = 8.0f;

        // ---- 内部状態 ------------------------------------

        private GameObject _root;
        private TMP_Text _display;
        private Button _firstKey;
        private bool _shown;
        private float _deleteTimer;

        private readonly Button[,] _keys = new Button[COLUMNS, ROWS];
        private readonly List<Button> _tools = new List<Button>();

        // ---- 公開API -------------------------------------

        /// <summary>パネルを開く。入力欄を選んで決定したときに呼ぶ</summary>
        public void Open()
        {
            if (_root == null) return;

            SetShown(true);
        }

        /// <summary>パネルを閉じる</summary>
        public void Close()
        {
            SetShown(false);
        }

        /// <summary>
        /// パネルを閉じて、次に触るものを選んでおく。
        /// 閉じただけだと何も選ばれておらず、パッドで動かせなくなるため。
        /// </summary>
        private void CloseAndSelect(Selectable next)
        {
            Close();

            if (next == null || EventSystem.current == null) return;

            EventSystem.current.SetSelectedGameObject(next.gameObject);
        }

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            if (_font == null && _nameField != null && _nameField.textComponent != null)
            {
                _font = _nameField.textComponent.font;
            }

            Build();

            // 組み立てた直後は出たままなので、ここで確実に畳む
            _shown = false;
            if (_root != null) _root.SetActive(false);
        }

        private void OnEnable()
        {
            InputModeTracker.Ensure();
        }

        private void Update()
        {
            // 名前入力そのものが閉じたら、開いたままにしない
            if (_shown && !IsNameInputOpen()) { SetShown(false); return; }

            if (!_shown) return;

            if (_display != null && _nameField != null) _display.text = FormatDisplay(_nameField.text);

            KeepSelectionInside();
            UpdateDeleteInput();
        }

        /// <summary>
        /// 開いている間は、選択をパネルの中へ留める。
        ///
        /// 入力欄や後ろのボタンへ選択が移ると、パネルのキーを動かせなくなる。
        /// 誰が奪ったかを追いかけるより、開いている側が取り返すほうが確実。
        ///
        /// マウスや指のときは何もしない。選択枠を出さない決まりと衝突するため。
        /// </summary>
        private void KeepSelectionInside()
        {
            if (InputModeTracker.Current != InputMode.Gamepad) return;
            if (EventSystem.current == null || _root == null) return;

            GameObject selected = EventSystem.current.currentSelectedGameObject;

            // パネルの中のものが選ばれていれば、そのままでよい
            if (selected != null && selected.transform.IsChildOf(_root.transform)) return;

            if (_firstKey == null) return;

            EventSystem.current.SetSelectedGameObject(_firstKey.gameObject);
        }

        private bool IsNameInputOpen()
        {
            if (_nameInputGroup == null) return false;
            if (!_nameInputGroup.gameObject.activeInHierarchy) return false;

            return _nameInputGroup.alpha > 0.01f;
        }

        /// <summary>Bボタンと Backspace で1文字消す。押しっぱなしなら続けて消す</summary>
        private void UpdateDeleteInput()
        {
            bool pressed = false;
            bool held = false;

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.buttonEast.wasPressedThisFrame) pressed = true;
                if (gamepad.buttonEast.isPressed) held = true;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.backspaceKey.wasPressedThisFrame) pressed = true;
                if (keyboard.backspaceKey.isPressed) held = true;
            }

            if (pressed)
            {
                Backspace();
                _deleteTimer = REPEAT_DELAY;
                return;
            }

            if (!held) { _deleteTimer = 0.0f; return; }

            _deleteTimer -= Time.unscaledDeltaTime;
            if (_deleteTimer > 0.0f) return;

            Backspace();
            _deleteTimer = REPEAT_INTERVAL;
        }

        private void SetShown(bool shown)
        {
            if (_shown == shown) return;

            _shown = shown;
            if (_root != null) _root.SetActive(shown);

            _deleteTimer = 0.0f;

            if (!shown) return;

            if (EventSystem.current == null) return;

            // 開いた時点で五十音の左上を選ぶ。
            // このパネルはパッドのためのものなので、キーを選んでおけばすぐ動かせる
            if (_firstKey != null) EventSystem.current.SetSelectedGameObject(_firstKey.gameObject);
        }

        // ---- 文字の出し入れ ------------------------------

        private void Append(string character)
        {
            if (_nameField == null || string.IsNullOrEmpty(character)) return;
            if (_nameField.text.Length >= _maxLength) return;

            _nameField.text += character;
            PlayClip(_inputClip);
        }

        private void Backspace()
        {
            if (_nameField == null) return;

            string text = _nameField.text;
            if (text.Length == 0) return;

            _nameField.text = text.Substring(0, text.Length - 1);
            PlayClip(_deleteClip);
        }

        /// <summary>音を鳴らす。UIの音はまとめ役から出す</summary>
        private void PlayClip(AudioClip clip)
        {
            if (clip == null || UiSoundPlayer.Instance == null) return;

            UiSoundPlayer.Instance.PlayOneShot(clip, _soundVolume);
        }

        /// <summary>最後の1文字を、対応表に沿って別の文字へ置き換える(濁点・小文字など)</summary>
        private void Convert(string from, string to)
        {
            if (_nameField == null) return;

            string text = _nameField.text;
            if (text.Length == 0) return;

            int index = from.IndexOf(text[text.Length - 1]);
            if (index < 0) return;

            _nameField.text = text.Substring(0, text.Length - 1) + to[index];
            PlayClip(_inputClip);
        }

        private string FormatDisplay(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? "ここに はいるよ" : text;
        }

        // ---- 組み立て ------------------------------------

        private void Build()
        {
            _root = new GameObject("KanaPanel", typeof(RectTransform), typeof(Image));
            _root.transform.SetParent(transform, false);

            var rootRect = _root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var backdrop = _root.GetComponent<Image>();
            backdrop.color = new Color(0.05f, 0.08f, 0.12f, 0.94f);
            backdrop.raycastTarget = true;

            float gridWidth = COLUMNS * _keySize + (COLUMNS - 1) * _keySpacing;

            CreateLabel(rootRect, "Heading", "なまえを いれてね", 56, new Vector2(0.0f, 390.0f), new Vector2(1200.0f, 90.0f));

            CreateBox(rootRect, new Vector2(0.0f, 292.0f), new Vector2(760.0f, 104.0f));
            _display = CreateLabel(rootRect, "Display", "", 60, new Vector2(0.0f, 292.0f), new Vector2(720.0f, 96.0f));
            _display.color = new Color(0.12f, 0.14f, 0.18f, 1.0f);

            CreateLabel(rootRect, "Hint", "Bボタンで 1もじ けす", 30, new Vector2(0.0f, 222.0f), new Vector2(900.0f, 46.0f));

            BuildGrid(rootRect, gridWidth);
            BuildTools(rootRect, gridWidth);
            WireNavigation();
            WireOutside();
        }

        private void BuildGrid(RectTransform parent, float gridWidth)
        {
            var gridObject = new GameObject("Keys", typeof(RectTransform), typeof(GridLayoutGroup));
            gridObject.transform.SetParent(parent, false);

            var gridRect = gridObject.GetComponent<RectTransform>();
            gridRect.anchorMin = new Vector2(0.5f, 0.5f);
            gridRect.anchorMax = new Vector2(0.5f, 0.5f);
            gridRect.pivot = new Vector2(0.5f, 1.0f);
            gridRect.anchoredPosition = new Vector2(0.0f, 180.0f);
            gridRect.sizeDelta = new Vector2(gridWidth, ROWS * _keySize + (ROWS - 1) * _keySpacing);

            var grid = gridObject.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(_keySize, _keySize);
            grid.spacing = new Vector2(_keySpacing, _keySpacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = COLUMNS;
            grid.childAlignment = TextAnchor.UpperCenter;

            for (int row = 0; row < ROWS; row++)
            {
                for (int column = 0; column < COLUMNS; column++)
                {
                    string character = GYOU[column][row].ToString();

                    if (character == " ")
                    {
                        // 文字が無いところは空けておく。詰めると並びが崩れて読みにくい
                        var blank = new GameObject("Blank", typeof(RectTransform));
                        blank.transform.SetParent(gridRect, false);
                        continue;
                    }

                    Button button = CreateKey(gridRect, character, character, 52);
                    _keys[column, row] = button;
                    if (_firstKey == null) _firstKey = button;

                    string captured = character;
                    button.onClick.AddListener(() => Append(captured));
                }
            }
        }

        private void BuildTools(RectTransform parent, float gridWidth)
        {
            var rowObject = new GameObject("Tools", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rowObject.transform.SetParent(parent, false);

            var rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0.5f, 0.5f);
            rowRect.anchorMax = new Vector2(0.5f, 0.5f);
            rowRect.pivot = new Vector2(0.5f, 1.0f);
            rowRect.anchoredPosition = new Vector2(0.0f, -350.0f);
            rowRect.sizeDelta = new Vector2(gridWidth, 100.0f);

            var layout = rowObject.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 14.0f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            AddTool(rowRect, "゛", 150.0f, () => Convert(DAKUTEN_FROM, DAKUTEN_TO));
            AddTool(rowRect, "゜", 150.0f, () => Convert(HANDAKU_FROM, HANDAKU_TO));
            AddTool(rowRect, "小", 150.0f, () => Convert(SMALL_FROM, SMALL_TO));
            AddTool(rowRect, "ー", 150.0f, () => Append("ー"));
            AddTool(rowRect, "けす", 180.0f, Backspace);
            // このパネルは文字を入れるためのもの。ここで先へ進めてしまうと、
            // 名前を見直す間もなくマルチプレイが始まってしまう。閉じるところまでにとどめる
            AddTool(rowRect, "もどる", 200.0f, () => CloseAndSelect(_nameField),
                UiSoundPlayer.SoundKind.Cancel);
            AddTool(rowRect, "けってい", 240.0f, () => CloseAndSelect(_okButton),
                UiSoundPlayer.SoundKind.Decide);
        }

        private void AddTool(RectTransform parent, string label, float width, UnityEngine.Events.UnityAction action,
            UiSoundPlayer.SoundKind soundKind = UiSoundPlayer.SoundKind.None)
        {
            Button button = CreateKey(parent, label, label, 40, soundKind);

            var element = button.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = 92.0f;
            element.flexibleWidth = 0.0f;

            button.onClick.AddListener(action);
            _tools.Add(button);
        }

        // ---- 移動のつなぎ --------------------------------

        /// <summary>
        /// 端まで行ったら反対側へ回り込むよう、行き先を手で決める。
        /// 自動任せだと『や行』の空きで止まったり、端で行き止まりになったりして操作しづらい。
        /// </summary>
        private void WireNavigation()
        {
            for (int column = 0; column < COLUMNS; column++)
            {
                for (int row = 0; row < ROWS; row++)
                {
                    Button button = _keys[column, row];
                    if (button == null) continue;

                    var navigation = new Navigation { mode = Navigation.Mode.Explicit };

                    navigation.selectOnLeft = StepInRow(row, column, -1);
                    navigation.selectOnRight = StepInRow(row, column, 1);
                    navigation.selectOnUp = StepInColumn(column, row, -1);
                    navigation.selectOnDown = StepInColumn(column, row, 1);

                    button.navigation = navigation;
                }
            }

            for (int i = 0; i < _tools.Count; i++)
            {
                var navigation = new Navigation { mode = Navigation.Mode.Explicit };

                navigation.selectOnLeft = _tools[(i - 1 + _tools.Count) % _tools.Count];
                navigation.selectOnRight = _tools[(i + 1) % _tools.Count];

                // 道具は文字の列より数が多い。対応する列だけへ戻すと、
                // はみ出したぶん(もどる・けってい)へ上下で行き来できなくなる。
                //
                // 上は近い列の一番下、下は同じ道具の列を一周させる。
                // 下でも動けるようにしておくと、行き止まりに感じない
                int column = NearestColumn(i);
                navigation.selectOnUp = LastInColumn(column);

                // 下は道具の列の中で一周させる。
                // パネルの外へ抜けると、閉じ忘れたまま先へ進めてしまう
                navigation.selectOnDown = FirstInColumn(column);

                _tools[i].navigation = navigation;
            }

            // 文字の一番下の行からは、真下の道具ではなく
            // 左端の道具から順に届くようにする。
            // こうしておけば、右へたどるだけで『けってい』まで必ず行ける
            for (int column = 0; column < COLUMNS; column++)
            {
                var last = LastInColumn(column) as Button;
                if (last == null) continue;

                Navigation navigation = last.navigation;
                navigation.selectOnDown = NearestTool(column);
                last.navigation = navigation;
            }
        }

        /// <summary>
        /// 名前入力の部品と、五十音のパネルをつなぐ。
        ///
        /// この2つは別々に組まれているので、そのままでは行き来する道が無い。
        /// 入力欄から下、決定と取り消しから上、それぞれの行き先を決めておく。
        /// </summary>
        private void WireOutside()
        {
            // パネルが閉じているときの行き来は、シーンで決めてある。
            //
            //   入力欄 ↓ もどる ←→ けってい
            //
            // ここで上書きすると、開いていない五十音のキーへ繋がってしまい、
            // 行った先で何も選べなくなる。触らない。
        }

        /// <summary>同じ行を左右にたどる。端まで来たら反対の端から続ける</summary>
        private Selectable StepInRow(int row, int column, int step)
        {
            for (int i = 1; i <= COLUMNS; i++)
            {
                int next = ((column + step * i) % COLUMNS + COLUMNS) % COLUMNS;
                if (_keys[next, row] != null) return _keys[next, row];
            }

            return null;
        }

        /// <summary>同じ列を上下にたどる。行き止まりなら道具の列へ移る</summary>
        private Selectable StepInColumn(int column, int row, int step)
        {
            for (int i = 1; i < ROWS; i++)
            {
                int next = row + step * i;
                if (next < 0 || next >= ROWS) break;

                if (_keys[column, next] != null) return _keys[column, next];
            }

            return NearestTool(column);
        }

        private Selectable FirstInColumn(int column)
        {
            for (int row = 0; row < ROWS; row++)
            {
                if (_keys[column, row] != null) return _keys[column, row];
            }

            return _firstKey;
        }

        private Selectable LastInColumn(int column)
        {
            for (int row = ROWS - 1; row >= 0; row--)
            {
                if (_keys[column, row] != null) return _keys[column, row];
            }

            return _firstKey;
        }

        private Selectable NearestTool(int column)
        {
            if (_tools.Count == 0) return null;

            int index = Mathf.RoundToInt(column / (float)(COLUMNS - 1) * (_tools.Count - 1));
            return _tools[Mathf.Clamp(index, 0, _tools.Count - 1)];
        }

        private int NearestColumn(int toolIndex)
        {
            if (_tools.Count <= 1) return 0;

            int column = Mathf.RoundToInt(toolIndex / (float)(_tools.Count - 1) * (COLUMNS - 1));
            return Mathf.Clamp(column, 0, COLUMNS - 1);
        }

        // ---- 部品作り ------------------------------------

        private Button CreateKey(RectTransform parent, string name, string label, int fontSize)
        {
            return CreateKey(parent, name, label, fontSize, UiSoundPlayer.SoundKind.None);
        }

        private Button CreateKey(
            RectTransform parent, string name, string label, int fontSize, UiSoundPlayer.SoundKind soundKind)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = Color.white;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.normalColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
            colors.highlightedColor = new Color(1.0f, 0.93f, 0.62f, 1.0f);

            // 選択中は濃くする。パッドではここが唯一の手がかりになる
            colors.selectedColor = new Color(1.0f, 0.78f, 0.20f, 1.0f);
            colors.pressedColor = new Color(1.0f, 0.65f, 0.10f, 1.0f);
            button.colors = colors;

            var textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(go.transform, false);

            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.12f, 0.14f, 0.18f, 1.0f);
            text.raycastTarget = false;
            if (_font != null) text.font = _font;

            // 文字のキーは自前で音を鳴らすので、ボタン共通の音は付けさせない
            go.AddComponent<UiButtonSoundKind>().SetKind(soundKind);

            return button;
        }

        /// <summary>入れた文字を載せる下地</summary>
        private static void CreateBox(RectTransform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject("DisplayBox", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var image = go.GetComponent<Image>();
            image.color = new Color(1.0f, 1.0f, 1.0f, 0.95f);
            image.raycastTarget = false;
        }

        private TMP_Text CreateLabel(RectTransform parent, string name, string label, int fontSize, Vector2 position, Vector2 size)
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
            text.text = label;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            if (_font != null) text.font = _font;

            return text;
        }
    }
}
