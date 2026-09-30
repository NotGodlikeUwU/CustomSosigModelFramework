using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CustomSosigReplacer
{
    // The supplied mask and noise are baked into a legacy particle texture:
    // H3VR's Unity version cannot compile a ShaderLab source file at runtime.
    internal sealed class BloodImpactVfx : MonoBehaviour
    {
        private const int PoolSize = 24;
        private readonly List<ParticleSystem> _bursts = new List<ParticleSystem>(PoolSize);
        private readonly List<ParticleSystem> _droplets = new List<ParticleSystem>(PoolSize);
        private Material _burstMaterial;
        private Material _dropletMaterial;
        private Texture2D _burstTexture;
        private Texture2D _dropletTexture;
        private int _next;

        internal void Initialize(string maskPath, string noisePath)
        {
            Shader shader = Shader.Find("Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader == null)
            {
                Plugin.Log.LogWarning("No legacy alpha-blended shader for the custom blood effect.");
                return;
            }
            _burstTexture = BuildBurstTexture(maskPath, noisePath);
            _dropletTexture = BuildDropletTexture();
            _burstMaterial = new Material(shader);
            _burstMaterial.name = "Custom Sosig tutorial-mask blood burst";
            _burstMaterial.mainTexture = _burstTexture;
            _dropletMaterial = new Material(shader);
            _dropletMaterial.name = "Custom Sosig blood droplets";
            _dropletMaterial.mainTexture = _dropletTexture;
            Plugin.Log.LogInfo("Custom blood VFX ready: " + (maskPath == null ? "procedural fallback" : "supplied mask and noise") + ".");
        }

        internal void Spawn(Vector3 point, Vector3 normal)
        {
            if (_burstMaterial == null) return;
            ParticleSystem burst;
            ParticleSystem droplets;
            if (_bursts.Count < PoolSize)
            {
                burst = CreateSystem(true);
                droplets = CreateSystem(false);
                _bursts.Add(burst);
                _droplets.Add(droplets);
            }
            else
            {
                burst = _bursts[_next];
                droplets = _droplets[_next];
                burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                droplets.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            _next = (_next + 1) % PoolSize;
            Vector3 direction = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
            Quaternion orientation = Quaternion.LookRotation(direction);
            burst.transform.position = point + direction * 0.014f;
            burst.transform.rotation = orientation;
            droplets.transform.position = point + direction * 0.01f;
            droplets.transform.rotation = orientation;
            burst.Play(true);
            droplets.Play(true);
            burst.Emit(2);
            droplets.Emit(7);
        }

        private static Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                Destroy(texture);
                throw new IOException("Could not decode blood VFX texture: " + path);
            }
            return texture;
        }

        private static Texture2D BuildBurstTexture(string maskPath, string noisePath)
        {
            Texture2D mask = LoadTexture(maskPath);
            Texture2D noise = LoadTexture(noisePath);
            Texture2D output = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            output.name = "Custom Sosig supplied blood mask";
            Color[] pixels = new Color[128 * 128];
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    float u = (x + 0.5f) / 128f;
                    float v = (y + 0.5f) / 128f;
                    float masked = mask != null ? mask.GetPixelBilinear(u, v).r : Mathf.Clamp01(1f - Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f);
                    float noisy = noise != null ? noise.GetPixelBilinear(u * 1.7f, v * 1.7f).r : 1f;
                    float alpha = Mathf.Clamp01((masked - 0.08f) * 1.35f) * Mathf.Lerp(0.65f, 1f, noisy);
                    pixels[y * 128 + x] = new Color(1f, 1f, 1f, alpha);
                }
            output.SetPixels(pixels);
            output.Apply(false, true);
            if (mask != null) Destroy(mask);
            if (noise != null) Destroy(noise);
            return output;
        }

        private static Texture2D BuildDropletTexture()
        {
            Texture2D output = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            output.name = "Custom Sosig blood droplet alpha";
            Color[] pixels = new Color[16 * 16];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float dx = (x - 7.5f) / 7.5f;
                    float dy = (y - 7.5f) / 7.5f;
                    pixels[y * 16 + x] = new Color(1f, 1f, 1f,
                        Mathf.Clamp01((0.9f - Mathf.Sqrt(dx * dx + dy * dy)) * 9f));
                }
            output.SetPixels(pixels);
            output.Apply(false, true);
            return output;
        }

        private ParticleSystem CreateSystem(bool burst)
        {
            GameObject obj = new GameObject(burst ? "Custom Sosig Masked Blood Burst" : "Custom Sosig Blood Droplets");
            obj.transform.SetParent(transform, false);
            ParticleSystem system = obj.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.55f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = burst ? new ParticleSystem.MinMaxCurve(0.15f, 0.26f) : new ParticleSystem.MinMaxCurve(0.22f, 0.48f);
            main.startSpeed = burst ? new ParticleSystem.MinMaxCurve(0.06f, 0.25f) : new ParticleSystem.MinMaxCurve(0.8f, 2.1f);
            main.startSize = burst ? new ParticleSystem.MinMaxCurve(0.2f, 0.32f) : new ParticleSystem.MinMaxCurve(0.006f, 0.016f);
            main.startColor = burst
                ? new ParticleSystem.MinMaxGradient(new Color(0.37f, 0.008f, 0.012f, 0.9f), new Color(0.58f, 0.025f, 0.02f, 1f))
                : new ParticleSystem.MinMaxGradient(new Color(0.22f, 0.004f, 0.007f, 0.8f), new Color(0.5f, 0.018f, 0.012f, 1f));
            main.gravityModifier = burst ? 0.15f : 1f;
            main.maxParticles = burst ? 4 : 20;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = burst ? 15f : 30f;
            shape.radius = 0.005f;
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.75f, 0.65f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(fade);
            ParticleSystemRenderer renderer = obj.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = burst ? _burstMaterial : _dropletMaterial;
            renderer.renderMode = burst ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
            if (!burst) { renderer.lengthScale = 1.4f; renderer.velocityScale = 0.25f; }
            return system;
        }

        private void OnDestroy()
        {
            if (_burstMaterial != null) Destroy(_burstMaterial);
            if (_dropletMaterial != null) Destroy(_dropletMaterial);
            if (_burstTexture != null) Destroy(_burstTexture);
            if (_dropletTexture != null) Destroy(_dropletTexture);
        }
    }
}
