using UnityEngine;

namespace ProjectKMP.Gorilla
{
    /// <summary>
    /// 破壊光線の見た目を、始点・方向・長さ・太さに合わせて調整する。
    /// 外側の柔らかいグロー(Glow)と、内側の明るい芯(Core)の2枚のLineRendererを重ねて
    /// ビームらしい厚みを出す。テクスチャをUVスクロールさせてエネルギーが流れる演出にする。
    /// さらに任意で、螺旋リボン・照り返しライト・先端フレア・マズルフラッシュを足せる
    /// (いずれも既定値では無効なので、使いたいプレハブ側だけで値を入れる)。
    /// 発射終了時は FadeOut() を呼ぶことで、パッと消えず徐々に透明になってから自分で消える。
    /// </summary>
    public class DestructionBeamVisual : MonoBehaviour
    {
        // ---- 定数 ----------------------------------------

        private const float HELIX_POINTS_PER_METER = 10f;
        private const int HELIX_MAX_POINTS = 160;

        // ---- インスペクタ設定(本体) ------------------------

        [SerializeField, Tooltip("外側の柔らかいグロー部分")]
        private LineRenderer _glowLine;

        [SerializeField, Tooltip("内側の明るい芯の部分")]
        private LineRenderer _coreLine;

        [SerializeField, Tooltip("芯の太さをグローの太さに対してどれくらいの割合にするか")]
        private float _coreWidthRatio = 0.4f;

        [SerializeField, Min(0.05f), Tooltip("見た目の太さを当たり判定の太さの何倍にするか。1未満にすると帯が細くなり、下側が地面にめり込んで欠けるのを防げる(当たり判定は変わらない)")]
        private float _visualWidthScale = 1.0f;

        [SerializeField, Tooltip("テクスチャが流れる速さ(UVスクロール)")]
        private float _scrollSpeed = 3.0f;

        [SerializeField, Min(0f), Tooltip("芯のスクロールをグローの何倍の速さにするか。上げるほどエネルギーが噴き出す勢いが出る")]
        private float _coreScrollMultiplier = 1.6f;

        [SerializeField, Range(0f, 1f), Tooltip("芯の色をどれだけ白熱色(白)に寄せるか")]
        private float _coreWhiteHot = 0f;

        [SerializeField, Min(1f), Tooltip("芯の明るさの倍率。Bloomを強く出したいときに上げる")]
        private float _coreIntensityBoost = 1f;

        [SerializeField, Tooltip("光線がゆらめく速さ")]
        private float _pulseSpeed = 14f;

        [SerializeField, Tooltip("光線の太さの揺れ幅(太さに対する割合)")]
        private float _pulseAmount = 0.12f;

        [Header("太さの形")]
        [SerializeField, Min(0.05f), Tooltip("発射口の太さ倍率")]
        private float _startWidthRatio = 1.0f;

        [SerializeField, Min(0.05f), Tooltip("中央の太さ倍率。1より小さくするとくびれる")]
        private float _midWidthRatio = 0.85f;

        [SerializeField, Min(0.05f), Tooltip("着弾点の太さ倍率。1より大きくすると先が広がる")]
        private float _endWidthRatio = 0.7f;

        [Header("輪郭のゆらぎ")]
        [SerializeField, Range(2, 64), Tooltip("光線を分ける点の数。増やすほど細かく揺らせる。2だと直線のまま")]
        private int _segmentCount = 2;

        [SerializeField, Min(0.0f), Tooltip("横に揺れる幅を見た目の半径の何倍にするか。0で揺れない")]
        private float _wobbleAmount = 0.0f;

        [SerializeField, Tooltip("揺れが流れる速さ")]
        private float _wobbleSpeed = 6.0f;

        [SerializeField, Min(0.05f), Tooltip("揺れの細かさ(1mあたりの波の数)")]
        private float _wobbleFrequency = 0.8f;

        [Header("パーティクル(任意)")]
        [SerializeField, Tooltip("光線に沿ってきらきら飛び散るパーティクル。発生範囲と量が光線の長さに合わせて自動で伸びる。未設定でもよい")]
        private ParticleSystem[] _alongBeamParticles;

        [SerializeField, Min(0f), Tooltip("光線1mあたりの粒の発生量(毎秒)。沿いパーティクルにのみ使う")]
        private float _sparkleRatePerMeter = 8f;

