using System.Collections.Generic;
using System.Reflection;
using FistVR;
using HarmonyLib;
using UnityEngine;

namespace CustomSosigReplacer
{
    internal sealed class SosigVisualProxy : MonoBehaviour
    {
        private const int NativeHiddenHitboxLayer = 10;
        private const int CustomHitboxLayer = 11;
        private const int FloorProbeMask = ~((1 << NativeHiddenHitboxLayer) | (1 << CustomHitboxLayer));
        private readonly HashSet<Renderer> _proxyRenderers = new HashSet<Renderer>();
        private readonly Dictionary<Renderer, bool> _hiddenRenderers = new Dictionary<Renderer, bool>();
        private readonly Dictionary<GameObject, int> _originalHitboxLayers = new Dictionary<GameObject, int>();
        private readonly Dictionary<string, Transform> _bonesByName = new Dictionary<string, Transform>();
        private readonly Dictionary<string, int> _boneIndicesByName = new Dictionary<string, int>();
        private readonly Dictionary<SosigHand, HandDriveBinding> _handDriveBindings = new Dictionary<SosigHand, HandDriveBinding>();
        private readonly List<RagdollBinding> _ragdollParts = new List<RagdollBinding>();
        private readonly SosigAnimationDirector _animationDirector = new SosigAnimationDirector();
        private Sosig _sosig;
        private RuntimeSkinnedAsset _asset;
        private Plugin.AnimationSet _animations;
        private Plugin.Settings _settings;
        private Material _eyeSurfaceMaterial;
        private float _modelHeight;
        private float _armorHitboxScale;
        private Transform _visualRoot;
        private Transform _motionRoot;
        private Transform _assetRoot;
        private Transform[] _bones;
        private Quaternion[] _bindRotations;
        private bool[] _lowerBodyMask;
        private bool[] _bodyWithoutArmsMask;
        private bool[] _upperBodyMask;
        private bool[] _weaponBodyMask;
        private Vector3 _lastLowerPosition;
        private Vector3 _smoothedForward = Vector3.forward;
        private float _rescanTimer;
        private float _armorOverlayScanTimer;
        private float _motionPhase;
        private float _stateTime;
        private float _ballisticTime;
        private float _controlledTime;
        private float _ballisticWeight;
        private float _poseTransitionTime;
        private float _smoothedSpeed;
        private readonly LocomotionClock _locomotionClock = new LocomotionClock();
        private float _locomotionWeight;
        private float _locomotionPhase;
        private Quaternion[] _previousPresentationPose;
        private Quaternion[] _transitionFromPose;
        private float _presentationTransitionTime = 1f;
        private RuntimeBoneClip _presentationClip;
        private float _floorProbeTimer;
        private float _floorHeight;
        private Vector3 _floorProbePosition;
        private Vector3 _horizontalVelocity;
        private bool _hasFloor;
        private bool _initialized;
        private bool _meatImpacts;
        private string _addonId;
        private GameObject _originalSmallMustardBurst;
        private GameObject _originalLargeMustardBurst;
        private GameObject _originalExplosion;
        private bool _originalUsesGibs;
        private bool _mustardBurstReplaced;
        private bool _deathFxReplaced;
        private readonly List<ParticleSystemRenderer> _suppressedBleedRenderers = new List<ParticleSystemRenderer>();
        private bool _ragdollActive;
        private bool _ballisticEpisodeActive;
        private int _fallEpisode;
        private RuntimeBoneClip _episodeFallClip;
        private float[] _soleFloorHeights;
        private bool[] _soleHasFloor;
        private float _soleProbeTimer;
        private readonly RaycastHit[] _groundHits = new RaycastHit[32];
        private int _poseTransitionDirection;
        private int _customHitboxCount;
        private Sosig.SosigBodyState _previousBodyState;
        private Sosig.SosigBodyPose _previousBodyPose;
        private static bool _diagnosticsLogged;
        private static bool _hitboxDiagnosticsLogged;
        private static bool _missingPhysicalMaterialLogged;
        private static bool _missingImpactDefinitionLogged;
        private static bool _handAuthorityDiagnosticsLogged;
        private static readonly FieldInfo ActiveAimField = AccessTools.Field(typeof(SosigHand), "HasActiveAimPoint");
        private static readonly FieldInfo AimPointField = AccessTools.Field(typeof(SosigHand), "m_aimTowardPoint");

        public void Initialize(Sosig sosig, RuntimeSkinnedAsset asset, Plugin.AnimationSet animations, Material[] materials, Material eyeSurfaceMaterial, string modelName, float modelHeight, float armorHitboxScale, Plugin.Settings settings, bool meatImpacts, string addonId)
        {
            _sosig = sosig;
            _asset = asset;
            _animations = animations;
            _settings = settings;
            _meatImpacts = meatImpacts;
            _addonId = addonId;
            _eyeSurfaceMaterial = eyeSurfaceMaterial;
            _modelHeight = modelHeight;
            _armorHitboxScale = armorHitboxScale;
            _visualRoot = CreateChild(null, "Custom Sosig Visual - " + sosig.GetInstanceID());
            _motionRoot = CreateChild(_visualRoot, "Procedural Motion");
            _assetRoot = CreateChild(_motionRoot, modelName);
            _assetRoot.localPosition = Vector3.down * _asset.MinY;

            int layer = FindVisualLayer();
            _visualRoot.gameObject.layer = layer;
            _motionRoot.gameObject.layer = layer;
            _assetRoot.gameObject.layer = layer;
            BuildSkeleton(layer);
            _soleFloorHeights = new float[_asset.SoleVertices.Length];
            _soleHasFloor = new bool[_asset.SoleVertices.Length];
            BuildRenderers(materials, layer);
            _lastLowerPosition = transform.position;
            UpdateRootPose(true);
            ResetDrivenBones();
            ApplyStableIdlePose();
            if (_settings.UseCustomHitboxes)
            {
                BuildDamageHitboxes();
                MoveOriginalHitboxesOutOfProjectileMask();
            }
            SyncRagdollBodies();
            if (_settings.HideOriginal) HideOriginalVisuals();
            _initialized = true;
            _originalExplosion = _sosig.DamageFX_Explosion;
            _originalUsesGibs = _sosig.UsesGibs;
            _sosig.DamageFX_Explosion = Plugin.SilentMustardBurst;
            _sosig.UsesGibs = false;
            _deathFxReplaced = true;
            if (_meatImpacts)
            {
                _originalSmallMustardBurst = _sosig.DamageFX_SmallMustardBurst;
                _originalLargeMustardBurst = _sosig.DamageFX_LargeMustardBurst;
                _sosig.DamageFX_SmallMustardBurst = Plugin.SilentMustardBurst;
                _sosig.DamageFX_LargeMustardBurst = Plugin.SilentMustardBurst;
                _mustardBurstReplaced = true;
            }
            RefreshArmorOverlays();
            _previousBodyState = _sosig.BodyState;
            _previousBodyPose = _sosig.BodyPose;
            if (!_diagnosticsLogged)
            {
                _diagnosticsLogged = true;
                Plugin.Log.LogInfo("Custom proxy attached with full-body authored animation, independent humanoid facing and COD-style animated hitboxes. links=" + (_sosig.Links == null ? 0 : _sosig.Links.Count) + ", hiddenRenderers=" + _hiddenRenderers.Count + ", customHitboxes=" + _customHitboxCount + ".");
            }
        }

