using UnityEngine;

namespace CustomSosigReplacer
{
    // A renderer-only copy of an effective custom armor hitbox. It never owns
    // a Collider or IFVRDamageable, so the visualization cannot alter damage.
    internal sealed class ArmorHitboxOverlay
    {
        internal readonly GameObject Root;
        private readonly Mesh _mesh;

        private ArmorHitboxOverlay(GameObject root, Mesh mesh)
        {
            Root = root;
            _mesh = mesh;
        }

        internal static ArmorHitboxOverlay Create(Collider hitbox, Material material, int visualLayer)
        {
            if (hitbox == null || material == null) return null;
            Mesh mesh;
            Vector3 center;
            Vector3 scale;
            BoxCollider box = hitbox as BoxCollider;
            CapsuleCollider capsule = hitbox as CapsuleCollider;
            if (box != null)
            {
                mesh = CreateBoxMesh();
                center = box.center;
                scale = box.size;
            }
            else if (capsule != null && capsule.direction == 1)
            {
                mesh = CreateCapsuleMesh(capsule.radius, capsule.height);
                center = capsule.center;
                scale = Vector3.one;
            }
            else return null;

            GameObject root = new GameObject("Armor Hitbox Overlay");
            root.layer = visualLayer;
            root.transform.SetParent(hitbox.transform, false);
            root.transform.localPosition = center;
            root.transform.localScale = scale;
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            root.SetActive(false);
            return new ArmorHitboxOverlay(root, mesh);
        }

        internal void Dispose()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
        }

        private static Mesh CreateBoxMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = "Custom Sosig armor box overlay";
            mesh.vertices = new[] {
                new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f),
                new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f),
                new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f)
            };
            mesh.triangles = new[] {
                4,5,6, 4,6,7, 1,0,3, 1,3,2,
                0,4,7, 0,7,3, 5,1,2, 5,2,6,
                3,7,6, 3,6,2, 0,1,5, 0,5,4
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateCapsuleMesh(float radius, float height)
        {
            const int segments = 12;
            const int hemisphereRings = 5;
            int rings = (hemisphereRings + 1) * 2;
            float halfCylinder = Mathf.Max(0f, height * 0.5f - radius);
            Vector3[] vertices = new Vector3[rings * segments];
            int[] triangles = new int[(rings - 1) * segments * 6];
            for (int ring = 0; ring < rings; ring++)
            {
                bool upper = ring > hemisphereRings;
                int step = upper ? ring - hemisphereRings - 1 : ring;
                float angle = upper
                    ? step * Mathf.PI * 0.5f / hemisphereRings
                    : -Mathf.PI * 0.5f + step * Mathf.PI * 0.5f / hemisphereRings;
                float y = (upper ? halfCylinder : -halfCylinder) + Mathf.Sin(angle) * radius;
                float ringRadius = Mathf.Cos(angle) * radius;
                for (int segment = 0; segment < segments; segment++)
                {
                    float around = segment * Mathf.PI * 2f / segments;
                    vertices[ring * segments + segment] = new Vector3(
                        Mathf.Cos(around) * ringRadius, y, Mathf.Sin(around) * ringRadius);
                }
            }
            int offset = 0;
            for (int ring = 0; ring < rings - 1; ring++)
            for (int segment = 0; segment < segments; segment++)
            {
                int next = (segment + 1) % segments;
                int a = ring * segments + segment;
                int b = ring * segments + next;
                int c = (ring + 1) * segments + segment;
                int d = (ring + 1) * segments + next;
                triangles[offset++] = a; triangles[offset++] = c; triangles[offset++] = b;
                triangles[offset++] = b; triangles[offset++] = c; triangles[offset++] = d;
            }
            Mesh mesh = new Mesh();
            mesh.name = "Custom Sosig armor capsule overlay";
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
