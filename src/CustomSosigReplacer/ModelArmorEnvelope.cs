using System.Collections.Generic;
using UnityEngine;

namespace CustomSosigReplacer
{
    // Bind-pose envelopes are calculated once per loaded addon, not once per
    // Sosig. They enclose the model vertices assigned to each body segment.
    internal static class ModelArmorEnvelope
    {
        internal struct Shape
        {
            internal readonly float CenterY;
            internal readonly float Radius;
            internal readonly float Height;
            internal readonly int VertexCount;

            internal Shape(float centerY, float radius, float height, int vertexCount)
            {
                CenterY = centerY;
                Radius = radius;
                Height = height;
                VertexCount = vertexCount;
            }
        }

        private sealed class Segment
        {
            internal string Label;
            internal string Start;
            internal string End;
            internal bool Armor;
            internal Vector3 Origin;
            internal Vector3 Axis;
            internal float Minimum = float.MaxValue;
            internal float Maximum = float.MinValue;
            internal float Radius;
            internal int Count;

            internal Segment(string label, string start, string end, bool armor)
            {
                Label = label; Start = start; End = end; Armor = armor;
            }
        }

        private const string Prefix = "ValveBiped.Bip01_";
        private const float SkinClearance = 0.035f;

        internal static Dictionary<string, Shape> Build(RuntimeSkinnedAsset asset)
        {
            var result = new Dictionary<string, Shape>();
            if (asset == null || asset.Bones == null || asset.Meshes == null) return result;
            Segment[] segments = {
                new Segment("Hips", "Pelvis", "Spine2", true),
                new Segment("Chest", "Spine2", "Neck1", true),
                new Segment("Head", "Head1", null, true),
                new Segment("LeftUpperArm", "L_UpperArm", "L_Forearm", false),
                new Segment("LeftLowerArm", "L_Forearm", "L_Hand", false),
                new Segment("RightUpperArm", "R_UpperArm", "R_Forearm", false),
                new Segment("RightLowerArm", "R_Forearm", "R_Hand", false),
                new Segment("LeftUpperLeg", "L_Thigh", "L_Calf", true),
                new Segment("LeftLowerLeg", "L_Calf", "L_Foot", true),
                new Segment("RightUpperLeg", "R_Thigh", "R_Calf", true),
                new Segment("RightLowerLeg", "R_Calf", "R_Foot", true)
            };
            var indices = new Dictionary<string, int>();
            Matrix4x4[] rest = new Matrix4x4[asset.Bones.Length];
            for (int index = 0; index < asset.Bones.Length; index++)
            {
                RuntimeSkinnedAsset.BoneDefinition bone = asset.Bones[index];
                indices[bone.Name] = index;
                Matrix4x4 local = Matrix4x4.TRS(bone.LocalPosition, bone.LocalRotation, bone.LocalScale);
                rest[index] = bone.ParentIndex >= 0 ? rest[bone.ParentIndex] * local : local;
            }
            var anchorToSegment = new Dictionary<int, int>();
            for (int index = 0; index < segments.Length; index++)
            {
                Segment segment = segments[index];
                int start;
                if (!indices.TryGetValue(Prefix + segment.Start, out start)) continue;
                anchorToSegment[start] = index;
                segment.Origin = rest[start].MultiplyPoint3x4(Vector3.zero);
                if (segment.End == null)
                {
                    segment.Axis = rest[start].MultiplyVector(Vector3.up).normalized;
                }
                else
                {
                    int end;
                    if (!indices.TryGetValue(Prefix + segment.End, out end)) continue;
                    segment.Axis = (rest[end].MultiplyPoint3x4(Vector3.zero) - segment.Origin).normalized;
                }
            }
            int[] assigned = new int[asset.Bones.Length];
            for (int bone = 0; bone < assigned.Length; bone++)
            {
                assigned[bone] = -1;
                for (int cursor = bone; cursor >= 0; cursor = asset.Bones[cursor].ParentIndex)
                {
                    int mapped;
                    if (!anchorToSegment.TryGetValue(cursor, out mapped)) continue;
                    assigned[bone] = mapped;
                    break;
                }
            }
            foreach (RuntimeSkinnedAsset.MeshDefinition mesh in asset.Meshes)
            {
                Vector3[] vertices = mesh.Mesh.vertices;
                BoneWeight[] weights = mesh.Mesh.boneWeights;
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                {
                    BoneWeight weight = weights[vertex];
                    int dominant = weight.boneIndex0;
                    float strongest = weight.weight0;
                    if (weight.weight1 > strongest) { dominant = weight.boneIndex1; strongest = weight.weight1; }
                    if (weight.weight2 > strongest) { dominant = weight.boneIndex2; strongest = weight.weight2; }
                    if (weight.weight3 > strongest) { dominant = weight.boneIndex3; }
                    if (dominant < 0 || dominant >= assigned.Length) continue;
                    int segmentIndex = assigned[dominant];
                    if (segmentIndex < 0) continue;
                    Segment segment = segments[segmentIndex];
                    if (!segment.Armor || segment.Axis.sqrMagnitude < 0.9f) continue;
                    Vector3 offset = vertices[vertex] - segment.Origin;
                    float y = Vector3.Dot(offset, segment.Axis);
                    float radial = (offset - segment.Axis * y).magnitude;
                    segment.Minimum = Mathf.Min(segment.Minimum, y);
                    segment.Maximum = Mathf.Max(segment.Maximum, y);
                    segment.Radius = Mathf.Max(segment.Radius, radial);
                    segment.Count++;
                }
            }
            foreach (Segment segment in segments)
            {
                if (!segment.Armor || segment.Count < 16) continue;
                float radius = segment.Radius + SkinClearance;
                float height = segment.Maximum - segment.Minimum + SkinClearance * 2f;
                result[segment.Label] = new Shape((segment.Minimum + segment.Maximum) * 0.5f,
                    radius, height, segment.Count);
            }
            return result;
        }
    }
}
