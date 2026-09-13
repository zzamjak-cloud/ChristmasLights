using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CAT.ChristmasLights
{
    [CustomEditor(typeof(ChristmasLights))]
    [CanEditMultipleObjects]
    public class ChristmasLightsEditor : UnityEditor.Editor
    {
        private SerializedProperty maskAssetGuid;
        private SerializedProperty idMap;
        private SerializedProperty bakedLightCount;

        private SerializedProperty maskThreshold;
        private SerializedProperty minPixelCount;
        private SerializedProperty dilatePixels;
        private SerializedProperty idMapDownscale;
        private SerializedProperty bakeSeed;

        private SerializedProperty palette;
        private SerializedProperty cycleSpeed;
        private SerializedProperty brightness;

        private SerializedProperty motion;
        private SerializedProperty motionAmount;
        private SerializedProperty waveDirection;

        private SerializedProperty twinkleAmount;
        private SerializedProperty twinkleSpeed;

        private SerializedProperty baseTint;
        private SerializedProperty additive;
        private SerializedProperty lightsOnly;

        private bool showBakeOptions;

        private void OnEnable()
        {
            maskAssetGuid = serializedObject.FindProperty("maskAssetGuid");
            idMap = serializedObject.FindProperty("idMap");
            bakedLightCount = serializedObject.FindProperty("bakedLightCount");

            maskThreshold = serializedObject.FindProperty("maskThreshold");
            minPixelCount = serializedObject.FindProperty("minPixelCount");
            dilatePixels = serializedObject.FindProperty("dilatePixels");
            idMapDownscale = serializedObject.FindProperty("idMapDownscale");
            bakeSeed = serializedObject.FindProperty("bakeSeed");

            palette = serializedObject.FindProperty("palette");
            cycleSpeed = serializedObject.FindProperty("cycleSpeed");
            brightness = serializedObject.FindProperty("brightness");

            motion = serializedObject.FindProperty("motion");
            motionAmount = serializedObject.FindProperty("motionAmount");
            waveDirection = serializedObject.FindProperty("waveDirection");

            twinkleAmount = serializedObject.FindProperty("twinkleAmount");
            twinkleSpeed = serializedObject.FindProperty("twinkleSpeed");

            baseTint = serializedObject.FindProperty("baseTint");
            additive = serializedObject.FindProperty("additive");
            lightsOnly = serializedObject.FindProperty("lightsOnly");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPreviewSection();
            EditorGUILayout.Space();
            DrawSourceSection();
            EditorGUILayout.Space();
            DrawPaletteSection();
            EditorGUILayout.Space();
            DrawMotionSection();
            EditorGUILayout.Space();
            DrawCompositeSection();
            EditorGUILayout.Space();
            DrawDiagnostics();

            serializedObject.ApplyModifiedProperties();

            // 진행바가 흐르도록 미리보기 중에는 인스펙터를 계속 다시 그린다.
            if (ChristmasLightsPreview.IsRunning) Repaint();
        }

        private void DrawPreviewSection()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Play 모드에서는 셰이더가 엔진 시간으로 그대로 돕니다.", MessageType.None);
                return;
            }

            if (!ChristmasLightsPreview.IsRunning)
            {
                if (GUILayout.Button($"▶  {ChristmasLightsPreview.DefaultDuration:0}초 미리보기", GUILayout.Height(30f)))
                {
                    ChristmasLightsPreview.Start();
                }

                EditorGUILayout.HelpBox(
                    "Play 모드에 들어가지 않고 씬/게임 뷰에서 전구 순환을 재생합니다. 재생 중에도 값을 바꾸면 바로 반영됩니다.",
                    MessageType.None);
                return;
            }

            Rect bar = EditorGUILayout.GetControlRect(false, 30f);
            EditorGUI.ProgressBar(bar, ChristmasLightsPreview.Progress,
                $"미리보기 {ChristmasLightsPreview.Elapsed:0.0}s / {ChristmasLightsPreview.Duration:0}s");

            if (GUILayout.Button("■  중지", GUILayout.Height(22f)))
            {
                ChristmasLightsPreview.Stop();
            }
        }

        private void DrawSourceSection()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            DrawMaskField();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(idMap, new GUIContent("ID Map (자동 생성)"));
            }

            showBakeOptions = EditorGUILayout.Foldout(showBakeOptions, "Bake Options", true);
            if (showBakeOptions)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(maskThreshold, new GUIContent("Threshold"));
                    EditorGUILayout.PropertyField(minPixelCount, new GUIContent("Min Pixel Count"));
                    EditorGUILayout.PropertyField(dilatePixels, new GUIContent("Dilate Pixels"));
                    EditorGUILayout.PropertyField(idMapDownscale,
                        new GUIContent("ID Map Downscale", "ID 맵은 압축할 수 없어 해상도가 그대로 메모리다. 전구가 작지 않다면 2배까지 줄여도 된다."));
                    EditorGUILayout.PropertyField(bakeSeed, new GUIContent("Seed"));
                }
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(maskAssetGuid.stringValue) || maskAssetGuid.hasMultipleDifferentValues))
            {
                if (GUILayout.Button("ID 맵 굽기", GUILayout.Height(26f)))
                {
                    BakeIDMap();
                }
            }

            if (idMap.objectReferenceValue != null && bakedLightCount.intValue > 0)
            {
                EditorGUILayout.HelpBox($"전구 {bakedLightCount.intValue}개 감지됨", MessageType.Info);
            }
        }

        /// <summary>
        /// 마스크는 빌드에 포함되면 안 되는 에디터 입력이라 컴포넌트가 GUID 만 들고 있다.
        /// 인스펙터에서는 평범한 ObjectField 처럼 보이게 다리를 놓는다.
        /// </summary>
        private void DrawMaskField()
        {
            Texture2D current = ResolveMask(maskAssetGuid.stringValue);

            EditorGUI.BeginChangeCheck();
            var picked = (Texture2D)EditorGUILayout.ObjectField(
                new GUIContent("Mask Texture", "전구 자리를 흰색으로 칠한 흑백 마스크 한 장. 참조가 아니라 GUID 로 저장되어 빌드에 포함되지 않는다."),
                current, typeof(Texture2D), false);

            if (EditorGUI.EndChangeCheck())
            {
                string path = picked != null ? AssetDatabase.GetAssetPath(picked) : null;
                maskAssetGuid.stringValue = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
            }

            if (!string.IsNullOrEmpty(maskAssetGuid.stringValue) && current == null)
            {
                EditorGUILayout.HelpBox("마스크 GUID 는 있는데 에셋을 찾을 수 없습니다. 다시 지정하세요.", MessageType.Warning);
            }
        }

        internal static Texture2D ResolveMask(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private void DrawPaletteSection()
        {
            EditorGUILayout.LabelField("Palette", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(palette, new GUIContent("Palette", "Fixed 모드 = 딱딱 끊기는 전환, Blend 모드 = 부드러운 전환"));
            EditorGUILayout.PropertyField(cycleSpeed, new GUIContent("Cycle Speed"));
            EditorGUILayout.PropertyField(brightness, new GUIContent("Brightness", "1 을 넘기면 HDR 발광이 되어 Bloom 에 반응한다."));
        }

        private void DrawMotionSection()
        {
            EditorGUILayout.LabelField("Motion", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(motion, new GUIContent("Motion"));

            var mode = (ChristmasLightsMotion)motion.enumValueIndex;
            using (new EditorGUI.DisabledScope(mode == ChristmasLightsMotion.Random || mode == ChristmasLightsMotion.Sync))
            {
                EditorGUILayout.PropertyField(motionAmount, new GUIContent("Motion Amount"));
            }

            if (mode == ChristmasLightsMotion.Wave)
            {
                EditorGUILayout.PropertyField(waveDirection, new GUIContent("Wave Direction"));
            }

            EditorGUILayout.LabelField("Twinkle", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(twinkleAmount, new GUIContent("Twinkle Amount"));
            using (new EditorGUI.DisabledScope(Mathf.Approximately(twinkleAmount.floatValue, 0f)))
            {
                EditorGUILayout.PropertyField(twinkleSpeed, new GUIContent("Twinkle Speed"));
            }
        }

        private void DrawCompositeSection()
        {
            EditorGUILayout.LabelField("Composite", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(baseTint, new GUIContent("Base Tint"));
            EditorGUILayout.PropertyField(additive, new GUIContent("Additive", "원본 위에 더할지, 마스크 영역의 색을 대체할지"));
            EditorGUILayout.PropertyField(lightsOnly, new GUIContent("Lights Only", "원본 그림을 숨기고 전구만 그린다."));
        }

        private void DrawDiagnostics()
        {
            if (idMap.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("ID 맵이 없습니다. 마스크를 지정하고 [ID 맵 굽기] 를 누르세요. 지금은 모든 전구가 같이 바뀝니다.", MessageType.Warning);
            }

            foreach (Object each in targets)
            {
                var lights = (ChristmasLights)each;
                WarnAboutSprite(lights);
                WarnAboutMaterial(lights);
            }
        }

        private static void WarnAboutSprite(ChristmasLights lights)
        {
            Sprite sprite = lights.ResolveSprite();
            if (sprite == null) return;

            if (sprite.packed && sprite.packingRotation != SpritePackingRotation.None)
            {
                EditorGUILayout.HelpBox(
                    $"'{sprite.name}' 이 아틀라스에서 회전 패킹되어 ID 맵 UV 를 맞출 수 없습니다. 아틀라스에서 회전을 끄거나 이 스프라이트를 제외하세요.",
                    MessageType.Error);
            }

            if (lights.GetComponent<Image>() is Image image && image.type != Image.Type.Simple)
            {
                EditorGUILayout.HelpBox(
                    $"Image Type 이 {image.type} 입니다. Sliced/Tiled 는 UV 가 늘어나 ID 맵이 어긋나므로 Simple 을 쓰세요.",
                    MessageType.Warning);
            }
        }

        private static void WarnAboutMaterial(ChristmasLights lights)
        {
            if (lights.GetComponent<Graphic>() != null) return;

            var renderer = lights.GetComponent<Renderer>();
            if (renderer == null) return;

            Shader shader = Shader.Find(ChristmasLights.ShaderName);
            Material current = renderer.sharedMaterial;
            if (current != null && current.shader == shader) return;

            EditorGUILayout.HelpBox($"Renderer 의 머티리얼이 '{ChristmasLights.ShaderName}' 셰이더를 쓰지 않습니다.", MessageType.Warning);
            if (GUILayout.Button("머티리얼 에셋 생성 후 할당"))
            {
                CreateMaterialAsset(lights, renderer, shader);
            }
        }

        private static void CreateMaterialAsset(ChristmasLights lights, Renderer renderer, Shader shader)
        {
            if (shader == null)
            {
                EditorUtility.DisplayDialog("ChristmasLights", $"셰이더 '{ChristmasLights.ShaderName}' 를 찾을 수 없습니다.", "확인");
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject(
                "ChristmasLights 머티리얼 저장", $"{lights.name}_ChristmasLights", "mat", string.Empty);
            if (string.IsNullOrEmpty(path)) return;

            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();

            Undo.RecordObject(renderer, "Assign ChristmasLights Material");
            renderer.sharedMaterial = material;
            EditorUtility.SetDirty(renderer);

            lights.Apply();
        }

        private void BakeIDMap()
        {
            var lights = (ChristmasLights)target;
            var options = new ChristmasLightsIDMapBaker.Options
            {
                threshold = maskThreshold.floatValue,
                minPixelCount = minPixelCount.intValue,
                dilatePixels = dilatePixels.intValue,
                downscale = idMapDownscale.intValue,
                seed = bakeSeed.intValue
            };

            ChristmasLightsIDMapBaker.Result result =
                ChristmasLightsIDMapBaker.Bake(ResolveMask(maskAssetGuid.stringValue), options);

            if (!result.Success)
            {
                EditorUtility.DisplayDialog("ID 맵 굽기 실패", result.error, "확인");
                return;
            }

            idMap.objectReferenceValue = result.idMap;
            bakedLightCount.intValue = result.lightCount;
            serializedObject.ApplyModifiedProperties();

            lights.Apply();

            if (!string.IsNullOrEmpty(result.warning))
            {
                Debug.LogWarning($"ChristmasLights: {result.warning}", lights);
            }
            Debug.Log($"ChristmasLights: 전구 {result.lightCount}개 감지 → {result.assetPath}", lights);
        }
    }
}
