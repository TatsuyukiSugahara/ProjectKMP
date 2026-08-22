using ProjectKMP.Battle;
using ProjectKMP.Field;
using ProjectKMP.Player;
using ProjectKMP.Presentation;
using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 咆哮ステート。HPが一定の割合を切ってフェーズが上がった瞬間に入る。
    ///
    /// 「ここから激しくなる」ことを伝えるための区切りなので、見せ場として作る。
    ///   ・息を吸って体が膨らむ(溜め)
    ///   ・吠えた瞬間に画面が光り、時間が一瞬止まり、カメラが揺れる
    ///   ・空気の振動が輪になって広がり、通ったところの草がなぎ倒れていく
    ///   ・波に飲まれたプレイヤーは後ろへ吹き飛ばされる
    ///
    /// ダメージはごく小さく、吹き飛ばしが主。倒すためではなく、
    /// 「距離を取らされて仕切り直しになる」ための技として扱う。
    /// </summary>
    public class GorillaStateRoar : IGorillaState
    {
        /// <summary>息を吸って溜める時間(秒)</summary>
        private const float INHALE_SEC = 0.45f;

        /// <summary>吠えてから波が広がりきるまでの時間(秒)</summary>
        private const float BLAST_SEC = 0.85f;

        /// <summary>溜め中のアニメーション再生速度倍率。ゆっくり見せて力を溜めている感を出す</summary>
        private const float ANIM_SPEED_MULTIPLIER = 0.5f;

        /// <summary>息を吸って体が膨らむ最大量(倍率)</summary>
        private const float INHALE_SCALE = 0.16f;

        /// <summary>吠えた瞬間に体が縦に伸びる量(倍率)。吠える形に見せる</summary>
        private const float BLAST_SCALE_Y = 0.1f;

        /// <summary>溜め中に体を細かく震わせる幅(メートル)</summary>
        private const float SHAKE_AMOUNT = 0.06f;

        /// <summary>草をなぎ倒す輪の太さ(メートル)。波の先端まわりだけを倒す</summary>
        private const float GRASS_RING_WIDTH = 3.0f;

        /// <summary>追い打ちの輪を出す回数。1本だと単発に見えるので重ねる</summary>
        private const int EXTRA_RING_COUNT = 2;

        private enum Phase
        {
            /// <summary>息を吸って溜める</summary>
            Inhale,
            /// <summary>吠えて波が広がる</summary>
            Blast,
        }

        private Phase _phase;
        private float _elapsedTime;
        private float _baseAnimatorSpeed;
        private Vector3 _baseScale;
        private Vector3 _basePosition;

        /// <summary>いま波の先端が届いている距離(メートル)。草をなぎ倒す輪の外側になる</summary>
        private float _waveRadius;

        private bool _hasKnockedBack;
        private int _spawnedExtraRings;

        public void Enter(GorillaAI owner)
        {
            _phase = Phase.Inhale;
            _elapsedTime = 0.0f;
            _waveRadius = 0.0f;
            _hasKnockedBack = false;
            _spawnedExtraRings = 0;

            _baseScale = owner.transform.localScale;
            _basePosition = owner.transform.position;

            _baseAnimatorSpeed = owner.Animator != null ? owner.Animator.speed : 1.0f;
            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed * ANIM_SPEED_MULTIPLIER;

            owner.PlayAnimation(GorillaAI.ANIM_ROAR);

            Debug.Log($"[Gorilla] フェーズ {owner.Phase} へ移行");
        }

        public void Update(GorillaAI owner)
        {
            _elapsedTime += Time.deltaTime;

            if (_phase == Phase.Inhale) UpdateInhale(owner);
            else UpdateBlast(owner);
        }

        public void Exit(GorillaAI owner)
        {
            // 途中で抜けても膨らんだままにならないよう必ず戻す
            owner.transform.localScale = _baseScale;
            owner.transform.position = _basePosition;

            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed;
        }

        // ---- 溜め ----------------------------------------

        /// <summary>息を吸う。体が膨らみながら震えて、来るぞという間を作る</summary>
        private void UpdateInhale(GorillaAI owner)
        {
            float rate = Mathf.Clamp01(_elapsedTime / INHALE_SEC);

            owner.transform.localScale = _baseScale * (1.0f + INHALE_SCALE * rate);

            Vector2 jitter = Random.insideUnitCircle * (SHAKE_AMOUNT * rate);
            owner.transform.position = _basePosition + new Vector3(jitter.x, 0.0f, jitter.y);

            if (_elapsedTime < INHALE_SEC) return;

            BeginBlast(owner);
        }

        // ---- 咆哮 ----------------------------------------

        /// <summary>吠えた瞬間。画面・時間・カメラ・プレイヤーをまとめて動かす</summary>
        private void BeginBlast(GorillaAI owner)
        {
            _phase = Phase.Blast;
            _elapsedTime = 0.0f;

            owner.transform.position = _basePosition;
            if (owner.Animator != null) owner.Animator.speed = _baseAnimatorSpeed;

            Color color = ThemeColor(owner);

            // 息を吐き切った形。縦に伸びて、横は締まる
            owner.transform.localScale = Vector3.Scale(_baseScale, new Vector3(0.94f, 1.0f + BLAST_SCALE_Y, 0.94f));

            // 一瞬だけ時間を止めて、吠えた瞬間に重みを出す
            HitStop.Play(0.06f, 0.08f, 0.1f);
            ScreenFlash.Play(new Color(color.r, color.g, color.b, 0.35f), 0.22f);
            HitFlash.Play(owner.transform, color, 0.5f, 1.0f);
            Onomatopoeia.Play(
                owner.transform.position + Vector3.up * 3.5f, "ゴアアアッ", color, owner.Phase >= 4 ? 1.5f : 1.2f, 0.9f);

            ShakeCamera(owner);
            BgmPlayer.Duck(owner.Phase >= 4 ? 0.7f : 0.5f, 0.2f, 0.6f);

            ShockwaveRing.Play(_basePosition, color, owner.RoarRadius, BLAST_SEC * 0.8f, 1.4f);
        }

        /// <summary>波が外へ広がっていく。草をなぎ倒し、飲まれたプレイヤーを吹き飛ばす</summary>
        private void UpdateBlast(GorillaAI owner)
        {
            float rate = Mathf.Clamp01(_elapsedTime / BLAST_SEC);

            // 吠えきった体をゆっくり元へ戻す
            Vector3 blastScale = Vector3.Scale(_baseScale, new Vector3(0.94f, 1.0f + BLAST_SCALE_Y, 0.94f));
            owner.transform.localScale = Vector3.Lerp(blastScale, _baseScale, rate);

            UpdateWave(owner);
            SpawnExtraRings(owner, rate);

            if (_elapsedTime < BLAST_SEC) return;

            if (owner.IsPlayerLost()) owner.ChangeState(new GorillaStatePatrol());
            else owner.ChangeState(new GorillaStateChase());
        }

        /// <summary>
        /// 空気の振動として、輪を外へ広げていく。
        /// 通り過ぎたところの草だけをなぎ倒すので、波が抜けていくのが目で追える。
        /// </summary>
        private void UpdateWave(GorillaAI owner)
        {
            float previousRadius = _waveRadius;
            _waveRadius = Mathf.Min(owner.RoarRadius, _waveRadius + owner.RoarWaveSpeed * Time.deltaTime);

            if (_waveRadius > previousRadius)
            {
                // 波の先端まわりだけを倒す。内側は前のフレームで倒し終わっている
                float inner = Mathf.Max(0.0f, previousRadius - GRASS_RING_WIDTH);
                GrassField.FlattenRingAt(_basePosition, inner, _waveRadius, 1.0f);
            }

            // 波が届いた瞬間に、自分が操作しているプレイヤーだけ吹き飛ばす
            TryKnockbackLocalPlayer(owner);
        }

        /// <summary>追い打ちの輪を時間差で重ねる。1本だけだと空気が震えているように見えない</summary>
        private void SpawnExtraRings(GorillaAI owner, float rate)
        {
            if (_spawnedExtraRings >= EXTRA_RING_COUNT) return;

            // 0.25 と 0.5 のタイミングで1本ずつ増やす
            float nextAt = 0.25f * (_spawnedExtraRings + 1);
            if (rate < nextAt) return;

            _spawnedExtraRings++;
            ShockwaveRing.Play(
                _basePosition, ThemeColor(owner),
                owner.RoarRadius * (0.55f + 0.2f * _spawnedExtraRings), BLAST_SEC * 0.5f, 0.7f);
        }

        // ---- プレイヤーへの影響 --------------------------

        /// <summary>
        /// 自分が操作しているローカルプレイヤーだけを対象に、波が届いたら吹き飛ばす。
        /// (他の攻撃と同じ方式。各自が自分のぶんだけ判定することで多重ヒットを避ける)
        /// ダメージは小さく、後ろへ大きく飛ばすことで「仕切り直し」を作るのが狙い。
        /// </summary>
        private void TryKnockbackLocalPlayer(GorillaAI owner)
        {
            if (_hasKnockedBack) return;

            PlayerAttack localAttack = PlayerAttack.Local;
            if (localAttack == null) return;

            PlayerHealth localHealth = localAttack.GetComponent<PlayerHealth>();
            if (localHealth == null || localHealth.IsDead) return;

            Vector3 toPlayer = localHealth.transform.position - _basePosition;
            toPlayer.y = 0.0f;
            float distance = toPlayer.magnitude;

            // 波の先端がまだ届いていない
            if (distance > _waveRadius) return;

            // 範囲の外にいた場合は、波が広がりきっても当たらない
            if (distance > owner.RoarRadius) { _hasKnockedBack = true; return; }

            _hasKnockedBack = true;

            // 近いほど強く飛ばす。端でかすっただけなら軽く押される程度にする
            float closeness = 1.0f - Mathf.Clamp01(distance / Mathf.Max(0.01f, owner.RoarRadius));
            float knockback = owner.RoarKnockbackDistance * Mathf.Lerp(0.4f, 1.0f, closeness);

            localHealth.ApplyDamage(
                owner.RoarDamage, -1, _basePosition,
                knockback, owner.RoarKnockbackDurationSec, owner.RoarKnockbackArcHeight);
        }

        // ---- 演出の小物 ----------------------------------

        /// <summary>カメラを揺らす。空気が震えている感じを画面側からも出す</summary>
        private void ShakeCamera(GorillaAI owner)
        {
            var camera = Object.FindAnyObjectByType<ThirdPersonCamera>();
            if (camera == null) return;

            camera.Shake(owner.RoarCameraShake, BLAST_SEC * 0.7f);
        }

        /// <summary>フェーズが進むほど赤く。何段階目かが見ただけで伝わるようにする</summary>
        private static Color ThemeColor(GorillaAI owner)
        {
            return owner.Phase >= 4
                ? new Color(1.0f, 0.22f, 0.12f, 1.0f)
                : new Color(1.0f, 0.7f, 0.18f, 1.0f);
        }
    }
}
