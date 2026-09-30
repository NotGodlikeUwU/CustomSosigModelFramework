using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace CustomSosigReplacer
{
    internal sealed class RuntimeSkinnedAsset
    {
        public readonly float MinY;
        public readonly float MaxY;
        public readonly BoneDefinition[] Bones;
        public readonly Matrix4x4[] Bindposes;
        public readonly MeshDefinition[] Meshes;
        public Dictionary<string, ModelArmorEnvelope.Shape> ArmorEnvelopes;
        public SoleVertex[] SoleVertices;

        private RuntimeSkinnedAsset(float minY, float maxY, BoneDefinition[] bones, Matrix4x4[] bindposes, MeshDefinition[] meshes)
        {
            MinY = minY;
            MaxY = maxY;
            Bones = bones;
            Bindposes = bindposes;
            Meshes = meshes;
        }

        public float Height { get { return MaxY - MinY; } }

        public static RuntimeSkinnedAsset Load(string path, ModelAddon addon)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Addon skinned mesh data was not found.", path);
            using (FileStream file = File.OpenRead(path))
            using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
            using (BinaryReader reader = new BinaryReader(gzip))
            {
                string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (magic != "CSK1") throw new IOException("Unexpected skinned mesh signature: " + magic);
                int version = reader.ReadInt32();
                if (version != 1) throw new IOException("Unsupported skinned mesh version: " + version);
                float minY = reader.ReadSingle();
                float maxY = reader.ReadSingle();
                int boneCount = reader.ReadInt32();
                if (boneCount <= 0 || boneCount > 255) throw new IOException("Invalid bone count: " + boneCount);

                BoneDefinition[] bones = new BoneDefinition[boneCount];
                for (int index = 0; index < boneCount; index++)
                {
                    int byteCount = reader.ReadInt32();
                    if (byteCount <= 0 || byteCount > 1024) throw new IOException("Invalid bone name length.");
                    string name = Encoding.UTF8.GetString(reader.ReadBytes(byteCount));
                    if (addon != null) name = addon.CanonicalBone(name);
                    int parent = reader.ReadInt32();
                    Vector3 position = ReadVector3(reader);
                    Quaternion rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    Vector3 scale = ReadVector3(reader);
                    bones[index] = new BoneDefinition(name, parent, position, rotation, scale);
                }

                Matrix4x4[] bindposes = new Matrix4x4[boneCount];
                for (int index = 0; index < boneCount; index++)
                {
                    Matrix4x4 matrix = new Matrix4x4();
                    matrix.m00 = reader.ReadSingle(); matrix.m01 = reader.ReadSingle(); matrix.m02 = reader.ReadSingle(); matrix.m03 = reader.ReadSingle();
                    matrix.m10 = reader.ReadSingle(); matrix.m11 = reader.ReadSingle(); matrix.m12 = reader.ReadSingle(); matrix.m13 = reader.ReadSingle();
                    matrix.m20 = reader.ReadSingle(); matrix.m21 = reader.ReadSingle(); matrix.m22 = reader.ReadSingle(); matrix.m23 = reader.ReadSingle();
                    matrix.m30 = reader.ReadSingle(); matrix.m31 = reader.ReadSingle(); matrix.m32 = reader.ReadSingle(); matrix.m33 = reader.ReadSingle();
                    bindposes[index] = matrix;
                }

                int meshCount = reader.ReadInt32();
                if (meshCount <= 0 || meshCount > 16) throw new IOException("Invalid primitive count: " + meshCount);
                MeshDefinition[] meshes = new MeshDefinition[meshCount];
                int[] soleBones = new int[4];
                string[] soleNames = { "ValveBiped.Bip01_L_Foot", "ValveBiped.Bip01_L_Toe0", "ValveBiped.Bip01_R_Foot", "ValveBiped.Bip01_R_Toe0" };
                float[] soleMinimum = { float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue };
                SoleVertex[] soleCandidates = new SoleVertex[4];
                for (int sole = 0; sole < soleBones.Length; sole++)
                    soleBones[sole] = Array.FindIndex(bones, bone => bone.Name == soleNames[sole]);
                for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
                {
                    int materialIndex = reader.ReadInt32();
                    int vertexCount = reader.ReadInt32();
                    if (vertexCount <= 0 || vertexCount > 65000) throw new IOException("Invalid vertex count: " + vertexCount);
                    Vector3[] vertices = new Vector3[vertexCount];
                    Vector3[] normals = new Vector3[vertexCount];
                    Vector2[] uv = new Vector2[vertexCount];
                    BoneWeight[] weights = new BoneWeight[vertexCount];
                    for (int vertex = 0; vertex < vertexCount; vertex++)
                    {
                        vertices[vertex] = ReadVector3(reader);
                        normals[vertex] = ReadVector3(reader);
                        uv[vertex] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                        BoneWeight weight = new BoneWeight();
                        weight.boneIndex0 = reader.ReadByte(); weight.boneIndex1 = reader.ReadByte();
                        weight.boneIndex2 = reader.ReadByte(); weight.boneIndex3 = reader.ReadByte();
                        weight.weight0 = reader.ReadSingle(); weight.weight1 = reader.ReadSingle();
                        weight.weight2 = reader.ReadSingle(); weight.weight3 = reader.ReadSingle();
                        weights[vertex] = weight;
                        // Cache actual skinned sole vertices once per asset.
                        // A foot bone pivot is an ankle, not the bottom of a boot.
                        int dominant = weight.boneIndex0;
                        float strongest = weight.weight0;
                        if (weight.weight1 > strongest) { dominant = weight.boneIndex1; strongest = weight.weight1; }
                        if (weight.weight2 > strongest) { dominant = weight.boneIndex2; strongest = weight.weight2; }
                        if (weight.weight3 > strongest) { dominant = weight.boneIndex3; strongest = weight.weight3; }
                        for (int sole = 0; sole < soleBones.Length; sole++)
                            if (soleBones[sole] >= 0 && dominant == soleBones[sole] && vertices[vertex].y < soleMinimum[sole])
                            {
                                soleMinimum[sole] = vertices[vertex].y;
                                soleCandidates[sole] = new SoleVertex(vertices[vertex], weight);
                            }
                    }
                    int indexCount = reader.ReadInt32();
                    if (indexCount <= 0 || indexCount % 3 != 0) throw new IOException("Invalid triangle index count: " + indexCount);
                    int[] triangles = new int[indexCount];
                    for (int index = 0; index < indexCount; index++) triangles[index] = reader.ReadUInt16();

                    Mesh mesh = new Mesh();
                    mesh.name = "Custom Sosig Model Primitive " + meshIndex;
                    mesh.vertices = vertices;
                    mesh.normals = normals;
                    mesh.uv = uv;
                    mesh.boneWeights = weights;
                    mesh.bindposes = bindposes;
                    mesh.triangles = triangles;
                    mesh.RecalculateTangents();
                    mesh.RecalculateBounds();
                    Bounds bounds = mesh.bounds;
                    bounds.Expand(0.5f);
                    mesh.bounds = bounds;
                    meshes[meshIndex] = new MeshDefinition(mesh, materialIndex);
                }
                var asset = new RuntimeSkinnedAsset(minY, maxY, bones, bindposes, meshes);
                var soles = new List<SoleVertex>();
                for (int sole = 0; sole < soleCandidates.Length; sole++)
                    if (soleMinimum[sole] < float.MaxValue) soles.Add(soleCandidates[sole]);
                if (soles.Count < 2) throw new IOException("Addon mesh has insufficient foot vertices for grounding.");
                asset.SoleVertices = soles.ToArray();
                asset.ArmorEnvelopes = ModelArmorEnvelope.Build(asset);
                return asset;
            }
        }

        private static Vector3 ReadVector3(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        internal sealed class BoneDefinition
        {
            public readonly string Name;
            public readonly int ParentIndex;
            public readonly Vector3 LocalPosition;
            public readonly Quaternion LocalRotation;
            public readonly Vector3 LocalScale;

            public BoneDefinition(string name, int parentIndex, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
            {
                Name = name; ParentIndex = parentIndex; LocalPosition = localPosition; LocalRotation = localRotation; LocalScale = localScale;
            }
        }

        internal struct SoleVertex
        {
            public readonly Vector3 Position;
            public readonly BoneWeight Weight;
            public SoleVertex(Vector3 position, BoneWeight weight) { Position = position; Weight = weight; }
        }

        internal sealed class MeshDefinition
        {
            public readonly Mesh Mesh;
            public readonly int MaterialIndex;
            public MeshDefinition(Mesh mesh, int materialIndex) { Mesh = mesh; MaterialIndex = materialIndex; }
        }
    }
}
