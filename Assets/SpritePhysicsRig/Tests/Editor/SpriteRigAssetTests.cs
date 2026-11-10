using System.Collections.Generic;
using NUnit.Framework;
using ShizukuCity.SpritePhysicsRig.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace ShizukuCity.SpritePhysicsRig.Tests
{
    public sealed class SpriteRigAssetTests
    {
        SpriteRigAsset asset;

        [SetUp]
        public void SetUp()
        {
            asset = ScriptableObject.CreateInstance<SpriteRigAsset>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void Validate_AcceptsMinimalCounterClockwiseRig()
        {
            PopulateMinimalRig(clockwise: false);

            List<RigValidationIssue> issues = new List<RigValidationIssue>();

            Assert.That(asset.Validate(issues), Is.True, JoinIssues(issues));
            Assert.That(issues, Is.Empty);
        }

        [Test]
        public void InitializeDefaultQuad_CreatesValidReusableRig()
        {
            asset.InitializeDefaultQuad();
            List<RigValidationIssue> issues = new List<RigValidationIssue>();

            Assert.That(asset.Validate(issues), Is.True, JoinIssues(issues));
            Assert.That(asset.Vertices.Count, Is.EqualTo(4));
            Assert.That(asset.Triangles.Count, Is.EqualTo(6));
            Assert.That(asset.Bones.Count, Is.EqualTo(1));
            Assert.That(asset.Weights.Count, Is.EqualTo(4));
        }

        [Test]
        public void Validate_RejectsClockwiseTriangle()
        {
            PopulateMinimalRig(clockwise: true);

            List<RigValidationIssue> issues = new List<RigValidationIssue>();

            Assert.That(asset.Validate(issues), Is.False);
            Assert.That(issues.Exists(issue => issue.Code == RigValidationCode.ClockwiseTriangle), Is.True);
        }

        [Test]
        public void Validate_DetectsCyclicBoneHierarchy()
        {
            PopulateMinimalRig(clockwise: false);
            SerializedObject serializedAsset = new SerializedObject(asset);
            SerializedProperty bones = serializedAsset.FindProperty("bones");
            bones.GetArrayElementAtIndex(0).FindPropertyRelative("parentIndex").intValue = 1;
            bones.InsertArrayElementAtIndex(1);
            ConfigureBone(bones.GetArrayElementAtIndex(1), "child", 0);
            serializedAsset.ApplyModifiedPropertiesWithoutUndo();

            List<RigValidationIssue> issues = new List<RigValidationIssue>();

            Assert.That(asset.Validate(issues), Is.False);
            Assert.That(issues.Exists(issue => issue.Code == RigValidationCode.CyclicBoneHierarchy), Is.True);
        }

        [Test]
        public void NormalizeVertexWeights_MergesDuplicateInfluences()
        {
            PopulateMinimalRig(clockwise: false);
            SerializedObject serializedAsset = new SerializedObject(asset);
            SerializedProperty firstWeight = serializedAsset.FindProperty("weights").GetArrayElementAtIndex(0);
            firstWeight.FindPropertyRelative("boneIndex0").intValue = 0;
            firstWeight.FindPropertyRelative("weight0").floatValue = 0.25f;
            firstWeight.FindPropertyRelative("boneIndex1").intValue = 0;
            firstWeight.FindPropertyRelative("weight1").floatValue = 0.75f;
            serializedAsset.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(asset.NormalizeVertexWeights(0), Is.True);

            VertexWeight4 normalized = asset.Weights[0];
            Assert.That(normalized.BoneIndex0, Is.EqualTo(0));
            Assert.That(normalized.Weight0, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(normalized.Weight1, Is.Zero);
        }

        [Test]
        public void RigToSpriteLocal_AccountsForPivotPpuAndFlip()
        {
            Texture2D texture = new Texture2D(200, 100);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 200f, 100f),
                new Vector2(0.25f, 0.25f),
                100f);

            try
            {
                Vector3 local = SpriteRigCoordinateUtility.RigToSpriteLocal(
                    new Vector2(1f, 1f),
                    sprite,
                    flipX: false,
                    flipY: false);
                Vector3 flipped = SpriteRigCoordinateUtility.RigToSpriteLocal(
                    new Vector2(1f, 1f),
                    sprite,
                    flipX: true,
                    flipY: true);

                Assert.That(local.x, Is.EqualTo(1.5f).Within(0.0001f));
                Assert.That(local.y, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(flipped, Is.EqualTo(-local));
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void TryBuildTextureUvs_MapsNormalizedSpriteRect()
        {
            PopulateMinimalRig(clockwise: false);
            Texture2D texture = new Texture2D(200, 100);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(20f, 10f, 100f, 50f),
                new Vector2(0.5f, 0.5f),
                100f);
            Vector2[] output = new Vector2[asset.Vertices.Count];

            try
            {
                bool success = SpriteRigCoordinateUtility.TryBuildTextureUvs(
                    sprite,
                    asset.Vertices,
                    output,
                    out string error);

                Assert.That(success, Is.True, error);
                AssertVector2(output[0], new Vector2(0.1f, 0.1f));
                AssertVector2(output[1], new Vector2(0.6f, 0.1f));
                AssertVector2(output[2], new Vector2(0.1f, 0.6f));
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void SpriteLocalDeltaToRig_AccountsForSpriteScaleAndFlip()
        {
            Texture2D texture = new Texture2D(200, 100);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 200f, 100f),
                new Vector2(0.5f, 0.5f),
                100f);

            try
            {
                Vector2 delta = SpriteRigCoordinateUtility.SpriteLocalDeltaToRig(
                    new Vector2(2f, 1f),
                    sprite,
                    flipX: true,
                    flipY: false);

                Assert.That(delta, Is.EqualTo(new Vector2(-1f, 1f)));
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void SpriteLocalAndRigCoordinates_RoundTripWithFlip()
        {
            Texture2D texture = new Texture2D(200, 100);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 200f, 100f),
                new Vector2(0.25f, 0.75f),
                100f);

            try
            {
                Vector2 expected = new Vector2(0.8f, 0.2f);
                Vector3 local = SpriteRigCoordinateUtility.RigToSpriteLocal(
                    expected,
                    sprite,
                    flipX: true,
                    flipY: false);
                Vector2 actual = SpriteRigCoordinateUtility.SpriteLocalToRig(
                    local,
                    sprite,
                    flipX: true,
                    flipY: false);

                AssertVector2(actual, expected);
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void CpuSkinner_BindPosePreservesVertices()
        {
            PopulateMinimalRig(clockwise: false);
            CpuSpriteSkinner skinner = new CpuSpriteSkinner(asset);
            Vector2[] output = new Vector2[asset.Vertices.Count];

            skinner.Deform(output);

            for (int i = 0; i < output.Length; i++)
            {
                Assert.That(output[i], Is.EqualTo(asset.Vertices[i].Position));
            }
        }

        [Test]
        public void CpuSkinner_RotatesWeightedVerticesWithRootBone()
        {
            PopulateMinimalRig(clockwise: false);
            CpuSpriteSkinner skinner = new CpuSpriteSkinner(asset);
            Vector2[] output = new Vector2[asset.Vertices.Count];

            skinner.SetLocalPose(0, Vector2.zero, 90f);
            skinner.RecalculatePoseMatrices();
            skinner.Deform(output);

            Assert.That(output[0].x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(output[0].y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(output[1].x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(output[1].y, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(output[2].x, Is.EqualTo(-1f).Within(0.0001f));
            Assert.That(output[2].y, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void BonePhysicsSolver_DynamicBoneRespondsToGravityAndKeepsLength()
        {
            asset.InitializeDefaultQuad();
            BonePhysicsSolver solver = new BonePhysicsSolver(asset);
            BonePhysicsSettings settings = solver.GetBoneSettings(0);
            settings.Stiffness = 0f;
            settings.Damping = 1f;
            settings.MaximumAngularSpeed = 10000f;
            solver.SetBoneSettings(0, settings);

            RigPhysicsParameters parameters = RigPhysicsParameters.Default;
            for (int i = 0; i < 10; i++)
            {
                solver.Step(0.02f, Vector2.zero, parameters);
            }

            Vector2 start = solver.GetBoneStart(0);
            Vector2 endpoint = solver.GetBoneEndpoint(0);
            Assert.That(endpoint.y, Is.LessThan(start.y));
            Assert.That(Vector2.Distance(start, endpoint), Is.EqualTo(asset.Bones[0].Length).Within(0.0001f));
        }

        [Test]
        public void BonePhysicsSolver_FixedBoneIgnoresForces()
        {
            asset.InitializeDefaultQuad();
            BonePhysicsSolver solver = new BonePhysicsSolver(asset);
            BonePhysicsSettings settings = solver.GetBoneSettings(0);
            settings.SimulationMode = BoneSimulationMode.Fixed;
            solver.SetBoneSettings(0, settings);

            Vector2 before = solver.GetBoneEndpoint(0);
            RigPhysicsParameters parameters = RigPhysicsParameters.Default;
            parameters.Gravity = new Vector2(0f, -100f);
            parameters.ExternalForce = new Vector2(100f, 0f);
            solver.Step(0.02f, new Vector2(10f, 10f), parameters);

            Assert.That(solver.GetBoneEndpoint(0), Is.EqualTo(before));
            Assert.That(solver.GetWorldRotation(0), Is.EqualTo(asset.Bones[0].LocalRotation).Within(0.0001f));
        }

        [Test]
        public void BonePhysicsSolver_ClampsDynamicBoneAngle()
        {
            asset.InitializeDefaultQuad();
            BonePhysicsSolver solver = new BonePhysicsSolver(asset);
            BonePhysicsSettings settings = solver.GetBoneSettings(0);
            settings.Stiffness = 0f;
            settings.MinimumAngle = -10f;
            settings.MaximumAngle = 10f;
            settings.MaximumAngularSpeed = 10000f;
            solver.SetBoneSettings(0, settings);

            RigPhysicsParameters parameters = RigPhysicsParameters.Default;
            parameters.Gravity = new Vector2(0f, -1000f);
            for (int i = 0; i < 10; i++)
            {
                solver.Step(0.02f, Vector2.zero, parameters);
            }

            Assert.That(solver.GetWorldRotation(0), Is.InRange(-10.001f, 10.001f));
        }

        [Test]
        public void BonePhysicsSolver_FixedAndAnimatedModesHaveDistinctTargets()
        {
            asset.InitializeDefaultQuad();
            BonePhysicsSolver solver = new BonePhysicsSolver(asset);
            BonePhysicsSettings settings = solver.GetBoneSettings(0);
            settings.SimulationMode = BoneSimulationMode.Fixed;
            solver.SetBoneSettings(0, settings);
            solver.SetTargetLocalPose(0, new Vector2(0.2f, 0.2f), 90f);
            solver.ResetSimulation();

            Assert.That(solver.GetWorldRotation(0), Is.EqualTo(asset.Bones[0].LocalRotation).Within(0.0001f));
            AssertVector2(solver.GetBoneStart(0), asset.Bones[0].LocalPosition);

            settings.SimulationMode = BoneSimulationMode.Animated;
            solver.SetBoneSettings(0, settings);
            solver.ResetSimulation();

            Assert.That(solver.GetWorldRotation(0), Is.EqualTo(90f).Within(0.0001f));
            AssertVector2(solver.GetBoneStart(0), new Vector2(0.2f, 0.2f));
        }

        [Test]
        public void BonePhysicsSolver_ReprojectsDynamicStateForReferenceFrameMotion()
        {
            asset.InitializeDefaultQuad();
            BonePhysicsSolver solver = new BonePhysicsSolver(asset);
            Vector2 endpoint = solver.GetBoneEndpoint(0);
            Vector2 previous = solver.GetPreviousBoneEndpoint(0);

            solver.ReprojectBoneState(
                0,
                endpoint + new Vector2(-0.2f, 0f),
                previous + new Vector2(-0.2f, 0f),
                1f);

            Assert.That(solver.GetBoneEndpoint(0).x, Is.LessThan(endpoint.x));
            Assert.That(solver.GetPreviousBoneEndpoint(0).x, Is.LessThan(previous.x));
        }

        [TestCase(RigCollisionShape.BoneEndpoints)]
        [TestCase(RigCollisionShape.BoneCapsules)]
        public void CollisionSolver_PushesDynamicBoneOutOfBoxCollider(RigCollisionShape shape)
        {
            asset.InitializeDefaultQuad();
            Texture2D texture = new Texture2D(100, 100);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 100f, 100f),
                new Vector2(0.5f, 0.5f),
                100f);
            GameObject owner = new GameObject("RigCollisionOwner");
            SpriteRenderer source = owner.AddComponent<SpriteRenderer>();
            source.sprite = sprite;
            GameObject obstacle = new GameObject("RigCollisionObstacle");
            BoxCollider2D box = obstacle.AddComponent<BoxCollider2D>();
            obstacle.transform.position = new Vector3(0.5f, -0.05f, 0f);
            box.size = new Vector2(1f, 0.1f);
            BonePhysicsSolver physics = new BonePhysicsSolver(asset);
            BonePhysicsSettings settings = physics.GetBoneSettings(0);
            settings.CollisionRadius = 0.1f;
            settings.Stiffness = 0f;
            physics.SetBoneSettings(0, settings);
            RigCollisionSolver2D collision = new RigCollisionSolver2D(owner.transform, source, asset);

            try
            {
                Physics2D.SyncTransforms();
                bool collided = collision.Solve(
                    physics,
                    new RigCollisionParameters
                    {
                        LayerMask = ~0,
                        Shape = shape,
                        IncludeTriggers = false,
                        RadiusMultiplier = 1f,
                        Friction = 0f,
                        Bounciness = 0f,
                        SkinWidth = 0.001f,
                        CollisionIterations = 2,
                        ConstraintIterations = 4
                    });

                Assert.That(collided, Is.True);
                Assert.That(collision.LastQueryCount, Is.GreaterThan(0));
                Assert.That(collision.LastCorrectionCount, Is.GreaterThan(0));
                Assert.That(physics.GetBoneEndpoint(0).y, Is.GreaterThan(0.5f));
                Assert.That(
                    Vector2.Distance(physics.GetBoneStart(0), physics.GetBoneEndpoint(0)),
                    Is.EqualTo(asset.Bones[0].Length).Within(0.0001f));
            }
            finally
            {
                collision.Dispose();
                Object.DestroyImmediate(obstacle);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void DeformableRenderer_RebuildsAndSimulatesCompleteRuntimePipeline()
        {
            asset.InitializeDefaultQuad();
            Texture2D texture = new Texture2D(32, 32);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 32f, 32f),
                new Vector2(0.5f, 0.5f),
                32f);
            GameObject owner = new GameObject("DeformableRendererPipeline");
            owner.SetActive(false);
            SpriteRenderer source = owner.AddComponent<SpriteRenderer>();
            source.sprite = sprite;
            DeformableSpriteRenderer renderer = owner.AddComponent<DeformableSpriteRenderer>();
            SerializedObject serializedRenderer = new SerializedObject(renderer);
            serializedRenderer.FindProperty("sourceRenderer").objectReferenceValue = source;
            serializedRenderer.FindProperty("rigAsset").objectReferenceValue = asset;
            serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
            owner.SetActive(true);

            try
            {
                Assert.That(renderer.Rebuild(), Is.True, renderer.LastError);
                Assert.That(renderer.IsBuilt, Is.True);
                Assert.That(renderer.RuntimeMesh, Is.Not.Null);
                Assert.That(renderer.RuntimeMesh.vertexCount, Is.EqualTo(asset.Vertices.Count));
                Assert.That(renderer.RuntimeMesh.colors32.Length, Is.EqualTo(asset.Vertices.Count));
                Assert.That(renderer.RuntimeMesh.colors32[0], Is.EqualTo((Color32)Color.white));
                Assert.That(source.enabled, Is.False);
                MeshRenderer generatedRenderer = owner.GetComponentInChildren<MeshRenderer>(true);
                Assert.That(generatedRenderer, Is.Not.Null);
                Assert.That(generatedRenderer.enabled, Is.True);
                Assert.That(generatedRenderer.sharedMaterial, Is.Not.Null);
                Assert.That(
                    generatedRenderer.sharedMaterial.shader.name,
                    Is.EqualTo("ShizukuCity/Sprite Physics Rig Unlit"));

                renderer.SimulatePhysics(0.02f);

                Assert.That(renderer.Diagnostics.SimulatedFrameCount, Is.EqualTo(1));
                Assert.That(renderer.RuntimeMesh.bounds.size.x, Is.GreaterThan(0f));
                Assert.That(renderer.RuntimeMesh.bounds.size.y, Is.GreaterThan(0f));

                renderer.enabled = false;
                Assert.That(source.enabled, Is.True);
                renderer.enabled = true;
                Assert.That(renderer.IsBuilt, Is.True);
                Assert.That(source.enabled, Is.False);

                SerializedObject missingRigRenderer = new SerializedObject(renderer);
                missingRigRenderer.FindProperty("rigAsset").objectReferenceValue = null;
                missingRigRenderer.ApplyModifiedPropertiesWithoutUndo();
                LogAssert.Expect(
                    LogType.Warning,
                    "DeformableSpriteRenderer on 'DeformableRendererPipeline' could not build: A SpriteRigAsset is required.");
                Assert.That(renderer.Rebuild(), Is.False);
                Assert.That(source.enabled, Is.True);
                Assert.DoesNotThrow(() => renderer.SimulatePhysics(0.02f));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void AnimatedBinding_AddsTargetLocalRotationToAuthoredBoneRotation()
        {
            asset.InitializeDefaultQuad();
            SerializedObject serializedAsset = new SerializedObject(asset);
            SerializedProperty rootBone = serializedAsset
                .FindProperty("bones")
                .GetArrayElementAtIndex(0);
            rootBone.FindPropertyRelative("localRotation").floatValue = 130f;
            rootBone
                .FindPropertyRelative("physics")
                .FindPropertyRelative("simulationMode")
                .enumValueIndex = (int)BoneSimulationMode.Animated;
            serializedAsset.ApplyModifiedPropertiesWithoutUndo();

            Texture2D texture = new Texture2D(32, 32);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 32f, 32f),
                new Vector2(0.5f, 0.5f),
                32f);
            GameObject owner = new GameObject("AnimatedBindingRotation");
            owner.SetActive(false);
            SpriteRenderer source = owner.AddComponent<SpriteRenderer>();
            source.sprite = sprite;
            Transform target = new GameObject("RotationTarget").transform;
            target.SetParent(owner.transform, false);
            DeformableSpriteRenderer renderer = owner.AddComponent<DeformableSpriteRenderer>();
            SerializedObject serializedRenderer = new SerializedObject(renderer);
            serializedRenderer.FindProperty("sourceRenderer").objectReferenceValue = source;
            serializedRenderer.FindProperty("rigAsset").objectReferenceValue = asset;
            SerializedProperty bindings = serializedRenderer.FindProperty("transformBindings");
            bindings.arraySize = 1;
            SerializedProperty binding = bindings.GetArrayElementAtIndex(0);
            binding.FindPropertyRelative("boneId").stringValue = asset.Bones[0].StableId;
            binding.FindPropertyRelative("target").objectReferenceValue = target;
            binding.FindPropertyRelative("drivePosition").boolValue = false;
            binding.FindPropertyRelative("driveRotation").boolValue = true;
            serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
            owner.SetActive(true);

            try
            {
                Assert.That(renderer.Rebuild(), Is.True, renderer.LastError);
                renderer.SimulatePhysics(0.02f);
                Assert.That(renderer.TryGetBoneWorldMatrix(0, out Matrix4x4 zeroOffset), Is.True);
                Assert.That(MatrixRotation(zeroOffset), Is.EqualTo(130f).Within(0.01f));

                target.localRotation = Quaternion.Euler(0f, 0f, 30f);
                renderer.SimulatePhysics(0.02f);
                Assert.That(renderer.TryGetBoneWorldMatrix(0, out Matrix4x4 rotated), Is.True);
                Assert.That(MatrixRotation(rotated), Is.EqualTo(160f).Within(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void EditorUtility_GenerateGridCreatesValidRig()
        {
            SpriteRigEditorUtility.GenerateGrid(asset, 2, 2);
            List<RigValidationIssue> issues = new List<RigValidationIssue>();

            Assert.That(asset.Validate(issues), Is.True, JoinIssues(issues));
            Assert.That(asset.Vertices.Count, Is.EqualTo(9));
            Assert.That(asset.Triangles.Count, Is.EqualTo(24));
            Assert.That(asset.Bones.Count, Is.EqualTo(1));
            Assert.That(asset.Weights.Count, Is.EqualTo(9));
        }

        [Test]
        public void EditorUtility_GenerateFromSpriteGeometryCreatesValidRig()
        {
            Texture2D texture = new Texture2D(64, 32);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 64f, 32f),
                new Vector2(0.5f, 0.5f),
                32f,
                0,
                SpriteMeshType.FullRect);

            try
            {
                Assert.That(
                    SpriteRigEditorUtility.GenerateFromSpriteGeometry(asset, sprite, out string error),
                    Is.True,
                    error);
                List<RigValidationIssue> issues = new List<RigValidationIssue>();
                Assert.That(asset.Validate(issues), Is.True, JoinIssues(issues));
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void EditorUtility_AutomaticWeightsRemainNormalized()
        {
            SpriteRigEditorUtility.GenerateGrid(asset, 2, 2);
            SpriteRigEditorUtility.AddBone(asset, 0, new Vector2(0.5f, 0f), 0f, 0.5f);

            Assert.That(SpriteRigEditorUtility.GenerateAutomaticWeights(asset), Is.True);

            for (int vertexIndex = 0; vertexIndex < asset.Weights.Count; vertexIndex++)
            {
                float sum = 0f;
                for (int influence = 0; influence < SpriteRigAsset.MaximumInfluenceCount; influence++)
                {
                    sum += asset.Weights[vertexIndex].GetWeight(influence);
                }
                Assert.That(sum, Is.EqualTo(1f).Within(0.0001f));
            }
        }

        [Test]
        public void EditorUtility_DeleteBoneRemapsWeightsToValidIndices()
        {
            SpriteRigEditorUtility.GenerateGrid(asset, 1, 1);
            int childIndex = SpriteRigEditorUtility.AddBone(asset, 0, new Vector2(0.5f, 0f), 0f, 0.5f);
            SpriteRigEditorUtility.GenerateAutomaticWeights(asset);

            Assert.That(SpriteRigEditorUtility.DeleteBone(asset, childIndex), Is.True);
            List<RigValidationIssue> issues = new List<RigValidationIssue>();
            Assert.That(asset.Validate(issues), Is.True, JoinIssues(issues));
            Assert.That(asset.Bones.Count, Is.EqualTo(1));
        }

        [Test]
        public void EditorUtility_DeleteVertexRemapsTopologyAndWeights()
        {
            SpriteRigEditorUtility.GenerateGrid(asset, 2, 2);

            Assert.That(SpriteRigEditorUtility.DeleteVertex(asset, 4), Is.True);

            List<RigValidationIssue> issues = new List<RigValidationIssue>();
            Assert.That(asset.Validate(issues), Is.True, JoinIssues(issues));
            Assert.That(asset.Vertices.Count, Is.EqualTo(8));
            Assert.That(asset.Weights.Count, Is.EqualTo(8));
        }

        [Test]
        public void EditorUtility_MirrorWeightsPreservesValidRig()
        {
            SpriteRigEditorUtility.GenerateGrid(asset, 2, 1);
            SpriteRigEditorUtility.AddBone(asset, 0, new Vector2(0.5f, 0f), 0f, 0.5f);
            SpriteRigEditorUtility.GenerateAutomaticWeights(asset);

            Assert.That(SpriteRigEditorUtility.MirrorWeightsHorizontally(asset), Is.True);

            List<RigValidationIssue> issues = new List<RigValidationIssue>();
            Assert.That(asset.Validate(issues), Is.True, JoinIssues(issues));
        }

        void PopulateMinimalRig(bool clockwise)
        {
            SerializedObject serializedAsset = new SerializedObject(asset);

            SerializedProperty vertices = serializedAsset.FindProperty("vertices");
            vertices.arraySize = 3;
            SetVertex(vertices.GetArrayElementAtIndex(0), new Vector2(0f, 0f));
            SetVertex(vertices.GetArrayElementAtIndex(1), new Vector2(1f, 0f));
            SetVertex(vertices.GetArrayElementAtIndex(2), new Vector2(0f, 1f));

            SerializedProperty edges = serializedAsset.FindProperty("edges");
            edges.arraySize = 3;
            SetEdge(edges.GetArrayElementAtIndex(0), 0, 1);
            SetEdge(edges.GetArrayElementAtIndex(1), 1, 2);
            SetEdge(edges.GetArrayElementAtIndex(2), 2, 0);

            SerializedProperty triangles = serializedAsset.FindProperty("triangles");
            triangles.arraySize = 3;
            triangles.GetArrayElementAtIndex(0).intValue = 0;
            triangles.GetArrayElementAtIndex(1).intValue = clockwise ? 2 : 1;
            triangles.GetArrayElementAtIndex(2).intValue = clockwise ? 1 : 2;

            SerializedProperty bones = serializedAsset.FindProperty("bones");
            bones.arraySize = 1;
            ConfigureBone(bones.GetArrayElementAtIndex(0), "root", -1);

            SerializedProperty weights = serializedAsset.FindProperty("weights");
            weights.arraySize = 3;
            for (int i = 0; i < weights.arraySize; i++)
            {
                SetSingleBoneWeight(weights.GetArrayElementAtIndex(i));
            }

            serializedAsset.ApplyModifiedPropertiesWithoutUndo();
            asset.EnsureStableBoneIds();
        }

        static float MatrixRotation(Matrix4x4 matrix)
        {
            return Mathf.Atan2(matrix.m10, matrix.m00) * Mathf.Rad2Deg;
        }

        static void SetVertex(SerializedProperty vertex, Vector2 position)
        {
            vertex.FindPropertyRelative("position").vector2Value = position;
            vertex.FindPropertyRelative("hasCustomUv").boolValue = false;
        }

        static void SetEdge(SerializedProperty edge, int vertexA, int vertexB)
        {
            edge.FindPropertyRelative("vertexA").intValue = vertexA;
            edge.FindPropertyRelative("vertexB").intValue = vertexB;
            edge.FindPropertyRelative("boundary").boolValue = true;
        }

        static void ConfigureBone(SerializedProperty bone, string name, int parentIndex)
        {
            bone.FindPropertyRelative("stableId").stringValue = string.Empty;
            bone.FindPropertyRelative("name").stringValue = name;
            bone.FindPropertyRelative("parentIndex").intValue = parentIndex;
            bone.FindPropertyRelative("localPosition").vector2Value = Vector2.zero;
            bone.FindPropertyRelative("localRotation").floatValue = 0f;
            bone.FindPropertyRelative("length").floatValue = 1f;

            SerializedProperty physics = bone.FindPropertyRelative("physics");
            physics.FindPropertyRelative("simulationMode").enumValueIndex = (int)BoneSimulationMode.Dynamic;
            physics.FindPropertyRelative("mass").floatValue = 1f;
            physics.FindPropertyRelative("gravityScale").floatValue = 1f;
            physics.FindPropertyRelative("stiffness").floatValue = 8f;
            physics.FindPropertyRelative("damping").floatValue = 0.86f;
            physics.FindPropertyRelative("inertia").floatValue = 0.75f;
            physics.FindPropertyRelative("minimumAngle").floatValue = -180f;
            physics.FindPropertyRelative("maximumAngle").floatValue = 180f;
            physics.FindPropertyRelative("maximumAngularSpeed").floatValue = 720f;
            physics.FindPropertyRelative("collisionRadius").floatValue = 0f;
            physics.FindPropertyRelative("externalForceMultiplier").floatValue = 1f;
        }

        static void SetSingleBoneWeight(SerializedProperty weight)
        {
            for (int i = 0; i < SpriteRigAsset.MaximumInfluenceCount; i++)
            {
                weight.FindPropertyRelative($"boneIndex{i}").intValue = 0;
                weight.FindPropertyRelative($"weight{i}").floatValue = i == 0 ? 1f : 0f;
            }
        }

        static string JoinIssues(List<RigValidationIssue> issues)
        {
            return string.Join("\n", issues);
        }

        static void AssertVector2(Vector2 actual, Vector2 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
        }
    }
}