        private void BuildDamageHitboxes()
        {
            SosigLink head = FindLink(SosigLink.SosigBodyPart.Head, 0);
            SosigLink torso = FindLink(SosigLink.SosigBodyPart.Torso, 1);
            SosigLink upper = FindLink(SosigLink.SosigBodyPart.UpperLink, 2);
            SosigLink lower = FindLink(SosigLink.SosigBodyPart.LowerLink, 3);
            const int hitboxLayer = CustomHitboxLayer; // COD Zombies body-part layer; included in H3VR's projectile mask.

            RagdollBinding hips = AddBoxPart("Hips", "ValveBiped.Bip01_Pelvis", "ValveBiped.Bip01_Spine2", 0.14f, upper, 1, 10f, null, hitboxLayer);
            RagdollBinding chest = AddBoxPart("Chest", "ValveBiped.Bip01_Spine2", "ValveBiped.Bip01_Neck1", 0.15f, torso, 2, 8f, hips, hitboxLayer);
            AddHeadPart("Head", "ValveBiped.Bip01_Head1", 0.105f, 0.21f, head, 3, 3f, chest, hitboxLayer);

            RagdollBinding leftUpperArm = AddCapsulePart("LeftUpperArm", "ValveBiped.Bip01_L_UpperArm", "ValveBiped.Bip01_L_Forearm", 0.055f, torso, 1, 2f, chest, hitboxLayer);
            AddCapsulePart("LeftLowerArm", "ValveBiped.Bip01_L_Forearm", "ValveBiped.Bip01_L_Hand", 0.047f, torso, 1, 1.5f, leftUpperArm, hitboxLayer);
            RagdollBinding rightUpperArm = AddCapsulePart("RightUpperArm", "ValveBiped.Bip01_R_UpperArm", "ValveBiped.Bip01_R_Forearm", 0.055f, torso, 1, 2f, chest, hitboxLayer);
            AddCapsulePart("RightLowerArm", "ValveBiped.Bip01_R_Forearm", "ValveBiped.Bip01_R_Hand", 0.047f, torso, 1, 1.5f, rightUpperArm, hitboxLayer);

            RagdollBinding leftUpperLeg = AddCapsulePart("LeftUpperLeg", "ValveBiped.Bip01_L_Thigh", "ValveBiped.Bip01_L_Calf", 0.08f, lower, 1, 4f, hips, hitboxLayer);
            AddCapsulePart("LeftLowerLeg", "ValveBiped.Bip01_L_Calf", "ValveBiped.Bip01_L_Foot", 0.065f, lower, 1, 3f, leftUpperLeg, hitboxLayer);
            RagdollBinding rightUpperLeg = AddCapsulePart("RightUpperLeg", "ValveBiped.Bip01_R_Thigh", "ValveBiped.Bip01_R_Calf", 0.08f, lower, 1, 4f, hips, hitboxLayer);
            AddCapsulePart("RightLowerLeg", "ValveBiped.Bip01_R_Calf", "ValveBiped.Bip01_R_Foot", 0.065f, lower, 1, 3f, rightUpperLeg, hitboxLayer);

            BuildArmorInterceptors();

            if (!_hitboxDiagnosticsLogged)
            {
                _hitboxDiagnosticsLogged = true;
                int projectileMask = AM.PLM;
                Plugin.Log.LogInfo("COD-style hitboxes: count=" + _customHitboxCount + ", layer=" + hitboxLayer + ", layerInProjectileMask=" + ((projectileMask & (1 << hitboxLayer)) != 0) + ", projectileMask=0x" + projectileMask.ToString("X8") + ".");
            }
        }

        private SosigLink FindLink(SosigLink.SosigBodyPart bodyPart, int fallbackIndex)
        {
            if (_sosig.Links != null)
            {
                for (int index = 0; index < _sosig.Links.Count; index++)
                {
                    SosigLink candidate = _sosig.Links[index];
                    if (candidate != null && candidate.BodyPart == bodyPart) return candidate;
                }
                if (fallbackIndex >= 0 && fallbackIndex < _sosig.Links.Count) return _sosig.Links[fallbackIndex];
            }
            return null;
        }

        private RagdollBinding AddBoxPart(string label, string startBoneName, string endBoneName, float radius, SosigLink target, int multiplier, float mass, RagdollBinding parent, int layer)
        {
            RagdollBinding binding = CreatePart(label, startBoneName, endBoneName, target, multiplier, mass, parent, layer);
            if (binding == null) return null;
            float length = Vector3.Distance(binding.Bone.position, binding.EndBone.position);
            BoxCollider collider = binding.GameObject.AddComponent<BoxCollider>();
            collider.center = Vector3.up * length * 0.5f;
            collider.size = new Vector3(radius * 2f, Mathf.Max(0.01f, length), radius * 2f);
            FinishPart(binding, collider, target, multiplier);
            return binding;
        }

        private RagdollBinding AddCapsulePart(string label, string startBoneName, string endBoneName, float radius, SosigLink target, int multiplier, float mass, RagdollBinding parent, int layer)
        {
            RagdollBinding binding = CreatePart(label, startBoneName, endBoneName, target, multiplier, mass, parent, layer);
            if (binding == null) return null;
            float length = Vector3.Distance(binding.Bone.position, binding.EndBone.position);
            CapsuleCollider collider = binding.GameObject.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.radius = Mathf.Min(radius, length * 0.45f);
            collider.height = Mathf.Max(collider.radius * 2f, length);
            collider.center = Vector3.up * length * 0.5f;
            FinishPart(binding, collider, target, multiplier);
            return binding;
        }

        private RagdollBinding AddHeadPart(string label, string boneName, float radius, float height, SosigLink target, int multiplier, float mass, RagdollBinding parent, int layer)
        {
            Transform bone;
            if (target == null || !_bonesByName.TryGetValue(boneName, out bone)) return null;
            RagdollBinding binding = CreatePartObject(label, bone, null, target, mass, parent, layer, Quaternion.identity);
            CapsuleCollider collider = binding.GameObject.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.radius = radius;
            collider.height = Mathf.Max(radius * 2f, height);
            FinishPart(binding, collider, target, multiplier);
            return binding;
        }

        private RagdollBinding CreatePart(string label, string startBoneName, string endBoneName, SosigLink target, int multiplier, float mass, RagdollBinding parent, int layer)
        {
            Transform start;
            Transform end;
            if (target == null || !_bonesByName.TryGetValue(startBoneName, out start) || !_bonesByName.TryGetValue(endBoneName, out end)) return null;
            Vector3 segment = end.position - start.position;
            if (segment.sqrMagnitude < 0.000001f) return null;
            Vector3 localDirection = start.InverseTransformDirection(segment.normalized);
            Quaternion bodyRotationFromBone = Quaternion.FromToRotation(Vector3.up, localDirection);
            return CreatePartObject(label, start, end, target, mass, parent, layer, bodyRotationFromBone);
        }

        private RagdollBinding CreatePartObject(string label, Transform bone, Transform endBone, SosigLink target, float mass, RagdollBinding parent, int layer, Quaternion bodyRotationFromBone)
        {
            GameObject part = new GameObject("Custom Ragdoll Part - " + label);
            part.layer = layer;
            part.transform.SetParent(null, true);
            Rigidbody body = part.AddComponent<Rigidbody>();
            body.mass = mass;
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;

            RagdollBinding binding = new RagdollBinding();
            binding.GameObject = part;
            binding.Bone = bone;
            binding.EndBone = endBone;
            binding.Body = body;
            binding.Parent = parent;
            binding.BodyRotationFromBone = bodyRotationFromBone;
            SyncRagdollBody(binding);

            binding.Label = label;
            // Angular reference frames are established only on death.

            _ragdollParts.Add(binding);
            return binding;
        }

        private void FinishPart(RagdollBinding binding, Collider collider, SosigLink target, int multiplier)
        {
            binding.Collider = collider;
            IgnoreOriginalColliders(collider);
            CopyPhysicalMaterial(target, collider, binding.GameObject, _meatImpacts);
            // Arm hitboxes share the torso SosigLink for damage, but a torso
            // vest must not absorb shots to exposed arms.
            bool armorEligible = !binding.Label.Contains("Arm");
            binding.Hitbox = binding.GameObject.AddComponent<CustomSosigHitbox>();
            binding.Hitbox.Initialize(target, binding.Body, multiplier, armorEligible, _meatImpacts, _addonId);
            _customHitboxCount++;
        }

        private void BuildArmorInterceptors()
        {
            if (_asset.ArmorEnvelopes == null) return;
            for (int index = 0; index < _ragdollParts.Count; index++)
            {
                RagdollBinding binding = _ragdollParts[index];
                if (binding == null || binding.Collider == null || binding.Hitbox == null) continue;
                ModelArmorEnvelope.Shape shape;
                if (!_asset.ArmorEnvelopes.TryGetValue(binding.Label, out shape)) continue;
                // The addon-specific fit trims extreme equipment vertices from
                // the bind-pose envelope without affecting ragdoll physics.
                // The original collider remains unchanged for ragdoll physics.
                BoxCollider armor = binding.GameObject.AddComponent<BoxCollider>();
                armor.center = Vector3.up * shape.CenterY;
                armor.size = new Vector3(shape.Radius * 2f, shape.Height, shape.Radius * 2f) * _armorHitboxScale;
                armor.sharedMaterial = binding.Collider.sharedMaterial;
                armor.enabled = false;
                IgnoreOriginalColliders(armor);
                binding.ArmorCollider = armor;
            }
        }

        private static SoftJointLimit CreateLimit(float limit)
        {
            SoftJointLimit result = new SoftJointLimit();
            result.limit = limit;
            return result;
        }

        private static void CopyPhysicalMaterial(SosigLink target, Collider destinationCollider, GameObject destination, bool meatImpacts)
        {
            if (target == null || target.C == null) return;
            if (destinationCollider != null) destinationCollider.sharedMaterial = target.C.sharedMaterial;

            PMat source = target.C.GetComponent<PMat>();
            if (source == null) source = target.GetComponent<PMat>();
            if (source == null) source = target.C.GetComponentInParent<PMat>();
            if (source == null) source = target.GetComponentInChildren<PMat>(true);
            if (source == null)
            {
                if (!_missingPhysicalMaterialLogged)
                {
                    _missingPhysicalMaterialLogged = true;
                    Plugin.Log.LogWarning("Could not find the native Sosig PMat for a custom hitbox; using the game default material.");
                }
                if (meatImpacts)
                {
                    PMat fallback = destination.AddComponent<PMat>();
                    fallback.MatDef = Plugin.GetSilentImpactMaterial(null);
                }
                return;
            }
            PMat copy = destination.AddComponent<PMat>();
            copy.Def = source.Def;
            if (meatImpacts && source.MatDef == null && !_missingImpactDefinitionLogged)
            {
                _missingImpactDefinitionLogged = true;
                Plugin.Log.LogInfo("Native Sosig PMat has no MatDef; using the game's default ballistic definition with custom impacts disabled.");
            }
            copy.MatDef = meatImpacts ? Plugin.GetSilentImpactMaterial(source.MatDef) : source.MatDef;
            copy.Condition = source.Condition;
        }

