using UnityEngine;
using UnityEngine.UI;

namespace CAT.ChristmasLights
{
    /// <summary>전구가 색을 바꾸는 순서를 결정하는 모드.</summary>
    public enum ChristmasLightsMotion
    {
        /// <summary>전구마다 완전히 독립적인 위상.</summary>
        Random,
        /// <summary>왼쪽에서 오른쪽으로 흐른다.</summary>
        Chase,
        /// <summary>지정한 방향으로 물결친다.</summary>
        Wave,
        /// <summary>모든 전구가 동시에 바뀐다.</summary>
        Sync
    }

    /// <summary>
    /// 흑백 마스크에서 구운 ID 맵과 Gradient LUT 로 전구를 순환시킨다.
    /// 시간 진행은 셰이더의 _Time 이 담당하므로 매 프레임 갱신하지 않는다.
    /// </summary>
    [AddComponentMenu("CAT/Effects/ChristmasLights")]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class ChristmasLights : MonoBehaviour
    {
        public const string ShaderName = "CAT/Effects/ChristmasLights";
        private const int LutSize = 256;

        [Header("Source")]
        // 마스크는 ID 맵을 굽는 에디터 입력일 뿐이라 런타임에 필요 없다.
        // Texture2D 로 들고 있으면 씬 의존성으로 끌려가 빌드에 통째로 포함되므로 GUID 만 저장한다.
        [SerializeField, HideInInspector] private string maskAssetGuid;

        [Tooltip("마스크에서 자동 생성된 ID 맵. 직접 편집하지 않는다.")]
        [SerializeField] private Texture2D idMap;

        [SerializeField, HideInInspector] private int bakedLightCount;

        [Header("Bake Options")]
        [Tooltip("이 밝기 이상인 픽셀을 전구로 본다.")]
        [SerializeField, Range(0.01f, 0.99f)] private float maskThreshold = 0.5f;

        [Tooltip("이 픽셀 수보다 작은 덩어리는 노이즈로 보고 버린다.")]
        [SerializeField, Min(1)] private int minPixelCount = 4;

        [Tooltip("경계 밖으로 ID 를 팽창시켜 인접 전구 ID 가 섞이는 것을 막는다. 최소 1 이어야 한다.")]
        [SerializeField, Range(1, 8)] private int dilatePixels = 2;

        [Tooltip("ID 맵을 아트보다 낮은 해상도로 굽는다. ID 맵은 압축할 수 없어 메모리를 그대로 먹는다.")]
        [SerializeField, Range(1, 4)] private int idMapDownscale = 1;

        [Tooltip("전구에 ID 를 배정하는 난수 시드. 바꾸면 순환 패턴이 달라진다.")]
        [SerializeField] private int bakeSeed = 12345;

        [Header("Palette")]
        [Tooltip("Fixed 모드는 딱딱 끊기는 전환, Blend 모드는 부드러운 전환이 된다.")]
        [SerializeField] private Gradient palette = CreateDefaultPalette();

        [SerializeField, Range(0f, 5f)] private float cycleSpeed = 0.5f;
        [SerializeField, Range(0f, 8f)] private float brightness = 1.5f;

        [Header("Motion")]
        [SerializeField] private ChristmasLightsMotion motion = ChristmasLightsMotion.Random;

        [Tooltip("Chase / Wave 에서 정렬된 흐름과 랜덤 위상을 섞는 비율.")]
        [SerializeField, Range(0f, 1f)] private float motionAmount = 1f;

        [SerializeField] private Vector2 waveDirection = new Vector2(1f, 0f);

        [Header("Twinkle")]
        [SerializeField, Range(0f, 1f)] private float twinkleAmount;
        [SerializeField, Range(0f, 10f)] private float twinkleSpeed = 2f;

        [Header("Composite")]
        [SerializeField] private Color baseTint = Color.white;

        [Tooltip("켜면 원본 위에 전구색을 더하고, 끄면 마스크 영역의 색을 대체한다.")]
        [SerializeField] private bool additive = true;

        [Tooltip("켜면 원본 그림을 숨기고 전구만 그린다.")]
        [SerializeField] private bool lightsOnly;

        private static readonly int IDMapID       = Shader.PropertyToID("_IDMap");
        private static readonly int PaletteLUTID  = Shader.PropertyToID("_PaletteLUT");
        private static readonly int IDMapRectID   = Shader.PropertyToID("_IDMapRect");
        private static readonly int BaseTintID    = Shader.PropertyToID("_BaseTint");
        private static readonly int SpeedID       = Shader.PropertyToID("_Speed");
        private static readonly int BrightnessID  = Shader.PropertyToID("_Brightness");
        private static readonly int MotionModeID  = Shader.PropertyToID("_MotionMode");
        private static readonly int MotionAmtID   = Shader.PropertyToID("_MotionAmount");
        private static readonly int WaveDirID     = Shader.PropertyToID("_WaveDir");
        private static readonly int TwinkleAmtID  = Shader.PropertyToID("_TwinkleAmount");
        private static readonly int TwinkleSpdID  = Shader.PropertyToID("_TwinkleSpeed");
        private static readonly int AdditiveID    = Shader.PropertyToID("_Additive");
        private static readonly int LightsOnlyID  = Shader.PropertyToID("_LightsOnly");

        private Renderer targetRenderer;
        private Graphic targetGraphic;
        private MaterialPropertyBlock block;
        private Texture2D paletteLut;
        private Material uiMaterial;

        /// <summary>마스크 텍스처의 에셋 GUID. 에디터 툴만 사용한다.</summary>
        public string MaskAssetGuid
        {
            get => maskAssetGuid;
            set => maskAssetGuid = value;
        }

        public Texture2D IDMap => idMap;
        public int BakedLightCount => bakedLightCount;
        public float MaskThreshold => maskThreshold;
        public int MinPixelCount => minPixelCount;
        public int DilatePixels => dilatePixels;
        public int IDMapDownscale => idMapDownscale;
        public int BakeSeed => bakeSeed;

        private void OnEnable()
        {
            Apply();
        }

        private void OnDisable()
        {
            ReleaseGenerated();
        }

        private void OnValidate()
        {
            // 인스펙터에서 값을 바꾼 순간에만 갱신한다.
            if (!isActiveAndEnabled) return;

#if UNITY_EDITOR
            // OnValidate 안에서 머티리얼을 만들거나 Graphic 을 더티 처리하면 경고가 날 수 있어 한 프레임 미룬다.
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null && isActiveAndEnabled) Apply();
                };
                return;
            }