        [SerializeField, Tooltip("光線の先端に追従するパーティクル(先端の光球など)。未設定でもよい")]
        private ParticleSystem[] _tipParticles;

        [SerializeField, Min(0f), Tooltip("先端パーティクルの粒の大きさを光線の半径の何倍にするか。0なら大きさに手を触れない")]
        private float _tipParticleSizeRatio = 0f;

        [SerializeField, Tooltip("発射口(根元)に置くパーティクル。マズルフラッシュ用。未設定でもよい")]
        private ParticleSystem[] _muzzleParticles;

        [Header("螺旋リボン(任意)")]
        [SerializeField, Min(0), Tooltip("ビームに巻きつく螺旋リボンの本数。0で無効")]
        private int _helixStrandCount = 0;

        [SerializeField, Tooltip("螺旋の材質。未設定なら芯(Core)のマテリアルを複製して使う")]
        private Material _helixMaterial;

        [SerializeField, Tooltip("螺旋の色")]
        private Color _helixColor = new Color(0.7f, 0.9f, 1f, 1f);

        [SerializeField, Min(0f), Tooltip("螺旋の巻き半径を光線の見た目の半径の何倍にするか")]
        private float _helixRadiusRatio = 0.95f;

        [SerializeField, Min(0f), Tooltip("1mあたり何周巻くか")]
        private float _helixTurnsPerMeter = 0.45f;

        [SerializeField, Tooltip("螺旋が流れる速さ")]
        private float _helixScrollSpeed = 6f;

        [SerializeField, Min(0f), Tooltip("螺旋のリボンの太さを光線の見た目の直径の何倍にするか")]
        private float _helixWidthRatio = 0.18f;

        [SerializeField, Min(0.01f), Tooltip("根元から何mかけて螺旋を軸から離すか。発射口では軸に寄っている方が自然")]
        private float _helixEmergeMeters = 1.2f;

        [Header("照り返しライト(任意)")]
        [SerializeField, Min(0), Tooltip("ビームに沿って置くポイントライトの数。0で無効。URPの同時ライト数上限に注意")]
        private int _beamLightCount = 0;

        [SerializeField, Tooltip("照り返しライトの色")]
        private Color _beamLightColor = new Color(0.5f, 0.8f, 1f, 1f);

        [SerializeField, Min(0f), Tooltip("照り返しライトの明るさ")]
        private float _beamLightIntensity = 6f;

        [SerializeField, Min(0f), Tooltip("照り返しライトの届く距離を光線の見た目の半径の何倍にするか")]
        private float _beamLightRangeRatio = 5f;

        [SerializeField, Range(0f, 1f), Tooltip("ライトの明滅の揺れ幅")]
        private float _lightFlickerAmount = 0.25f;

        [SerializeField, Tooltip("ライトの明滅の速さ")]
        private float _lightFlickerSpeed = 22f;

        [Header("先端フレア / マズルフラッシュ(任意)")]
        [SerializeField, Min(0f), Tooltip("先端に置くライトの明るさ。0で無効")]
        private float _tipLightIntensity = 0f;

        [SerializeField, Tooltip("先端ライトの色")]
        private Color _tipLightColor = new Color(0.85f, 0.95f, 1f, 1f);

        [SerializeField, Min(0f), Tooltip("先端ライトの届く距離を光線の見た目の半径の何倍にするか")]
        private float _tipLightRangeRatio = 7f;

        [SerializeField, Min(0f), Tooltip("発射口に置くライトの明るさ。0で無効")]
        private float _muzzleLightIntensity = 0f;

        [SerializeField, Tooltip("発射口ライトの色")]
        private Color _muzzleLightColor = new Color(0.8f, 0.92f, 1f, 1f);

        [SerializeField, Min(0f), Tooltip("発射口ライトの届く距離を光線の見た目の半径の何倍にするか")]
        private float _muzzleLightRangeRatio = 5f;

        [SerializeField, Min(0f), Tooltip("発射の瞬間だけ発射口を強く光らせる時間(秒)")]
        private float _muzzleFlashDurationSec = 0.18f;

        [SerializeField, Min(0f), Tooltip("発射の瞬間の閃光の強さ(通常の明るさに対する上乗せ倍率)")]
        private float _muzzleFlashBoost = 3f;

