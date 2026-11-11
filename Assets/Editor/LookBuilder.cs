using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SurviveI95.EditorTools
{
    /// <summary>
    /// Lights the scene from a look in Resources/Data/looks.json: sky, sun, haze, color grade and bloom.
    /// The sun's direction and the haze color are measured from the sky picture, so shadows and horizon always agree with it.
    /// </summary>
    public static class LookBuilder
    {
        const string Gen = "Assets/Generated", Skies = "Assets/ThirdParty/PolyHaven/Skies/";

        [Serializable]
        public class Look
        {
            public string id, sky, tonemap;
            public float skyExposure, sunAzimuth, sunIntensity, shadowStrength, ambient, groundBounce, hazeDensity, hazeBrightness;
            public Color sunColor;
            public float exposure, contrast, saturation, temperature, tint, bloomThreshold, bloomIntensity, bloomScatter, vignette;
        }
        [Serializable] class LookFile { public List<Look> looks; }

        public static Look Load(string id)
        {
            var look = JsonUtility.FromJson<LookFile>(File.ReadAllText("Assets/Resources/Data/looks.json")).looks.Find(l => l.id == id);
            if (look == null) throw new InvalidOperationException("Unknown look: " + id);
            return look;
        }

        /// <summary>Replaces the scene's sun, sky, haze and grade. Returns what was measured, for the test log.</summary>
        public static string Apply(string id, Camera cam)
        {
            var look = Load(id);
            foreach (var old in new[] { "Sun", "Look" }) { var go = GameObject.Find(old); if (go != null) UnityEngine.Object.DestroyImmediate(go); }

            string skyFile = Skies + look.sky + ".hdr";
            // Mipmaps make a visible vertical seam where the panorama's edges meet.
            if (AssetImporter.GetAtPath(skyFile) is TextureImporter imp && (imp.mipmapEnabled || imp.wrapMode != TextureWrapMode.Repeat || imp.maxTextureSize < 4096))
            { imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Repeat; imp.maxTextureSize = 4096; imp.SaveAndReimport(); }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(skyFile);
            if (tex == null) throw new InvalidOperationException("Missing sky: " + skyFile + " (run node Tools/fetch_assets.js environment)");
            Measure(tex, out float sunLongitude, out float sunElevation, out Color horizon, out Color overhead);

            // Compass azimuth: 0 is north (+Z), 90 is east (+X). The panorama is turned until its sun sits there.
            float rotation = Mathf.Repeat(90f - look.sunAzimuth - sunLongitude, 360f);
            var sky = new Material(Shader.Find("Skybox/Panoramic"));
            sky.SetTexture("_MainTex", tex);
            sky.SetFloat("_Exposure", look.skyExposure);
            sky.SetFloat("_Rotation", rotation);
            Directory.CreateDirectory(Gen + "/Materials");
            AssetDatabase.DeleteAsset(Gen + "/Materials/Sky.mat");
            AssetDatabase.CreateAsset(sky, Gen + "/Materials/Sky.mat");
            RenderSettings.skybox = sky;
            // Shade is lit by the blue sky from above and by sunlight bouncing off the ground from below.
            // The sky picture alone has no sunlit ground in it, and leaves everything in shade bright blue.
            float bounce = look.groundBounce * look.sunIntensity * Mathf.Sin(sunElevation * Mathf.Deg2Rad);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // Measured light is linear, and Unity expects these colors as screen (gamma) values, hence .gamma. Without it shade came out navy.
            var sunLinear = look.sunColor.linear;
            RenderSettings.ambientSkyColor = (overhead * (look.skyExposure * look.ambient)).gamma;
            RenderSettings.ambientEquatorColor = (horizon * (look.skyExposure * look.ambient)).gamma;
            RenderSettings.ambientGroundColor = new Color(sunLinear.r * bounce, sunLinear.g * bounce * .97f, sunLinear.b * bounce * .9f).gamma;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential; // real air thins the view evenly with distance; the squared kind keeps the middle distance too clear
            RenderSettings.fogDensity = look.hazeDensity;
            var haze = horizon * (look.skyExposure * look.hazeBrightness); haze.a = 1f;
            RenderSettings.fogColor = haze.gamma;

            var sun = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = look.sunIntensity; sun.color = look.sunColor;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = look.shadowStrength;
            float az = look.sunAzimuth * Mathf.Deg2Rad, el = sunElevation * Mathf.Deg2Rad;
            var toSun = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
            sun.transform.rotation = Quaternion.LookRotation(-toSun);
            RenderSettings.sun = sun;
            DynamicGI.UpdateEnvironment();

            var volume = new GameObject("Look", typeof(Volume)).GetComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = Grade(look);

            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.dithering = true; // hides banding in the sky's smooth gradients
                EditorUtility.SetDirty(data);
            }
            return FormattableString.Invariant($"look={look.id} sunElevation={sunElevation:F1} sunAzimuth={look.sunAzimuth:F0} skyRotation={rotation:F1} haze=({haze.r:F2},{haze.g:F2},{haze.b:F2}) overhead=({overhead.r:F2},{overhead.g:F2},{overhead.b:F2}) bounce={bounce:F2}");
        }

        /// <summary>Applies a look to the open scene. In edit mode the scene is saved; in play mode it lasts until stop.</summary>
        public static string Relight(string id)
        {
            var camGo = GameObject.FindGameObjectWithTag("MainCamera");
            string result = Apply(id, camGo != null ? camGo.GetComponent<Camera>() : null);
            if (!EditorApplication.isPlaying)
            {
                var scene = EditorSceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                result += " saved";
            }
            return result;
        }

        static VolumeProfile Grade(Look look)
        {
            string file = Gen + "/Look_" + look.id + ".asset";
            AssetDatabase.DeleteAsset(file);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, file);

            var tone = Add<Tonemapping>(profile);
            tone.mode.Override(look.tonemap == "aces" ? TonemappingMode.ACES : look.tonemap == "neutral" ? TonemappingMode.Neutral : TonemappingMode.None);
            var color = Add<ColorAdjustments>(profile);
            color.postExposure.Override(look.exposure); color.contrast.Override(look.contrast); color.saturation.Override(look.saturation);
            var white = Add<WhiteBalance>(profile);
            white.temperature.Override(look.temperature); white.tint.Override(look.tint);
            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(look.bloomThreshold); bloom.intensity.Override(look.bloomIntensity); bloom.scatter.Override(look.bloomScatter);
            bloom.highQualityFiltering.Override(true);
            var vignette = Add<Vignette>(profile);
            vignette.intensity.Override(look.vignette); vignette.smoothness.Override(0.4f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var part = profile.Add<T>();
            part.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(part, profile);
            return part;
        }

        // Finds the sun (the brightest patch) and the average color just above the horizon in a latitude-longitude sky picture.
        static void Measure(Texture2D tex, out float sunLongitude, out float sunElevation, out Color horizon, out Color overhead)
        {
            const int W = 2048, H = 1024;
            var rt = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(W, H, TextureFormat.RGBAFloat, false, true);
            copy.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = copy.GetPixels();
            UnityEngine.Object.DestroyImmediate(copy);

            float peak = 0f;
            foreach (var p in px) peak = Mathf.Max(peak, p.r + p.g + p.b);
            // Averaged as directions, so a sun that straddles the picture's left and right edge still comes out right.
            Vector3 sum = Vector3.zero;
            Vector3 hazeSum = Vector3.zero, skySum = Vector3.zero; int hazeCount = 0; float skyWeight = 0f;
            for (int y = 0; y < H; y++)
            {
                float lat = (1f - (y + .5f) / H) * Mathf.PI; // 0 straight up, pi straight down
                float elevation = 90f - lat * Mathf.Rad2Deg;
                bool band = elevation > 1f && elevation < 5f;
                for (int x = 0; x < W; x++)
                {
                    var p = px[y * W + x];
                    float lum = p.r + p.g + p.b;
                    if (lum > peak * .25f)
                    {
                        float lon = (.5f - (x + .5f) / W) * 2f * Mathf.PI;
                        sum += lum * new Vector3(Mathf.Cos(lon) * Mathf.Sin(lat), Mathf.Cos(lat), Mathf.Sin(lon) * Mathf.Sin(lat));
                    }
                    if (band) { hazeSum += new Vector3(p.r, p.g, p.b); hazeCount++; }
                    // The sky as a flat roof sees it: weighted toward straight up, with the sun's own disc capped out.
                    if (elevation > 0f) { float w = Mathf.Sin(lat) * Mathf.Cos(lat); skySum += w * new Vector3(Mathf.Min(p.r, 4f), Mathf.Min(p.g, 4f), Mathf.Min(p.b, 4f)); skyWeight += w; }
                }
            }
            var dir = sum.normalized;
            sunElevation = Mathf.Asin(dir.y) * Mathf.Rad2Deg;
            sunLongitude = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
            hazeSum /= Mathf.Max(1, hazeCount);
            horizon = new Color(hazeSum.x, hazeSum.y, hazeSum.z, 1f);
            skySum /= Mathf.Max(1e-5f, skyWeight);
            overhead = new Color(skySum.x, skySum.y, skySum.z, 1f);
            if (sunElevation < 5f) Debug.LogError("[Look] sun measured below the horizon in " + tex.name + ": " + sunElevation.ToString("F1", CultureInfo.InvariantCulture));
        }
    }
}
