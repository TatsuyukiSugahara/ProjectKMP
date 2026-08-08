using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI
{
    /// <summary>
    /// UIの効果音をまとめて鳴らす。シーンにひとつ置けば、そのシーンのボタンすべてに
    /// 決定音・キャンセル音・カーソル移動音が自動でつながる(ボタン側の設定は不要)。
    ///
    /// どの音を鳴らすかはボタンの名前で振り分ける。「Back」「Quit」などはキャンセル音、
    /// 「OK」「Start」などは決定音。名前で決められないものは既定のクリック音になる。
    /// 名前に頼らず決めたい場合は、そのボタンに UiButtonSoundKind を付ける。
    ///
    /// 実行中に生成されるボタン(ロビーの参加者カードなど)は自動では拾えないので、
    /// 生成した側から Bind(button) を呼ぶこと。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class UiSoundPlayer : MonoBehaviour
    {
        /// <summary>ボタンを押したときに鳴らす音の種類</summary>
        public enum SoundKind
        {
            /// <summary>役割が決まっていないボタン</summary>
            Click,

            /// <summary>先へ進むボタン</summary>
            Decide,

            /// <summary>戻る・やめるボタン</summary>
            Cancel,
        }

        // ---- インスペクタ設定 ------------------------------

        [Header("音")]
        [SerializeField, Tooltip("役割が決まっていないボタンの音")]
        private AudioClip _clickClip;

        [SerializeField, Range(0.0f, 1.0f)] private float _clickVolume = 0.7f;

        [SerializeField, Tooltip("先へ進むボタンの音")]
        private AudioClip _decideClip;

        [SerializeField, Range(0.0f, 1.0f)] private float _decideVolume = 0.7f;

        [SerializeField, Tooltip("戻る・やめるボタンの音")]
        private AudioClip _cancelClip;

        [SerializeField, Range(0.0f, 1.0f)] private float _cancelVolume = 0.7f;

        [SerializeField, Tooltip("選択が移ったときの音")]
        private AudioClip _cursorClip;

        [SerializeField, Range(0.0f, 1.0f)] private float _cursorVolume = 0.5f;

        [Header("つなぎ方")]
        [SerializeField, Tooltip("開始時に、このシーンのボタンへ自動で音をつなぐ")]
        private bool _autoBindButtons = true;

        [SerializeField, Tooltip("マウスを乗せたときにもカーソル移動音を鳴らす")]
        private bool _playCursorOnHover = true;

        [Header("名前による振り分け")]
        [SerializeField, Tooltip("この語を名前に含むボタンはキャンセル音。大文字小文字は区別しない")]
        private string[] _cancelKeywords = new string[]
        {
            "back", "cancel", "quit", "close", "exit",
            "leave", "return", "戻", "やめ",
        };

        [SerializeField, Tooltip("この語を名前に含むボタンは決定音。キャンセルの判定が優先される")]
        private string[] _decideKeywords = new string[]
        {
            "ok", "start", "play", "single", "multi", "join", "yes", "decide",
            "confirm", "retry", "again", "next", "決定", "はじ", "すすむ",
        };

        // ---- 内部状態 ------------------------------------

        private AudioSource _source;

        /// <summary>同じボタンに二重で登録しないための控え</summary>
        private readonly HashSet<int> _boundButtonIds = new HashSet<int>();

        // ---- 公開API -------------------------------------

        /// <summary>シーンにあるUI音の再生役。無い場合は null</summary>
        public static UiSoundPlayer Instance { get; private set; }

        /// <summary>種類を指定して鳴らす</summary>
        public void Play(SoundKind kind)
        {
            switch (kind)
            {
                case SoundKind.Decide: PlayOneShot(_decideClip, _decideVolume); break;
                case SoundKind.Cancel: PlayOneShot(_cancelClip, _cancelVolume); break;
                default: PlayOneShot(_clickClip, _clickVolume); break;
            }
        }

        /// <summary>選択が移ったときの音を鳴らす</summary>
        public void PlayCursor()
        {
            PlayOneShot(_cursorClip, _cursorVolume);
        }

        /// <summary>
        /// 任意の音をこの再生役から鳴らす。決まった4種に当てはまらない音
        /// (ゲームクリアのファンファーレなど)を、鳴らす側がクリップを持って呼ぶときに使う。
        /// </summary>
        public void PlayOneShot(AudioClip clip, float volume = 1.0f)
        {
            if (clip == null || _source == null) return;

            // 連打しても前の音を切らずに重ねる
            _source.PlayOneShot(clip, volume);
        }

        /// <summary>指定したボタンに音をつなぐ。実行中に作ったボタン用</summary>
        public void Bind(Button button)
        {
            if (button == null) return;
            if (!_boundButtonIds.Add(button.GetInstanceID())) return;

            SoundKind kind = ResolveKind(button);
            button.onClick.AddListener(() => Play(kind));

            // カーソル移動音は、選ばれた瞬間とマウスが乗った瞬間に鳴らしたい。
            // onClick のようなイベントが無いので、受け取り役をその場で付ける
            var trigger = button.gameObject.GetComponent<UiCursorSoundTrigger>();
            if (trigger == null) trigger = button.gameObject.AddComponent<UiCursorSoundTrigger>();
            trigger.PlayOnHover = _playCursorOnHover;
        }

        /// <summary>いまシーンにあるボタンを探して、まとめてつなぐ。つないだ数を返す</summary>
        public int BindAll()
        {
            // 非表示のボタン(まだ開いていないメニューなど)も先につないでおく
            Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            int added = 0;
            foreach (Button button in buttons)
            {
                int before = _boundButtonIds.Count;
                Bind(button);
                if (_boundButtonIds.Count != before) added++;
            }

            return added;
        }

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            Instance = this;

            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;

            // UIの音は距離で小さくならないよう2Dで鳴らす
            _source.spatialBlend = 0.0f;
        }

        private void Start()
        {
            if (!_autoBindButtons) return;

            int count = BindAll();
            Debug.Log($"[UiSoundPlayer] ボタン {count} 個に効果音をつなぎました");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- 内部処理 ------------------------------------


        /// <summary>そのボタンで鳴らす音を決める。明示指定があればそれを優先する</summary>
        private SoundKind ResolveKind(Button button)
        {
            var explicitKind = button.GetComponent<UiButtonSoundKind>();
            if (explicitKind != null) return explicitKind.Kind;

            string name = button.name.ToLowerInvariant();

            // 「BackButton」のように両方に当たる名前もあるので、キャンセルを先に見る
            if (ContainsAny(name, _cancelKeywords)) return SoundKind.Cancel;
            if (ContainsAny(name, _decideKeywords)) return SoundKind.Decide;

            return SoundKind.Click;
        }

        private static bool ContainsAny(string lowerName, string[] keywords)
        {
            if (keywords == null) return false;

            foreach (string keyword in keywords)
            {
                if (string.IsNullOrEmpty(keyword)) continue;
                if (lowerName.Contains(keyword.ToLowerInvariant())) return true;
            }

            return false;
        }
    }
}
