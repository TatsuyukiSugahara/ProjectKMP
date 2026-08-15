using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectKMP.Core
{
    /// <summary>
    /// その日のクリアタイムの記録。
    ///
    /// 展示では『もう一回やりたい理由』が要る。順位が出るだけで、
    /// 待っている人も含めて場が盛り上がり、並び直す人が出る。
    ///
    /// 記録は端末に保存する。日付が変わったら空にするので、
    /// 展示の初日と二日目で記録が混ざらない。
    ///
    /// サーバーには置かない。展示ごとに独立していればよく、
    /// 通信を挟むと当日つながらなかったときに詰むため。
    /// </summary>
    public static class BestTimeBoard
    {
        // ---- 定数 ----------------------------------------

        private const string KEY_DATE = "BestTimeBoard.Date";
        private const string KEY_ENTRIES = "BestTimeBoard.Entries";

        /// <summary>覚えておく件数。多いと読みづらいので上位だけ</summary>
        public const int MAX_ENTRIES = 5;

        /// <summary>1件ぶんの区切り</summary>
        private const char ENTRY_SEPARATOR = '|';
        private const char FIELD_SEPARATOR = ',';

        // ---- 型 ------------------------------------------

        /// <summary>記録1件</summary>
        public readonly struct Entry
        {
            public readonly string Name;
            public readonly double Seconds;

            public Entry(string name, double seconds)
            {
                Name = name;
                Seconds = seconds;
            }
        }

        // ---- 公開API -------------------------------------

        /// <summary>
        /// 記録を1件加える。速い順に並べ替えて、上位だけを残す。
        /// 何位に入ったかを返す(1が1位)。入らなければ0。
        /// </summary>
        public static int Submit(string name, double seconds)
        {
            if (seconds <= 0.0) return 0;

            List<Entry> entries = Load();
            entries.Add(new Entry(SanitizeName(name), seconds));
            entries.Sort((a, b) => a.Seconds.CompareTo(b.Seconds));

            if (entries.Count > MAX_ENTRIES) entries.RemoveRange(MAX_ENTRIES, entries.Count - MAX_ENTRIES);

            Save(entries);

            // 同じ秒数が並んだときのために、名前も見て自分の行を探す
            for (int i = 0; i < entries.Count; i++)
            {
                if (Math.Abs(entries[i].Seconds - seconds) > 0.0001) continue;

                return i + 1;
            }

            return 0;
        }

        /// <summary>いまの記録を速い順に返す</summary>
        public static List<Entry> Load()
        {
            var entries = new List<Entry>();

            // 日付が変わっていたら忘れる。前日の記録が残っていると、その日の目標にならない
            if (PlayerPrefs.GetString(KEY_DATE, string.Empty) != Today()) return entries;

            string raw = PlayerPrefs.GetString(KEY_ENTRIES, string.Empty);
            if (string.IsNullOrEmpty(raw)) return entries;

            foreach (string line in raw.Split(ENTRY_SEPARATOR))
            {
                if (string.IsNullOrEmpty(line)) continue;

                string[] fields = line.Split(FIELD_SEPARATOR);
                if (fields.Length < 2) continue;

                if (!double.TryParse(fields[1], out double seconds)) continue;

                entries.Add(new Entry(fields[0], seconds));
            }

            return entries;
        }

        /// <summary>記録を消す。展示の入れ替えなどで使う</summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(KEY_ENTRIES);
            PlayerPrefs.DeleteKey(KEY_DATE);
            PlayerPrefs.Save();
        }

        // ---- 内部処理 ------------------------------------

        private static void Save(List<Entry> entries)
        {
            var builder = new System.Text.StringBuilder();

            foreach (Entry entry in entries)
            {
                if (builder.Length > 0) builder.Append(ENTRY_SEPARATOR);

                builder.Append(entry.Name).Append(FIELD_SEPARATOR).Append(entry.Seconds.ToString("F2"));
            }

            PlayerPrefs.SetString(KEY_DATE, Today());
            PlayerPrefs.SetString(KEY_ENTRIES, builder.ToString());
            PlayerPrefs.Save();
        }

        private static string Today()
        {
            return DateTime.Now.ToString("yyyyMMdd");
        }

        /// <summary>区切り文字が名前に混ざると読み出せなくなるので取り除く</summary>
        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "ななしさん";

            return name
                .Replace(ENTRY_SEPARATOR.ToString(), string.Empty)
                .Replace(FIELD_SEPARATOR.ToString(), string.Empty)
                .Trim();
        }
    }
}
