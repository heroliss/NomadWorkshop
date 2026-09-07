using UnityEngine;

namespace Game.NomadWorkshop.Foundation
{
    /// <summary>
    /// 正式场景与携物实验共用的水罐几何。底面为局部 Y=0，把手中心为 Y=.56；
    /// 调用方拥有根物体和材质，本工厂不创建物品身份、碰撞或库存。
    /// </summary>
    public static class FoundationWaterCanVisualFactory
    {
        public const float GripHeight = .56f;
        public const float BodyHalfWidth = .17f;
        public const string PalmTargetName = "Right Palm Contact";
        public static readonly Vector3 OpeningPosition = new(.12f, .445f, .085f);

        public static Transform Create(Transform parent, Material shell, Material hardware,
            Material water, out Transform fill)
        {
            var root = new GameObject("Water Can 01 [physical carrier]").transform;
            root.SetParent(parent, false);
            Part("Can Body", new(0,.2f,0), new(BodyHalfWidth * 2f,.4f,.24f), shell);
            Part("Handle Left", new(-.11f,.47f,0), new(.055f,.18f,.055f), hardware);
            Part("Handle Right", new(.11f,.47f,0), new(.055f,.18f,.055f), hardware);
            Part("Handle Top", new(0,GripHeight,0), new(.27f,.055f,.055f), hardware);
            var palmTarget = new GameObject(PalmTargetName).transform;
            palmTarget.SetParent(root, false);
            palmTarget.localPosition = new Vector3(0, GripHeight + .0275f, 0);
            // 罐口放在把手前侧，避免装水线穿过右侧把手立柱；开盖后仍有可辨认的注水颈。
            Part("Pouring Neck", new(.12f,.415f,.085f), new(.082f,.025f,.082f), shell, PrimitiveType.Cylinder);
            Part("Open Mouth", new(.12f,.441f,.085f), new(.06f,.0015f,.06f), hardware, PrimitiveType.Cylinder);
            Part("Sealed Cap", OpeningPosition + Vector3.up * .014f, new(.09f,.012f,.09f), hardware, PrimitiveType.Cylinder);
            fill = Part("Contains Water", new(0,.2f,-.126f), new(.22f,.22f,.015f), water);
            fill.gameObject.SetActive(false);
            return root;

            Transform Part(string name, Vector3 center, Vector3 size, Material material,
                PrimitiveType type = PrimitiveType.Cube)
            {
                GameObject part = GameObject.CreatePrimitive(type);
                part.name = name;
                part.transform.SetParent(root, false);
                part.transform.localPosition = center;
                part.transform.localScale = size;
                part.GetComponent<Renderer>().sharedMaterial = material;
                Collider collider = part.GetComponent<Collider>();
                collider.enabled = false;
                if (Application.isPlaying) Object.Destroy(collider);
                else Object.DestroyImmediate(collider);
                return part.transform;
            }
        }

        /// <summary>按当前 Avatar 臂长标定持桶握点，返回居民逻辑根的局部坐标。</summary>
        public static Vector3 GetGripPosition(ResidentHumanoidPresentation humanoid)
        {
            Animator animator = humanoid.Animator;
            Transform upper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            float length = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
            Vector3 shoulder = humanoid.transform.InverseTransformPoint(upper.position);
            // 肩骨外侧再留出半个罐宽和衣物/身体净空；只按臂长外移少量会把罐壳藏进大腿。
            return new Vector3(shoulder.x + Mathf.Sign(shoulder.x) * (BodyHalfWidth + .10f),
                shoulder.y - length * .72f, .08f);
        }
    }
}