        private void IgnoreOriginalColliders(Collider customCollider)
        {
            if (_sosig.Links == null) return;
            for (int index = 0; index < _sosig.Links.Count; index++)
            {
                SosigLink link = _sosig.Links[index];
                if (link == null) continue;
                foreach (Collider nativeCollider in link.GetComponentsInChildren<Collider>(true))
                    if (nativeCollider != null && nativeCollider.GetComponentInParent<SosigWeapon>() == null)
                        Physics.IgnoreCollision(customCollider, nativeCollider, true);
            }
        }

        private void MoveOriginalHitboxesOutOfProjectileMask()
        {
            const int hiddenLayer = NativeHiddenHitboxLayer;
            if ((AM.PLM & (1 << hiddenLayer)) != 0)
            {
                Plugin.Log.LogWarning("Cannot isolate native Sosig hitboxes because layer " + hiddenLayer + " is included in the current projectile mask.");
                return;
            }

            if (_sosig.Links == null) return;
            for (int index = 0; index < _sosig.Links.Count; index++)
            {
                SosigLink link = _sosig.Links[index];
                if (link == null || link.C == null) continue;
                GameObject colliderObject = link.C.gameObject;
                if (!_originalHitboxLayers.ContainsKey(colliderObject)) _originalHitboxLayers.Add(colliderObject, colliderObject.layer);
                colliderObject.layer = hiddenLayer;
            }
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private void BuildSkeleton(int layer)
        {
            _bones = new Transform[_asset.Bones.Length];
            _bindRotations = new Quaternion[_asset.Bones.Length];
            _previousPresentationPose = new Quaternion[_asset.Bones.Length];
            _transitionFromPose = new Quaternion[_asset.Bones.Length];
            _lowerBodyMask = new bool[_asset.Bones.Length];
            _bodyWithoutArmsMask = new bool[_asset.Bones.Length];
            _upperBodyMask = new bool[_asset.Bones.Length];
            _weaponBodyMask = new bool[_asset.Bones.Length];
            for (int index = 0; index < _asset.Bones.Length; index++)
            {
                RuntimeSkinnedAsset.BoneDefinition definition = _asset.Bones[index];
                Transform parent = definition.ParentIndex >= 0 ? _bones[definition.ParentIndex] : _assetRoot;
                Transform bone = CreateChild(parent, definition.Name);
                bone.gameObject.layer = layer;
                bone.localPosition = definition.LocalPosition;
                bone.localRotation = definition.LocalRotation;
                bone.localScale = definition.LocalScale;
                _bones[index] = bone;
                _bindRotations[index] = definition.LocalRotation;
                _previousPresentationPose[index] = definition.LocalRotation;
                string lowerName = definition.Name.ToLowerInvariant();
                _lowerBodyMask[index] = lowerName.Contains("pelvis") || lowerName.Contains("thigh") || lowerName.Contains("calf") || lowerName.Contains("foot") || lowerName.Contains("toe");
                _bodyWithoutArmsMask[index] = !lowerName.Contains("clavicle") && !lowerName.Contains("upperarm") && !lowerName.Contains("forearm") && !lowerName.Contains("hand") && !lowerName.Contains("finger");
                bool arm = lowerName.Contains("clavicle") || lowerName.Contains("upperarm") || lowerName.Contains("forearm") || lowerName.Contains("hand") || lowerName.Contains("finger");
                bool spine = lowerName.Contains("spine");
                bool head = lowerName.Contains("neck") || lowerName.Contains("head");
                _upperBodyMask[index] = arm || spine || head;
                _weaponBodyMask[index] = arm || spine;
                _bonesByName[definition.Name] = bone;
                _boneIndicesByName[definition.Name] = index;
            }
        }

        private void BuildRenderers(Material[] materials, int layer)
        {
            for (int index = 0; index < _asset.Meshes.Length; index++)
            {
                RuntimeSkinnedAsset.MeshDefinition definition = _asset.Meshes[index];
                GameObject primitive = new GameObject("Custom Sosig Model Primitive " + index);
                primitive.layer = layer;
                primitive.transform.SetParent(_assetRoot, false);
                SkinnedMeshRenderer renderer = primitive.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = definition.Mesh;
                renderer.bones = _bones;
                renderer.rootBone = _bones[0];
                renderer.sharedMaterial = materials[Mathf.Clamp(definition.MaterialIndex, 0, materials.Length - 1)];
                if (definition.Mesh.subMeshCount == 2 && _eyeSurfaceMaterial != null)
                    renderer.sharedMaterials = new[] { renderer.sharedMaterial, _eyeSurfaceMaterial };
                renderer.updateWhenOffscreen = true;
                renderer.localBounds = definition.Mesh.bounds;
                _proxyRenderers.Add(renderer);
            }
        }

        private int FindVisualLayer()
        {
            if (_sosig.Renderers != null)
            {
                for (int index = 0; index < _sosig.Renderers.Length; index++)
                    if (_sosig.Renderers[index] != null) return _sosig.Renderers[index].gameObject.layer;
            }
            return gameObject.layer;
        }

        private void LateUpdate()
        {
            if (!_initialized || _sosig == null || _visualRoot == null) return;

            _armorOverlayScanTimer -= Time.deltaTime;
            if (_armorOverlayScanTimer <= 0f)
            {
                _armorOverlayScanTimer = 0.5f;
                RefreshArmorOverlays();
            }

            if (_ragdollActive)
            {
                DriveBonesFromRagdoll();
                return;
            }

            if (_sosig.BodyState == Sosig.SosigBodyState.Dead && _ragdollParts.Count > 0)
            {
                ActivateRagdoll();
                DriveBonesFromRagdoll();
                return;
            }

            _animationDirector.Update(_sosig, Time.deltaTime);
            float speed = UpdateRootPose(false);
            AnimateRig(speed);
            SyncRagdollBodies();
            if (_settings.HideOriginal)
            {
                _rescanTimer -= Time.deltaTime;
                if (_rescanTimer <= 0f)
                {
                    _rescanTimer = 1f;
                    HideOriginalVisuals();
                }
            }
        }

        private void SyncRagdollBodies()
        {
            for (int index = 0; index < _ragdollParts.Count; index++) SyncRagdollBody(_ragdollParts[index]);
        }

        internal void RefreshArmorOverlays()
        {
            if (!_initialized || _sosig == null) return;
            bool show = Plugin.ShowArmorHitboxes && !_ragdollActive &&
                _sosig.BodyState != Sosig.SosigBodyState.Dead;
            for (int index = 0; index < _ragdollParts.Count; index++)
            {
                RagdollBinding binding = _ragdollParts[index];
                if (binding == null || binding.Collider == null || binding.Hitbox == null) continue;
                bool hasArmor = !_ragdollActive && _sosig.BodyState != Sosig.SosigBodyState.Dead &&
                    binding.Hitbox.HasArmorProtection;
                if (binding.ArmorCollider != null) binding.ArmorCollider.enabled = hasArmor;
                bool protectedZone = show && hasArmor;
                if (protectedZone && binding.ArmorOverlay == null)
                    binding.ArmorOverlay = ArmorHitboxOverlay.Create(
                        binding.ArmorCollider != null ? binding.ArmorCollider : binding.Collider,
                        Plugin.GetArmorOverlayMaterial(), _visualRoot.gameObject.layer);
                if (binding.ArmorOverlay != null && binding.ArmorOverlay.Root != null)
                    binding.ArmorOverlay.Root.SetActive(protectedZone);
            }
        }

        private static void SyncRagdollBody(RagdollBinding binding)
        {
            if (binding == null || binding.Body == null || binding.Bone == null) return;
            binding.Body.position = binding.Bone.position;
            binding.Body.rotation = binding.Bone.rotation * binding.BodyRotationFromBone;
        }

        private void ActivateRagdoll()
        {
            if (_ragdollActive) return;
            SyncRagdollBodies();

            for (int index = 0; index < _ragdollParts.Count; index++)
            {
                RagdollBinding binding = _ragdollParts[index];
                if (binding.ArmorCollider != null) binding.ArmorCollider.enabled = false;
                binding.GameObject.layer = 0; // Default permits self-contact without altering global layer rules.
                binding.Body.velocity = Vector3.zero;
                binding.Body.angularVelocity = Vector3.zero;
                binding.Body.maxDepenetrationVelocity = 1.5f;
                binding.Body.solverIterations = 12;
                binding.Body.solverVelocityIterations = 6;
                binding.Body.drag = 0.15f;
                binding.Body.angularDrag = 0.8f;
                if (binding.Parent != null)
                {
                    CharacterJoint joint = binding.GameObject.AddComponent<CharacterJoint>();
                    joint.connectedBody = binding.Parent.Body;
                    joint.autoConfigureConnectedAnchor = false;
                    joint.anchor = Vector3.zero;
                    joint.connectedAnchor = binding.Parent.Body.transform.InverseTransformPoint(binding.Body.position);
                    joint.enableCollision = false;
                    joint.enableProjection = false;
                    bool hinge = binding.Label.IndexOf("Lower", System.StringComparison.Ordinal) >= 0;
                    joint.axis = binding.Body.transform.InverseTransformDirection(hinge ? _visualRoot.right : binding.Body.transform.up).normalized;
                    joint.swingAxis = Vector3.Cross(joint.axis, Vector3.forward).normalized;
                    if (joint.swingAxis.sqrMagnitude < 0.1f) joint.swingAxis = Vector3.up;
                    joint.lowTwistLimit = CreateLimit(hinge ? -15f : -30f);
                    joint.highTwistLimit = CreateLimit(hinge ? 100f : 30f);
                    joint.swing1Limit = CreateLimit(hinge ? 12f : 45f);
                    joint.swing2Limit = CreateLimit(hinge ? 12f : 35f);
                    binding.Joint = joint;
                }
                for (int otherIndex = 0; otherIndex < index; otherIndex++)
                {
                    var other = _ragdollParts[otherIndex];
                    bool adjacent = binding.Parent == other || other.Parent == binding;
                    Physics.IgnoreCollision(binding.Collider, other.Collider, adjacent);
                }
                if (binding.Joint != null && binding.Parent != null)
                {
                    binding.Joint.connectedAnchor = binding.Parent.Body.transform.InverseTransformPoint(binding.Body.position);
                }
            }

            for (int index = 0; index < _ragdollParts.Count; index++)
            {
                RagdollBinding binding = _ragdollParts[index];
                binding.GameObject.transform.SetParent(null, true);
            }

            for (int index = 0; index < _ragdollParts.Count; index++)
            {
                Rigidbody body = _ragdollParts[index].Body;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.useGravity = true;
                body.isKinematic = false;
                body.WakeUp();
            }

            _ragdollActive = true;
            RefreshArmorOverlays();
            Plugin.Log.LogInfo("Activated COD Zombies-style 11-part ragdoll for custom Sosig '" + _sosig.name + "'.");
        }

        private void DriveBonesFromRagdoll()
        {
            for (int index = 0; index < _ragdollParts.Count; index++)
            {
                RagdollBinding binding = _ragdollParts[index];
                if (binding.Body == null || binding.Bone == null) continue;
                binding.Bone.position = binding.Body.position;
                binding.Bone.rotation = binding.Body.rotation * Quaternion.Inverse(binding.BodyRotationFromBone);
            }
        }

        private float UpdateRootPose(bool immediate)
        {
            if (_sosig.Links == null || _sosig.Links.Count < 3 || _sosig.Links[0] == null || _sosig.Links[2] == null) return 0f;
            Vector3 lower = _sosig.Links[2].transform.position;
            Vector3 up = Vector3.up;
            bool inControl = _sosig.BodyState == Sosig.SosigBodyState.InControl;
            if (inControl || immediate)
            {
                Vector3 forward = GetDesiredHumanoidForward(up);
                if (forward.sqrMagnitude > 0.001f)
                {
                    forward.Normalize();
                    if (immediate) _smoothedForward = forward;
                    else
                    {
                        float maximumTurn = Mathf.Lerp(240f, 540f, Mathf.Clamp01(_smoothedSpeed / 2f)) * Time.deltaTime;
                        _smoothedForward = Vector3.RotateTowards(_smoothedForward, forward, maximumTurn * Mathf.Deg2Rad, 0f).normalized;
                    }
                }
            }

            Vector3 targetPosition = lower - up * _settings.FeetBelowLowerLink;
            UpdateFloorHeight(lower, immediate);
            // Standing is floor-locked to reject the links' normal walking bob.
            // Crouch/prone deliberately use the UpperLink height so the complete
            // visual follows the Sosig down instead of hovering above its hitboxes.
            bool standing = _sosig.BodyPose == Sosig.SosigBodyPose.Standing;
            if (_hasFloor && inControl && standing)
            {
                float groundedY = _floorHeight + 0.01f;
                targetPosition.y = groundedY;
            }
            Quaternion targetRotation = Quaternion.LookRotation(_smoothedForward, up) * Quaternion.Euler(0f, 180f + _settings.YawDegrees, 0f);
            float uniformScale = Mathf.Max(0.1f, _modelHeight) / Mathf.Max(0.1f, _asset.Height);
            if (immediate)
            {
                _visualRoot.position = targetPosition;
                _visualRoot.rotation = targetRotation;
            }
            else
            {
                float sharpness = inControl ? _settings.FollowSharpness : 18f;
                float t = 1f - Mathf.Exp(-Mathf.Max(0.1f, sharpness) * Time.deltaTime);
                _visualRoot.position = Vector3.Lerp(_visualRoot.position, targetPosition, t);
                _visualRoot.rotation = Quaternion.Slerp(_visualRoot.rotation, targetRotation, t);
            }
            _visualRoot.localScale = Vector3.one * uniformScale;
            Vector3 navigationPosition = _sosig.transform.position;
            Vector3 movement = navigationPosition - _lastLowerPosition;
            movement.y = 0f;
            Vector3 measuredVelocity = immediate ? Vector3.zero : movement / Mathf.Max(Time.deltaTime, 0.0001f);
            if (inControl && _sosig.Agent != null && _sosig.Agent.enabled && _sosig.Agent.isOnNavMesh)
                measuredVelocity = Vector3.ProjectOnPlane(_sosig.Agent.velocity, up);
            if (!inControl || measuredVelocity.sqrMagnitude > 64f) measuredVelocity = Vector3.zero;
            float measuredSpeed = measuredVelocity.magnitude;
            float speedBlend = immediate ? 1f : 1f - Mathf.Exp(-8f * Time.deltaTime);
            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, measuredSpeed, speedBlend);
            _horizontalVelocity = Vector3.Lerp(_horizontalVelocity, measuredVelocity, speedBlend);
            _lastLowerPosition = navigationPosition;
            return _smoothedSpeed;
        }