#endif
            Apply();
        }

        private void OnDidApplyAnimationProperties()
        {
            Apply();
        }

        /// <summary>현재 설정을 머티리얼에 반영한다.</summary>
        public void Apply()
        {
            CacheTargets();
            RebuildPaletteLut();

            if (targetGraphic != null)
            {
                ApplyToGraphic();
            }
            else if (targetRenderer != null)
            {
                ApplyToRenderer();
            }
        }

        /// <summary>베이커가 ID 맵을 구운 뒤 결과를 돌려줄 때 사용한다.</summary>
        public void SetBakedIDMap(Texture2D bakedMap, int lightCount)
        {
            idMap = bakedMap;
            bakedLightCount = lightCount;
            Apply();
        }

        private void CacheTargets()
        {
            targetGraphic = GetComponent<Graphic>();
            targetRenderer = targetGraphic != null ? null : GetComponent<Renderer>();

            if (targetGraphic == null && targetRenderer == null)
            {
                Debug.LogWarning($"ChristmasLights: '{name}' 에 Renderer 도 UI Graphic 도 없습니다.", this);
            }
        }

        private void ApplyToRenderer()
        {
            EnsureRendererMaterial();

            block ??= new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(block);
            WriteProperties(block);
            targetRenderer.SetPropertyBlock(block);
        }

        private void ApplyToGraphic()
        {
            // CanvasRenderer 는 MaterialPropertyBlock 을 지원하지 않아 Graphic 마다 인스턴스가 필요하다.
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"ChristmasLights: 셰이더 '{ShaderName}' 를 찾을 수 없습니다.", this);
                return;
            }

            if (uiMaterial == null || uiMaterial.shader != shader)
            {
                DestroySafe(uiMaterial);
                uiMaterial = new Material(shader)
                {
                    name = "ChristmasLights (UI Instance)",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            WriteProperties(uiMaterial);

            if (targetGraphic.material != uiMaterial)
            {
                targetGraphic.material = uiMaterial;
            }
            targetGraphic.SetMaterialDirty();
        }

        private void EnsureRendererMaterial()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"ChristmasLights: 셰이더 '{ShaderName}' 를 찾을 수 없습니다.", this);
                return;
            }

            Material current = targetRenderer.sharedMaterial;
            if (current != null && current.shader == shader)
            {
                return;
            }

            // 에디트 모드에서 임의로 머티리얼을 갈아끼우면 씬이 계속 더티해지므로 플레이 중에만 보정한다.
            // 에디터에서는 커스텀 인스펙터가 머티리얼 에셋 생성 버튼을 제공한다.
            if (Application.isPlaying)
            {
                targetRenderer.sharedMaterial = new Material(shader)
                {
                    name = "ChristmasLights (Runtime)",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        private void WriteProperties(MaterialPropertyBlock target)
        {
            if (idMap != null) target.SetTexture(IDMapID, idMap);
            if (paletteLut != null) target.SetTexture(PaletteLUTID, paletteLut);

            target.SetVector(IDMapRectID, ResolveIDMapRect());
            target.SetColor(BaseTintID, baseTint);
            target.SetFloat(SpeedID, cycleSpeed);
            target.SetFloat(BrightnessID, brightness);
            target.SetFloat(MotionModeID, (int)motion);
            target.SetFloat(MotionAmtID, ResolveMotionAmount());
            target.SetVector(WaveDirID, ResolveWaveDirection());
            target.SetFloat(TwinkleAmtID, twinkleAmount);
            target.SetFloat(TwinkleSpdID, twinkleSpeed);
            target.SetFloat(AdditiveID, additive ? 1f : 0f);
            target.SetFloat(LightsOnlyID, lightsOnly ? 1f : 0f);
        }

        private void WriteProperties(Material target)
        {
            if (idMap != null) target.SetTexture(IDMapID, idMap);
            if (paletteLut != null) target.SetTexture(PaletteLUTID, paletteLut);

            target.SetVector(IDMapRectID, ResolveIDMapRect());
            target.SetColor(BaseTintID, baseTint);
            target.SetFloat(SpeedID, cycleSpeed);
            target.SetFloat(BrightnessID, brightness);
            target.SetFloat(MotionModeID, (int)motion);
            target.SetFloat(MotionAmtID, ResolveMotionAmount());
            target.SetVector(WaveDirID, ResolveWaveDirection());
            target.SetFloat(TwinkleAmtID, twinkleAmount);
            target.SetFloat(TwinkleSpdID, twinkleSpeed);
            target.SetFloat(AdditiveID, additive ? 1f : 0f);
            target.SetFloat(LightsOnlyID, lightsOnly ? 1f : 0f);
        }

        private float ResolveMotionAmount()
        {
            // Random 은 정렬 위상을 전혀 섞지 않고, Sync 는 완전히 정렬 위상(=0)으로 고정한다.
            return motion switch
            {
                ChristmasLightsMotion.Random => 0f,
                ChristmasLightsMotion.Sync => 1f,
                _ => motionAmount
            };
        }

        private Vector4 ResolveWaveDirection()
        {
            Vector2 dir = waveDirection.sqrMagnitude < 1e-6f ? Vector2.right : waveDirection.normalized;
            return new Vector4(dir.x, dir.y, 0f, 0f);
        }

        /// <summary>
        /// 스프라이트가 아틀라스나 시트의 일부일 때 _MainTex UV 와 ID 맵 UV 가 어긋나므로 보정값을 만든다.
        /// </summary>
        private Vector4 ResolveIDMapRect()
        {
            Sprite sprite = ResolveSprite();
            if (sprite == null || sprite.texture == null)
            {
                return new Vector4(0f, 0f, 1f, 1f);
            }

            Rect rect = sprite.textureRect;
            float tw = sprite.texture.width;
            float th = sprite.texture.height;
            if (tw <= 0f || th <= 0f)
            {
                return new Vector4(0f, 0f, 1f, 1f);
            }

            return new Vector4(rect.x / tw, rect.y / th, rect.width / tw, rect.height / th);
        }

        /// <summary>_MainTex UV 보정을 위해 현재 대상이 쓰는 스프라이트를 찾는다.</summary>
        public Sprite ResolveSprite()
        {
            if (TryGetComponent(out Image image)) return image.sprite;
            if (TryGetComponent(out SpriteRenderer spriteRenderer)) return spriteRenderer.sprite;
            return null;
        }

        private void RebuildPaletteLut()
        {
            if (palette == null)
            {
                palette = CreateDefaultPalette();
            }

            if (paletteLut == null)
            {
                paletteLut = new Texture2D(LutSize, 1, TextureFormat.RGBAHalf, false, true)
                {
                    name = "ChristmasLights_PaletteLUT",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            bool linearSpace = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Color[] pixels = new Color[LutSize];
            for (int i = 0; i < LutSize; i++)
            {
                Color c = palette.Evaluate(i / (float)(LutSize - 1));
                pixels[i] = linearSpace ? c.linear : c;
            }

            paletteLut.SetPixels(pixels);
            paletteLut.Apply(false, false);
        }

        private void ReleaseGenerated()
        {
            // 파괴할 머티리얼이 Graphic 에 물려 있으면 먼저 떼어낸다.
            if (targetGraphic != null && uiMaterial != null && targetGraphic.material == uiMaterial)
            {
                targetGraphic.material = null;
                targetGraphic.SetMaterialDirty();
            }

            DestroySafe(paletteLut);
            paletteLut = null;

            DestroySafe(uiMaterial);
            uiMaterial = null;
        }

        private static void DestroySafe(Object obj)
        {
            if (obj == null) return;

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }

        private static Gradient CreateDefaultPalette()
        {
            // 기본은 딱딱 끊기는 전환(Fixed)으로 기존 동작에 가깝게 맞춘다.
            // Fixed 모드의 Evaluate 는 "다음 키"의 색을 돌려주므로 N색이면 키를 (i+1)/N 에 둬야
            // 첫 색이 사라지지 않고 각 색이 1/N 씩 균등하게 나온다.
            Gradient g = new Gradient { mode = GradientMode.Fixed };
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.red, 0.20f),
                    new GradientColorKey(Color.green, 0.40f),
                    new GradientColorKey(new Color(0.2f, 0.4f, 1f), 0.60f),
                    new GradientColorKey(Color.yellow, 0.80f),
                    new GradientColorKey(Color.magenta, 1.00f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                });
            return g;
        }
    }
}