        // ---- 内部状態 ------------------------------------

        private float _baseGlowWidth = 1f;
        private float _baseCoreWidth = 1f;
        private Material _glowMaterialInstance;
        private Material _coreMaterialInstance;
        private Material _helixMaterialInstance;

        // マテリアル本来の色。URP/Unlit などは頂点カラーを見ないので、色とフェードはこちらで操作する
        private Color _glowBaseMaterialColor = Color.white;
        private Color _coreBaseMaterialColor = Color.white;
        private Color _helixBaseMaterialColor = Color.white;

        private Color _glowStartColor;
        private Color _glowEndColor;
        private Color _coreStartColor;
        private Color _coreEndColor;

        private bool _isFadingOut;
        private float _fadeElapsed;
        private float _fadeDuration;

        // 螺旋やライトを毎フレーム動かすために、最後に指定された配置を覚えておく
        private bool _hasGeometry;
        private Vector3 _origin = Vector3.zero;
        private Vector3 _direction = Vector3.forward;
        private float _length;
        private float _visualRadius = 0.5f;

        private float _currentPulse = 1f;
        private float _visibilityMul = 1f;

        private AnimationCurve _widthCurve;
        private LineRenderer[] _helixLines;
        private float _helixPhase;

        private Light[] _beamLights;
        private Light _tipLight;
        private Light _muzzleLight;
        private float _muzzleFlashRemainSec;

        // ---- Unityイベント -------------------------------

        private void Awake()
        {
            _widthCurve = BuildWidthCurve();

            if (_glowLine != null)
            {
                _glowMaterialInstance = _glowLine.material;
                _glowStartColor = _glowLine.startColor;
                _glowEndColor = _glowLine.endColor;
                _glowBaseMaterialColor = GetMaterialColor(_glowMaterialInstance);
            }

            if (_coreLine != null)
            {
                _coreMaterialInstance = _coreLine.material;
                _coreStartColor = _coreLine.startColor;
                _coreEndColor = _coreLine.endColor;

                // 芯を白熱色・高輝度に寄せる調整は最初に1回だけマテリアルへ焼き込む
                _coreBaseMaterialColor = ApplyCoreLook(GetMaterialColor(_coreMaterialInstance));
                SetMaterialColor(_coreMaterialInstance, _coreBaseMaterialColor);
            }

            CreateHelixLines();
            CreateLights();

            // 生成された瞬間が発射の瞬間なので、そこから閃光を減衰させる
            _muzzleFlashRemainSec = _muzzleFlashDurationSec;
        }

        // ---- 公開API -------------------------------------

        /// <summary>始点・方向(正規化不要)・長さ・太さ(半径)を指定して光線の見た目を配置する</summary>
        public void Configure(Vector3 origin, Vector3 direction, float length, float radius)
        {
            _origin = origin;
            _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            _length = Mathf.Max(0f, length);

            // 当たり判定の半径とは別に、見た目だけを細くできるようにする
            _visualRadius = Mathf.Max(0.005f, radius * _visualWidthScale);
            _hasGeometry = true;

            _baseGlowWidth = Mathf.Max(0.01f, _visualRadius * 2f);
            _baseCoreWidth = Mathf.Max(0.01f, _baseGlowWidth * _coreWidthRatio);

            ApplyGeometry(_currentPulse);
        }