        private Vector3 GetDesiredHumanoidForward(Vector3 up)
        {
            // Never orient the skeleton from a gun that this skeleton itself
            // drives: that creates a body -> gun -> body feedback loop.
            Vector3 aimPoint;
            if (TryGetAimPoint(out aimPoint))
            {
                Vector3 aiming = Vector3.ProjectOnPlane(aimPoint - _visualRoot.position, up);
                if (aiming.sqrMagnitude > 0.001f) return aiming;
            }
            if (_horizontalVelocity.sqrMagnitude > 0.04f) return Vector3.ProjectOnPlane(_horizontalVelocity, up);
            Vector3 forward = Vector3.ProjectOnPlane(_sosig.transform.forward, up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(_sosig.Links[2].transform.forward, up);
            return forward;
        }

        private bool TryGetAimPoint(out Vector3 point)
        {
            point = Vector3.zero;
            if (_sosig.Hands == null || ActiveAimField == null || AimPointField == null) return false;
            foreach (SosigHand hand in _sosig.Hands)
            {
                if (hand == null || !hand.IsHoldingObject || hand.HeldObject == null ||
                    hand.HeldObject.Type != SosigWeapon.SosigWeaponType.Gun || !(bool)ActiveAimField.GetValue(hand)) continue;
                point = (Vector3)AimPointField.GetValue(hand);
                return true;
            }
            return false;
        }

        private void UpdateFloorHeight(Vector3 around, bool immediate)
        {
            _floorProbeTimer -= Time.deltaTime;
            Vector3 horizontalDelta = around - _floorProbePosition;
            horizontalDelta.y = 0f;
            if (!immediate && _floorProbeTimer > 0f && horizontalDelta.sqrMagnitude < 0.25f) return;
            _floorProbeTimer = 0.1f;
            _floorProbePosition = around;
            RaycastHit hit;
            _hasFloor = TryFindFloor(around, out hit);
            if (_hasFloor) _floorHeight = hit.point.y;
        }

        private bool TryFindFloor(Vector3 around, out RaycastHit bestHit)
        {
            // Custom ragdoll parts are deliberately unparented so that they can
            // become a physical ragdoll on death. A normal unmasked ray therefore
            // passes through and hits the proxy's own hips/chest/legs, creating a
            // vertical feedback loop. Neither body-hitbox layer is valid ground.
            int hitCount = Physics.RaycastNonAlloc(
                around + Vector3.up * 0.25f,
                Vector3.down,
                _groundHits,
                4f,
                FloorProbeMask,
                QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            bestHit = new RaycastHit();
            bool found = false;
            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = _groundHits[index];
                if (hit.collider == null || Vector3.Dot(hit.normal, Vector3.up) < 0.35f) continue;
                if (hit.point.y > around.y - 0.05f) continue;
                Transform hitTransform = hit.collider.transform;
                if (_visualRoot != null && (hitTransform == _visualRoot || hitTransform.IsChildOf(_visualRoot))) continue;
                if (hitTransform == _sosig.transform || hitTransform.IsChildOf(_sosig.transform)) continue;
                bool belongsToLink = false;
                for (int linkIndex = 0; linkIndex < _sosig.Links.Count; linkIndex++)
                {
                    SosigLink link = _sosig.Links[linkIndex];
                    if (link != null && (hitTransform == link.transform || hitTransform.IsChildOf(link.transform)))
                    {
                        belongsToLink = true;
                        break;
                    }
                }
                if (belongsToLink || hit.distance >= bestDistance) continue;
                bestDistance = hit.distance;
                bestHit = hit;
                found = true;
            }
            return found;
        }

        private void AnimateRig(float speed)
        {
            Sosig.SosigBodyState state = _sosig.BodyState;
            Sosig.SosigBodyPose pose = _sosig.BodyPose;
            Sosig.SosigBodyPose previousPose = _previousBodyPose;
            bool stateChanged = state != _previousBodyState;
            bool poseChanged = pose != _previousBodyPose;
            if (stateChanged || poseChanged)
            {
                _previousBodyState = state;
                _previousBodyPose = pose;
                _stateTime = 0f;
            }
            else _stateTime += Time.deltaTime;

            bool crouched = IsCrouchedPose(pose);
            if (poseChanged && state == Sosig.SosigBodyState.InControl && IsCrouchedPose(previousPose) != crouched)
                BeginPoseTransition(crouched);

            if (state == Sosig.SosigBodyState.Dead)
            {
                ResetDrivenBones();
                _animations.Death.SampleClamped(_stateTime, _bones, 1f);
                _motionRoot.localPosition = Vector3.zero;
                _motionRoot.localRotation = Quaternion.identity;
                return;
            }

            bool ballistic = state == Sosig.SosigBodyState.Ballistic;
            if (ballistic)
            {
                _controlledTime = 0f;
                if (!_ballisticEpisodeActive)
                {
                    _ballisticEpisodeActive = true;
                    _ballisticTime = 0f;
                    _episodeFallClip = ((_fallEpisode++ + _sosig.GetInstanceID()) & 1) == 0
                        ? _animations.Falling : _animations.KnockedDown;
                }
                else _ballisticTime += Time.deltaTime;
            }
            else if (state == Sosig.SosigBodyState.InControl)
            {
                _controlledTime += Time.deltaTime;
                // H3VR may briefly alternate Ballistic and InControl while a
                // Sosig collides or attempts recovery. Do not restart the fall
                // clip until control has remained stable for a real recovery.
                if (_controlledTime >= 0.3f)
                {
                    _ballisticEpisodeActive = false;
                    _ballisticTime = 0f;
                }
            }
            else _controlledTime = 0f;

            float ballisticTarget = ballistic ? 1f : 0f;
            float ballisticBlend = 1f - Mathf.Exp(-10f * Time.deltaTime);
            _ballisticWeight = Mathf.Lerp(_ballisticWeight, ballisticTarget, ballisticBlend);

            bool inControl = state == Sosig.SosigBodyState.InControl;
            if (!inControl) _poseTransitionDirection = 0;
            // Sosig locomotion is substantially slower than the source Mixamo
            // actor. Use speed to enter/leave locomotion, not to suppress most
            // of the authored upper-body animation.
            _locomotionClock.Advance(speed, Time.deltaTime, inControl, crouched);
            _locomotionWeight = _locomotionClock.Weight;
            float gaitAmount = inControl ? _locomotionWeight : 0f;
            ResetDrivenBones();
            if (inControl && gaitAmount > 0.025f)
            {
                // Phase is distance-driven; no minimum playback rate while idle.
                _locomotionPhase = _locomotionClock.Phase;
                _motionPhase = _locomotionPhase * _animations.Walk.Duration;
            }
            float controlledWeight = inControl ? 1f - _ballisticWeight : 0f;
            if (controlledWeight > 0.001f)
                _animations.RelaxedIdle.Sample(_stateTime, _bones, controlledWeight);
            RuntimeBoneClip transitionClip = GetPoseTransitionClip();
            RuntimeBoneClip selectedClip = _animations.RelaxedIdle;
            if (inControl && transitionClip != null)
            {
                selectedClip = transitionClip;
                transitionClip.SampleClamped(_poseTransitionTime, _bones, controlledWeight);
                _poseTransitionTime += Time.deltaTime;
                if (_poseTransitionTime >= transitionClip.Duration)
                {
                    _poseTransitionTime = 0f;
                    _poseTransitionDirection = 0;
                }
            }
            else if (inControl && crouched)
            {
                selectedClip = gaitAmount > 0.025f ? SelectCrouchClip() : _animations.CrouchIdle;
                _animations.CrouchIdle.Sample(_stateTime, _bones, controlledWeight);
                if (gaitAmount > 0.025f) selectedClip.Sample(_locomotionPhase * selectedClip.Duration, _bones, gaitAmount * controlledWeight);
            }
            else if (inControl)
            {
                selectedClip = gaitAmount > 0.025f ? SelectCombatLocomotionClip() : _animations.RelaxedIdle;
                if (gaitAmount > 0.025f) selectedClip.Sample(_locomotionPhase * selectedClip.Duration, _bones, gaitAmount * controlledWeight);
            }

            if (inControl) ApplyAuthoredWeaponLayers(controlledWeight);
            if (inControl) BlendPresentationTransition(selectedClip);

            if (_ballisticWeight > 0.001f)
                  (_episodeFallClip ?? _animations.Falling).SampleClamped(_ballisticTime, _bones, _ballisticWeight);

            if (_settings.EnableProceduralMotion && gaitAmount > 0f && _animationDirector.WeaponWeight < 0.1f)
            {
                float sine = Mathf.Sin(_motionPhase);
                _motionRoot.localPosition = Vector3.up * (Mathf.Abs(sine) * _settings.BobMeters / Mathf.Max(0.1f, _modelHeight));
                _motionRoot.localRotation = Quaternion.Euler(0f, 0f, sine * _settings.SwayDegrees * gaitAmount);
            }
            else
            {
                _motionRoot.localPosition = Vector3.zero;
                _motionRoot.localRotation = Quaternion.identity;
            }
            // While alive the authored full-body pose owns the humanoid skeleton.
            // Sosig links only take over during ballistic motion, where matching
            // the hidden physical body is more important than locomotion style.
            if (_ballisticWeight > 0.001f) ApplySosigLinkPose(_ballisticWeight);
            if (inControl) ApplyAimPresentation(1f - _ballisticWeight);
            if (inControl && (crouched || _poseTransitionDirection != 0)) AlignControlledPelvisHeightToLink();
            if (_ballisticWeight > 0.01f) AlignBallisticPelvisToLink();
            if (inControl && _ballisticWeight < 0.1f && _sosig.BodyPose != Sosig.SosigBodyPose.Prone) GroundAnimatedSoles();
            if (inControl) PrepareWeaponSupportBindings();
            if (inControl) ApplySosigHandPose(1f - _ballisticWeight);
            if (inControl) ApplyWeaponSupportHand(1f - _ballisticWeight);
        }

        private static bool IsCrouchedPose(Sosig.SosigBodyPose pose)
        {
            return pose == Sosig.SosigBodyPose.Crouching || pose == Sosig.SosigBodyPose.Prone;
        }

        private void BeginPoseTransition(bool toCrouch)
        {
            RuntimeBoneClip previousClip = GetPoseTransitionClip();
            float reversedProgress = 0f;
            if (previousClip != null && previousClip.Duration > 0f)
                reversedProgress = 1f - Mathf.Clamp01(_poseTransitionTime / previousClip.Duration);

            _poseTransitionDirection = toCrouch ? 1 : -1;
            RuntimeBoneClip nextClip = GetPoseTransitionClip();
            _poseTransitionTime = nextClip == null ? 0f : nextClip.Duration * reversedProgress;
        }

        private RuntimeBoneClip GetPoseTransitionClip()
        {
            if (_poseTransitionDirection > 0) return _animations.StandToCrouch;
            if (_poseTransitionDirection < 0) return _animations.CrouchToStand;
            return null;
        }

        private void ApplyStableLowerBodyPose(float weight)
        {
            _animations.Walk.SampleMasked(0f, _bones, weight, _lowerBodyMask);
            _animations.Walk.SampleMasked(_animations.Walk.Duration * 0.5f, _bones, weight * 0.5f, _lowerBodyMask);
        }

        private void ApplyStableIdlePose()
        {
            ApplyStableLowerBodyPose(1f);
        }

        private RuntimeBoneClip SelectLocomotionClip()
        {
            float forward = Vector3.Dot(_horizontalVelocity, _smoothedForward);
            Vector3 rightAxis = Vector3.Cross(Vector3.up, _smoothedForward);
            float right = Vector3.Dot(_horizontalVelocity, rightAxis);
            if (Mathf.Abs(right) > Mathf.Abs(forward) * 0.75f)
            {
                bool fast = _horizontalVelocity.magnitude > 1.6f;
                if (right >= 0f) return fast ? _animations.StrafeRightFast : _animations.StrafeRight;
                return fast ? _animations.StrafeLeftFast : _animations.StrafeLeft;
            }
            return forward < -0.05f ? _animations.Backwards : _animations.Walk;
        }

        private RuntimeBoneClip SelectCombatLocomotionClip()
        {
            float forward = Vector3.Dot(_horizontalVelocity, _smoothedForward);
            float right = Vector3.Dot(_horizontalVelocity, Vector3.Cross(Vector3.up, _smoothedForward));
            string direction = forward < 0f ? "backward" : "forward";
            if (Mathf.Abs(right) > Mathf.Abs(forward) * 0.5f) direction = (right > 0f ? "right_" : "left_") + direction;
            return _animations.CombatMotion["jog_" + direction];
        }

        private void BlendPresentationTransition(RuntimeBoneClip clip)
        {
            if (_presentationClip != clip)
            {
                if (_presentationClip != null)
                {
                    System.Array.Copy(_previousPresentationPose, _transitionFromPose, _bones.Length);
                    _presentationTransitionTime = 0f;
                }
                _presentationClip = clip;
            }
            _presentationTransitionTime += Time.deltaTime;
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_presentationTransitionTime / 0.18f));
            for (int index = 0; index < _bones.Length; index++)
            {
                if (blend < 1f) _bones[index].localRotation = Quaternion.Slerp(_transitionFromPose[index], _bones[index].localRotation, blend);
                _previousPresentationPose[index] = _bones[index].localRotation;
            }
        }

