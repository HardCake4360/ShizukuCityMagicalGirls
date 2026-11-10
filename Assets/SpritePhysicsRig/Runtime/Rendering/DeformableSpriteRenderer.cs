using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShizukuCity.SpritePhysicsRig
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class DeformableSpriteRenderer : MonoBehaviour
    {
        const string RenderObjectName = "__SpriteRigGeneratedRenderer";
        const string DefaultRigShaderName = "ShizukuCity/Sprite Physics Rig Unlit";
        static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int RendererColorId = Shader.PropertyToID("_RendererColor");

        [Serializable]
        public struct BonePoseOverride
        {
            [SerializeField] string boneId;
            [SerializeField] bool enabled;
            [SerializeField] Vector2 positionOffset;
            [SerializeField] float rotationOffset;

            public string BoneId => boneId;
            public bool Enabled => enabled;
            public Vector2 PositionOffset => positionOffset;
            public float RotationOffset => rotationOffset;
        }

        [Serializable]
        public struct BonePhysicsOverride
        {
            [SerializeField] string boneId;
            [SerializeField] bool enabled;
            [SerializeField] BonePhysicsSettings settings;

            public string BoneId => boneId;
            public bool Enabled => enabled;
            public BonePhysicsSettings Settings => settings;
        }

        [Serializable]
        public struct BoneTransformBinding
        {
            [SerializeField] string boneId;
            [SerializeField] Transform target;
            [SerializeField] bool drivePosition;
            [SerializeField] bool driveRotation;

            public string BoneId => boneId;
            public Transform Target => target;
            public bool DrivePosition => drivePosition;
            public bool DriveRotation => driveRotation;
        }

        public readonly struct RuntimeDiagnostics
        {
            public RuntimeDiagnostics(
                int simulatedFrameCount,
                bool teleported,
                int collisionQueryCount,
                int collisionCorrectionCount)
            {
                SimulatedFrameCount = simulatedFrameCount;
                Teleported = teleported;
                CollisionQueryCount = collisionQueryCount;
                CollisionCorrectionCount = collisionCorrectionCount;
            }

            public int SimulatedFrameCount { get; }
            public bool Teleported { get; }
            public int CollisionQueryCount { get; }
            public int CollisionCorrectionCount { get; }
        }

        [Header("Source")]
        [SerializeField] SpriteRenderer sourceRenderer;
        [SerializeField] SpriteRigAsset rigAsset;

        [Header("Mesh")]
        [SerializeField, Min(0f)] float boundsPadding = 0.1f;
        [SerializeField] bool updateWhenOffscreen = true;
        [SerializeField] Material generatedMaterialOverride;

        [Header("Manual Pose Preview")]
        [SerializeField] List<BonePoseOverride> poseOverrides = new List<BonePoseOverride>();

        [Header("Physics")]
        [SerializeField] bool physicsEnabled = true;
        [SerializeField] bool simulateWhenOffscreen = true;
        [SerializeField] Vector2 gravity = new Vector2(0f, -1f);
        [SerializeField] Vector2 externalForce;
        [SerializeField, Min(0f)] float stiffnessMultiplier = 1f;
        [SerializeField, Min(0f)] float dampingMultiplier = 1f;
        [SerializeField, Min(0f)] float inertiaMultiplier = 1f;
        [SerializeField, Range(1, 16)] int constraintIterations = 6;
        [SerializeField] List<BonePhysicsOverride> physicsOverrides = new List<BonePhysicsOverride>();

        [Header("Animation Bindings")]
        [SerializeField] List<BoneTransformBinding> transformBindings = new List<BoneTransformBinding>();

        [Header("Reference Frame")]
        [SerializeField, Min(0f)] float teleportDistanceThreshold = 3f;
        [SerializeField, Range(0f, 180f)] float teleportRotationThreshold = 90f;
        [SerializeField, Min(1f)] float teleportScaleRatioThreshold = 2f;

        [Header("Collision 2D")]
        [SerializeField] bool collisionEnabled;
        [SerializeField] RigCollisionShape collisionShape = RigCollisionShape.BoneEndpoints;
        [SerializeField] LayerMask collisionLayers = ~0;
        [SerializeField] bool includeTriggerColliders;
        [SerializeField, Min(0f)] float collisionRadiusMultiplier = 1f;
        [SerializeField, Range(0f, 1f)] float collisionFriction = 0.2f;
        [SerializeField, Range(0f, 1f)] float collisionBounciness;
        [SerializeField, Min(0f)] float collisionSkinWidth = 0.001f;
        [SerializeField, Range(1, 8)] int collisionIterations = 2;

        [Header("Diagnostics")]
        [SerializeField] bool showRigGizmos = true;
        [SerializeField] bool showCollisionGizmos = true;
        [SerializeField] Color boneGizmoColor = new Color(0.2f, 0.9f, 1f, 0.9f);
        [SerializeField] Color collisionGizmoColor = new Color(1f, 0.35f, 0.2f, 0.65f);
        [SerializeField, TextArea] string lastError;

        MeshFilter meshFilter;
        MeshRenderer meshRenderer;
        GameObject renderObject;
        Mesh runtimeMesh;
        Material runtimeMaterial;
        CpuSpriteSkinner skinner;
        BonePhysicsSolver physicsSolver;
        RigCollisionSolver2D collisionSolver;
        Transform[] boundTransforms;
        bool[] bindingDrivesPosition;
        bool[] bindingDrivesRotation;
        Matrix4x4[] animationWorldMatrices;
        float[] animationWorldRotations;
        byte[] animationTraversalState;
        Vector2[] deformedRigPositions;
        Vector3[] meshPositions;
        Vector2[] meshUvs;
        Color32[] meshColors;
        int[] meshTriangles;
        MaterialPropertyBlock propertyBlock;
        Sprite builtSprite;
        SpriteRigAsset builtRigAsset;
        bool builtFlipX;
        bool builtFlipY;
        bool poseDirty;
        bool buildSucceeded;
        Matrix4x4 previousLocalToWorldMatrix;
        Vector3 previousWorldPosition;
        Quaternion previousWorldRotation;
        Vector3 previousLossyScale;
        bool hasPreviousReferenceFrame;
#if UNITY_EDITOR
        bool validateRebuildQueued;
#endif
        int simulatedFrameCount;
        bool teleportedLastStep;
        int lastCollisionQueryCount;
        int lastCollisionCorrectionCount;

        public SpriteRenderer SourceRenderer => sourceRenderer;
        public SpriteRigAsset RigAsset => rigAsset;
        public Mesh RuntimeMesh => runtimeMesh;
        public string LastError => lastError;
        public bool IsBuilt => buildSucceeded;
        public int BoneCount => skinner != null ? skinner.BoneCount : 0;
        public RuntimeDiagnostics Diagnostics => new RuntimeDiagnostics(
            simulatedFrameCount,
            teleportedLastStep,
            lastCollisionQueryCount,
            lastCollisionCorrectionCount);
        public Vector2 Gravity
        {
            get => gravity;
            set => gravity = value;
        }
        public Vector2 ExternalForce
        {
            get => externalForce;
            set => externalForce = value;
        }
        public bool PhysicsEnabled
        {
            get => physicsEnabled;
            set
            {
                if (physicsEnabled == value)
                {
                    return;
                }

                physicsEnabled = value;
                ResetSimulation();
            }
        }

        void Reset()
        {
            sourceRenderer = GetComponent<SpriteRenderer>();
            CacheComponents();
        }

        void OnEnable()
        {
            CacheComponents();
            Rebuild();
        }

        void LateUpdate()
        {
            if (NeedsRebuild())
            {
                Rebuild();
            }

            if (!buildSucceeded)
            {
                return;
            }

            UpdateAnimatedTargets();
            SyncRendererProperties();
            if (poseDirty || updateWhenOffscreen || meshRenderer.isVisible)
            {
                ApplyDeformation();
            }
        }

        void FixedUpdate()
        {
            if (!Application.isPlaying
                || !physicsEnabled
                || !buildSucceeded
                || NeedsRebuild()
                || (!simulateWhenOffscreen && meshRenderer != null && !meshRenderer.isVisible))
            {
                RememberWorldPosition();
                return;
            }

            SimulatePhysics(Time.fixedDeltaTime);
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            CancelValidateRebuild();
#endif
            RestoreSourceRenderer();
            SetMeshRendererEnabled(false);
            DestroyRuntimeMesh();
            DestroyRuntimeMaterial();
            DestroyRenderObject();
            buildSucceeded = false;
            physicsSolver = null;
            DisposeCollisionSolver();
            hasPreviousReferenceFrame = false;
        }

        void OnDestroy()
        {
            RestoreSourceRenderer();
            DisposeCollisionSolver();
            DestroyRuntimeMesh();
            DestroyRuntimeMaterial();
            DestroyRenderObject();
        }

        void OnDrawGizmosSelected()
        {
            if (!showRigGizmos
                || physicsSolver == null
                || sourceRenderer == null
                || sourceRenderer.sprite == null)
            {
                return;
            }

            for (int boneIndex = 0; boneIndex < physicsSolver.BoneCount; boneIndex++)
            {
                Vector3 start = RigToWorldPoint(physicsSolver.GetBoneStart(boneIndex));
                Vector3 endpoint = RigToWorldPoint(physicsSolver.GetBoneEndpoint(boneIndex));
                Gizmos.color = boneGizmoColor;
                Gizmos.DrawLine(start, endpoint);
                Gizmos.DrawSphere(start, HandleWorldSize(0.006f));

                if (!showCollisionGizmos)
                {
                    continue;
                }

                BonePhysicsSettings settings = physicsSolver.GetBoneSettings(boneIndex);
                float radius = RigRadiusToWorld(
                    settings.CollisionRadius * Mathf.Max(0f, collisionRadiusMultiplier));
                if (radius <= 0f)
                {
                    continue;
                }

                Gizmos.color = collisionGizmoColor;
                Gizmos.DrawWireSphere(endpoint, radius);
                if (collisionShape == RigCollisionShape.BoneCapsules)
                {
                    Gizmos.DrawWireSphere(start, radius);
                    Vector3 perpendicular = Vector3.Cross(Vector3.forward, endpoint - start).normalized * radius;
                    Gizmos.DrawLine(start + perpendicular, endpoint + perpendicular);
                    Gizmos.DrawLine(start - perpendicular, endpoint - perpendicular);
                }
            }
        }

        void OnValidate()
        {
            boundsPadding = Mathf.Max(0f, boundsPadding);
            stiffnessMultiplier = Mathf.Max(0f, stiffnessMultiplier);
            dampingMultiplier = Mathf.Max(0f, dampingMultiplier);
            inertiaMultiplier = Mathf.Max(0f, inertiaMultiplier);
            constraintIterations = Mathf.Clamp(constraintIterations, 1, 16);
            teleportDistanceThreshold = Mathf.Max(0f, teleportDistanceThreshold);
            teleportScaleRatioThreshold = Mathf.Max(1f, teleportScaleRatioThreshold);
            collisionRadiusMultiplier = Mathf.Max(0f, collisionRadiusMultiplier);
            collisionSkinWidth = Mathf.Max(0f, collisionSkinWidth);
            collisionIterations = Mathf.Clamp(collisionIterations, 1, 8);
            if (sourceRenderer == null)
            {
                sourceRenderer = GetComponent<SpriteRenderer>();
            }

            if (isActiveAndEnabled)
            {
#if UNITY_EDITOR
                QueueValidateRebuild();
#endif
            }
        }

#if UNITY_EDITOR
        void QueueValidateRebuild()
        {
            if (validateRebuildQueued)
            {
                return;
            }

            validateRebuildQueued = true;
            UnityEditor.EditorApplication.delayCall += DelayedValidateRebuild;
        }

        void CancelValidateRebuild()
        {
            if (!validateRebuildQueued)
            {
                return;
            }

            UnityEditor.EditorApplication.delayCall -= DelayedValidateRebuild;
            validateRebuildQueued = false;
        }

        void DelayedValidateRebuild()
        {
            validateRebuildQueued = false;
            if (this != null && isActiveAndEnabled)
            {
                Rebuild();
            }
        }
#endif

        [ContextMenu("Rebuild Rig Mesh")]
        public bool Rebuild()
        {
#if UNITY_EDITOR
            CancelValidateRebuild();
#endif
            CacheComponents();
            buildSucceeded = false;
            lastError = string.Empty;
            DisposeCollisionSolver();
            SetMeshRendererEnabled(false);
            builtSprite = sourceRenderer != null ? sourceRenderer.sprite : null;
            builtRigAsset = rigAsset;
            builtFlipX = sourceRenderer != null && sourceRenderer.flipX;
            builtFlipY = sourceRenderer != null && sourceRenderer.flipY;

            if (!TryValidateInputs(out string validationError))
            {
                FailBuild(validationError);
                return false;
            }

            int vertexCount = rigAsset.Vertices.Count;
            deformedRigPositions = new Vector2[vertexCount];
            meshPositions = new Vector3[vertexCount];
            meshUvs = new Vector2[vertexCount];
            meshColors = new Color32[vertexCount];
            for (int i = 0; i < meshColors.Length; i++)
            {
                meshColors[i] = Color.white;
            }
            meshTriangles = BuildTriangleBuffer(sourceRenderer.flipX ^ sourceRenderer.flipY);

            if (!SpriteRigCoordinateUtility.TryBuildTextureUvs(
                    sourceRenderer.sprite,
                    rigAsset.Vertices,
                    meshUvs,
                    out string uvError))
            {
                FailBuild(uvError);
                return false;
            }

            DestroyRuntimeMesh();
            runtimeMesh = new Mesh
            {
                name = $"{name}_DeformedSpriteMesh",
                hideFlags = HideFlags.DontSave
            };
            runtimeMesh.MarkDynamic();
            runtimeMesh.vertices = meshPositions;
            runtimeMesh.uv = meshUvs;
            runtimeMesh.colors32 = meshColors;
            runtimeMesh.triangles = meshTriangles;
            meshFilter.sharedMesh = runtimeMesh;

            skinner = new CpuSpriteSkinner(rigAsset);
            ApplySerializedPoseOverrides();
            InitializePhysicsSolver();
            InitializeAnimationBindings();
            collisionSolver = new RigCollisionSolver2D(transform, sourceRenderer, rigAsset);
            simulatedFrameCount = 0;
            teleportedLastStep = false;
            lastCollisionQueryCount = 0;
            lastCollisionCorrectionCount = 0;
            buildSucceeded = true;
            poseDirty = true;
            RememberWorldPosition();

            sourceRenderer.enabled = false;
            SetMeshRendererEnabled(true);
            SyncRendererProperties();
            ApplyDeformation();
            return true;
        }

        public void ResetToBindPose()
        {
            if (skinner == null)
            {
                return;
            }

            skinner.ResetToBindPose();
            skinner.RecalculatePoseMatrices();
            if (physicsSolver != null)
            {
                physicsSolver.ResetTargetToBindPose();
                physicsSolver.ResetSimulation();
                physicsSolver.ApplyPoseTo(skinner);
            }
            poseDirty = true;
        }

        [ContextMenu("Reset Physics Simulation")]
        public void ResetSimulation()
        {
            if (physicsSolver == null || skinner == null)
            {
                return;
            }

            physicsSolver.ResetSimulation();
            physicsSolver.ApplyPoseTo(skinner);
            poseDirty = true;
            RememberWorldPosition();
        }

        [ContextMenu("Refresh Collision Ignore List")]
        public void RefreshCollisionIgnoreList()
        {
            collisionSolver?.RefreshIgnoredColliders();
        }

        public void SimulatePhysics(float deltaTime)
        {
            if (physicsSolver == null
                || skinner == null
                || sourceRenderer == null
                || sourceRenderer.sprite == null
                || rigAsset == null)
            {
                RememberWorldPosition();
                return;
            }

            UpdateAnimatedTargets();
            teleportedLastStep = HasTeleported();
            if (teleportedLastStep)
            {
                physicsSolver.ResetSimulation();
            }
            else
            {
                ReprojectPhysicsReferenceFrame();
            }

            RigPhysicsParameters parameters = new RigPhysicsParameters
            {
                Gravity = gravity,
                ExternalForce = externalForce,
                StiffnessMultiplier = stiffnessMultiplier,
                DampingMultiplier = dampingMultiplier,
                InertiaMultiplier = inertiaMultiplier,
                ConstraintIterations = constraintIterations
            };

            physicsSolver.Step(deltaTime, Vector2.zero, parameters);
            lastCollisionQueryCount = 0;
            lastCollisionCorrectionCount = 0;
            if (collisionEnabled && collisionSolver != null)
            {
                RigCollisionParameters collisionParameters = new RigCollisionParameters
                {
                    LayerMask = collisionLayers,
                    Shape = collisionShape,
                    IncludeTriggers = includeTriggerColliders,
                    RadiusMultiplier = collisionRadiusMultiplier,
                    Friction = collisionFriction,
                    Bounciness = collisionBounciness,
                    SkinWidth = collisionSkinWidth,
                    CollisionIterations = collisionIterations,
                    ConstraintIterations = constraintIterations
                };
                collisionSolver.Solve(physicsSolver, collisionParameters);
                lastCollisionQueryCount = collisionSolver.LastQueryCount;
                lastCollisionCorrectionCount = collisionSolver.LastCorrectionCount;
            }
            physicsSolver.ApplyPoseTo(skinner);
            poseDirty = true;
            simulatedFrameCount++;
            RememberWorldPosition();
        }

        public bool SetBoneLocalPose(string stableBoneId, Vector2 localPosition, float localRotation)
        {
            int boneIndex = FindBoneIndex(stableBoneId);
            if (boneIndex < 0)
            {
                return false;
            }

            return SetBoneLocalPose(boneIndex, localPosition, localRotation);
        }

        public bool SetBoneLocalPose(int boneIndex, Vector2 localPosition, float localRotation)
        {
            if (skinner == null || boneIndex < 0 || boneIndex >= skinner.BoneCount)
            {
                return false;
            }

            if (physicsSolver != null
                && physicsSolver.GetBoneSettings(boneIndex).SimulationMode == BoneSimulationMode.Fixed)
            {
                localPosition = rigAsset.Bones[boneIndex].LocalPosition;
                localRotation = rigAsset.Bones[boneIndex].LocalRotation;
            }

            skinner.SetLocalPose(boneIndex, localPosition, localRotation);
            skinner.RecalculatePoseMatrices();
            if (physicsSolver != null)
            {
                physicsSolver.SetTargetLocalPose(boneIndex, localPosition, localRotation);
            }
            poseDirty = true;
            return true;
        }

        public bool SetBonePhysicsSettings(string stableBoneId, BonePhysicsSettings settings)
        {
            int boneIndex = FindBoneIndex(stableBoneId);
            return SetBonePhysicsSettings(boneIndex, settings);
        }

        public bool SetBonePhysicsSettings(int boneIndex, BonePhysicsSettings settings)
        {
            if (physicsSolver == null || boneIndex < 0 || boneIndex >= physicsSolver.BoneCount)
            {
                return false;
            }

            BoneSimulationMode previousMode =
                physicsSolver.GetBoneSettings(boneIndex).SimulationMode;
            physicsSolver.SetBoneSettings(boneIndex, settings);
            if (previousMode != settings.SimulationMode)
            {
                physicsSolver.ResetSimulation();
                physicsSolver.ApplyPoseTo(skinner);
                poseDirty = true;
            }
            return true;
        }

        public bool TryGetBonePhysicsSettings(int boneIndex, out BonePhysicsSettings settings)
        {
            if (physicsSolver == null || boneIndex < 0 || boneIndex >= physicsSolver.BoneCount)
            {
                settings = default;
                return false;
            }

            settings = physicsSolver.GetBoneSettings(boneIndex);
            return true;
        }

        public bool TryGetBoneWorldMatrix(int boneIndex, out Matrix4x4 matrix)
        {
            if (skinner == null || boneIndex < 0 || boneIndex >= skinner.BoneCount)
            {
                matrix = Matrix4x4.identity;
                return false;
            }

            matrix = skinner.GetPoseWorldMatrix(boneIndex);
            return true;
        }

        void ApplySerializedPoseOverrides()
        {
            skinner.ResetToBindPose();
            for (int i = 0; i < poseOverrides.Count; i++)
            {
                BonePoseOverride poseOverride = poseOverrides[i];
                if (!poseOverride.Enabled)
                {
                    continue;
                }

                int boneIndex = FindBoneIndex(poseOverride.BoneId);
                if (boneIndex >= 0)
                {
                    skinner.SetLocalPoseOffset(boneIndex, poseOverride.PositionOffset, poseOverride.RotationOffset);
                }
            }

            skinner.RecalculatePoseMatrices();
        }

        void InitializePhysicsSolver()
        {
            physicsSolver = new BonePhysicsSolver(rigAsset);
            for (int i = 0; i < rigAsset.Bones.Count; i++)
            {
                physicsSolver.SetTargetLocalPose(
                    i,
                    skinner.GetLocalPosition(i),
                    skinner.GetLocalRotation(i));
            }

            for (int i = 0; i < physicsOverrides.Count; i++)
            {
                BonePhysicsOverride physicsOverride = physicsOverrides[i];
                if (!physicsOverride.Enabled)
                {
                    continue;
                }

                int boneIndex = FindBoneIndex(physicsOverride.BoneId);
                if (boneIndex >= 0)
                {
                    physicsSolver.SetBoneSettings(boneIndex, physicsOverride.Settings);
                }
            }

            physicsSolver.ResetSimulation();
            physicsSolver.ApplyPoseTo(skinner);
        }

        void ApplyDeformation()
        {
            if (!buildSucceeded || skinner == null || runtimeMesh == null)
            {
                return;
            }

            skinner.Deform(deformedRigPositions);
            Sprite sprite = sourceRenderer.sprite;
            for (int i = 0; i < deformedRigPositions.Length; i++)
            {
                meshPositions[i] = SpriteRigCoordinateUtility.RigToSpriteLocal(
                    deformedRigPositions[i],
                    sprite,
                    sourceRenderer.flipX,
                    sourceRenderer.flipY);
            }

            runtimeMesh.vertices = meshPositions;
            runtimeMesh.bounds = CalculateMeshBounds(meshPositions, boundsPadding);
            poseDirty = false;
        }

        static Bounds CalculateMeshBounds(Vector3[] positions, float padding)
        {
            if (positions == null || positions.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one * Mathf.Max(0f, padding * 2f));
            }

            Vector3 minimum = positions[0];
            Vector3 maximum = positions[0];
            for (int i = 1; i < positions.Length; i++)
            {
                minimum = Vector3.Min(minimum, positions[i]);
                maximum = Vector3.Max(maximum, positions[i]);
            }

            Bounds bounds = new Bounds((minimum + maximum) * 0.5f, maximum - minimum);
            bounds.Expand(Mathf.Max(0f, padding) * 2f);
            return bounds;
        }

        bool TryValidateInputs(out string error)
        {
            if (sourceRenderer == null)
            {
                error = "A source SpriteRenderer is required.";
                return false;
            }

            if (sourceRenderer.sprite == null)
            {
                error = "The source SpriteRenderer has no Sprite.";
                return false;
            }

            if (sourceRenderer.drawMode != SpriteDrawMode.Simple)
            {
                error = $"Sprite draw mode {sourceRenderer.drawMode} is not supported. Use Simple.";
                return false;
            }

            if (rigAsset == null)
            {
                error = "A SpriteRigAsset is required.";
                return false;
            }

            List<RigValidationIssue> issues = new List<RigValidationIssue>();
            if (!rigAsset.Validate(issues))
            {
                error = issues.Count > 0 ? issues[0].ToString() : "The SpriteRigAsset is invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        bool NeedsRebuild()
        {
            return sourceRenderer == null
                || builtSprite != sourceRenderer.sprite
                || builtRigAsset != rigAsset
                || builtFlipX != sourceRenderer.flipX
                || builtFlipY != sourceRenderer.flipY;
        }

        int[] BuildTriangleBuffer(bool reverseWinding)
        {
            int[] result = new int[rigAsset.Triangles.Count];
            for (int i = 0; i < result.Length; i += 3)
            {
                result[i] = rigAsset.Triangles[i];
                result[i + 1] = reverseWinding ? rigAsset.Triangles[i + 2] : rigAsset.Triangles[i + 1];
                result[i + 2] = reverseWinding ? rigAsset.Triangles[i + 1] : rigAsset.Triangles[i + 2];
            }

            return result;
        }

        int FindBoneIndex(string stableBoneId)
        {
            if (rigAsset == null || string.IsNullOrEmpty(stableBoneId))
            {
                return -1;
            }

            for (int i = 0; i < rigAsset.Bones.Count; i++)
            {
                if (string.Equals(rigAsset.Bones[i].StableId, stableBoneId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        void SyncRendererProperties()
        {
            if (sourceRenderer == null || meshRenderer == null || !buildSucceeded)
            {
                return;
            }

            sourceRenderer.enabled = false;
            meshRenderer.sharedMaterial = generatedMaterialOverride != null
                ? generatedMaterialOverride
                : GetOrCreateRuntimeMaterial();
            meshRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
            meshRenderer.sortingOrder = sourceRenderer.sortingOrder;
            meshRenderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
            meshRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            meshRenderer.receiveShadows = sourceRenderer.receiveShadows;

            propertyBlock ??= new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetTexture(MainTextureId, sourceRenderer.sprite.texture);
            propertyBlock.SetTexture(BaseMapId, sourceRenderer.sprite.texture);
            propertyBlock.SetColor(ColorId, sourceRenderer.color);
            propertyBlock.SetColor(BaseColorId, sourceRenderer.color);
            propertyBlock.SetColor(RendererColorId, sourceRenderer.color);
            meshRenderer.SetPropertyBlock(propertyBlock);
        }

        Material GetOrCreateRuntimeMaterial()
        {
            if (runtimeMaterial != null)
            {
                return runtimeMaterial;
            }

            Shader shader = Shader.Find(DefaultRigShaderName);
            if (shader == null)
            {
                lastError = $"Required shader '{DefaultRigShaderName}' was not found.";
                return sourceRenderer.sharedMaterial;
            }

            runtimeMaterial = new Material(shader)
            {
                name = $"{name}_SpriteRigRuntimeMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
            return runtimeMaterial;
        }

        void DestroyRuntimeMaterial()
        {
            if (runtimeMaterial == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(runtimeMaterial);
            }
            else
            {
                DestroyImmediate(runtimeMaterial);
            }

            runtimeMaterial = null;
        }

        void CacheComponents()
        {
            if (sourceRenderer == null)
            {
                sourceRenderer = GetComponent<SpriteRenderer>();
            }

            EnsureRenderObject();
        }

        void EnsureRenderObject()
        {
            if (renderObject != null && meshFilter != null && meshRenderer != null)
            {
                return;
            }

            Transform existing = transform.Find(RenderObjectName);
            if (existing != null)
            {
                renderObject = existing.gameObject;
                meshFilter = renderObject.GetComponent<MeshFilter>();
                meshRenderer = renderObject.GetComponent<MeshRenderer>();
            }

            if (renderObject == null)
            {
                renderObject = new GameObject(RenderObjectName)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                renderObject.transform.SetParent(transform, false);
            }

            if (meshFilter == null)
            {
                meshFilter = renderObject.GetComponent<MeshFilter>();
            }
            if (meshRenderer == null)
            {
                meshRenderer = renderObject.GetComponent<MeshRenderer>();
            }
            if (meshFilter == null)
            {
                meshFilter = renderObject.AddComponent<MeshFilter>();
            }
            if (meshRenderer == null)
            {
                meshRenderer = renderObject.AddComponent<MeshRenderer>();
            }
            renderObject.transform.localPosition = Vector3.zero;
            renderObject.transform.localRotation = Quaternion.identity;
            renderObject.transform.localScale = Vector3.one;
        }

        void DestroyRenderObject()
        {
            if (renderObject == null)
            {
                meshFilter = null;
                meshRenderer = null;
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(renderObject);
            }
            else
            {
                DestroyImmediate(renderObject);
            }

            renderObject = null;
            meshFilter = null;
            meshRenderer = null;
        }

        void RestoreSourceRenderer()
        {
            if (sourceRenderer != null)
            {
                sourceRenderer.enabled = true;
            }
        }

        void FailBuild(string error)
        {
            lastError = error;
            skinner = null;
            physicsSolver = null;
            DisposeCollisionSolver();
            RestoreSourceRenderer();
            DestroyRuntimeMesh();
            Debug.LogWarning($"{nameof(DeformableSpriteRenderer)} on '{name}' could not build: {error}", this);
        }

        void SetMeshRendererEnabled(bool enabled)
        {
            if (meshRenderer != null)
            {
                meshRenderer.enabled = enabled;
            }
        }

        void DestroyRuntimeMesh()
        {
            if (meshFilter != null && meshFilter.sharedMesh == runtimeMesh)
            {
                meshFilter.sharedMesh = null;
            }

            if (runtimeMesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(runtimeMesh);
            }
            else
            {
                DestroyImmediate(runtimeMesh);
            }

            runtimeMesh = null;
        }

        void InitializeAnimationBindings()
        {
            int boneCount = rigAsset.Bones.Count;
            boundTransforms = new Transform[boneCount];
            bindingDrivesPosition = new bool[boneCount];
            bindingDrivesRotation = new bool[boneCount];
            animationWorldMatrices = new Matrix4x4[boneCount];
            animationWorldRotations = new float[boneCount];
            animationTraversalState = new byte[boneCount];

            for (int i = 0; i < transformBindings.Count; i++)
            {
                BoneTransformBinding binding = transformBindings[i];
                int boneIndex = FindBoneIndex(binding.BoneId);
                if (boneIndex < 0 || binding.Target == null)
                {
                    continue;
                }

                boundTransforms[boneIndex] = binding.Target;
                bindingDrivesPosition[boneIndex] = binding.DrivePosition;
                bindingDrivesRotation[boneIndex] = binding.DriveRotation;
            }
        }

        void UpdateAnimatedTargets()
        {
            if (rigAsset == null
                || physicsSolver == null
                || skinner == null
                || boundTransforms == null
                || animationTraversalState == null
                || animationTraversalState.Length != rigAsset.Bones.Count)
            {
                return;
            }

            Array.Clear(animationTraversalState, 0, animationTraversalState.Length);
            for (int i = 0; i < rigAsset.Bones.Count; i++)
            {
                EvaluateAnimatedBone(i);
            }

            bool changed = false;
            for (int i = 0; i < rigAsset.Bones.Count; i++)
            {
                if (boundTransforms[i] == null
                    || physicsSolver.GetBoneSettings(i).SimulationMode != BoneSimulationMode.Animated)
                {
                    continue;
                }

                skinner.SetLocalPose(
                    i,
                    physicsSolver.GetTargetLocalPosition(i),
                    physicsSolver.GetTargetLocalRotation(i));
                changed = true;
            }

            if (changed)
            {
                skinner.RecalculatePoseMatrices();
                poseDirty = true;
            }
        }

        Matrix4x4 EvaluateAnimatedBone(int boneIndex)
        {
            if (animationTraversalState[boneIndex] == 2)
            {
                return animationWorldMatrices[boneIndex];
            }
            if (animationTraversalState[boneIndex] == 1)
            {
                return Matrix4x4.identity;
            }

            animationTraversalState[boneIndex] = 1;
            int parentIndex = rigAsset.Bones[boneIndex].ParentIndex;
            Matrix4x4 parentWorld = Matrix4x4.identity;
            float parentRotation = 0f;
            if (parentIndex >= 0)
            {
                if (physicsSolver.GetBoneSettings(parentIndex).SimulationMode
                    == BoneSimulationMode.Dynamic)
                {
                    parentWorld = physicsSolver.GetWorldMatrix(parentIndex);
                    parentRotation = physicsSolver.GetWorldRotation(parentIndex);
                }
                else
                {
                    parentWorld = EvaluateAnimatedBone(parentIndex);
                    parentRotation = animationWorldRotations[parentIndex];
                }
            }

            Vector2 localPosition = physicsSolver.GetTargetLocalPosition(boneIndex);
            float localRotation = physicsSolver.GetTargetLocalRotation(boneIndex);
            Transform binding = boundTransforms[boneIndex];
            BonePhysicsSettings settings = physicsSolver.GetBoneSettings(boneIndex);
            if (binding != null && settings.SimulationMode == BoneSimulationMode.Animated)
            {
                Vector2 worldRigPosition = WorldToRig(binding.position);

                if (bindingDrivesPosition[boneIndex])
                {
                    localPosition = parentWorld.inverse.MultiplyPoint3x4(worldRigPosition);
                }
                if (bindingDrivesRotation[boneIndex])
                {
                    float targetRotationOffset = Mathf.DeltaAngle(
                        0f,
                        binding.localEulerAngles.z);
                    localRotation = rigAsset.Bones[boneIndex].LocalRotation
                        + targetRotationOffset;
                }
                physicsSolver.SetTargetLocalPose(boneIndex, localPosition, localRotation);
            }

            Matrix4x4 local = Matrix4x4.TRS(
                localPosition,
                Quaternion.Euler(0f, 0f, localRotation),
                Vector3.one);
            animationWorldMatrices[boneIndex] = parentWorld * local;
            animationWorldRotations[boneIndex] = parentRotation + localRotation;
            animationTraversalState[boneIndex] = 2;
            return animationWorldMatrices[boneIndex];
        }

        bool HasTeleported()
        {
            if (!hasPreviousReferenceFrame)
            {
                return true;
            }

            if (teleportDistanceThreshold > 0f
                && Vector3.Distance(previousWorldPosition, transform.position) > teleportDistanceThreshold)
            {
                return true;
            }

            if (teleportRotationThreshold > 0f
                && Quaternion.Angle(previousWorldRotation, transform.rotation) > teleportRotationThreshold)
            {
                return true;
            }

            Vector3 currentScale = transform.lossyScale;
            return ScaleRatio(previousLossyScale.x, currentScale.x) > teleportScaleRatioThreshold
                || ScaleRatio(previousLossyScale.y, currentScale.y) > teleportScaleRatioThreshold;
        }

        void ReprojectPhysicsReferenceFrame()
        {
            Matrix4x4 currentWorldToLocal = transform.worldToLocalMatrix;
            for (int boneIndex = 0; boneIndex < physicsSolver.BoneCount; boneIndex++)
            {
                Vector2 endpoint = physicsSolver.GetBoneEndpoint(boneIndex);
                Vector2 previousEndpoint = physicsSolver.GetPreviousBoneEndpoint(boneIndex);
                Vector2 reprojectedEndpoint = ReprojectRigPoint(
                    endpoint,
                    previousLocalToWorldMatrix,
                    currentWorldToLocal);
                Vector2 reprojectedPrevious = ReprojectRigPoint(
                    previousEndpoint,
                    previousLocalToWorldMatrix,
                    currentWorldToLocal);
                physicsSolver.ReprojectBoneState(
                    boneIndex,
                    reprojectedEndpoint,
                    reprojectedPrevious,
                    inertiaMultiplier);
            }
        }

        Vector2 ReprojectRigPoint(
            Vector2 rigPoint,
            Matrix4x4 oldLocalToWorld,
            Matrix4x4 currentWorldToLocal)
        {
            Vector3 oldLocal = SpriteRigCoordinateUtility.RigToSpriteLocal(
                rigPoint,
                sourceRenderer.sprite,
                sourceRenderer.flipX,
                sourceRenderer.flipY);
            Vector3 world = oldLocalToWorld.MultiplyPoint3x4(oldLocal);
            Vector3 currentLocal = currentWorldToLocal.MultiplyPoint3x4(world);
            return SpriteRigCoordinateUtility.SpriteLocalToRig(
                currentLocal,
                sourceRenderer.sprite,
                sourceRenderer.flipX,
                sourceRenderer.flipY);
        }

        Vector2 WorldToRig(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return SpriteRigCoordinateUtility.SpriteLocalToRig(
                local,
                sourceRenderer.sprite,
                sourceRenderer.flipX,
                sourceRenderer.flipY);
        }

        Vector3 RigToWorldPoint(Vector2 rigPosition)
        {
            Vector3 local = SpriteRigCoordinateUtility.RigToSpriteLocal(
                rigPosition,
                sourceRenderer.sprite,
                sourceRenderer.flipX,
                sourceRenderer.flipY);
            return transform.TransformPoint(local);
        }

        float RigRadiusToWorld(float rigRadius)
        {
            float localRadius = rigRadius
                * sourceRenderer.sprite.rect.height
                / sourceRenderer.sprite.pixelsPerUnit;
            Vector3 scale = transform.lossyScale;
            return localRadius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        }

        float HandleWorldSize(float normalizedSize)
        {
            return Mathf.Max(0.001f, RigRadiusToWorld(normalizedSize));
        }

        static float ScaleRatio(float previous, float current)
        {
            float previousMagnitude = Mathf.Max(0.0001f, Mathf.Abs(previous));
            float currentMagnitude = Mathf.Max(0.0001f, Mathf.Abs(current));
            return Mathf.Max(previousMagnitude / currentMagnitude, currentMagnitude / previousMagnitude);
        }

        void RememberWorldPosition()
        {
            previousLocalToWorldMatrix = transform.localToWorldMatrix;
            previousWorldPosition = transform.position;
            previousWorldRotation = transform.rotation;
            previousLossyScale = transform.lossyScale;
            hasPreviousReferenceFrame = true;
        }

        void DisposeCollisionSolver()
        {
            collisionSolver?.Dispose();
            collisionSolver = null;
        }
    }
}
