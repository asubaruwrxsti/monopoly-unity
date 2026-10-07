using System;
using System.Collections.Generic;
using UnityEngine;

namespace Monopoly.Game
{
    public enum SfxKind { DiceShake, DiceHit, Hop, Coin, CashRegister, Whoosh, Pop, Fanfare, Sad, Click, Card }

    /// <summary>
    /// Tiny synthesiser for UI and game sounds, so the project needs no audio assets. Clips are generated once
    /// at startup; swap in recorded clips here later if you have them.
    /// </summary>
    public sealed class Sfx : MonoBehaviour
    {
        private const int Rate = 44100;
        private static Sfx instance;
        private readonly Dictionary<SfxKind, AudioClip> clips = new Dictionary<SfxKind, AudioClip>();
        private AudioSource source;
        private readonly System.Random noise = new System.Random(7);

        public static float Volume = 0.6f;

        public static void Play(SfxKind kind, float volume = 1f, float pitch = 1f)
        {
            if (instance == null) instance = new GameObject("Sfx").AddComponent<Sfx>();
            instance.PlayInternal(kind, volume, pitch);
        }

        private void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            if (FindFirstObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();

            clips[SfxKind.DiceShake] = Make("shake", 0.35f, t => Noise() * Env(t, 0.35f, 0.02f) * (0.5f + 0.5f * Mathf.Sin(t * 90f)) * 0.5f);
            clips[SfxKind.DiceHit] = Make("hit", 0.12f, t => (Noise() * 0.6f + Mathf.Sin(t * 2 * Mathf.PI * 900f) * 0.4f) * Mathf.Exp(-t * 45f));
            clips[SfxKind.Hop] = Make("hop", 0.09f, t => Mathf.Sin(2 * Mathf.PI * (520f + 900f * t) * t) * Mathf.Exp(-t * 40f) * 0.6f);
            clips[SfxKind.Coin] = Make("coin", 0.5f, t => (Mathf.Sin(2 * Mathf.PI * 1568f * t) + 0.5f * Mathf.Sin(2 * Mathf.PI * (t < 0.07f ? 1175f : 2093f) * t)) * Mathf.Exp(-t * 8f) * 0.45f);
            clips[SfxKind.CashRegister] = Make("cash", 0.7f, t =>
                (t < 0.08f ? Noise() * Mathf.Exp(-t * 60f) : 0f) +
                Mathf.Sin(2 * Mathf.PI * 2637f * t) * Mathf.Exp(-(t - 0.08f) * 6f) * (t > 0.08f ? 0.5f : 0f));
            clips[SfxKind.Whoosh] = Make("whoosh", 0.45f, t => Noise() * Mathf.Sin(t / 0.45f * Mathf.PI) * 0.35f);
            clips[SfxKind.Pop] = Make("pop", 0.15f, t => Mathf.Sin(2 * Mathf.PI * (300f + 1400f * t) * t) * Mathf.Exp(-t * 30f) * 0.7f);
            clips[SfxKind.Click] = Make("click", 0.05f, t => Mathf.Sin(2 * Mathf.PI * 1800f * t) * Mathf.Exp(-t * 120f) * 0.5f);
            clips[SfxKind.Card] = Make("card", 0.25f, t => Noise() * Mathf.Exp(-t * 18f) * 0.4f);
            clips[SfxKind.Fanfare] = Make("fanfare", 1.1f, t => Arpeggio(t, new[] { 523f, 659f, 784f, 1047f }, 0.12f) * 0.4f);
            clips[SfxKind.Sad] = Make("sad", 0.8f, t => Arpeggio(t, new[] { 392f, 370f, 349f, 330f }, 0.18f) * 0.35f);
        }

        private void PlayInternal(SfxKind kind, float volume, float pitch)
        {
            if (!clips.TryGetValue(kind, out var clip)) return;
            source.pitch = pitch;
            source.PlayOneShot(clip, volume * Volume);
        }

        private float Noise() => (float)(noise.NextDouble() * 2 - 1);

        private static float Env(float t, float length, float attack) => Mathf.Clamp01(t / attack) * Mathf.Clamp01((length - t) / (length * 0.5f));

        private static float Arpeggio(float t, float[] notes, float step)
        {
            int i = Mathf.Min(notes.Length - 1, (int)(t / step));
            float local = t - i * step;
            float decay = i == notes.Length - 1 ? Mathf.Exp(-local * 3f) : Mathf.Exp(-local * 10f);
            float f = notes[i];
            return (Mathf.Sin(2 * Mathf.PI * f * t) + 0.3f * Mathf.Sin(4 * Mathf.PI * f * t)) * decay;
        }

        private static AudioClip Make(string name, float seconds, Func<float, float> wave)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(wave(i / (float)Rate), -1f, 1f);
            // Short fade-out avoids clicks at the end.
            for (int i = Mathf.Max(0, n - 200); i < n; i++) data[i] *= (n - i) / 200f;
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