        private RuntimeBoneClip SelectCrouchClip()
        {
            float forward = Vector3.Dot(_horizontalVelocity, _smoothedForward);
            Vector3 rightAxis = Vector3.Cross(Vector3.up, _smoothedForward);
            float right = Vector3.Dot(_horizontalVelocity, rightAxis);
            if (forward < -0.05f) return _animations.CrouchBackward;
            if (right < -0.05f && Mathf.Abs(right) > Mathf.Abs(forward) * 0.65f) return _animations.CrouchLeft;
            return right >= 0f ? _animations.CrouchForwardRight : _animations.CrouchForwardLeft;
        }

        private void ApplyAuthoredWeaponLayers(float stateWeight)
        {
            float weaponWeight = _animationDirector.WeaponWeight * stateWeight;
            if (weaponWeight <= 0.001f) return;

            RuntimeBoneClip idle;
            RuntimeBoneClip aim;
            RuntimeBoneClip fire;
            RuntimeBoneClip reload;
            if (_animationDirector.Family == SosigAnimationDirector.WeaponFamily.Handgun)
            {
                idle = _animations.HandgunIdle;
                aim = _animations.HandgunAim;
                fire = _animations.HandgunFire;
                reload = _animations.HandgunReload;
            }
            else
            {
                idle = _animations.RifleIdle;
                aim = _animations.RifleAim;
                fire = _animations.RifleFire;
                reload = _animations.RifleReload;
            }

            idle.SampleClampedMasked(0f, _bones, weaponWeight, _weaponBodyMask);
            if (_locomotionWeight > 0.001f)
            {
                RuntimeBoneClip weaponJog = _animations.CombatMotion[_animationDirector.Family == SosigAnimationDirector.WeaponFamily.Handgun ? "handgun_jog" : "rifle_jog"];
                weaponJog.SampleMasked(_locomotionPhase * weaponJog.Duration, _bones, weaponWeight * _locomotionWeight, _weaponBodyMask);
            }
            RuntimeBoneClip breathing = _animations.CombatMotion["breathing"];
            breathing.SampleAdditive(_stateTime, _bones, weaponWeight * (1f - _locomotionWeight) * 0.35f, _weaponBodyMask, breathing, true);
            if (_animationDirector.AimWeight > 0.001f)
                aim.SampleAdditive(0f, _bones, weaponWeight * _animationDirector.AimWeight, _weaponBodyMask, idle, false);
            if (_animationDirector.FireTime < fire.Duration && _animationDirector.ReloadWeight < 0.25f)
            {
                float fadeOut = Mathf.Clamp01((fire.Duration - _animationDirector.FireTime) / 0.12f);
                fire.SampleAdditive(_animationDirector.FireTime, _bones, weaponWeight * fadeOut, _weaponBodyMask, fire, false);
            }
            if (_animationDirector.ReloadWeight > 0.001f)
                reload.SampleClampedMasked(_animationDirector.GetReloadClipTime(reload), _bones, weaponWeight * _animationDirector.ReloadWeight, _weaponBodyMask);
        }

