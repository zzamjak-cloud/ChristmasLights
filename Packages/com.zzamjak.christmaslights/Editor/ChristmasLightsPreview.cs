using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace CAT.ChristmasLights
{
    /// <summary>
    /// 에디트 모드에서 전구 순환을 재생한다.
    /// 에디트 모드의 _Time.y 는 멈춰 있으므로 경과 시간을 셰이더 전역 유니폼으로 직접 밀어 넣고
    /// 매 에디터 업데이트마다 뷰를 강제로 다시 그린다. Play 모드에 들어가지 않는다.
    /// </summary>
    [InitializeOnLoad]
    public static class ChristmasLightsPreview
    {
        /// <summary>기본 재생 길이(초).</summary>
        public const float DefaultDuration = 60f;

        private const string PreviewKeyword = "CATLIGHTS_PREVIEW_ON";
        private static readonly int PreviewTimeID = Shader.PropertyToID("_CATLightsPreviewTime");

        private static double startedAt;
        private static float duration;

        public static bool IsRunning { get; private set; }

        public static float Elapsed => IsRunning ? (float)(EditorApplication.timeSinceStartup - startedAt) : 0f;
        public static float Duration => duration;
        public static float Remaining => IsRunning ? Mathf.Max(0f, duration - Elapsed) : 0f;
        public static float Progress => IsRunning && duration > 0f ? Mathf.Clamp01(Elapsed / duration) : 0f;

        static ChristmasLightsPreview()
        {
            // 도메인 리로드 직후 이전 세션의 미리보기 상태가 남아 있지 않도록 초기화한다.
            ResetGlobals();
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
        }

        public static void Start(float seconds = DefaultDuration)
        {
            if (seconds <= 0f) return;

            duration = seconds;
            startedAt = EditorApplication.timeSinceStartup;

            if (!IsRunning)
            {
                IsRunning = true;
                EditorApplication.update += OnUpdate;
            }

            Push(0f);
        }

        public static void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            EditorApplication.update -= OnUpdate;
            ResetGlobals();
            InternalEditorUtility.RepaintAllViews();
        }

        /// <summary>실행 중이면 멈추고, 멈춰 있으면 시작한다.</summary>
        public static void Toggle(float seconds = DefaultDuration)
        {
            if (IsRunning) Stop();
            else Start(seconds);
        }

        private static void OnUpdate()
        {
            float elapsed = Elapsed;
            if (elapsed >= duration)
            {
                Stop();
                return;
            }

            Push(elapsed);
            InternalEditorUtility.RepaintAllViews();
        }

        private static void Push(float elapsed)
        {
            Shader.SetGlobalFloat(PreviewTimeID, elapsed);
            // 키워드로 켠다. shader_feature 라 빌드에서는 이 분기 자체가 사라진다.
            Shader.EnableKeyword(PreviewKeyword);
        }

        private static void ResetGlobals()
        {
            Shader.SetGlobalFloat(PreviewTimeID, 0f);
            Shader.DisableKeyword(PreviewKeyword);
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            // Play 모드에서는 _Time.y 가 정상 동작하므로 미리보기를 넘긴다.
            Stop();
        }
    }
}