        /// <summary>
        /// 光線の色をあとから塗り替える。合体ビームのように、
        /// 撃っている最中に見た目を変えたいときに使う。
        /// 変えるのは色味だけで、透明度・太さ・流れる速さには手を触れない。
        /// </summary>
        public void OverrideColor(Color color)
        {
            // 透明度は元のまま残す。ここを塗り替えると根元と先端の濃淡が崩れる
            Color Tint(Color original) => new Color(color.r, color.g, color.b, original.a);

            _glowStartColor = Tint(_glowStartColor);
            _glowEndColor = Tint(_glowEndColor);
            if (_glowLine != null)
            {
                _glowLine.startColor = _glowStartColor;
                _glowLine.endColor = _glowEndColor;
            }

            _glowBaseMaterialColor = Tint(_glowBaseMaterialColor);
            SetMaterialColor(_glowMaterialInstance, _glowBaseMaterialColor);

            _coreStartColor = Tint(_coreStartColor);
            _coreEndColor = Tint(_coreEndColor);
            if (_coreLine != null)
            {
                _coreLine.startColor = _coreStartColor;
                _coreLine.endColor = _coreEndColor;
            }

            // 芯は白熱寄せをかけ直す。素の色をそのまま入れると芯だけ暗く沈む
            _coreBaseMaterialColor = ApplyCoreLook(Tint(_coreBaseMaterialColor));
            SetMaterialColor(_coreMaterialInstance, _coreBaseMaterialColor);

            _helixColor = Tint(_helixColor);
            _helixBaseMaterialColor = Tint(_helixBaseMaterialColor);
            SetMaterialColor(_helixMaterialInstance, _helixBaseMaterialColor);

            if (_helixLines != null)
            {
                foreach (var line in _helixLines)
                {
                    if (line == null) continue;
                    line.startColor = _helixColor;
                    line.endColor = HelixEndColor(1f);
                }
            }

            // 照り返しも変えないと、光線だけ色が変わって地面が前の色のまま残る
            if (_beamLights != null)
            {
                foreach (var light in _beamLights)
                {
                    if (light != null) light.color = color;
                }
            }

            if (_tipLight != null) _tipLight.color = color;
            if (_muzzleLight != null) _muzzleLight.color = color;
        }

        /// <summary>徐々に透明にしてから自分自身を破棄する(パッと消えないようにする)</summary>
        public void FadeOut(float duration)
        {
            if (_isFadingOut) return;

            _isFadingOut = true;
            _fadeElapsed = 0f;
            _fadeDuration = Mathf.Max(0.01f, duration);

            // 新しい粒子は出さず、すでに出ている粒子だけ自然に消えるようにする
            var particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in particleSystems)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        // ---- 毎フレームの更新 ------------------------------

        private void Update()
        {
            if (_isFadingOut)
            {
                UpdateFadeOut();
                return;
            }

            // 発射中、少し脈打つように太さを揺らして電力っぽさを出す
            _currentPulse = 1f + Mathf.Sin(Time.time * _pulseSpeed) * _pulseAmount;
            _visibilityMul = 1f;
            _helixPhase += _helixScrollSpeed * Time.deltaTime;

            ApplyGeometry(_currentPulse);
            UpdateLightIntensity();

            // テクスチャを流してエネルギーが噴き出しているように見せる
            float scroll = Time.time * _scrollSpeed;
            if (_glowMaterialInstance != null) _glowMaterialInstance.mainTextureOffset = new Vector2(-scroll, 0f);
            if (_coreMaterialInstance != null) _coreMaterialInstance.mainTextureOffset = new Vector2(-scroll * _coreScrollMultiplier, 0f);
            if (_helixMaterialInstance != null) _helixMaterialInstance.mainTextureOffset = new Vector2(-scroll * _coreScrollMultiplier, 0f);
        }

        private void UpdateFadeOut()
        {
            _fadeElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_fadeElapsed / _fadeDuration);

            // 直線的にではなく、じわっと(最初はゆっくり、後半で消えきる)透明になるようにイーズをかける
            float eased = t * t * (3f - 2f * t); // smoothstep
            float alphaMul = 1f - eased;

            // 太さは0まで縮め切る(途中で破棄すると最後がプツッと切れて見えるため)
            float widthMul = 1f - eased;
            _visibilityMul = alphaMul;

            if (_glowLine != null)
            {
                _glowLine.startColor = MultiplyAlpha(_glowStartColor, alphaMul);
                _glowLine.endColor = MultiplyAlpha(_glowEndColor, alphaMul);
            }

            if (_coreLine != null)
            {
                _coreLine.startColor = MultiplyAlpha(_coreStartColor, alphaMul);
                _coreLine.endColor = MultiplyAlpha(_coreEndColor, alphaMul);
            }

            // 太さは形を保ったまま細らせたいので、配置ごと作り直す
            ApplyGeometry(widthMul);

            // マテリアル側の不透明度も一緒に下げる(頂点カラーが効かないシェーダー対策)
            FadeMaterialAlpha(_glowMaterialInstance, _glowBaseMaterialColor, alphaMul);
            FadeMaterialAlpha(_coreMaterialInstance, _coreBaseMaterialColor, alphaMul);

