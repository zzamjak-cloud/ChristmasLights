using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CAT.ChristmasLights
{
    /// <summary>
    /// 흑백 마스크를 플러드 필로 라벨링해 전구별 ID 맵을 굽는다.
    /// 결과 채널: R = 난수 ID, G/B = 덩어리 중심 좌표, A = 마스크 강도.
    /// </summary>
    public static class ChristmasLightsIDMapBaker
    {
        public struct Options
        {
            public float threshold;
            public int minPixelCount;
            public int dilatePixels;
            public int downscale;
            public int seed;
        }

        public struct Result
        {
            public Texture2D idMap;
            public int lightCount;
            public string assetPath;
            public string error;
            public string warning;

            public bool Success => idMap != null && string.IsNullOrEmpty(error);
        }

        private const int Background = -1;

        public static Result Bake(Texture2D mask, Options options)
        {
            if (mask == null)
            {
                return new Result { error = "마스크 텍스처가 비어 있습니다." };
            }

            string maskPath = AssetDatabase.GetAssetPath(mask);
            if (string.IsNullOrEmpty(maskPath))
            {
                return new Result { error = "마스크가 프로젝트 에셋이 아닙니다. Assets 아래의 텍스처를 지정하세요." };
            }

            Color32[] pixels;
            int width, height;
            using (new ReadableTextureScope(maskPath))
            {
                Texture2D reloaded = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
                if (reloaded == null)
                {
                    return new Result { error = "마스크 텍스처를 다시 불러오지 못했습니다." };
                }

                width = reloaded.width;
                height = reloaded.height;
                pixels = reloaded.GetPixels32();
            }

            int[] labels = new int[width * height];
            float[] coverage = new float[width * height];
            int labelCount = Label(pixels, width, height, options.threshold, labels, coverage);

            if (labelCount == 0)
            {
                return new Result { error = "전구를 하나도 찾지 못했습니다. 임계값을 낮추거나 마스크를 확인하세요." };
            }

            LabelStats[] stats = CollectStats(labels, width, height, labelCount);
            int[] remap = BuildRemap(stats, options.minPixelCount, out int keptCount);

            if (keptCount == 0)
            {
                return new Result { error = $"덩어리 {labelCount}개가 모두 최소 픽셀 수({options.minPixelCount})보다 작습니다." };
            }

            float[] idValues = BuildShuffledIds(keptCount, options.seed);

            // ID 맵은 블록 압축을 쓸 수 없어 해상도가 그대로 메모리다. 아트 해상도를 따라갈 이유는 없다.
            int downscale = Mathf.Clamp(options.downscale <= 0 ? 1 : options.downscale, 1, 4);
            int outWidth = Mathf.Max(1, width / downscale);
            int outHeight = Mathf.Max(1, height / downscale);

            Color32[] output = Encode(labels, coverage, stats, remap, idValues,
                width, height, downscale, outWidth, outHeight, out bool[] filled, out int lostLabels);

            // 팽창은 1픽셀 이상이어야 한다. 셰이더가 ID 맵을 선형으로 한 번만 읽기 때문에
            // 블롭 경계의 보간 탭이 같은 ID 위에 떨어지도록 여백이 필요하다.
            int dilatePasses = Mathf.Max(1, options.dilatePixels);
            int mixedEdges = Dilate(output, filled, outWidth, outHeight, dilatePasses);

            string outputPath = BuildOutputPath(maskPath);
            string writeError = WritePng(output, outWidth, outHeight, outputPath);
            if (!string.IsNullOrEmpty(writeError))
            {
                return new Result { error = writeError };
            }

            ConfigureImporter(outputPath, outWidth, outHeight);

            return new Result
            {
                idMap = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath),
                lightCount = keptCount,
                assetPath = outputPath,
                warning = BuildWarning(lostLabels, mixedEdges, downscale)
            };
        }

        private struct LabelStats
        {
            public int count;
            public double sumX;
            public double sumY;
        }

        /// <summary>8-연결 플러드 필로 전구 덩어리를 라벨링한다.</summary>
        private static int Label(Color32[] pixels, int width, int height, float threshold, int[] labels, float[] coverage)
        {
            byte cut = (byte)Mathf.Clamp(Mathf.RoundToInt(threshold * 255f), 1, 255);

            for (int i = 0; i < labels.Length; i++)
            {
                Color32 p = pixels[i];
                // 흰색 위 검정, 투명 배경 위 흰색 어느 쪽이든 받도록 밝기와 알파를 함께 본다.
                int luminance = Mathf.Max(p.r, Mathf.Max(p.g, p.b));
                int value = luminance * p.a / 255;

                coverage[i] = value / 255f;
                labels[i] = value >= cut ? 0 : Background;
            }

            int next = 0;
            Stack<int> stack = new Stack<int>();

            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] != 0) continue;

                int current = ++next;
                stack.Push(i);
                labels[i] = current;

                while (stack.Count > 0)
                {
                    int index = stack.Pop();
                    int x = index % width;
                    int y = index / width;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= height) continue;

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;

                            int nx = x + dx;
                            if (nx < 0 || nx >= width) continue;

                            int n = ny * width + nx;
                            if (labels[n] != 0) continue;

                            labels[n] = current;
                            stack.Push(n);
                        }
                    }
                }
            }

            return next;
        }

        private static LabelStats[] CollectStats(int[] labels, int width, int height, int labelCount)
        {
            LabelStats[] stats = new LabelStats[labelCount + 1];

            for (int i = 0; i < labels.Length; i++)
            {
                int label = labels[i];
                if (label <= 0) continue;

                stats[label].count++;
                stats[label].sumX += i % width;
                stats[label].sumY += i / width;
            }

            return stats;
        }

        /// <summary>최소 픽셀 수 미만인 덩어리를 버리고 남은 것을 0부터 다시 번호 매긴다.</summary>
        private static int[] BuildRemap(LabelStats[] stats, int minPixelCount, out int keptCount)
        {
            int[] remap = new int[stats.Length];
            keptCount = 0;

            for (int label = 1; label < stats.Length; label++)
            {
                remap[label] = stats[label].count >= minPixelCount ? keptCount++ : Background;
            }

            return remap;
        }

        /// <summary>
        /// ID 를 0~1 에 고르게 펼친 뒤 섞는다. 인접한 전구가 비슷한 ID 를 받아 같이 깜빡이는 것을 막는다.
        /// </summary>
        private static float[] BuildShuffledIds(int count, int seed)
        {
            float[] ids = new float[count];
            for (int i = 0; i < count; i++)
            {
                ids[i] = (i + 0.5f) / count;
            }

            System.Random random = new System.Random(seed);
            for (int i = count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (ids[i], ids[j]) = (ids[j], ids[i]);
            }

            return ids;
        }

        private static Color32[] Encode(int[] labels, float[] coverage, LabelStats[] stats, int[] remap,
            float[] idValues, int width, int height, int downscale, int outWidth, int outHeight,
            out bool[] filled, out int lostLabels)
        {
            var output = new Color32[outWidth * outHeight];
            filled = new bool[output.Length];
            var seen = new bool[idValues.Length];

            for (int oy = 0; oy < outHeight; oy++)
            {
                for (int ox = 0; ox < outWidth; ox++)
                {
                    int index = oy * outWidth + ox;
                    float alpha = AverageCoverage(coverage, width, height, ox, oy, downscale);
                    int label = PickLabel(labels, remap, width, height, ox, oy, downscale);

                    if (label == Background)
                    {
                        // RGB 는 비우되 알파에는 마스크 강도를 남겨 안티앨리어싱된 경계를 보존한다.
                        // RGB 는 뒤이어 Dilate 가 이웃 전구 값으로 채운다.
                        output[index] = new Color32(0, 0, 0, ToByte(alpha));
                        continue;
                    }

                    LabelStats s = stats[label];
                    float cx = (float)(s.sumX / s.count) / Mathf.Max(width - 1, 1);
                    float cy = (float)(s.sumY / s.count) / Mathf.Max(height - 1, 1);

                    int id = remap[label];
                    seen[id] = true;

                    output[index] = new Color32(ToByte(idValues[id]), ToByte(cx), ToByte(cy), ToByte(alpha));
                    filled[index] = true;
                }
            }

            lostLabels = 0;
            foreach (bool b in seen)
            {
                if (!b) lostLabels++;
            }

            return output;
        }

        /// <summary>다운스케일 블록에 전구가 조금이라도 걸치면 살린다. 작은 전구가 사라지는 것을 막는다.</summary>
        private static int PickLabel(int[] labels, int[] remap, int width, int height, int ox, int oy, int downscale)
        {
            for (int dy = 0; dy < downscale; dy++)
            {
                int y = oy * downscale + dy;
                if (y >= height) break;

                for (int dx = 0; dx < downscale; dx++)
                {
                    int x = ox * downscale + dx;
                    if (x >= width) break;

                    int label = labels[y * width + x];
                    if (label > 0 && remap[label] != Background) return label;
                }
            }

            return Background;
        }

        private static float AverageCoverage(float[] coverage, int width, int height, int ox, int oy, int downscale)
        {
            float sum = 0f;
            int count = 0;

            for (int dy = 0; dy < downscale; dy++)
            {
                int y = oy * downscale + dy;
                if (y >= height) break;

                for (int dx = 0; dx < downscale; dx++)
                {
                    int x = ox * downscale + dx;
                    if (x >= width) break;

                    sum += coverage[y * width + x];
                    count++;
                }
            }

            return count > 0 ? sum / count : 0f;
        }

        /// <summary>
        /// 배경 쪽으로 RGB 만 팽창시킨다. 알파는 0 으로 두므로 보이지는 않고,
        /// 셰이더가 ID 맵을 선형으로 읽을 때 블롭 경계의 보간 탭이 같은 ID 위에 떨어진다.
        /// 반환값은 서로 다른 전구의 팽창 영역이 맞닿은 픽셀 수다.
        /// </summary>
        private static int Dilate(Color32[] output, bool[] filled, int width, int height, int passes)
        {
            int mixedEdges = 0;
            var pending = new List<int>();
            var pendingColor = new List<Color32>();

            for (int pass = 0; pass < passes; pass++)
            {
                pending.Clear();
                pendingColor.Clear();

                for (int i = 0; i < output.Length; i++)
                {
                    if (filled[i]) continue;

                    int x = i % width;
                    int y = i / width;
                    if (!TryFindFilledNeighbor(filled, output, width, height, x, y, out Color32 source, out bool mixed))
                    {
                        continue;
                    }

                    if (mixed) mixedEdges++;

                    pending.Add(i);
                    pendingColor.Add(new Color32(source.r, source.g, source.b, output[i].a));
                }

                if (pending.Count == 0) break;

                for (int k = 0; k < pending.Count; k++)
                {
                    output[pending[k]] = pendingColor[k];
                    filled[pending[k]] = true;
                }
            }

            return mixedEdges;
        }

        private static bool TryFindFilledNeighbor(bool[] filled, Color32[] output, int width, int height,
            int x, int y, out Color32 source, out bool mixed)
        {
            bool found = false;
            source = default;
            mixed = false;

            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = y + dy;
                if (ny < 0 || ny >= height) continue;

                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;

                    int nx = x + dx;
                    if (nx < 0 || nx >= width) continue;

                    int n = ny * width + nx;
                    if (!filled[n]) continue;

                    if (!found)
                    {
                        source = output[n];
                        found = true;
                    }
                    else if (output[n].r != source.r)
                    {
                        mixed = true;
                    }
                }
            }

            return found;
        }

        private static string BuildWarning(int lostLabels, int mixedEdges, int downscale)
        {
            var parts = new List<string>();

            if (lostLabels > 0)
            {
                parts.Add($"다운스케일 {downscale}배에서 전구 {lostLabels}개가 ID 맵에 남지 않았습니다. 배율을 낮추세요.");
            }

            if (mixedEdges > 0)
            {
                parts.Add($"서로 다른 전구의 팽창 영역이 {mixedEdges}픽셀에서 맞닿습니다. 전구 간격을 넓히거나 Dilate 를 줄이세요.");
            }

            return parts.Count > 0 ? string.Join("\n", parts) : null;
        }

        private static byte ToByte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);

        private static string BuildOutputPath(string maskPath)
        {
            string directory = Path.GetDirectoryName(maskPath);
            string name = Path.GetFileNameWithoutExtension(maskPath);
            return $"{directory}/{name}_IDMap.png".Replace('\\', '/');
        }

        private static string WritePng(Color32[] pixels, int width, int height, string assetPath)
        {
            // linear:true 로 만들어 sRGB 변환 없이 raw 값이 그대로 기록되게 한다.
            Texture2D temp = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                temp.SetPixels32(pixels);
                temp.Apply(false, false);

                byte[] png = temp.EncodeToPNG();
                if (png == null || png.Length == 0)
                {
                    return "PNG 인코딩에 실패했습니다.";
                }

                File.WriteAllBytes(Path.GetFullPath(assetPath), png);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                return null;
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        /// <summary>ID 값이 뭉개지지 않도록 압축·밉맵·sRGB·알파 블리딩을 모두 끈다.</summary>
        private static void ConfigureImporter(string assetPath, int width, int height)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer) return;

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            // alphaIsTransparency 를 켜면 Unity 가 알파 블리딩으로 RGB 를 덮어써 ID 가 깨진다.
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            // 셰이더가 sampler_linear_clamp 로 한 번만 읽는다.
            // GLES3 는 인라인 샘플러 상태를 무시하고 이 필터를 그대로 쓰므로 Bilinear 여야 한다.
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = false;
            importer.maxTextureSize = NextPowerOfTwoSize(Mathf.Max(width, height));
            importer.SaveAndReimport();
        }

        private static int NextPowerOfTwoSize(int size)
        {
            int result = 32;
            while (result < size && result < 16384)
            {
                result *= 2;
            }
            return result;
        }

        /// <summary>베이크 동안만 마스크를 읽기 가능·무압축으로 바꾸고 원래 설정으로 되돌린다.</summary>
        private readonly struct ReadableTextureScope : System.IDisposable
        {
            private readonly TextureImporter importer;
            private readonly bool previousReadable;
            private readonly TextureImporterCompression previousCompression;
            private readonly bool changed;

            public ReadableTextureScope(string assetPath)
            {
                importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                previousReadable = importer != null && importer.isReadable;
                previousCompression = importer != null ? importer.textureCompression : TextureImporterCompression.Compressed;
                changed = false;

                if (importer == null) return;
                if (previousReadable && previousCompression == TextureImporterCompression.Uncompressed) return;

                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                changed = true;
            }

            public void Dispose()
            {
                if (!changed || importer == null) return;

                importer.isReadable = previousReadable;
                importer.textureCompression = previousCompression;
                importer.SaveAndReimport();
            }
        }
    }
}