        private void ResetDrivenBones()
        {
            if (_bones == null) return;
            for (int index = 0; index < _bones.Length; index++) _bones[index].localRotation = _bindRotations[index];
        }

        private void AddLocalRotation(string boneName, float x, float y, float z)
        {
            Transform bone;
            if (_bonesByName.TryGetValue(boneName, out bone)) bone.localRotation = bone.localRotation * Quaternion.Euler(x, y, z);
        }

        private void ApplySosigHandPose(float stateWeight)
        {
            if (!_settings.DriveHeldWeaponsFromAnimation || _sosig.Hands == null || _settings.HandIkWeight <= 0f || stateWeight <= 0f) return;
            for (int index = 0; index < _sosig.Hands.Count; index++)
            {
                SosigHand hand = _sosig.Hands[index];
                if (hand == null || !hand.IsHoldingObject || hand.HeldObject == null ||
                    hand.HeldObject.Type != SosigWeapon.SosigWeaponType.Gun || hand.HeldObject.RecoilHolder == null) continue;
                string side = hand.IsRightHand ? "R" : "L";
                Transform visualHand;
                if (!_bonesByName.TryGetValue("ValveBiped.Bip01_" + side + "_Hand", out visualHand)) continue;
                Vector3 grip = hand.HeldObject.RecoilHolder.position;
                // The native hand target belongs to the hidden Sosig rig and
                // can sit far above this humanoid's authored hand. Correct only
                // a nearby visual grip; never pull the arm across the body.
                if (Vector3.Distance(visualHand.position, grip) > 0.22f) continue;
                float poseWeight = Mathf.Clamp01(_settings.HandIkWeight * stateWeight);
                ApplyTwoBoneIk("ValveBiped.Bip01_" + side + "_UpperArm", "ValveBiped.Bip01_" + side + "_Forearm", "ValveBiped.Bip01_" + side + "_Hand", grip, poseWeight);
            }
        }

        private void PrepareWeaponSupportBindings()
        {
            if (!_settings.DriveHeldWeaponsFromAnimation || _sosig.Hands == null) return;
            for (int index = 0; index < _sosig.Hands.Count; index++)
            {
                SosigHand hand = _sosig.Hands[index];
                if (hand == null || !hand.IsRightHand || !hand.IsHoldingObject || hand.HeldObject == null ||
                    hand.HeldObject.Type != SosigWeapon.SosigWeaponType.Gun) continue;

                HandDriveBinding binding;
                if (!_handDriveBindings.TryGetValue(hand, out binding) || binding.Weapon != hand.HeldObject)
                {
                    binding = new HandDriveBinding();
                    binding.Weapon = hand.HeldObject;
                    RuntimeBoneClip reference = _animationDirector.Family == SosigAnimationDirector.WeaponFamily.Handgun
                        ? _animations.HandgunAim : _animations.RifleAim;
                    int boneIndex = _boneIndicesByName["ValveBiped.Bip01_R_Hand"];
                    Quaternion muzzleToGrip = hand.HeldObject.Muzzle != null && hand.HeldObject.RecoilHolder != null
                        ? Quaternion.Inverse(hand.HeldObject.Muzzle.rotation) * hand.HeldObject.RecoilHolder.rotation
                        : Quaternion.identity;
                    Quaternion referenceGrip = Quaternion.LookRotation(_smoothedForward, Vector3.up) * muzzleToGrip;
                    int leftIndex = _boneIndicesByName["ValveBiped.Bip01_L_Hand"];
                    Vector3 separation = ReferenceWorldPosition(reference, leftIndex) - ReferenceWorldPosition(reference, boneIndex);
                    binding.SupportGripLocal = Quaternion.Inverse(referenceGrip) * separation;
                    if (hand.HeldObject.O != null)
                        binding.AlternateGrip = hand.HeldObject.O.GetComponentInChildren<FVRAlternateGrip>();
                    _handDriveBindings[hand] = binding;
                    if (!_handAuthorityDiagnosticsLogged)
                    {
                        _handAuthorityDiagnosticsLogged = true;
                        Plugin.Log.LogInfo("Combat presentation: native Sosig aiming owns the gun; authored visual hands follow its grip.");
                    }
                }
            }
        }

        private Quaternion ReferenceWorldRotation(RuntimeBoneClip clip, int boneIndex)
        {
            int parent = _asset.Bones[boneIndex].ParentIndex;
            Quaternion local = clip.FirstRotation(boneIndex, _bindRotations[boneIndex]);
            return parent >= 0 ? ReferenceWorldRotation(clip, parent) * local : _assetRoot.rotation * local;
        }