            // 螺旋とライトも本体と一緒に消す(片方だけ残ると浮いて見える)
            FadeHelixColor(alphaMul);
            UpdateHelix(widthMul);
            UpdateLightIntensity();

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }

        // ---- 配置の反映 ------------------------------------

        private void ApplyGeometry(float widthMul)
        {
            if (!_hasGeometry) return;

            Vector3 end = _origin + _direction * _length;

            SetLine(_glowLine, _baseGlowWidth * widthMul);
            SetLine(_coreLine, _baseCoreWidth * widthMul);

            UpdateParticles(end);
            UpdateHelix(widthMul);
            UpdateLightPosition(end);
        }

        /// <summary>
        /// 光線を分割して並べ、太さはカーブで決める。
        /// カーブにすることで「発射口も着弾点も太く、中央がくびれる」形が作れる。
        /// 分割した点を横へ振ると輪郭がゆらぐが、根元と先端は動かさない
        /// (発射口と着弾点がぶれると、狙いがずれて見えてしまうため)。
        /// </summary>
        private void SetLine(LineRenderer line, float width)
        {
            if (line == null) return;

            int count = Mathf.Clamp(_segmentCount, 2, 64);

            line.useWorldSpace = true;
            line.positionCount = count;
            line.widthCurve = _widthCurve;
            line.widthMultiplier = width;

            bool wobble = _wobbleAmount > 0.0f && count > 2;

            Vector3 side = Vector3.zero;
            Vector3 up = Vector3.zero;
            if (wobble)
            {
                side = Vector3.Cross(_direction, Vector3.up);
                if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(_direction, Vector3.right);
                side.Normalize();
                up = Vector3.Cross(side, _direction).normalized;
            }

            for (int i = 0; i < count; i++)
            {
                float t = count <= 1 ? 0.0f : (float)i / (count - 1);
                float distance = t * _length;
                Vector3 point = _origin + _direction * distance;

                if (wobble)
                {
                    // 両端で0、中央で最大になるように振幅を絞る
                    float taper = Mathf.Sin(t * Mathf.PI);
                    float amplitude = _visualRadius * _wobbleAmount * taper;
                    float phase = distance * _wobbleFrequency * Mathf.PI * 2.0f - Time.time * _wobbleSpeed;

                    // 縦横で周期をずらすと、平面的な蛇行ではなく乱れた流れに見える
                    point += side * (Mathf.Sin(phase) * amplitude);
                    point += up * (Mathf.Cos(phase * 0.7f) * amplitude * 0.6f);
                }

                line.SetPosition(i, point);
            }
        }

        /// <summary>発射口・中央・着弾点の3点から太さのカーブを作る</summary>
        private AnimationCurve BuildWidthCurve()
        {
            return new AnimationCurve(
                new Keyframe(0.0f, _startWidthRatio),
                new Keyframe(0.5f, _midWidthRatio),
                new Keyframe(1.0f, _endWidthRatio));
        }

        /// <summary>
        /// パーティクルを光線の今の長さに合わせる。
        /// 沿いパーティクルは発生範囲(Box)を根元から先端まで引き伸ばし、量も長さに比例させる。
        /// 先端・発射口のパーティクルはそれぞれの位置へ動かす。
        /// </summary>
        private void UpdateParticles(Vector3 end)
        {
            if (_alongBeamParticles != null)
            {
                foreach (var ps in _alongBeamParticles)
                {
                    if (ps == null) continue;

                    var shape = ps.shape;
                    shape.scale = new Vector3(_visualRadius * 2f, _visualRadius * 2f, Mathf.Max(0.01f, _length));
                    shape.position = new Vector3(0f, 0f, _length * 0.5f);

                    var emission = ps.emission;
                    emission.rateOverTimeMultiplier = _sparkleRatePerMeter * _length;
                }
            }

            if (_tipParticles != null)
            {
                foreach (var ps in _tipParticles)
                {
                    if (ps == null) continue;

                    ps.transform.position = end;

                    if (_tipParticleSizeRatio > 0f)
                    {
                        var main = ps.main;
                        main.startSizeMultiplier = _visualRadius * _tipParticleSizeRatio;
                    }

                    // 伸びきる前は先端が根元と重なってしまうので、少し伸びてから出す
                    var emission = ps.emission;
                    emission.enabled = _length > _visualRadius;
                }
            }

            if (_muzzleParticles != null)
            {
                foreach (var ps in _muzzleParticles)
                {
                    if (ps == null) continue;
                    ps.transform.position = _origin;
                }
            }
        }

