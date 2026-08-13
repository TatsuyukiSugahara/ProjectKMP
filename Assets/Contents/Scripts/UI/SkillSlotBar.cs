using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectKMP.UI
{
    /// <summary>
    /// 技のボタンの並びを、いまの操作機器に合わせて作り替える。
    ///
    /// タッチでは指で押す場所なので、大きいまま置いておく必要がある。
    /// キーボードとパッドでは押す必要がないので、小さくして右下へ一列に寄せ、
    /// 代わりに『何を押せばよいか』のグリフを添える。
    ///
    /// 技の名前(ビーム・必殺技など)はどの機器でも出したまま。
    /// キーやボタンだけ見せられても、初めて触る人には何の技か分からないため。
    ///
    /// タッチのときの配置はシーンに置かれたものをそのまま使う。
    /// 二重に持つと、シーンをいじったときに食い違うため。
    /// </summary>
    public class SkillSlotBar : MonoBehaviour
    {
        // ---- 設定 ----------------------------------------

        /// <summary>技1つぶんの参照</summary>
        [Serializable]
        public class Slot
        {
            [Tooltip("動かすボタン")]
            public RectTransform Target;

            [Tooltip("押せるかどうかを切り替える対象。未設定なら Target から探す")]
            public Graphic Raycast;

            [Tooltip("添えるグリフ。未設定なら添えない")]
            public InputGlyph Glyph;
        }

        [SerializeField, Tooltip("右から順に並べる。よく使う技を右(親指に近い側)へ置く")]
        private List<Slot> _slots = new List<Slot>();

        [Header("押さないときの並び")]
        [SerializeField, Min(10.0f), Tooltip("1つぶんの大きさ(px)")]
        private float _compactSize = 150.0f;

        [SerializeField, Min(0.0f), Tooltip("間隔(px)")]
        private float _compactSpacing = 20.0f;

        [SerializeField, Tooltip("画面の右下からの余白(px)")]
        private Vector2 _compactMargin = new Vector2(70.0f, 70.0f);

        // ---- 内部状態 ------------------------------------

        /// <summary>シーンに置かれていた配置。タッチのときはこれへ戻す</summary>
        private struct Pose
        {
            public Vector2 AnchorMin;
            public Vector2 AnchorMax;
            public Vector2 Pivot;
            public Vector2 Position;
            public Vector2 Size;
        }

        private readonly List<Pose> _touchPoses = new List<Pose>();

        // ---- 内部処理 ------------------------------------

        private void Awake()
        {
            foreach (Slot slot in _slots)
            {
                if (slot.Target == null) { _touchPoses.Add(default); continue; }

                _touchPoses.Add(new Pose
                {
                    AnchorMin = slot.Target.anchorMin,
                    AnchorMax = slot.Target.anchorMax,
                    Pivot = slot.Target.pivot,
                    Position = slot.Target.anchoredPosition,
                    Size = slot.Target.sizeDelta,
                });

                if (slot.Raycast == null) slot.Raycast = slot.Target.GetComponent<Graphic>();
            }
        }

        private void OnEnable()
        {
            InputModeTracker.Ensure();
            InputModeTracker.Changed += Apply;

            Apply(InputModeTracker.Current);
        }

        private void OnDisable()
        {
            InputModeTracker.Changed -= Apply;
        }

        /// <summary>いまの機器に合わせて並べ直す</summary>
        private void Apply(InputMode mode)
        {
            bool touch = mode == InputMode.Touch;

            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (slot.Target == null) continue;

                if (touch) ApplyTouch(slot, i);
                else ApplyCompact(slot, i);

                // 押せない並びのときは、指やマウスが当たっても反応しないようにする
                if (slot.Raycast != null) slot.Raycast.raycastTarget = touch;
            }
        }

        private void ApplyTouch(Slot slot, int index)
        {
            if (index >= _touchPoses.Count) return;

            Pose pose = _touchPoses[index];

            slot.Target.anchorMin = pose.AnchorMin;
            slot.Target.anchorMax = pose.AnchorMax;
            slot.Target.pivot = pose.Pivot;
            slot.Target.anchoredPosition = pose.Position;
            slot.Target.sizeDelta = pose.Size;
        }

        private void ApplyCompact(Slot slot, int index)
        {
            // 右下を基準にして、右から左へ並べる
            slot.Target.anchorMin = new Vector2(1.0f, 0.0f);
            slot.Target.anchorMax = new Vector2(1.0f, 0.0f);
            slot.Target.pivot = new Vector2(0.5f, 0.5f);
            slot.Target.sizeDelta = new Vector2(_compactSize, _compactSize);

            float step = _compactSize + _compactSpacing;
            float x = -(_compactMargin.x + _compactSize * 0.5f + index * step);
            float y = _compactMargin.y + _compactSize * 0.5f;

            slot.Target.anchoredPosition = new Vector2(x, y);
        }
    }
}
