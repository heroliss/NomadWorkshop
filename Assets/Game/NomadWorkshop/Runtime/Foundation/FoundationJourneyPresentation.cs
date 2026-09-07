using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 首版履带与路旁地景的距离驱动表现。绝对旅程位置决定部件姿态；不移动甲板、导航或居民。
    /// 由 WorldView 持有并随其 Bag 释放；动态材质、粒子纹理也由本实例独占。
    /// </summary>
    public sealed class FoundationJourneyPresentation : IDisposable
    {
        private const float TrackRadius = .7f;
        private const float TrackHalfStraight = .93f;
        private const int ShoesPerPod = 42;
        private const float MaximumDustLifetime = 1.2f;
        private static readonly float LoopLength = 4f * TrackHalfStraight + 2f * Mathf.PI * TrackRadius;
        private readonly Transform _vehicle;
        private readonly Transform _environment;
        private readonly List<Wheel> _wheels = new();
        private readonly List<Shoe> _shoes = new();
        private readonly List<Vector3> _podCenters = new();
        private readonly List<Scenery> _scenery = new();
        private readonly List<Renderer> _ground = new();
        private readonly List<ParticleSystem> _dust = new();
        private readonly MaterialPropertyBlock _surface = new();
        private readonly Material _dustMaterial;
        private readonly Texture2D _dustTexture;
        private long _previousPosition;
        private long _previousTick;
        private bool _hasState;
        private bool _disposed;

        private readonly struct Wheel
        {
            internal readonly Transform Part;
            internal readonly Quaternion Rotation;
            internal Wheel(Transform part, Quaternion rotation) { Part = part; Rotation = rotation; }
        }

        private readonly struct Shoe
        {
            internal readonly Transform Part;
            internal readonly Vector3 PodCenter;
            internal readonly float Distance;
            internal readonly Quaternion RotationBasis;
            internal Shoe(Transform part, Vector3 center, float distance, Quaternion basis)
            { Part = part; PodCenter = center; Distance = distance; RotationBasis = basis; }
        }

        private readonly struct Scenery
        {
            internal readonly Transform Part;
            internal readonly Vector3 Position;
            internal Scenery(Transform part, Vector3 position) { Part = part; Position = position; }
        }

        /// <summary>绑定由 Blender 配方导出的轴与履带片；缺件立即拒绝，避免静默展示半套行驶效果。</summary>
        public FoundationJourneyPresentation(Transform vehicle, Transform environment)
        {
            _vehicle = vehicle;
            _environment = environment;
            foreach (Transform part in vehicle.GetComponentsInChildren<Transform>(true))
            {
                if (part.GetComponent<Renderer>() != null) continue;
                if (part.name.Contains("_Wheel_"))
                    _wheels.Add(new Wheel(part, Quaternion.Inverse(vehicle.rotation) * part.rotation));
                else if (part.name.Contains("_Shoe_") &&
                    int.TryParse(part.name.Substring(part.name.LastIndexOf('_') + 1), NumberStyles.None,
                        CultureInfo.InvariantCulture, out int index))
                {
                    float distance = LoopLength * index / ShoesPerPod;
                    TrackPose(distance, out Vector3 offset, out float angle);
                    Vector3 center = vehicle.InverseTransformPoint(part.parent.position);
                    Vector3 actual = vehicle.InverseTransformPoint(part.position);
                    if (Vector3.Distance(actual, center + offset) > .003f)
                        throw new InvalidOperationException("履带导入轴或间距不匹配：" + part.name);
                    Quaternion rotation = Quaternion.Inverse(vehicle.rotation) * part.rotation;
                    _shoes.Add(new Shoe(part, center, distance,
                        Quaternion.Inverse(Quaternion.AngleAxis(angle, Vector3.right)) * rotation));
                }
                else if (part.name.StartsWith("TrackPod_", StringComparison.Ordinal))
                    _podCenters.Add(vehicle.InverseTransformPoint(part.position));
            }
            if (_podCenters.Count != 4 || _wheels.Count != 12 || _shoes.Count != 4 * ShoesPerPod)
                throw new InvalidOperationException($"行驶样板需要 12 轮轴 / {4*ShoesPerPod} 履带片，实际 {_wheels.Count} / {_shoes.Count}。");
            foreach (Transform part in environment.GetComponentsInChildren<Transform>(true))
                if (part.GetComponent<Renderer>() == null && part.name.StartsWith("Desert outcrop_", StringComparison.Ordinal))
                    _scenery.Add(new Scenery(part, environment.InverseTransformPoint(part.position)));
            foreach (Renderer renderer in environment.GetComponentsInChildren<Renderer>())
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.name == "NW1_Ground")
                    _ground.Add(renderer);

            _dustTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "工坊扬尘柔边", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float radius = new Vector2((x-15.5f)/15.5f, (y-15.5f)/15.5f).magnitude;
                    pixels[y*32+x] = new Color(1,1,1, Mathf.Pow(Mathf.Clamp01(1-radius), 2));
                }
            _dustTexture.SetPixels(pixels);
            _dustTexture.Apply(false, true);
            _dustMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "工坊行驶扬尘" };
            _dustMaterial.SetTexture("_BaseMap", _dustTexture);
            _dustMaterial.SetColor("_BaseColor", new Color(.65f, .48f, .31f, .30f));
            _dustMaterial.SetFloat("_Surface", 1);
            _dustMaterial.SetFloat("_Blend", 0);
            _dustMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            _dustMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            _dustMaterial.SetFloat("_ZWrite", 0);
            _dustMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _dustMaterial.renderQueue = (int)RenderQueue.Transparent;
            foreach (Vector3 center in _podCenters)
                CreateDust(center + Vector3.down * .49f);
        }

        /// <summary>
        /// 每帧投影已提交的状态；扬尘只消费已提交模拟毫秒，暂停或重复投影不推进粒子。
        /// 空间重建须先清除瞬态；时间回退也会清除，不按单帧位移大小猜测是否发生读档。
        /// 返程使用有符号距离，当前直线路线表现为倒行，尚不模拟车头掉转。
        /// </summary>
        public void Render(long positionMicrometers, long simulationTick, bool moving)
        {
            if (_disposed) return;
            double distance = positionMicrometers / 1000000d;
            if (!_hasState || positionMicrometers != _previousPosition)
            {
                float wheelAngle = (float)(distance / .44d * Mathf.Rad2Deg % 360d);
                foreach (Wheel wheel in _wheels)
                    wheel.Part.rotation = _vehicle.rotation * Quaternion.AngleAxis(wheelAngle, Vector3.right) * wheel.Rotation;
                foreach (Shoe shoe in _shoes)
                {
                    TrackPose(shoe.Distance + distance, out Vector3 offset, out float angle);
                    shoe.Part.SetPositionAndRotation(_vehicle.TransformPoint(shoe.PodCenter + offset),
                        _vehicle.rotation * Quaternion.AngleAxis(angle, Vector3.right) * shoe.RotationBasis);
                }
                foreach (Scenery scenery in _scenery)
                {
                    Vector3 position = scenery.Position;
                    position.z = (float)PositiveModulo(position.z - distance + 30d, 60d) - 30f;
                    scenery.Part.position = _environment.TransformPoint(position);
                }
                _surface.SetVector("_BaseMap_ST", new Vector4(1, 1, 0, (float)PositiveModulo(distance / 4d, 1d)));
                foreach (Renderer renderer in _ground) renderer.SetPropertyBlock(_surface);
            }
            bool reset = !_hasState || simulationTick < _previousTick;
            // 长帧只保留仍可能存活的扬尘历史，不追赶无限粒子步，也不重复乘游戏倍率。
            float elapsed = reset ? 0f : Mathf.Min((simulationTick - _previousTick) / 1000f,
                MaximumDustLifetime + .05f);
            foreach (ParticleSystem dust in _dust)
            {
                if (reset) dust.Simulate(0f, false, true, false);
                var emission = dust.emission;
                emission.enabled = moving;
                // Simulate 完成后保持暂停；禁止再 Play，否则 Unity 会额外按帧时钟推进一遍。
                if (elapsed > 0f && (moving || dust.particleCount > 0))
                    dust.Simulate(elapsed, false, false, false);
            }
            _previousPosition = positionMicrometers;
            _previousTick = simulationTick;
            _hasState = true;
        }

        /// <summary>设施/世界空间重建后丢弃旧路段扬尘；下一次 Render 从新状态建立基准，不补播读档间隔。</summary>
        public void ClearTransientEffects()
        {
            if (_disposed) return;
            foreach (ParticleSystem dust in _dust) dust.Simulate(0f, false, true, false);
            _hasState = false;
        }

        private void CreateDust(Vector3 localPosition)
        {
            var root = new GameObject("履带扬尘");
            root.transform.SetParent(_vehicle, false);
            root.transform.localPosition = localPosition;
            ParticleSystem dust = root.AddComponent<ParticleSystem>();
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = dust.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.65f, MaximumDustLifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(.25f, .65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.15f, .40f);
            main.startColor = Color.white;
            main.maxParticles = 36;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.simulationSpeed = 1f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = dust.emission;
            emission.rateOverTime = 18;
            var shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = .38f;
            var velocity = dust.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.y = .16f;
            var color = dust.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white,0), new GradientColorKey(Color.white,1) },
                new[] { new GradientAlphaKey(0,0), new GradientAlphaKey(.7f,.15f), new GradientAlphaKey(0,1) });
            color.color = gradient;
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _dustMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _dust.Add(dust);
        }

        private static double PositiveModulo(double value, double period) => (value % period + period) % period;

        private static void TrackPose(double distance, out Vector3 offset, out float angleDegrees)
        {
            float d = (float)PositiveModulo(distance, LoopLength);
            float straight = 2 * TrackHalfStraight;
            float arc = Mathf.PI * TrackRadius;
            float y, z, angle;
            if (d < straight) { y = TrackRadius; z = -TrackHalfStraight+d; angle = 0; }
            else if (d < straight+arc)
            {
                angle = (d-straight)/TrackRadius;
                y = TrackRadius*Mathf.Cos(angle); z = TrackHalfStraight+TrackRadius*Mathf.Sin(angle);
            }
            else if (d < 2*straight+arc)
            { y = -TrackRadius; z = TrackHalfStraight-(d-straight-arc); angle = Mathf.PI; }
            else
            {
                float a = (d-2*straight-arc)/TrackRadius;
                y = -TrackRadius*Mathf.Cos(a); z = -TrackHalfStraight-TrackRadius*Mathf.Sin(a); angle = Mathf.PI+a;
            }
            offset = new Vector3(0,y,z);
            angleDegrees = angle*Mathf.Rad2Deg;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (ParticleSystem dust in _dust) if (dust != null) UnityEngine.Object.Destroy(dust.gameObject);
            if (_dustMaterial != null) UnityEngine.Object.Destroy(_dustMaterial);
            if (_dustTexture != null) UnityEngine.Object.Destroy(_dustTexture);
        }
    }
}