        // ---- 螺旋リボン ------------------------------------

        private void CreateHelixLines()
        {
            if (_helixStrandCount <= 0) return;

            Material source = _helixMaterial != null
                ? _helixMaterial
                : (_coreLine != null ? _coreLine.sharedMaterial : null);

            // 材質が無いと描けないので、その場合は螺旋そのものを作らない
            if (source == null) return;

            _helixMaterialInstance = new Material(source);

            // 頂点カラーが効かないシェーダーでも色が乗るよう、マテリアル側に色を入れる
            _helixBaseMaterialColor = _helixColor;
            SetMaterialColor(_helixMaterialInstance, _helixColor);

            _helixLines = new LineRenderer[_helixStrandCount];

            for (int i = 0; i < _helixStrandCount; i++)
            {
                var go = new GameObject("HelixStrand" + i);
                go.transform.SetParent(transform, false);

                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.sharedMaterial = _helixMaterialInstance;
                // Tile だとテクスチャが繰り返されて塊が並んで見えるので、1枚を引き伸ばす
                line.textureMode = LineTextureMode.Stretch;
                line.alignment = LineAlignment.View;
                line.numCapVertices = 2;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.positionCount = 0;
                line.startColor = _helixColor;
                line.endColor = HelixEndColor(1f);

                _helixLines[i] = line;
            }
        }

