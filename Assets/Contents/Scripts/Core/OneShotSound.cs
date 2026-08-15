using UnityEngine;

namespace ProjectKMP.Core
{
    /// <summary>
    /// 場所を指定して音を1回鳴らす。
    ///
    /// Unity 標準の PlayClipAtPoint は距離による減り方がきつく、
    /// 10m も離れるとほとんど聞こえない。戦っている距離では届かない。
    /// そのうえ毎回 GameObject を作って捨てるので、連鎖のときに数が跳ねる。
    ///
    /// 鳴らす側を用意して使い回し、減り方も自分で決める。
    /// </summary>
    public class OneShotSound : MonoBehaviour
    {
        // ---- 内部状態 ------------------------------------

        private static GameObjectPool _pool;

        private AudioSource _source;
        private float _remainSec;

        // ---- 公開API -------------------------------------

        /// <summary>
        /// 指定した場所で1回鳴らす。
        /// pitchRange を入れると、鳴るたびに音程が少しばらつく。
        /// 同じ音が重なったときのうなりを避け、厚みに変えるための工夫。
        /// </summary>
        public static void Play(
            AudioClip clip, Vector3 position, float volume,
            float minDistance = 12.0f, float maxDistance = 90.0f,
            float spatialBlend = 0.55f, float pitchRange = 0.1f)
        {
            if (clip == null) return;

            if (_pool == null) _pool = new GameObjectPool(CreateOne, 8);

            GameObject go = _pool.Rent();
            go.transform.position = position;

            var sound = go.GetComponent<OneShotSound>();
            sound.Begin(clip, volume, minDistance, maxDistance, spatialBlend, pitchRange);
        }

        // ---- 内部処理 ------------------------------------

        private static GameObject CreateOne()
        {
            var go = new GameObject("OneShotSound", typeof(AudioSource), typeof(OneShotSound));

            var source = go.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.dopplerLevel = 0.0f;
            source.rolloffMode = AudioRolloffMode.Linear;

            go.GetComponent<OneShotSound>()._source = source;

            return go;
        }

        private void Begin(
            AudioClip clip, float volume, float minDistance, float maxDistance, float spatialBlend, float pitchRange)
        {
            if (_source == null) return;

            _source.clip = clip;
            _source.volume = volume;

            // 完全に位置へ寄せると遠くで聞こえなくなる。
            // 半分だけ位置に寄せて、どちらで起きたかは分かる程度に留める
            _source.spatialBlend = spatialBlend;
            _source.minDistance = minDistance;
            _source.maxDistance = maxDistance;
            _source.pitch = 1.0f + Random.Range(-pitchRange, pitchRange);

            _source.Play();

            _remainSec = clip.length / Mathf.Max(0.1f, _source.pitch) + 0.05f;
        }

        private void Update()
        {
            if (_remainSec <= 0.0f) return;

            _remainSec -= Time.unscaledDeltaTime;
            if (_remainSec > 0.0f) return;

            _remainSec = 0.0f;
            _pool.Return(gameObject);
        }
    }
}
