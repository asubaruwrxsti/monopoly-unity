using System.Collections.Generic;
using UnityEngine;

namespace Monopoly.Game
{
    /// <summary>Renders each token once into a small texture for the UI (player chips, token picker).</summary>
    public static class TokenPortraits
    {
        private const int Size = 192;
        private const int Layer = 31;
        private static readonly Dictionary<int, Texture2D> Cache = new Dictionary<int, Texture2D>();

        public static Texture2D Get(int tokenId)
        {
            if (Cache.TryGetValue(tokenId, out var tex) && tex != null) return tex;
            tex = Render(TokenCatalog.Get(tokenId));
            Cache[tokenId] = tex;
            return tex;
        }

        private static Texture2D Render(TokenDef def)
        {
            // Each token gets its own spot far below the board, so stages rendered in the same frame never overlap.
            var root = new GameObject("Portrait Stage");
            root.transform.position = new Vector3(def.Id * 25f, -500f, 0);
            var model = TokenModels.Build(def, root.transform);
            model.Root.transform.localRotation = Quaternion.Euler(0, 155f, 0);
            if (model.Animator != null) model.Animator.Update(0f);
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;

            var renderers = root.GetComponentsInChildren<Renderer>();
            Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(root.transform.position, Vector3.one * 0.5f);
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            var camGo = new GameObject("Portrait Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.fieldOfView = 22f;
            cam.allowHDR = false;
            float radius = b.extents.magnitude;
            float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
            Vector3 dir = new Vector3(0.0f, 0.45f, -1f).normalized;
            cam.transform.position = b.center + dir * distance;
            cam.transform.LookAt(b.center);

            var rt = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            cam.Render();
            RenderSettings.fog = fog;

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = def.Name + " Portrait" };
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(root);
            return tex;
        }
    }
}