        private void UpdateHelix(float widthMul)
        {
            if (_helixLines == null || !_hasGeometry) return;

            int pointCount = Mathf.Clamp(
                Mathf.CeilToInt(_length * HELIX_POINTS_PER_METER), 2, HELIX_MAX_POINTS);

            float helixRadius = _visualRadius * _helixRadiusRatio;
            float width = Mathf.Max(0.001f, _visualRadius * 2f * _helixWidthRatio * widthMul);

            // ビームの軸に垂直な2本の基準ベクトルを作る(この平面上で円を描く)
            Vector3 side = Vector3.Cross(_direction, Vector3.up);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(_direction, Vector3.right);
            side.Normalize();
            Vector3 up = Vector3.Cross(side, _direction).normalized;

            for (int s = 0; s < _helixLines.Length; s++)
            {
                LineRenderer line = _helixLines[s];
                if (line == null) continue;

                line.positionCount = pointCount;
                line.startWidth = width;
                line.endWidth = width * 0.6f;

                float strandOffset = _helixLines.Length <= 1
                    ? 0f
                    : Mathf.PI * 2f * s / _helixLines.Length;

                for (int i = 0; i < pointCount; i++)
                {
                    float t = pointCount <= 1 ? 0f : (float)i / (pointCount - 1);
                    float distance = t * _length;
                    float angle = distance * _helixTurnsPerMeter * Mathf.PI * 2f + strandOffset - _helixPhase;

                    // 発射口では軸に寄せておき、少し進んでから巻きつかせる
                    float emerge = Mathf.Clamp01(distance / Mathf.Max(0.01f, _helixEmergeMeters));
                    Vector3 radial = (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * helixRadius * emerge;

                    line.SetPosition(i, _origin + _direction * distance + radial);
                }
            }
        }

        private void FadeHelixColor(float alphaMul)
        {
            FadeMaterialAlpha(_helixMaterialInstance, _helixBaseMaterialColor, alphaMul);

            if (_helixLines == null) return;

            foreach (var line in _helixLines)
            {
                if (line == null) continue;
                line.startColor = MultiplyAlpha(_helixColor, alphaMul);
                line.endColor = HelixEndColor(alphaMul);
            }
        }

        private Color HelixEndColor(float alphaMul)
        {
            return new Color(_helixColor.r, _helixColor.g, _helixColor.b, _helixColor.a * 0.2f * alphaMul);
        }

        // ---- ライト ----------------------------------------

        private void CreateLights()
        {
            if (_beamLightCount > 0 && _beamLightIntensity > 0f)
            {
                _beamLights = new Light[_beamLightCount];
                for (int i = 0; i < _beamLightCount; i++)
                {
                    _beamLights[i] = CreateLight("BeamLight" + i, _beamLightColor, _beamLightIntensity);
                }
            }

            if (_tipLightIntensity > 0f) _tipLight = CreateLight("TipLight", _tipLightColor, _tipLightIntensity);
            if (_muzzleLightIntensity > 0f) _muzzleLight = CreateLight("MuzzleLight", _muzzleLightColor, _muzzleLightIntensity);
        }

        private Light CreateLight(string lightName, Color color, float intensity)
        {
            var go = new GameObject(lightName);
            go.transform.SetParent(transform, false);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;

            // 影は落とさない(ビームの照り返しなので負荷に見合わない)
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            return light;
        }

        private void UpdateLightPosition(Vector3 end)
        {
            if (_beamLights != null)
            {
                float range = Mathf.Max(0.1f, _visualRadius * _beamLightRangeRatio);
                for (int i = 0; i < _beamLights.Length; i++)
                {
                    Light light = _beamLights[i];
                    if (light == null) continue;

                    // 根元と先端に寄りすぎないよう、区間の中央へ等間隔に置く
                    float t = _beamLights.Length <= 1 ? 0.5f : (i + 0.5f) / _beamLights.Length;
                    light.transform.position = _origin + _direction * (_length * t);
                    light.range = range;
                }
            }

            if (_tipLight != null)
            {
                _tipLight.transform.position = end;
                _tipLight.range = Mathf.Max(0.1f, _visualRadius * _tipLightRangeRatio);
            }

            if (_muzzleLight != null)
            {
                _muzzleLight.transform.position = _origin;
                _muzzleLight.range = Mathf.Max(0.1f, _visualRadius * _muzzleLightRangeRatio);
            }
        }

        private void UpdateLightIntensity()
        {
            float flicker = 1f + Mathf.Sin(Time.time * _lightFlickerSpeed) * _lightFlickerAmount;

            if (_beamLights != null)
            {
                foreach (var light in _beamLights)
                {
                    if (light != null) light.intensity = _beamLightIntensity * flicker * _visibilityMul;
                }
            }

            if (_tipLight != null)
            {
                _tipLight.intensity = _tipLightIntensity * flicker * _visibilityMul;
            }

            if (_muzzleLight != null)
            {
                // 発射の瞬間だけ強く光らせ、二次関数的にすばやく通常の明るさへ落とす
                float boost = 1f;
                if (_muzzleFlashRemainSec > 0f && _muzzleFlashDurationSec > 0f)
                {
                    _muzzleFlashRemainSec -= Time.deltaTime;
                    float t = Mathf.Clamp01(_muzzleFlashRemainSec / _muzzleFlashDurationSec);
                    boost = 1f + _muzzleFlashBoost * t * t;
                }

                _muzzleLight.intensity = _muzzleLightIntensity * boost * flicker * _visibilityMul;
            }
        }

        // ---- 内部処理 --------------------------------------

        private Color ApplyCoreLook(Color color)
        {
            Color hot = Color.Lerp(color, new Color(1f, 1f, 1f, color.a), _coreWhiteHot);
            return new Color(hot.r * _coreIntensityBoost, hot.g * _coreIntensityBoost, hot.b * _coreIntensityBoost, hot.a);
        }

        /// <summary>シェーダーごとに色のプロパティ名が違うので、存在するものを拾う</summary>
        private static Color GetMaterialColor(Material material)
        {
            if (material == null) return Color.white;
            if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
            if (material.HasProperty("_Color")) return material.GetColor("_Color");
            if (material.HasProperty("_TintColor")) return material.GetColor("_TintColor");
            return Color.white;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null) return;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", color);
        }

        private static void FadeMaterialAlpha(Material material, Color baseColor, float alphaMul)
        {
            if (material == null) return;
            SetMaterialColor(material, MultiplyAlpha(baseColor, alphaMul));
        }

        private static Color MultiplyAlpha(Color color, float mul)
        {
            color.a *= mul;
            return color;
        }

        private void OnDestroy()
        {
            if (_glowMaterialInstance != null) Destroy(_glowMaterialInstance);
            if (_coreMaterialInstance != null) Destroy(_coreMaterialInstance);
            if (_helixMaterialInstance != null) Destroy(_helixMaterialInstance);
        }
    }
}