        private Vector3 ReferenceWorldPosition(RuntimeBoneClip clip, int boneIndex)
        {
            int parent = _asset.Bones[boneIndex].ParentIndex;
            Vector3 local = _asset.Bones[boneIndex].LocalPosition * _visualRoot.lossyScale.x;
            return parent >= 0 ? ReferenceWorldPosition(clip, parent) + ReferenceWorldRotation(clip, parent) * local
                : _assetRoot.position + _assetRoot.rotation * local;
        }

        private void ApplyWeaponSupportHand(float stateWeight)
        {
            if (_animationDirector.Family != SosigAnimationDirector.WeaponFamily.Rifle || _sosig.Hands == null) return;
            // A dual-wielding Sosig must keep its second hand free to hold its
            // own weapon. Reloading must also release the support hand.
            foreach (SosigHand hand in _sosig.Hands)
                if (hand != null && !hand.IsRightHand && hand.IsHoldingObject) return;
            foreach (SosigHand hand in _sosig.Hands)
            {
                HandDriveBinding binding;
                if (hand == null || !hand.IsRightHand || !hand.IsHoldingObject || hand.HeldObject == null ||
                    !_handDriveBindings.TryGetValue(hand, out binding) || binding.Weapon != hand.HeldObject || binding.Weapon.RecoilHolder == null) continue;
                Transform grip = binding.AlternateGrip == null ? null : binding.AlternateGrip.PoseOverride;
                Vector3 target = grip != null ? grip.position : binding.Weapon.RecoilHolder.TransformPoint(binding.SupportGripLocal);
                // Never pull a hand through the torso to chase a falling or
                // distant weapon. Native dropping remains authoritative.
                Transform current;
                if (!_bonesByName.TryGetValue("ValveBiped.Bip01_L_Hand", out current) || Vector3.Distance(target, current.position) > 0.22f) continue;
                float weight = stateWeight * _animationDirector.WeaponWeight * (1f - _animationDirector.ReloadWeight);
                ApplyTwoBoneIk("ValveBiped.Bip01_L_UpperArm", "ValveBiped.Bip01_L_Forearm", "ValveBiped.Bip01_L_Hand", target, weight);
            }
        }

        private void ApplyAimPresentation(float weight)
        {
            Vector3 target;
            if (!TryGetAimPoint(out target) || _animationDirector.WeaponWeight < 0.01f) return;
            Vector3 direction = target - GetChestPosition();
            if (direction.sqrMagnitude < 0.01f) return;
            float pitch = Mathf.Clamp(Mathf.Atan2(direction.y, Vector3.ProjectOnPlane(direction, Vector3.up).magnitude) * Mathf.Rad2Deg, -45f, 50f);
            Vector3 right = Vector3.Cross(Vector3.up, _smoothedForward).normalized;
            Transform spine;
            if (_bonesByName.TryGetValue("ValveBiped.Bip01_Spine1", out spine))
                spine.rotation = Quaternion.AngleAxis(-pitch * weight * _animationDirector.WeaponWeight, right) * spine.rotation;
        }

        private void ApplySosigLinkPose(float weight)
        {
            SosigLink baseLink = FindLink(SosigLink.SosigBodyPart.UpperLink, 2);
            SosigLink torsoLink = FindLink(SosigLink.SosigBodyPart.Torso, 1);
            SosigLink headLink = FindLink(SosigLink.SosigBodyPart.Head, 0);
            if (baseLink == null || torsoLink == null || headLink == null) return;

            Vector3 torsoDirection = torsoLink.transform.position - baseLink.transform.position;
            Vector3 headDirection = headLink.transform.position - torsoLink.transform.position;

            Transform spine; Transform spine2; Transform neck; Transform head;
            if (_bonesByName.TryGetValue("ValveBiped.Bip01_Spine", out spine) &&
                _bonesByName.TryGetValue("ValveBiped.Bip01_Spine2", out spine2))
                AimBoneDirection(spine, spine2, torsoDirection, weight * 0.45f);
            if (_bonesByName.TryGetValue("ValveBiped.Bip01_Spine2", out spine2) &&
                _bonesByName.TryGetValue("ValveBiped.Bip01_Neck1", out neck))
                AimBoneDirection(spine2, neck, headDirection, weight * 0.6f);
            if (_bonesByName.TryGetValue("ValveBiped.Bip01_Neck1", out neck) &&
                _bonesByName.TryGetValue("ValveBiped.Bip01_Head1", out head))
                AimBoneDirection(neck, head, headDirection, weight * 0.9f);
        }

        private void ApplyTwoBoneIk(string upperName, string forearmName, string handName, Vector3 target, float weight)
        {
            Transform upper; Transform forearm; Transform hand;
            if (!_bonesByName.TryGetValue(upperName, out upper) || !_bonesByName.TryGetValue(forearmName, out forearm) || !_bonesByName.TryGetValue(handName, out hand)) return;
            Vector3 shoulder = upper.position;
            float upperLength = Vector3.Distance(shoulder, forearm.position);
            float lowerLength = Vector3.Distance(forearm.position, hand.position);
            Vector3 toTarget = target - shoulder;
            float targetDistance = toTarget.magnitude;
            if (upperLength < 0.001f || lowerLength < 0.001f || targetDistance < 0.001f) return;

            float combinedLength = upperLength + lowerLength;
            // Keep a small bend at full extension and prevent the forearm from
            // folding through the upper arm when a Sosig hand target is too close.
            float reach = Mathf.Clamp(targetDistance, combinedLength * 0.28f, combinedLength * 0.985f);
            Vector3 direction = toTarget / targetDistance;
            Vector3 outward = upper.position - GetChestPosition();
            Vector3 pole = outward - _visualRoot.up * 0.45f;
            pole -= direction * Vector3.Dot(pole, direction);
            if (pole.sqrMagnitude < 0.000001f) pole = Vector3.Cross(direction, _visualRoot.forward);
            pole.Normalize();

            float along = (upperLength * upperLength - lowerLength * lowerLength + reach * reach) / (2f * reach);
            float bend = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 desiredElbow = shoulder + direction * along + pole * bend;
            AimBoneDirection(upper, forearm, desiredElbow - shoulder, weight);
            AimBoneDirection(forearm, hand, target - forearm.position, weight);
        }

        private void ApplyAnatomicalJointLimits()
        {
            float weight = Mathf.Clamp01(_settings.JointLimitWeight);
            if (weight <= 0f) return;

            ConstrainJoint("ValveBiped.Bip01_Spine", "ValveBiped.Bip01_Spine1", 30f, 24f, weight);
            ConstrainJoint("ValveBiped.Bip01_Spine1", "ValveBiped.Bip01_Spine2", 30f, 24f, weight);
            ConstrainJoint("ValveBiped.Bip01_Spine2", "ValveBiped.Bip01_Spine4", 35f, 28f, weight);
            ConstrainJoint("ValveBiped.Bip01_Spine4", "ValveBiped.Bip01_Neck1", 35f, 28f, weight);
            ConstrainJoint("ValveBiped.Bip01_Neck1", "ValveBiped.Bip01_Head1", 50f, 35f, weight);

            ConstrainArm("R", weight);
            ConstrainArm("L", weight);
            ConstrainLeg("R", weight);
            ConstrainLeg("L", weight);
        }

        private void ConstrainArm(string side, float weight)
        {
            string prefix = "ValveBiped.Bip01_" + side + "_";
            ConstrainJoint(prefix + "Clavicle", prefix + "UpperArm", 45f, 25f, weight);
            ConstrainJoint(prefix + "UpperArm", prefix + "Forearm", 125f, 70f, weight);
            ConstrainJoint(prefix + "Forearm", prefix + "Hand", 150f, 18f, weight);
        }

        private void ConstrainLeg(string side, float weight)
        {
            string prefix = "ValveBiped.Bip01_" + side + "_";
            ConstrainJoint(prefix + "Thigh", prefix + "Calf", 95f, 42f, weight);
            ConstrainJoint(prefix + "Calf", prefix + "Foot", 145f, 10f, weight);
            ConstrainJoint(prefix + "Foot", prefix + "Toe0", 55f, 25f, weight);
        }

        private void ConstrainJoint(string boneName, string childName, float maxSwing, float maxTwist, float weight)
        {
            Transform bone; Transform child;
            if (!_bonesByName.TryGetValue(boneName, out bone) || !_bonesByName.TryGetValue(childName, out child)) return;
            int boneIndex;
            if (!_boneIndicesByName.TryGetValue(boneName, out boneIndex)) return;
            Vector3 twistAxis = child.localPosition.normalized;
            Quaternion constrained = AnatomicalJointLimiter.ClampRelativeToBind(bone.localRotation, _bindRotations[boneIndex], twistAxis, maxSwing, maxTwist);
            bone.localRotation = Quaternion.Slerp(bone.localRotation, constrained, weight);
        }

