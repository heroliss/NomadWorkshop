using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// Foundation 的可替换参数化灰盒工厂。它用真实尺寸和功能部件表达占地、操作面与维护结构，
    /// 但不把程序生成 Mesh 当作最终美术；正式 Prefab 接入后调用方和玩法定义都无需改变。
    /// </summary>
    public sealed class FoundationFacilityGrayboxFactory : IDisposable
    {
        private readonly List<Material> _materials = new();
        private readonly Dictionary<string, Palette> _palettes = new(StringComparer.Ordinal);

        public Renderer[] Build(Transform root, NomadFacilityDefinition definition)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (definition.Prefab != null)
            {
                GameObject instance = UnityEngine.Object.Instantiate(definition.Prefab, root);
                instance.name = definition.Prefab.name;
                instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                return root.GetComponentsInChildren<Renderer>();
            }

            Palette palette = GetPalette(definition);
            switch (definition.Function)
            {
                case NomadFacilityFunction.VehicleWaterTank:
                    BuildVehicleWaterTank(root, definition.PrototypeSize, palette);
                    break;
                case NomadFacilityFunction.DrinkingStation:
                    BuildDrinkingStation(root, definition.PrototypeSize, palette);
                    break;
                case NomadFacilityFunction.FieldKitchen:
                    BuildFieldKitchen(root, definition.PrototypeSize, palette);
                    break;
                case NomadFacilityFunction.Toilet:
                    BuildDryToilet(root, definition.PrototypeSize, palette);
                    break;
                default:
                    Primitive(
                        PrimitiveType.Cube,
                        "Replaceable Prototype Body",
                        root,
                        new Vector3(0f, definition.PrototypeSize.y * 0.5f, 0f),
                        definition.PrototypeSize,
                        palette.Paint);
                    break;
            }
            return root.GetComponentsInChildren<Renderer>();
        }

        public void Dispose()
        {
            for (var i = 0; i < _materials.Count; i++)
            {
                Material material = _materials[i];
                if (material == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(material);
                else UnityEngine.Object.DestroyImmediate(material);
            }
            _materials.Clear();
            _palettes.Clear();
        }

        private void BuildVehicleWaterTank(Transform root, Vector3 size, Palette palette)
        {
            float tankLength = Mathf.Min(size.x * 0.92f, 1.62f);
            float tankRadius = Mathf.Min(size.z * 0.48f, 0.46f);
            float tankCenterY = Mathf.Max(0.72f, size.y * 0.58f);
            Primitive(
                PrimitiveType.Cylinder,
                "Horizontal Potable Water Tank",
                root,
                new Vector3(0f, tankCenterY, 0f),
                new Vector3(tankRadius, tankLength * 0.5f, tankRadius),
                palette.Paint,
                new Vector3(0f, 0f, 90f));
            for (var band = -1; band <= 1; band += 2)
            {
                Primitive(
                    PrimitiveType.Cylinder,
                    $"Tank Retaining Band {(band < 0 ? "L" : "R")}",
                    root,
                    new Vector3(band * tankLength * 0.28f, tankCenterY, 0f),
                    new Vector3(tankRadius * 1.045f, 0.045f, tankRadius * 1.045f),
                    palette.Steel,
                    new Vector3(0f, 0f, 90f));
            }
            for (var side = -1; side <= 1; side += 2)
            {
                Primitive(
                    PrimitiveType.Cube,
                    $"Frame Rail {(side < 0 ? "Front" : "Rear")}",
                    root,
                    new Vector3(0f, 0.15f, side * size.z * 0.43f),
                    new Vector3(size.x * 1.05f, 0.18f, 0.12f),
                    palette.DarkMetal);
                Primitive(
                    PrimitiveType.Cube,
                    $"Tank Saddle {(side < 0 ? "Front" : "Rear")}",
                    root,
                    new Vector3(side * tankLength * 0.3f, 0.34f, 0f),
                    new Vector3(0.16f, 0.42f, size.z * 1.02f),
                    palette.DarkMetal);
            }
            Primitive(
                PrimitiveType.Cylinder,
                "Service Pipe",
                root,
                new Vector3(tankLength * 0.34f, 0.43f, -size.z * 0.57f),
                new Vector3(0.055f, 0.32f, 0.055f),
                palette.Steel,
                new Vector3(90f, 0f, 0f));
            Primitive(
                PrimitiveType.Cylinder,
                "Manual Shutoff Valve",
                root,
                new Vector3(tankLength * 0.34f, 0.43f, -size.z * 0.78f),
                new Vector3(0.13f, 0.025f, 0.13f),
                palette.Warning,
                new Vector3(90f, 0f, 0f));
            Primitive(
                PrimitiveType.Cylinder,
                "Pressure Gauge",
                root,
                new Vector3(0f, tankCenterY + tankRadius * 0.82f, -tankRadius * 0.55f),
                new Vector3(0.1f, 0.025f, 0.1f),
                palette.Indicator,
                new Vector3(90f, 0f, 0f));
        }

        private void BuildDrinkingStation(Transform root, Vector3 size, Palette palette)
        {
            Primitive(
                PrimitiveType.Cube,
                "Insulated Station Cabinet",
                root,
                new Vector3(0f, size.y * 0.5f, 0f),
                size,
                palette.Paint);
            Primitive(
                PrimitiveType.Cube,
                "Replaceable Filter Access Panel",
                root,
                new Vector3(0f, size.y * 0.48f, -size.z * 0.515f),
                new Vector3(size.x * 0.7f, size.y * 0.5f, 0.035f),
                palette.DarkMetal);
            Primitive(
                PrimitiveType.Cylinder,
                "Clean Water Reservoir",
                root,
                new Vector3(0f, size.y + 0.28f, 0.05f),
                new Vector3(size.x * 0.34f, 0.28f, size.x * 0.34f),
                palette.Water);
            Primitive(
                PrimitiveType.Cylinder,
                "Tap Spout",
                root,
                new Vector3(0f, size.y * 0.72f, -size.z * 0.68f),
                new Vector3(0.045f, 0.16f, 0.045f),
                palette.Steel,
                new Vector3(90f, 0f, 0f));
            Primitive(
                PrimitiveType.Cube,
                "Tap Lever",
                root,
                new Vector3(0.09f, size.y * 0.83f, -size.z * 0.54f),
                new Vector3(0.055f, 0.2f, 0.055f),
                palette.Warning,
                new Vector3(0f, 0f, -20f));
            Primitive(
                PrimitiveType.Cube,
                "Drip Tray",
                root,
                new Vector3(0f, size.y * 0.47f, -size.z * 0.68f),
                new Vector3(size.x * 0.62f, 0.055f, size.z * 0.32f),
                palette.Steel);
            Primitive(
                PrimitiveType.Cube,
                "Water Quality Indicator",
                root,
                new Vector3(-size.x * 0.3f, size.y * 0.78f, -size.z * 0.535f),
                new Vector3(0.08f, 0.08f, 0.025f),
                palette.Indicator);
        }

        private void BuildFieldKitchen(Transform root, Vector3 size, Palette palette)
        {
            Primitive(
                PrimitiveType.Cube,
                "Kitchen Cabinet Shell",
                root,
                new Vector3(0f, size.y * 0.47f, 0f),
                new Vector3(size.x, size.y * 0.88f, size.z * 0.94f),
                palette.Paint);
            Primitive(
                PrimitiveType.Cube,
                "Stainless Worktop",
                root,
                new Vector3(0f, size.y + 0.025f, 0f),
                new Vector3(size.x * 1.035f, 0.08f, size.z * 1.04f),
                palette.Steel);
            Primitive(
                PrimitiveType.Cube,
                "Rear Splash Guard",
                root,
                new Vector3(0f, size.y + 0.2f, size.z * 0.45f),
                new Vector3(size.x, 0.38f, 0.055f),
                palette.Steel);

            for (var door = 0; door < 3; door++)
            {
                float normalized = door - 1f;
                Primitive(
                    PrimitiveType.Cube,
                    $"Service Door {door + 1}",
                    root,
                    new Vector3(
                        normalized * size.x * 0.315f,
                        size.y * 0.42f,
                        -size.z * 0.49f),
                    new Vector3(size.x * 0.29f, size.y * 0.67f, 0.045f),
                    palette.Paint);
                Primitive(
                    PrimitiveType.Cube,
                    $"Door Handle {door + 1}",
                    root,
                    new Vector3(
                        normalized * size.x * 0.315f + size.x * 0.1f,
                        size.y * 0.56f,
                        -size.z * 0.535f),
                    new Vector3(0.035f, 0.22f, 0.035f),
                    palette.DarkMetal);
            }

            for (var burner = -1; burner <= 1; burner += 2)
            {
                Primitive(
                    PrimitiveType.Cylinder,
                    $"Cooktop {(burner < 0 ? "A" : "B")}",
                    root,
                    new Vector3(burner * size.x * 0.24f, size.y + 0.085f, 0f),
                    new Vector3(0.24f, 0.018f, 0.24f),
                    palette.DarkMetal);
            }
            for (var vent = 0; vent < 4; vent++)
            {
                Primitive(
                    PrimitiveType.Cube,
                    $"Vent Louver {vent + 1}",
                    root,
                    new Vector3(
                        -size.x * 0.315f,
                        size.y * (0.72f - vent * 0.055f),
                        -size.z * 0.54f),
                    new Vector3(size.x * 0.2f, 0.025f, 0.025f),
                    palette.DarkMetal);
            }
            for (var button = 0; button < 3; button++)
            {
                Primitive(
                    PrimitiveType.Cylinder,
                    $"Control Button {button + 1}",
                    root,
                    new Vector3(
                        size.x * (0.22f + button * 0.055f),
                        size.y * 0.72f,
                        -size.z * 0.545f),
                    new Vector3(0.03f, 0.012f, 0.03f),
                    button == 0 ? palette.Warning : palette.Indicator,
                    new Vector3(90f, 0f, 0f));
            }
        }

        private void BuildDryToilet(Transform root, Vector3 size, Palette palette)
        {
            Primitive(
                PrimitiveType.Cube,
                "Sealed Waste Holding Tank",
                root,
                new Vector3(0f, size.y * 0.28f, 0.1f),
                new Vector3(size.x, size.y * 0.56f, size.z),
                palette.DarkMetal);
            Primitive(
                PrimitiveType.Cylinder,
                "Toilet Pedestal",
                root,
                new Vector3(0f, size.y * 0.56f, -size.z * 0.1f),
                new Vector3(size.x * 0.36f, size.y * 0.22f, size.z * 0.32f),
                palette.Paint);
            Primitive(
                PrimitiveType.Cylinder,
                "Seat",
                root,
                new Vector3(0f, size.y * 0.78f, -size.z * 0.18f),
                new Vector3(size.x * 0.48f, 0.035f, size.z * 0.42f),
                palette.Rubber);
            Primitive(
                PrimitiveType.Cylinder,
                "Hinged Lid",
                root,
                new Vector3(0f, size.y * 0.83f, -size.z * 0.06f),
                new Vector3(size.x * 0.44f, 0.025f, size.z * 0.39f),
                palette.Paint,
                new Vector3(-18f, 0f, 0f));
            Primitive(
                PrimitiveType.Cylinder,
                "Odor Vent Pipe",
                root,
                new Vector3(size.x * 0.37f, size.y * 0.78f, size.z * 0.34f),
                new Vector3(0.055f, size.y * 0.58f, 0.055f),
                palette.Steel);
            Primitive(
                PrimitiveType.Cube,
                "Waste Level Indicator",
                root,
                new Vector3(-size.x * 0.3f, size.y * 0.42f, -size.z * 0.515f),
                new Vector3(0.075f, 0.18f, 0.025f),
                palette.Warning);
        }

        private Palette GetPalette(NomadFacilityDefinition definition)
        {
            if (_palettes.TryGetValue(definition.Id, out Palette existing)) return existing;
            Color baseColor = definition.PrototypeColor;
            var palette = new Palette(
                CreateLit($"M_{definition.Id}_Paint", baseColor, 0.18f, 0.32f),
                CreateLit(
                    $"M_{definition.Id}_Steel",
                    new Color(0.38f, 0.43f, 0.43f),
                    0.78f,
                    0.48f),
                CreateLit(
                    $"M_{definition.Id}_DarkMetal",
                    new Color(0.055f, 0.075f, 0.08f),
                    0.62f,
                    0.22f),
                CreateLit(
                    $"M_{definition.Id}_Rubber",
                    new Color(0.045f, 0.05f, 0.048f),
                    0f,
                    0.08f),
                CreateLit(
                    $"M_{definition.Id}_Warning",
                    new Color(0.95f, 0.32f, 0.06f),
                    0.12f,
                    0.26f),
                CreateLit(
                    $"M_{definition.Id}_Indicator",
                    new Color(0.1f, 0.95f, 0.56f),
                    0f,
                    0.38f,
                    new Color(0.05f, 1.25f, 0.48f)),
                CreateLit(
                    $"M_{definition.Id}_Water",
                    new Color(0.08f, 0.48f, 0.72f),
                    0.05f,
                    0.78f));
            _palettes.Add(definition.Id, palette);
            return palette;
        }

        private Material CreateLit(
            string name,
            Color color,
            float metallic,
            float smoothness,
            Color? emission = null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
            }
            _materials.Add(material);
            return material;
        }

        private static GameObject Primitive(
            PrimitiveType type,
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            Vector3? localEulerAngles = null)
        {
            GameObject instance = GameObject.CreatePrimitive(type);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.Euler(
                localEulerAngles ?? Vector3.zero);
            instance.transform.localScale = localScale;
            if (instance.TryGetComponent(out Renderer renderer))
                renderer.sharedMaterial = material;
            if (instance.TryGetComponent(out Collider collider))
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(collider);
                else UnityEngine.Object.DestroyImmediate(collider);
            }
            return instance;
        }

        private readonly struct Palette
        {
            public Palette(
                Material paint,
                Material steel,
                Material darkMetal,
                Material rubber,
                Material warning,
                Material indicator,
                Material water)
            {
                Paint = paint;
                Steel = steel;
                DarkMetal = darkMetal;
                Rubber = rubber;
                Warning = warning;
                Indicator = indicator;
                Water = water;
            }

            public Material Paint { get; }
            public Material Steel { get; }
            public Material DarkMetal { get; }
            public Material Rubber { get; }
            public Material Warning { get; }
            public Material Indicator { get; }
            public Material Water { get; }
        }
    }
}
