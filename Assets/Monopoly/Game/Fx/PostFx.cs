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

        private void OnDestroy() => depthOfField = null;

        public static void SetFocusDistance(float distance)
        {
            if (depthOfField != null) depthOfField.focusDistance.value = distance;
        }
    }
}
