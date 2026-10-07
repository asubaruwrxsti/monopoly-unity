using UnityEngine;
using UnityEngine.Rendering;

namespace Monopoly.Game
{
    /// <summary>
    /// Captures the final 3D image every frame into a small, blurred, frosted texture. The HUD draws it behind
    /// "glass" panels (positioned so it lines up with the screen) to fake a backdrop blur, which UI Toolkit
    /// doesn't support natively.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class BlurCapture : MonoBehaviour
    {
        private const int Downsample = 4;
        private const int Iterations = 3;
        private const float Frost = 0.62f;

        private static readonly int DirectionId = Shader.PropertyToID("_BlurDirection");
        private static readonly int FrostId = Shader.PropertyToID("_Frost");

        public static RenderTexture Texture { get; private set; }

        private Camera cam;
        private CommandBuffer buffer;
        private Material material;
        private RenderTexture ping;
        private Vector2Int size;

        private void OnEnable()
        {
            cam = GetComponent<Camera>();
            var source = Resources.Load<Material>("Monopoly/Blur");
            var shader = source != null ? source.shader : Shader.Find("Hidden/Monopoly/Blur");
            if (shader == null || !shader.isSupported)
            {
                enabled = false;
                return;
            }
            material = new Material(shader);
            buffer = new CommandBuffer { name = "Frosted UI blur" };
            cam.AddCommandBuffer(CameraEvent.AfterImageEffects, buffer);
        }

        private void OnDisable()
        {
            if (buffer != null) cam.RemoveCommandBuffer(CameraEvent.AfterImageEffects, buffer);
            Release();
        }

        private void LateUpdate()
        {
            var wanted = new Vector2Int(Mathf.Max(16, Screen.width / Downsample), Mathf.Max(16, Screen.height / Downsample));
            if (wanted == size && Texture != null) return;
            size = wanted;
            Release();
            ping = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32) { name = "Blur Ping", filterMode = FilterMode.Bilinear };
            Texture = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32) { name = "Frosted Backdrop", filterMode = FilterMode.Bilinear };

            buffer.Clear();
            buffer.SetGlobalFloat(FrostId, 0f);
            buffer.Blit(BuiltinRenderTextureType.CurrentActive, Texture);
            for (int i = 0; i < Iterations; i++)
            {
                buffer.SetGlobalVector(DirectionId, new Vector4(1.5f, 0, 0, 0));
                buffer.Blit(Texture, ping, material);
                buffer.SetGlobalVector(DirectionId, new Vector4(0, 1.5f, 0, 0));
                if (i == Iterations - 1) buffer.SetGlobalFloat(FrostId, Frost);
                buffer.Blit(ping, Texture, material);
            }
        }

        private void Release()
        {
            if (ping != null) ping.Release();
            if (Texture != null) Texture.Release();
            ping = null;
            Texture = null;
        }
    }
}
