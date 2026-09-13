using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CAT.ChristmasLights
{
    /// <summary>
    /// ChristmasLights 샘플 씬과 거기 쓰이는 아트·머티리얼·ID 맵을 통째로 생성한다.
    /// 아트도 코드로 그리므로 외부 에셋 없이 재현된다.
    /// </summary>
    public static class ChristmasLightsSampleBuilder
    {
        private const string Root = "Assets/Samples/ChristmasLights";
        private const string ArtDir = Root + "/Art";
        private const string TreePath = ArtDir + "/Tree.png";
        private const string MaskPath = ArtDir + "/TreeLights_Mask.png";
        private const string MaterialPath = Root + "/ChristmasLightsSample_Tree.mat";
        private const string ProfilePath = Root + "/ChristmasLightsSample_Volume.asset";
        private const string ScenePath = Root + "/ChristmasLightsSample.unity";

        private const int TexWidth = 256;
        private const int TexHeight = 384;
        private const float PixelsPerUnit = 100f;

        [MenuItem("CAT/Effects/ChristmasLights 샘플 씬 생성")]
        private static void BuildFromMenu()
        {
            Build();
            EditorUtility.DisplayDialog("ChristmasLights", $"샘플 씬을 만들었습니다.\n{ScenePath}", "확인");
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(ScenePath);
        }

        /// <summary>배치 모드에서 -executeMethod 로도 부를 수 있는 진입점.</summary>
        public static void Build()
        {
            EnsureFolders();

            Sprite treeSprite = CreateTreeSprite();
            Texture2D mask = CreateMaskTexture();
            Texture2D idMap = BakeIDMap(mask, out int lightCount);
            Material material = CreateMaterial();
            VolumeProfile profile = CreateVolumeProfile();

            BuildScene(treeSprite, mask, idMap, lightCount, material, profile);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"SAMPLE_RESULT: lightCount={lightCount} scene={ScenePath}");
        }

        private static void EnsureFolders()
        {
            Directory.CreateDirectory(Path.GetFullPath(ArtDir));
            AssetDatabase.Refresh();
        }

        // ------------------------------------------------------------------
        // 아트 생성
        // ------------------------------------------------------------------

        private readonly struct Tier
        {
            public readonly float BaseY;
            public readonly float TopY;
            public readonly float HalfWidth;

            public Tier(float baseY, float topY, float halfWidth)
            {
                BaseY = baseY;
                TopY = topY;
                HalfWidth = halfWidth;
            }

            public float HalfWidthAt(float y)
            {
                float t = Mathf.InverseLerp(BaseY, TopY, y);
                return HalfWidth * (1f - t);
            }
        }

        private static readonly Tier[] Tiers =
        {
            new Tier(50f, 195f, 120f),
            new Tier(150f, 285f, 95f),
            new Tier(240f, 370f, 66f)
        };

        private const float TrunkLeft = 116f;
        private const float TrunkRight = 140f;
        private const float TrunkTop = 58f;
        private const float CenterX = 128f;

        /// <summary>2x 슈퍼샘플링으로 계단현상 없이 트리 실루엣을 그린다.</summary>
        private static Sprite CreateTreeSprite()
        {
            var pixels = new Color[TexWidth * TexHeight];
            var trunkColor = new Color(0.30f, 0.19f, 0.11f, 1f);

            for (int y = 0; y < TexHeight; y++)
            {
                for (int x = 0; x < TexWidth; x++)
                {
                    float treeCoverage = 0f;
                    float trunkCoverage = 0f;

                    for (int sy = 0; sy < 2; sy++)
                    {
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float px = x + 0.25f + sx * 0.5f;
                            float py = y + 0.25f + sy * 0.5f;

                            if (IsInsideTree(px, py)) treeCoverage += 0.25f;
                            if (px >= TrunkLeft && px <= TrunkRight && py <= TrunkTop) trunkCoverage += 0.25f;
                        }
                    }

                    // 위로 갈수록 밝아지는 완만한 그라데이션
                    float shade = Mathf.Lerp(0.75f, 1.15f, y / (float)TexHeight);
                    var needle = new Color(0.07f * shade, 0.24f * shade, 0.13f * shade, 1f);

                    Color c = Color.clear;
                    if (trunkCoverage > 0f) c = new Color(trunkColor.r, trunkColor.g, trunkColor.b, trunkCoverage);
                    if (treeCoverage > 0f)
                    {
                        float a = Mathf.Max(c.a, treeCoverage);
                        Color rgb = treeCoverage >= c.a ? needle : c;
                        c = new Color(rgb.r, rgb.g, rgb.b, a);
                    }

                    pixels[y * TexWidth + x] = c;
                }
            }

            WritePng(pixels, TreePath);
            ConfigureSpriteImporter(TreePath);
            return AssetDatabase.LoadAssetAtPath<Sprite>(TreePath);
        }

        private static bool IsInsideTree(float x, float y)
        {
            foreach (Tier tier in Tiers)
            {
                if (y < tier.BaseY || y > tier.TopY) continue;
                if (Mathf.Abs(x - CenterX) <= tier.HalfWidthAt(y)) return true;
            }
            return false;
        }

        /// <summary>전구 자리를 흰 원으로 찍은 마스크. 사용자가 손으로 그릴 그림과 같은 형식이다.</summary>
        private static Texture2D CreateMaskTexture()
        {
            var bulbs = new List<Vector3>(); // xy = 중심, z = 반지름

            foreach (Tier tier in Tiers)
            {
                foreach (float t in new[] { 0.20f, 0.46f, 0.72f })
                {
                    float y = Mathf.Lerp(tier.BaseY, tier.TopY, t);
                    float usable = tier.HalfWidthAt(y) - 9f;
                    if (usable <= 0f) { bulbs.Add(new Vector3(CenterX, y, 4.5f)); continue; }

                    int perSide = Mathf.FloorToInt(usable / 24f);
                    for (int k = -perSide; k <= perSide; k++)
                    {
                        bulbs.Add(new Vector3(CenterX + k * 24f, y, 4.5f));
                    }
                }
            }

            // 꼭대기 별은 조금 더 큰 전구로 둔다.
            bulbs.Add(new Vector3(CenterX, 372f, 7f));

            var pixels = new Color[TexWidth * TexHeight];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.black;

            for (int y = 0; y < TexHeight; y++)
            {
                for (int x = 0; x < TexWidth; x++)
                {
                    float coverage = 0f;
                    foreach (Vector3 bulb in bulbs)
                    {
                        for (int sy = 0; sy < 2; sy++)
                        {
                            for (int sx = 0; sx < 2; sx++)
                            {
                                float px = x + 0.25f + sx * 0.5f;
                                float py = y + 0.25f + sy * 0.5f;
                                float dx = px - bulb.x;
                                float dy = py - bulb.y;
                                if (dx * dx + dy * dy <= bulb.z * bulb.z) coverage += 0.25f;
                            }
                        }
                        if (coverage >= 1f) break;
                    }

                    coverage = Mathf.Clamp01(coverage);
                    pixels[y * TexWidth + x] = new Color(coverage, coverage, coverage, 1f);
                }
            }

            WritePng(pixels, MaskPath);
            ConfigureMaskImporter(MaskPath);
            Debug.Log($"SAMPLE_MASK: bulbsDrawn={bulbs.Count}");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
        }

        private static string AssetGuid(Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
        }

        private static Texture2D BakeIDMap(Texture2D mask, out int lightCount)
        {
            ChristmasLightsIDMapBaker.Result result = ChristmasLightsIDMapBaker.Bake(mask,
                new ChristmasLightsIDMapBaker.Options
                {
                    threshold = 0.5f,
                    minPixelCount = 4,
                    dilatePixels = 2,
                    downscale = 2,
                    seed = 20251225
                });

            if (!result.Success)
            {
                throw new System.Exception($"ID 맵 베이크 실패: {result.error}");
            }

            if (!string.IsNullOrEmpty(result.warning))
            {
                Debug.LogWarning($"SAMPLE_BAKE_WARNING: {result.warning}");
            }

            lightCount = result.lightCount;
            return result.idMap;
        }

        private static void WritePng(Color[] pixels, string assetPath)
        {
            var tex = new Texture2D(TexWidth, TexHeight, TextureFormat.RGBA32, false, true);
            try
            {
                tex.SetPixels(pixels);
                tex.Apply(false, false);
                File.WriteAllBytes(Path.GetFullPath(assetPath), tex.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void ConfigureSpriteImporter(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            // 실제 아트다. 모바일에서 무압축으로 두면 768KB 를 그대로 먹으므로 기본 압축을 쓴다.
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
        }

        private static void ConfigureMaskImporter(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer) return;

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------
        // 에셋
        // ------------------------------------------------------------------

        private static Material CreateMaterial()
        {
            Shader shader = Shader.Find(ChristmasLights.ShaderName);
            if (shader == null) throw new System.Exception($"셰이더 '{ChristmasLights.ShaderName}' 를 찾을 수 없습니다.");

            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null)
            {
                existing.shader = shader;
                return existing;
            }

            var material = new Material(shader) { name = "ChristmasLightsSample_Tree" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        private static VolumeProfile CreateVolumeProfile()
        {
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (existing != null) return existing;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            // HDR Brightness 가 실제로 번지는 걸 보여주기 위해 Bloom 을 켠다.
            var bloom = profile.Add<Bloom>(true);
            bloom.hideFlags = HideFlags.HideInHierarchy;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.85f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 1.1f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.72f;
            AssetDatabase.AddObjectToAsset(bloom, profile);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        // ------------------------------------------------------------------
        // 씬
        // ------------------------------------------------------------------

        private static void BuildScene(Sprite treeSprite, Texture2D mask, Texture2D idMap, int lightCount,
            Material material, VolumeProfile profile)
        {
            // 샘플 씬이 이미 열려 있으면 같은 경로로 덮어쓸 수 없다. 그 씬을 직접 비우고 다시 채운다.
            Scene opened = SceneManager.GetSceneByPath(ScenePath);
            if (opened.IsValid() && opened.isLoaded)
            {
                RebuildOpenedScene(opened, treeSprite, mask, idMap, lightCount, material, profile);
                return;
            }

            // 열려 있지 않으면 열린 씬을 건드리지 않도록 additive 로 만들어 저장하고 다시 닫는다.
            // 다만 이름 없는 미저장 씬이 열려 있으면 Unity 가 additive 생성을 거부하므로
            // (배치 모드의 기본 상태가 그렇다) 그때만 Single 로 만든다.
            Scene previousActive = SceneManager.GetActiveScene();
            // 저장된 적 없는(path 가 빈) 씬이 열려 있으면 dirty 여부와 무관하게 additive 가 거부된다.
            bool untitled = string.IsNullOrEmpty(previousActive.path);

            if (untitled && previousActive.isDirty && !Application.isBatchMode
                && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            NewSceneMode mode = untitled ? NewSceneMode.Single : NewSceneMode.Additive;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
            if (mode == NewSceneMode.Additive) SceneManager.SetActiveScene(scene);

            try
            {
                PopulateScene(treeSprite, mask, idMap, lightCount, material, profile);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (mode == NewSceneMode.Additive)
                {
                    if (previousActive.IsValid()) SceneManager.SetActiveScene(previousActive);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void RebuildOpenedScene(Scene scene, Sprite treeSprite, Texture2D mask, Texture2D idMap,
            int lightCount, Material material, VolumeProfile profile)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Object.DestroyImmediate(root);
            }

            Scene previousActive = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            try
            {
                PopulateScene(treeSprite, mask, idMap, lightCount, material, profile);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (previousActive.IsValid() && previousActive != scene) SceneManager.SetActiveScene(previousActive);
            }
        }

        private static void PopulateScene(Sprite treeSprite, Texture2D mask, Texture2D idMap, int lightCount,
            Material material, VolumeProfile profile)
        {
            CreateCamera();
            CreateVolume(profile);

            var trees = new GameObject("Trees");

            CreateTree(trees.transform, "Tree_Random", new Vector3(-3.1f, 0f, 0f), treeSprite, material,
                mask, idMap, lightCount, lights =>
                {
                    SetPrivate(lights, "motion", ChristmasLightsMotion.Random);
                    SetPrivate(lights, "cycleSpeed", 0.45f);
                });

            CreateTree(trees.transform, "Tree_Chase", Vector3.zero, treeSprite, material,
                mask, idMap, lightCount, lights =>
                {
                    SetPrivate(lights, "motion", ChristmasLightsMotion.Chase);
                    SetPrivate(lights, "motionAmount", 1f);
                    SetPrivate(lights, "cycleSpeed", 0.35f);
                });

            CreateTree(trees.transform, "Tree_Twinkle", new Vector3(3.1f, 0f, 0f), treeSprite, material,
                mask, idMap, lightCount, lights =>
                {
                    SetPrivate(lights, "motion", ChristmasLightsMotion.Random);
                    SetPrivate(lights, "cycleSpeed", 0.25f);
                    SetPrivate(lights, "twinkleAmount", 0.7f);
                    SetPrivate(lights, "twinkleSpeed", 3.2f);
                });

            CreateUI(treeSprite, mask, idMap, lightCount);
        }

        private static void CreateCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.position = new Vector3(0f, 0f, -10f);

            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 3f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.04f, 0.05f, 0.09f, 1f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;

            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
        }

        private static void CreateVolume(VolumeProfile profile)
        {
            var go = new GameObject("Global Volume");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;
        }

        private static void CreateTree(Transform parent, string name, Vector3 position, Sprite sprite,
            Material material, Texture2D mask, Texture2D idMap, int lightCount,
            System.Action<ChristmasLights> configure)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;

            var lights = go.AddComponent<ChristmasLights>();
            ApplyCommonSettings(lights, mask, idMap, lightCount);
            configure(lights);
            lights.Apply();
        }

        private static void CreateUI(Sprite sprite, Texture2D mask, Texture2D idMap, int lightCount)
        {
            // EventSystem 은 두지 않는다. 샘플에 인터랙션이 없고, 레거시 StandaloneInputModule 은
            // Active Input Handling 이 New Input System 전용인 프로젝트에서 에러를 낸다.
            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();

            var imageGo = new GameObject("UI_Tree (Image)", typeof(RectTransform));
            imageGo.transform.SetParent(canvasGo.transform, false);

            var rect = (RectTransform)imageGo.transform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-60f, 60f);
            rect.sizeDelta = new Vector2(TexWidth, TexHeight);

            var image = imageGo.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;

            var lights = imageGo.AddComponent<ChristmasLights>();
            ApplyCommonSettings(lights, mask, idMap, lightCount);
            SetPrivate(lights, "motion", ChristmasLightsMotion.Wave);
            SetPrivate(lights, "motionAmount", 1f);
            SetPrivate(lights, "waveDirection", new Vector2(0f, 1f));
            SetPrivate(lights, "cycleSpeed", 0.4f);
            lights.Apply();

            CreateLabel(canvasGo.transform, "Label_Random", "Random", new Vector2(-560f, -330f));
            CreateLabel(canvasGo.transform, "Label_Chase", "Chase", new Vector2(0f, -330f));
            CreateLabel(canvasGo.transform, "Label_Twinkle", "Random + Twinkle", new Vector2(560f, -330f));
        }

        private static void CreateLabel(Transform parent, string name, string text, Vector2 anchoredPosition)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) return;

            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(420f, 48f);

            var label = go.AddComponent<Text>();
            label.font = font;
            label.fontSize = 30;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.85f, 0.88f, 0.95f, 1f);
            label.text = text;
            label.raycastTarget = false;
        }

        private static void ApplyCommonSettings(ChristmasLights lights, Texture2D mask, Texture2D idMap, int lightCount)
        {
            SetPrivate(lights, "maskAssetGuid", AssetGuid(mask));
            SetPrivate(lights, "idMap", idMap);
            SetPrivate(lights, "bakedLightCount", lightCount);
            SetPrivate(lights, "bakeSeed", 20251225);
            SetPrivate(lights, "idMapDownscale", 2);
            SetPrivate(lights, "palette", CreateSamplePalette());
            SetPrivate(lights, "brightness", 2.4f);
            SetPrivate(lights, "additive", true);
            SetPrivate(lights, "lightsOnly", false);
        }

        private static Gradient CreateSamplePalette()
        {
            var gradient = new Gradient { mode = GradientMode.Fixed };
            gradient.SetKeys(
                new[]
                {
                    // Fixed 모드는 "다음 키"의 색을 쓰므로 5색이면 키를 0.2 간격으로 1.0 까지 둔다.
                    new GradientColorKey(new Color(1.00f, 0.16f, 0.18f), 0.20f),
                    new GradientColorKey(new Color(1.00f, 0.82f, 0.45f), 0.40f),
                    new GradientColorKey(new Color(0.20f, 0.95f, 0.42f), 0.60f),
                    new GradientColorKey(new Color(0.28f, 0.55f, 1.00f), 0.80f),
                    new GradientColorKey(new Color(0.90f, 0.35f, 1.00f), 1.00f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }

        /// <summary>
        /// 컴포넌트 필드는 전부 [SerializeField] private 이라 샘플 빌더에서만 리플렉션으로 채운다.
        /// 런타임 API 를 샘플 때문에 넓히지 않기 위한 선택이다.
        /// </summary>
        private static void SetPrivate(ChristmasLights target, string fieldName, object value)
        {
            FieldInfo field = typeof(ChristmasLights).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new System.Exception($"필드를 찾을 수 없습니다: {fieldName}");

            field.SetValue(target, value);
            EditorUtility.SetDirty(target);
        }
    }
}
