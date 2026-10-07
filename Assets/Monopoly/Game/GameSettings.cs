using System;
using UnityEngine;

namespace Monopoly.Game
{
    public enum GraphicsQuality { Low, Medium, High }

    /// <summary>
    /// Player preferences, saved in PlayerPrefs and applied immediately. Defaults suit the device: phones start on
    /// Medium quality (cooler, smoother), desktops on High.
    /// </summary>
    public static class GameSettings
    {
        private const string Prefix = "monopoly.settings.";

        public static GraphicsQuality Quality = Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer
            ? GraphicsQuality.Medium : GraphicsQuality.High;
        /// <summary>Frosted-glass panels (a blurred copy of the 3D scene behind the UI).</summary>
        public static bool Blur = true;
        public static int FrameRate = 60;
        /// <summary>0 = off, 1 = low, 2 = medium, 3 = high.</summary>
        public static int Volume = 2;
        /// <summary>If true, moving the camera yourself only lasts until the next turn; otherwise until you tap Focus.</summary>
        public static bool CameraAutoReturn;
        public static bool CameraShake = true;
        /// <summary>Animation speed multiplier during games: 1, 2 or 4.</summary>
        public static int GameSpeed = 1;
        /// <summary>How quickly computer players act: 0 relaxed, 1 normal, 2 fast.</summary>
        public static int CpuSpeed = 1;

        public static event Action Changed;

        public static float VolumeLevel => new[] { 0f, 0.3f, 0.6f, 1f }[Mathf.Clamp(Volume, 0, 3)];
        public static float CpuDelay => new[] { 1.1f, 0.6f, 0.25f }[Mathf.Clamp(CpuSpeed, 0, 2)];

        public static void Load()
        {
            Quality = (GraphicsQuality)PlayerPrefs.GetInt(Prefix + "quality", (int)Quality);
            Blur = PlayerPrefs.GetInt(Prefix + "blur", Blur ? 1 : 0) == 1;
            FrameRate = PlayerPrefs.GetInt(Prefix + "fps", FrameRate);
            Volume = PlayerPrefs.GetInt(Prefix + "volume", Volume);
            CameraAutoReturn = PlayerPrefs.GetInt(Prefix + "cameraAutoReturn", CameraAutoReturn ? 1 : 0) == 1;
            CameraShake = PlayerPrefs.GetInt(Prefix + "cameraShake", CameraShake ? 1 : 0) == 1;
            GameSpeed = PlayerPrefs.GetInt(Prefix + "gameSpeed", GameSpeed);
            CpuSpeed = PlayerPrefs.GetInt(Prefix + "cpuSpeed", CpuSpeed);
        }

        /// <summary>Saves and applies everything, then notifies listeners (the HUD, the game flow).</summary>
        public static void Save()
        {
            PlayerPrefs.SetInt(Prefix + "quality", (int)Quality);
            PlayerPrefs.SetInt(Prefix + "blur", Blur ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "fps", FrameRate);
            PlayerPrefs.SetInt(Prefix + "volume", Volume);
            PlayerPrefs.SetInt(Prefix + "cameraAutoReturn", CameraAutoReturn ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "cameraShake", CameraShake ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "gameSpeed", GameSpeed);
            PlayerPrefs.SetInt(Prefix + "cpuSpeed", CpuSpeed);
            PlayerPrefs.Save();
            Apply();
        }

        public static void Apply()
        {
            Application.targetFrameRate = FrameRate;
            QualitySettings.vSyncCount = 0;
            Sfx.Volume = VolumeLevel;
            var postFx = UnityEngine.Object.FindFirstObjectByType<PostFx>();
            if (postFx != null) postFx.ApplyQuality(Quality);
            else ApplyShadows(Quality);
            var blur = UnityEngine.Object.FindFirstObjectByType<BlurCapture>();
            if (blur != null) blur.enabled = Blur;
            Changed?.Invoke();
        }

        public static void ApplyShadows(GraphicsQuality q)
        {
            bool phone = Application.isMobilePlatform;
            switch (q)
            {
                case GraphicsQuality.High:
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.shadowResolution = phone ? ShadowResolution.High : ShadowResolution.VeryHigh;
                    QualitySettings.shadowDistance = phone ? 45f : 60f;
                    QualitySettings.shadowCascades = 2;
                    QualitySettings.antiAliasing = phone ? 2 : 4;
                    break;
                case GraphicsQuality.Medium:
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.shadowResolution = ShadowResolution.Medium;
                    QualitySettings.shadowDistance = 32f;
                    QualitySettings.shadowCascades = 1;
                    QualitySettings.antiAliasing = 0;
                    break;
                default:
                    QualitySettings.shadows = ShadowQuality.Disable;
                    QualitySettings.antiAliasing = 0;
                    break;
            }
        }
    }
}