        private Vector3 GetChestPosition()
        {
            Transform chest;
            return _bonesByName.TryGetValue("ValveBiped.Bip01_Spine2", out chest) ? chest.position : _visualRoot.position;
        }

        private void AlignControlledPelvisHeightToLink()
        {
            SosigLink upperLink = FindLink(SosigLink.SosigBodyPart.UpperLink, 2);
            Transform pelvis;
            if (upperLink == null || !_bonesByName.TryGetValue("ValveBiped.Bip01_Pelvis", out pelvis)) return;
            _visualRoot.position += Vector3.up * (upperLink.transform.position.y - pelvis.position.y);
        }

        private Vector3 SkinnedSolePosition(RuntimeSkinnedAsset.SoleVertex sole)
        {
            BoneWeight weight = sole.Weight;
            return WeightedSolePoint(sole.Position, weight.boneIndex0, weight.weight0)
                + WeightedSolePoint(sole.Position, weight.boneIndex1, weight.weight1)
                + WeightedSolePoint(sole.Position, weight.boneIndex2, weight.weight2)
                + WeightedSolePoint(sole.Position, weight.boneIndex3, weight.weight3);
        }

        private Vector3 WeightedSolePoint(Vector3 vertex, int boneIndex, float weight)
        {
            if (weight <= 0f) return Vector3.zero;
            return _bones[boneIndex].TransformPoint(_asset.Bindposes[boneIndex].MultiplyPoint3x4(vertex)) * weight;
        }

        private void GroundAnimatedSoles()
        {
            _soleProbeTimer -= Time.deltaTime;
            bool probe = _soleProbeTimer <= 0f;
            if (probe) _soleProbeTimer = 0.05f;
            float minimumClearance = float.MaxValue;
            Vector3 lower = _sosig.Links[2].transform.position;
            for (int index = 0; index < _asset.SoleVertices.Length; index++)
            {
                Vector3 sole = SkinnedSolePosition(_asset.SoleVertices[index]);
                if (probe)
                {
                    RaycastHit hit;
                    _soleHasFloor[index] = TryFindFloor(new Vector3(sole.x, lower.y, sole.z), out hit);
                    if (_soleHasFloor[index]) _soleFloorHeights[index] = hit.point.y;
                }
                if (!_soleHasFloor[index] || lower.y - _soleFloorHeights[index] > 1.4f) continue;
                minimumClearance = Mathf.Min(minimumClearance, sole.y - _soleFloorHeights[index]);
            }
            // Resolve from the final skin pose, after crouch/pelvis alignment,
            // before weapon targets and hitboxes are synchronized. Never move
            // the physics body or NavMeshAgent to correct visual foot contact.
            if (minimumClearance < float.MaxValue)
                _visualRoot.position += Vector3.up * (0.008f - minimumClearance);
        }

        private void AlignBallisticPelvisToLink()
        {
            SosigLink upperLink = FindLink(SosigLink.SosigBodyPart.UpperLink, 2);
            Transform pelvis;
            if (upperLink == null || !_bonesByName.TryGetValue("ValveBiped.Bip01_Pelvis", out pelvis)) return;
            Vector3 correction = upperLink.transform.position - pelvis.position;
            _visualRoot.position += correction * _ballisticWeight;

            if (!_hasFloor) return;
            float lowest = float.MaxValue;
            for (int index = 0; index < _asset.SoleVertices.Length; index++)
                lowest = Mathf.Min(lowest, SkinnedSolePosition(_asset.SoleVertices[index]).y);
            // Falling retains its native airborne trajectory. Only remove boot
            // penetration; do not pull the falling model down onto the floor.
            if (lowest < _floorHeight) _visualRoot.position += Vector3.up * (_floorHeight - lowest);
        }

        private static void AimBoneDirection(Transform bone, Transform child, Vector3 desiredDirection, float weight)
        {
            Vector3 current = child.position - bone.position;
            if (current.sqrMagnitude < 0.000001f || desiredDirection.sqrMagnitude < 0.000001f) return;
            Quaternion aimed = Quaternion.FromToRotation(current, desiredDirection) * bone.rotation;
            bone.rotation = Quaternion.Slerp(bone.rotation, aimed, Mathf.Clamp01(weight));
        }

        private void HideOriginalVisuals()
        {
            if (_sosig.Renderers != null)
                for (int index = 0; index < _sosig.Renderers.Length; index++) HideRenderer(_sosig.Renderers[index]);
            if (_sosig.Meshes != null)
                for (int index = 0; index < _sosig.Meshes.Length; index++)
                    if (_sosig.Meshes[index] != null) HideRenderer(_sosig.Meshes[index].GetComponent<Renderer>());
            if (_sosig.Links != null)
            {
                for (int linkIndex = 0; linkIndex < _sosig.Links.Count; linkIndex++)
                {
                    SosigLink link = _sosig.Links[linkIndex];
                    if (link == null) continue;
                    HideRenderer(link.SubRenderer);
                    Renderer[] linkRenderers = link.GetComponentsInChildren<Renderer>(true);
                    for (int rendererIndex = 0; rendererIndex < linkRenderers.Length; rendererIndex++) HideRenderer(linkRenderers[rendererIndex]);
                }
            }
            Renderer[] renderers = _sosig.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++) HideRenderer(renderers[index]);
        }

        private void HideRenderer(Renderer renderer)
        {
            if (renderer == null || _proxyRenderers.Contains(renderer)) return;
            if (renderer is ParticleSystemRenderer || renderer is LineRenderer) return;
            if (IsHeadIcon(renderer.transform)) return;
            if (renderer.GetComponentInParent<SosigWeapon>() != null) return;
            if (!_hiddenRenderers.ContainsKey(renderer)) _hiddenRenderers.Add(renderer, renderer.enabled);
            renderer.enabled = false;
        }

        private bool IsHeadIcon(Transform candidate)
        {
            if (_sosig.HeadIcons == null) return false;
            for (int index = 0; index < _sosig.HeadIcons.Count; index++)
            {
                GameObject icon = _sosig.HeadIcons[index];
                if (icon != null && candidate.IsChildOf(icon.transform)) return true;
            }
            return false;
        }

        public void Detach()
        {
            _initialized = false;
            if (_deathFxReplaced && _sosig != null)
            {
                _sosig.DamageFX_Explosion = _originalExplosion;
                _sosig.UsesGibs = _originalUsesGibs;
            }
            _deathFxReplaced = false;
            if (_mustardBurstReplaced && _sosig != null)
            {
                _sosig.DamageFX_SmallMustardBurst = _originalSmallMustardBurst;
                _sosig.DamageFX_LargeMustardBurst = _originalLargeMustardBurst;
            }
            _mustardBurstReplaced = false;
            foreach (ParticleSystemRenderer renderer in _suppressedBleedRenderers)
                if (renderer != null) renderer.enabled = true;
            _suppressedBleedRenderers.Clear();
            if (_visualRoot != null) _visualRoot.gameObject.SetActive(false);
            foreach (KeyValuePair<Renderer, bool> pair in _hiddenRenderers) if (pair.Key != null) pair.Key.enabled = pair.Value;
            _hiddenRenderers.Clear();
            foreach (KeyValuePair<GameObject, int> pair in _originalHitboxLayers) if (pair.Key != null) pair.Key.layer = pair.Value;
            _originalHitboxLayers.Clear();
            for (int index = 0; index < _ragdollParts.Count; index++)
            {
                RagdollBinding binding = _ragdollParts[index];
                if (binding != null && binding.ArmorOverlay != null) binding.ArmorOverlay.Dispose();
                if (binding != null && binding.GameObject != null)
                {
                    if (binding.Collider != null) binding.Collider.enabled = false;
                    if (binding.ArmorCollider != null) binding.ArmorCollider.enabled = false;
                    Destroy(binding.GameObject);
                }
            }
            _ragdollParts.Clear();
            if (_visualRoot != null) Destroy(_visualRoot.gameObject);
            _visualRoot = null;
        }

        private void OnDestroy()
        {
            Detach();
        }

        internal bool UsesBloodImpacts { get { return _meatImpacts && _initialized; } }

        internal void SuppressNativeBleed(ParticleSystem system)
        {
            if (system == null) return;
            foreach (ParticleSystemRenderer renderer in system.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (renderer == null || !renderer.enabled) continue;
                renderer.enabled = false;
                _suppressedBleedRenderers.Add(renderer);
            }
        }

        private sealed class RagdollBinding
        {
            public string Label;
            public GameObject GameObject;
            public Transform Bone;
            public Transform EndBone;
            public Rigidbody Body;
            public Collider Collider;
            public Collider ArmorCollider;
            public CustomSosigHitbox Hitbox;
            public ArmorHitboxOverlay ArmorOverlay;
            public CharacterJoint Joint;
            public RagdollBinding Parent;
            public Quaternion BodyRotationFromBone;
        }

        private sealed class HandDriveBinding
        {
            public SosigWeapon Weapon;
            public Vector3 SupportGripLocal;
            public FVRAlternateGrip AlternateGrip;
        }
    }
}
