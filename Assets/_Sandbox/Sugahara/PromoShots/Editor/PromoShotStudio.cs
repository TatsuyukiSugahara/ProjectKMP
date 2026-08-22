using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectKMP.Sandbox.PromoShots
{
    /// <summary>
    /// アピール画像を撮るための仮設スタジオ。
    ///
    /// 実行せずにエディタ上で役者を並べ、ライトと空を撮影用に差し替えて1枚撮る。
    /// 置いたものはすべて DontSave なのでシーンには残らず、いじった環境設定も撮り終わりに戻す。
    /// </summary>
    public static class PromoShotStudio
    {
        // ---- 定数 ----------------------------------------

        private const string STAGE_NAME = "__PROMO_STAGE__";
        private const int WIDTH = 1920;
        private const int HEIGHT = 1080;

        private static readonly int BASE_MAP_ID = Shader.PropertyToID("_BaseMap");
        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");

        // ---- 環境の退避 ----------------------------------

        private static UnityEngine.Material _savedSkybox;
        private static float _savedAmbient;
        private static Color _savedFog;
        private static float _savedSunIntensity;
        private static Color _savedSunColor;
        private static Quaternion _savedSunRotation;
        private static Light _sun;

        // ---- 公開API -------------------------------------

        /// <summary>撮影用の親を作り直す。前回の役者は消える</summary>
        public static GameObject NewStage()
        {
            var old = GameObject.Find(STAGE_NAME);
            if (old != null) Object.DestroyImmediate(old);

            // 前に手で作った置き場が残っていると写り込むので一緒に消す
            var stray = GameObject.Find("__SHOT__");
            if (stray != null) Object.DestroyImmediate(stray);

            RestoreEnvironment();

            var stage = new GameObject(STAGE_NAME);
            stage.hideFlags = HideFlags.DontSave;
            return stage;
        }

        /// <summary>置いたものと差し替えた環境をすべて片付ける</summary>
        public static void Cleanup()
        {
            var stage = GameObject.Find(STAGE_NAME);
            if (stage != null) Object.DestroyImmediate(stage);
            RestoreEnvironment();
        }

        /// <summary>ゴリラを置く。gold で毛を金色にする</summary>
        public static GameObject Gorilla(
            GameObject stage, Vector3 position, float scale, float yaw,
            string clip, float normalizedTime, bool gold, bool aura)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Contents/Prefabs/Gorilla/Gorilla.prefab");
            GameObject go = Place(prefab, stage, position, scale, yaw);

            Renderer body = null;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = r.name == "Mesh_LOD0";
                if (r.name == "Mesh_LOD0") body = r;
            }

            Pose(go, clip, normalizedTime);
            if (gold && body != null) PaintFur(body, new Color(1.5f, 0.88f, 0.14f, 1.0f));
            if (aura) AttachAura(go);
            return go;
        }

        /// <summary>プレイヤー(犬)を置く</summary>
        public static GameObject Dog(
            GameObject stage, Vector3 position, float scale, float yaw, string clip, float normalizedTime)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/NetworkPrefabs/PF_Player_Online.prefab");
            GameObject go = Place(prefab, stage, position, scale, yaw);

            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "NameLabel") { r.enabled = false; continue; }
                r.enabled = r.name == "Mesh_LOD0";
            }

            Pose(go, clip, normalizedTime);
            return go;
        }

        /// <summary>エフェクトのプレハブを置いて、指定秒ぶん進めた状態にする</summary>
        public static GameObject Effect(
            GameObject stage, string assetPath, Vector3 position, float scale, float yaw, float simulateSec)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) return null;

            GameObject go = Place(prefab, stage, position, scale, yaw);
            SimulateParticles(go, simulateSec);
            return go;
        }

        /// <summary>粒を指定秒ぶん進める</summary>
        public static void SimulateParticles(GameObject go, float seconds)
        {
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Simulate(seconds, true, true, true);
            }
        }

        /// <summary>撮影用のライトを足す</summary>
        public static Light Lamp(
            GameObject stage, Vector3 position, Color color, float intensity, float range)
        {
            var go = new GameObject("Lamp");
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(stage.transform, false);
            go.transform.position = position;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        /// <summary>空とライトを夜にする</summary>
        public static void Night(float sunIntensity, float ambient)
        {
            RestoreEnvironment();
            SaveEnvironment();

            var skyAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(
                "Assets/Contents/Art/Field/Materials/MAT_Field_Sky.mat");
            if (skyAsset != null)
            {
                var copy = new UnityEngine.Material(skyAsset) { hideFlags = HideFlags.DontSave };
                copy.SetFloat("_NightBlend", 1.0f);
                RenderSettings.skybox = copy;
            }

            if (_sun != null)
            {
                _sun.intensity = sunIntensity;
                _sun.color = new Color(0.55f, 0.68f, 1.0f, 1.0f);
            }

            RenderSettings.ambientIntensity = ambient;
            RenderSettings.fogColor = new Color(0.05f, 0.08f, 0.16f, 1.0f);
        }

        /// <summary>昼のまま、太陽の向きと強さだけ撮影向きに整える</summary>
        public static void Day(float sunIntensity, float ambient, float sunPitch, float sunYaw)
        {
            // コマンドを打つたびにUnityがスクリプトを組み直すので、退避しておいた値は消えている。
            // 戻すのを当てにせず、昼の設定をここで作り直す
            RestoreEnvironment();
            SaveEnvironment();

            var skyAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(
                "Assets/Contents/Art/Field/Materials/MAT_Field_Sky.mat");
            if (skyAsset != null)
            {
                var copy = new UnityEngine.Material(skyAsset) { hideFlags = HideFlags.DontSave };
                copy.SetFloat("_NightBlend", 0.0f);
                RenderSettings.skybox = copy;
            }

            if (_sun != null)
            {
                _sun.intensity = sunIntensity;
                _sun.color = Color.white;
                _sun.transform.rotation = Quaternion.Euler(sunPitch, sunYaw, 0.0f);
            }

            RenderSettings.ambientIntensity = ambient;
            RenderSettings.fogColor = new Color(0.72f, 0.82f, 0.9f, 1.0f);
        }

        /// <summary>1枚撮って PromoImages へ書き出す</summary>
        public static void Shoot(Vector3 position, Vector3 lookAt, float fov, string fileName)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) return;

            Vector3 oldPosition = cam.transform.position;
            Quaternion oldRotation = cam.transform.rotation;
            float oldFov = cam.fieldOfView;
            UnityEngine.RenderTexture oldTarget = cam.targetTexture;

            cam.transform.position = position;
            cam.transform.LookAt(lookAt);
            cam.fieldOfView = fov;

            var rt = new UnityEngine.RenderTexture(WIDTH, HEIGHT, 24, UnityEngine.RenderTextureFormat.DefaultHDR)
            {
                antiAliasing = 8,
            };
            cam.targetTexture = rt;
            cam.Render();

            UnityEngine.RenderTexture previous = UnityEngine.RenderTexture.active;
            UnityEngine.RenderTexture.active = rt;
            var shot = new UnityEngine.Texture2D(WIDTH, HEIGHT, UnityEngine.TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0.0f, 0.0f, WIDTH, HEIGHT), 0, 0);
            shot.Apply();
            UnityEngine.RenderTexture.active = previous;

            cam.targetTexture = oldTarget;
            cam.transform.position = oldPosition;
            cam.transform.rotation = oldRotation;
            cam.fieldOfView = oldFov;

            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PromoImages"));
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, fileName + ".png"), shot.EncodeToPNG());

            Object.DestroyImmediate(shot);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        // ---- 内部処理 ------------------------------------

        private static GameObject Place(GameObject prefab, GameObject stage, Vector3 position, float scale, float yaw)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(stage.transform, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0.0f, yaw, 0.0f);
            go.transform.localScale = Vector3.one * scale;

            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
            foreach (LODGroup group in go.GetComponentsInChildren<LODGroup>(true)) group.enabled = false;
            foreach (Collider collider in go.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            return go;
        }

        private static void Pose(GameObject go, string clip, float normalizedTime)
        {
            var animator = go.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null) return;

            foreach (AnimationClip candidate in animator.runtimeAnimatorController.animationClips)
            {
                if (candidate.name != clip) continue;
                candidate.SampleAnimation(animator.gameObject, candidate.length * normalizedTime);
                return;
            }
        }

        private static void PaintFur(Renderer body, Color fur)
        {
            Texture source = body.sharedMaterial.GetTexture(BASE_MAP_ID);
            if (source == null) return;

            int w = source.width;
            int h = source.height;
            UnityEngine.RenderTexture temporary = UnityEngine.RenderTexture.GetTemporary(
                w, h, 0, UnityEngine.RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            UnityEngine.RenderTexture previous = UnityEngine.RenderTexture.active;

            Graphics.Blit(source, temporary);
            UnityEngine.RenderTexture.active = temporary;

            var palette = new UnityEngine.Texture2D(w, h, UnityEngine.TextureFormat.RGBAHalf, false, true)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.DontSave,
            };
            palette.ReadPixels(new Rect(0.0f, 0.0f, w, h), 0, 0);

            UnityEngine.RenderTexture.active = previous;
            UnityEngine.RenderTexture.ReleaseTemporary(temporary);

            Color reference = palette.GetPixel(5, 6);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c = palette.GetPixel(x, y);
                    if (Mathf.Abs(c.r - reference.r) > 0.004f) continue;
                    if (Mathf.Abs(c.g - reference.g) > 0.004f) continue;
                    if (Mathf.Abs(c.b - reference.b) > 0.004f) continue;
                    palette.SetPixel(x, y, fur);
                }
            }
            palette.Apply(false, false);

            var block = new MaterialPropertyBlock();
            body.GetPropertyBlock(block, 0);
            block.SetTexture(BASE_MAP_ID, palette);
            block.SetColor(BASE_COLOR_ID, Color.white);
            body.SetPropertyBlock(block, 0);
        }

        private static void AttachAura(GameObject gorilla)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Contents/Prefabs/Gorilla/PF_Gorilla_RageAura.prefab");
            if (prefab == null) return;

            var aura = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            aura.hideFlags = HideFlags.DontSave;
            aura.transform.SetParent(gorilla.transform, false);

            float parentScale = Mathf.Max(0.0001f, gorilla.transform.lossyScale.x);
            aura.transform.localPosition = Vector3.up * (0.9f / parentScale);
            aura.transform.localScale = Vector3.one * (1.0f / parentScale);

            foreach (ParticleSystem ps in aura.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                Color start = main.startColor.color;
                main.startColor = new Color(1.6f, 1.0f, 0.3f, start.a);
                ps.Simulate(5.0f, true, true, true);
            }
        }

        private static void SaveEnvironment()
        {
            if (_savedSkybox != null) return;

            _savedSkybox = RenderSettings.skybox;
            _savedAmbient = RenderSettings.ambientIntensity;
            _savedFog = RenderSettings.fogColor;

            _sun = RenderSettings.sun;
            if (_sun == null)
            {
                foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (light.type != LightType.Directional) continue;
                    _sun = light;
                    break;
                }
            }

            if (_sun == null) return;
            _savedSunIntensity = _sun.intensity;
            _savedSunColor = _sun.color;
            _savedSunRotation = _sun.transform.rotation;
        }

        private static void RestoreEnvironment()
        {
            if (_savedSkybox == null) return;

            RenderSettings.skybox = _savedSkybox;
            RenderSettings.ambientIntensity = _savedAmbient;
            RenderSettings.fogColor = _savedFog;

            if (_sun != null)
            {
                _sun.intensity = _savedSunIntensity;
                _sun.color = _savedSunColor;
                _sun.transform.rotation = _savedSunRotation;
            }

            _savedSkybox = null;
        }
    }
}
