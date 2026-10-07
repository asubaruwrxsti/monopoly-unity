using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace Monopoly.Game
{
    /// <summary>
    /// Owns the scene's post-processing volume (bloom, colour grading, vignette, ambient occlusion, depth of field)
    /// and keeps depth of field focused on whatever the camera is looking at. Set up by Monopoly > Rebuild Game Scene.
    /// </summary>
    public sealed class PostFx : MonoBehaviour
    {
        [SerializeField] private PostProcessVolume volume;

        private static DepthOfField depthOfField;

        private void Awake()
        {
            if (volume != null && volume.profile != null && volume.profile.TryGetSettings(out DepthOfField dof))
                depthOfField = dof;
        }

        /// <summary>
        /// High: every effect. Medium (phone default): drops the expensive ones that show least on a small screen
        /// (ambient occlusion, depth of field, SMAA) and uses fast bloom. Low: colour grading only, no shadows.
        /// </summary>
        public void ApplyQuality(GraphicsQuality q)
        {
            var profile = volume != null ? volume.profile : null;
            if (profile != null)
            {
                if (profile.TryGetSettings(out AmbientOcclusion ao)) ao.enabled.value = q == GraphicsQuality.High;
                if (profile.TryGetSettings(out DepthOfField dof))
                {
                    dof.enabled.value = q == GraphicsQuality.High;
                    depthOfField = q == GraphicsQuality.High ? dof : null;
                }
                if (profile.TryGetSettings(out Bloom bloom))
                {
                    bloom.enabled.value = q != GraphicsQuality.Low;
                    bloom.fastMode.value = q != GraphicsQuality.High;
                }
                if (profile.TryGetSettings(out Vignette vignette)) vignette.enabled.value = q != GraphicsQuality.Low;
            }

            foreach (var layer in FindObjectsByType<PostProcessLayer>(FindObjectsSortMode.None))
            {
                layer.antialiasingMode = q == GraphicsQuality.High && !Application.isMobilePlatform
                    ? PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing
                    : q == GraphicsQuality.Low ? PostProcessLayer.Antialiasing.None : PostProcessLayer.Antialiasing.FastApproximateAntialiasing;
                layer.fastApproximateAntialiasing.fastMode = true;
            }
            GameSettings.ApplyShadows(q);
        }

        private void OnDestroy() => depthOfField = null;

        public static void SetFocusDistance(float distance)
        {
            if (depthOfField != null) depthOfField.focusDistance.value = distance;
        }
    }
}
