using System;
using UnityEngine;

namespace ShizukuCity.SpritePhysicsRig
{
    public struct RigPhysicsParameters
    {
        public Vector2 Gravity;
        public Vector2 ExternalForce;
        public float StiffnessMultiplier;
        public float DampingMultiplier;
        public float InertiaMultiplier;
        public int ConstraintIterations;

        public static RigPhysicsParameters Default => new RigPhysicsParameters
        {
            Gravity = new Vector2(0f, -1f),
            ExternalForce = Vector2.zero,
            StiffnessMultiplier = 1f,
            DampingMultiplier = 1f,
            InertiaMultiplier = 1f,
            ConstraintIterations = 6
        };
    }

    public sealed class BonePhysicsSolver
    {
        const float Epsilon = 0.000001f;

        readonly SpriteRigAsset rigAsset;
        readonly BonePhysicsSettings[] settings;
        readonly Vector2[] targetLocalPositions;
        readonly float[] targetLocalRotations;
        readonly Vector2[] endpoints;
        readonly Vector2[] previousEndpoints;
        readonly Matrix4x4[] worldMatrices;
        readonly float[] worldRotations;
        readonly float[] previousWorldRotations;
        readonly int[] evaluationOrder;

        public int BoneCount => settings.Length;

        public BonePhysicsSolver(SpriteRigAsset rigAsset)
        {
            this.rigAsset = rigAsset != null ? rigAsset : throw new ArgumentNullException(nameof(rigAsset));

            int boneCount = rigAsset.Bones.Count;
            settings = new BonePhysicsSettings[boneCount];
            targetLocalPositions = new Vector2[boneCount];
            targetLocalRotations = new float[boneCount];
            endpoints = new Vector2[boneCount];
            previousEndpoints = new Vector2[boneCount];
            worldMatrices = new Matrix4x4[boneCount];
            worldRotations = new float[boneCount];
            previousWorldRotations = new float[boneCount];
            evaluationOrder = BuildEvaluationOrder();

            for (int i = 0; i < boneCount; i++)
            {
                RigBone bone = rigAsset.Bones[i];
                settings[i] = bone.Physics;
                targetLocalPositions[i] = bone.LocalPosition;
                targetLocalRotations[i] = bone.LocalRotation;
            }

            ResetSimulation();
        }

        public BonePhysicsSettings GetBoneSettings(int boneIndex)
        {
            ValidateBoneIndex(boneIndex);
            return settings[boneIndex];
        }

        public void SetBoneSettings(int boneIndex, BonePhysicsSettings value)
        {
            ValidateBoneIndex(boneIndex);
            settings[boneIndex] = Sanitize(value);
        }

        public void SetTargetLocalPose(int boneIndex, Vector2 localPosition, float localRotation)
        {
            ValidateBoneIndex(boneIndex);
            targetLocalPositions[boneIndex] = localPosition;
            targetLocalRotations[boneIndex] = localRotation;
        }

        public Vector2 GetTargetLocalPosition(int boneIndex)
        {
            ValidateBoneIndex(boneIndex);
            return targetLocalPositions[boneIndex];
        }

        public float GetTargetLocalRotation(int boneIndex)
        {
            ValidateBoneIndex(boneIndex);
            return targetLocalRotations[boneIndex];
        }

        public void ResetTargetToBindPose()
        {
            for (int i = 0; i < BoneCount; i++)
            {
                targetLocalPositions[i] = rigAsset.Bones[i].LocalPosition;
                targetLocalRotations[i] = rigAsset.Bones[i].LocalRotation;
            }
        }

        public void ResetSimulation()
        {
            EvaluateTargetPose();
            for (int i = 0; i < BoneCount; i++)
            {
                Vector2 start = GetWorldStart(i);
                Vector2 direction = Direction(worldRotations[i]);
                endpoints[i] = start + direction * rigAsset.Bones[i].Length;
                previousEndpoints[i] = endpoints[i];
                previousWorldRotations[i] = worldRotations[i];
            }
        }

        public void Step(
            float deltaTime,
            Vector2 referenceFrameDelta,
            RigPhysicsParameters parameters)
        {
            if (deltaTime <= 0f || BoneCount == 0)
            {
                return;
            }

            float safeDeltaTime = Mathf.Min(deltaTime, 0.05f);
            ApplyReferenceFrameMotion(referenceFrameDelta, parameters.InertiaMultiplier);
            Integrate(safeDeltaTime, parameters);

            int iterations = Mathf.Clamp(parameters.ConstraintIterations, 1, 16);
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                SolveConstraints(safeDeltaTime, parameters.StiffnessMultiplier, iteration == 0);
            }

            for (int i = 0; i < BoneCount; i++)
            {
                previousWorldRotations[i] = worldRotations[i];
            }
        }

        public void ApplyPoseTo(CpuSpriteSkinner skinner)
        {
            if (skinner == null)
            {
                throw new ArgumentNullException(nameof(skinner));
            }

            if (skinner.BoneCount != BoneCount)
            {
                throw new ArgumentException("Skinner and physics solver bone counts do not match.", nameof(skinner));
            }

            for (int i = 0; i < BoneCount; i++)
            {
                int parentIndex = rigAsset.Bones[i].ParentIndex;
                float parentWorldRotation = parentIndex >= 0 ? worldRotations[parentIndex] : 0f;
                float localRotation = Mathf.DeltaAngle(0f, worldRotations[i] - parentWorldRotation);
                Vector2 localPosition = settings[i].SimulationMode == BoneSimulationMode.Fixed
                    ? rigAsset.Bones[i].LocalPosition
                    : targetLocalPositions[i];
                skinner.SetLocalPose(i, localPosition, localRotation);
            }

            skinner.RecalculatePoseMatrices();
        }

        public Matrix4x4 GetWorldMatrix(int boneIndex)
        {
            ValidateBoneIndex(boneIndex);
            return worldMatrices[boneIndex];
        }

        public Vector2 GetBoneStart(int boneIndex)
        {
            ValidateBoneIndex(boneIndex);
            return GetWorldStart(boneIndex);
        }

        public Vector2 GetBoneEndpoint(int boneIndex)
        {
            ValidateBoneIndex(boneIndex);
            return endpoints[boneIndex];
        }

        public Vector2 GetPreviousBoneEndpoint(int boneIndex)
        {
            ValidateBoneIndex(boneIndex);
            return previousEndpoints[boneIndex];
        }

        public float GetWorldRotation(int boneIndex)
        {
            ValidateBoneIndex(boneIndex);
            return worldRotations[boneIndex];
        }

        public void ReprojectBoneState(
            int boneIndex,
            Vector2 reprojectedEndpoint,
            Vector2 reprojectedPreviousEndpoint,
            float inertiaWeight)
        {
            ValidateBoneIndex(boneIndex);
            if (settings[boneIndex].SimulationMode != BoneSimulationMode.Dynamic)
            {
                return;
            }

            float weight = Mathf.Max(0f, inertiaWeight * settings[boneIndex].Inertia);
            endpoints[boneIndex] = Vector2.LerpUnclamped(endpoints[boneIndex], reprojectedEndpoint, weight);
            previousEndpoints[boneIndex] = Vector2.LerpUnclamped(
                previousEndpoints[boneIndex],
                reprojectedPreviousEndpoint,
                weight);
        }

        public void ApplyCollisionCorrection(
            int boneIndex,
            Vector2 correction,
            Vector2 outwardNormal,
            float friction,
            float bounciness)
        {
            ValidateBoneIndex(boneIndex);
            if (settings[boneIndex].SimulationMode != BoneSimulationMode.Dynamic)
            {
                return;
            }

            endpoints[boneIndex] += correction;
            Vector2 normal = outwardNormal.sqrMagnitude > Epsilon
                ? outwardNormal.normalized
                : Vector2.up;
            Vector2 velocity = endpoints[boneIndex] - previousEndpoints[boneIndex];
            float normalVelocity = Vector2.Dot(velocity, normal);
            Vector2 tangentialVelocity = velocity - normal * normalVelocity;
            tangentialVelocity *= 1f - Mathf.Clamp01(friction);
            float outgoingNormalVelocity = normalVelocity < 0f
                ? -normalVelocity * Mathf.Clamp01(bounciness)
                : normalVelocity;
            Vector2 correctedVelocity = tangentialVelocity + normal * outgoingNormalVelocity;
            previousEndpoints[boneIndex] = endpoints[boneIndex] - correctedVelocity;
        }

        public void ResolveConstraintsAfterCollision(int iterations)
        {
            int safeIterations = Mathf.Clamp(iterations, 1, 16);
            for (int i = 0; i < safeIterations; i++)
            {
                SolveConstraints(0f, 0f, false);
            }
        }

        void ApplyReferenceFrameMotion(Vector2 frameDelta, float globalInertia)
        {
            if (frameDelta.sqrMagnitude <= Epsilon)
            {
                return;
            }

            for (int i = 0; i < BoneCount; i++)
            {
                BonePhysicsSettings boneSettings = settings[i];
                if (boneSettings.SimulationMode != BoneSimulationMode.Dynamic)
                {
                    continue;
                }

                Vector2 compensation = frameDelta
                    * Mathf.Max(0f, boneSettings.Inertia)
                    * Mathf.Max(0f, globalInertia);
                endpoints[i] -= compensation;
                previousEndpoints[i] -= compensation;
            }
        }

        void Integrate(float deltaTime, RigPhysicsParameters parameters)
        {
            float deltaTimeSquared = deltaTime * deltaTime;
            for (int i = 0; i < BoneCount; i++)
            {
                BonePhysicsSettings boneSettings = settings[i];
                if (boneSettings.SimulationMode != BoneSimulationMode.Dynamic)
                {
                    continue;
                }

                float damping = Mathf.Clamp01(boneSettings.Damping * Mathf.Max(0f, parameters.DampingMultiplier));
                Vector2 velocity = (endpoints[i] - previousEndpoints[i]) * damping;
                float inverseMass = 1f / Mathf.Max(0.0001f, boneSettings.Mass);
                Vector2 acceleration =
                    parameters.Gravity * boneSettings.GravityScale
                    + parameters.ExternalForce * boneSettings.ExternalForceMultiplier;
                acceleration *= inverseMass;

                previousEndpoints[i] = endpoints[i];
                endpoints[i] += velocity + acceleration * deltaTimeSquared;
            }
        }

        void SolveConstraints(float deltaTime, float globalStiffness, bool applyRestAndSpeed)
        {
            for (int orderIndex = 0; orderIndex < evaluationOrder.Length; orderIndex++)
            {
                int boneIndex = evaluationOrder[orderIndex];
                RigBone bone = rigAsset.Bones[boneIndex];
                BonePhysicsSettings boneSettings = settings[boneIndex];
                int parentIndex = bone.ParentIndex;
                Matrix4x4 parentMatrix = parentIndex >= 0 ? worldMatrices[parentIndex] : Matrix4x4.identity;
                float parentRotation = parentIndex >= 0 ? worldRotations[parentIndex] : 0f;
                Vector2 targetLocalPosition = boneSettings.SimulationMode == BoneSimulationMode.Fixed
                    ? bone.LocalPosition
                    : targetLocalPositions[boneIndex];
                float targetLocalRotation = boneSettings.SimulationMode == BoneSimulationMode.Fixed
                    ? bone.LocalRotation
                    : targetLocalRotations[boneIndex];
                Vector2 start = parentMatrix.MultiplyPoint3x4(targetLocalPosition);
                float targetWorldRotation = parentRotation + targetLocalRotation;

                if (boneSettings.SimulationMode != BoneSimulationMode.Dynamic)
                {
                    worldRotations[boneIndex] = targetWorldRotation;
                    endpoints[boneIndex] = start + Direction(targetWorldRotation) * bone.Length;
                    previousEndpoints[boneIndex] = endpoints[boneIndex];
                    worldMatrices[boneIndex] = CreateWorldMatrix(start, targetWorldRotation);
                    continue;
                }

                Vector2 endpoint = endpoints[boneIndex];
                if (applyRestAndSpeed)
                {
                    float stiffness = Mathf.Max(0f, boneSettings.Stiffness * globalStiffness);
                    float restBlend = 1f - Mathf.Exp(-stiffness * deltaTime);
                    Vector2 restEndpoint = start + Direction(targetWorldRotation) * bone.Length;
                    endpoint = Vector2.LerpUnclamped(endpoint, restEndpoint, restBlend);
                }

                Vector2 delta = endpoint - start;
                float currentRotation = delta.sqrMagnitude > Epsilon
                    ? Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg
                    : targetWorldRotation;
                float relativeAngle = Mathf.DeltaAngle(targetWorldRotation, currentRotation);
                relativeAngle = Mathf.Clamp(relativeAngle, boneSettings.MinimumAngle, boneSettings.MaximumAngle);
                currentRotation = targetWorldRotation + relativeAngle;

                if (applyRestAndSpeed && boneSettings.MaximumAngularSpeed > 0f)
                {
                    float maximumStep = boneSettings.MaximumAngularSpeed * deltaTime;
                    currentRotation = Mathf.MoveTowardsAngle(
                        previousWorldRotations[boneIndex],
                        currentRotation,
                        maximumStep);
                }

                endpoint = start + Direction(currentRotation) * bone.Length;
                endpoints[boneIndex] = endpoint;
                worldRotations[boneIndex] = currentRotation;
                worldMatrices[boneIndex] = CreateWorldMatrix(start, currentRotation);
            }
        }

        void EvaluateTargetPose()
        {
            for (int orderIndex = 0; orderIndex < evaluationOrder.Length; orderIndex++)
            {
                int boneIndex = evaluationOrder[orderIndex];
                int parentIndex = rigAsset.Bones[boneIndex].ParentIndex;
                Matrix4x4 parentMatrix = parentIndex >= 0 ? worldMatrices[parentIndex] : Matrix4x4.identity;
                float parentRotation = parentIndex >= 0 ? worldRotations[parentIndex] : 0f;
                bool fixedBone = settings[boneIndex].SimulationMode == BoneSimulationMode.Fixed;
                Vector2 localPosition = fixedBone
                    ? rigAsset.Bones[boneIndex].LocalPosition
                    : targetLocalPositions[boneIndex];
                float localRotation = fixedBone
                    ? rigAsset.Bones[boneIndex].LocalRotation
                    : targetLocalRotations[boneIndex];
                Vector2 start = parentMatrix.MultiplyPoint3x4(localPosition);
                float rotation = parentRotation + localRotation;
                worldRotations[boneIndex] = rotation;
                worldMatrices[boneIndex] = CreateWorldMatrix(start, rotation);
            }
        }

        Vector2 GetWorldStart(int boneIndex)
        {
            return new Vector2(worldMatrices[boneIndex].m03, worldMatrices[boneIndex].m13);
        }

        int[] BuildEvaluationOrder()
        {
            int[] result = new int[rigAsset.Bones.Count];
            byte[] state = new byte[rigAsset.Bones.Count];
            int writeIndex = 0;

            for (int i = 0; i < rigAsset.Bones.Count; i++)
            {
                Visit(i, state, result, ref writeIndex);
            }

            return result;
        }

        void Visit(int boneIndex, byte[] state, int[] result, ref int writeIndex)
        {
            if (state[boneIndex] == 2)
            {
                return;
            }

            if (state[boneIndex] == 1)
            {
                throw new InvalidOperationException("Cannot simulate a cyclic bone hierarchy.");
            }

            state[boneIndex] = 1;
            int parentIndex = rigAsset.Bones[boneIndex].ParentIndex;
            if (parentIndex >= 0)
            {
                Visit(parentIndex, state, result, ref writeIndex);
            }

            state[boneIndex] = 2;
            result[writeIndex++] = boneIndex;
        }

        static Matrix4x4 CreateWorldMatrix(Vector2 position, float rotation)
        {
            return Matrix4x4.TRS(
                position,
                Quaternion.Euler(0f, 0f, rotation),
                Vector3.one);
        }

        static Vector2 Direction(float angle)
        {
            float radians = angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        static BonePhysicsSettings Sanitize(BonePhysicsSettings value)
        {
            value.Mass = Mathf.Max(0.0001f, value.Mass);
            value.Stiffness = Mathf.Max(0f, value.Stiffness);
            value.Damping = Mathf.Clamp01(value.Damping);
            value.Inertia = Mathf.Max(0f, value.Inertia);
            if (value.MinimumAngle > value.MaximumAngle)
            {
                float swap = value.MinimumAngle;
                value.MinimumAngle = value.MaximumAngle;
                value.MaximumAngle = swap;
            }

            value.MaximumAngularSpeed = Mathf.Max(0f, value.MaximumAngularSpeed);
            value.CollisionRadius = Mathf.Max(0f, value.CollisionRadius);
            value.ExternalForceMultiplier = Mathf.Max(0f, value.ExternalForceMultiplier);
            return value;
        }

        void ValidateBoneIndex(int boneIndex)
        {
            if (boneIndex < 0 || boneIndex >= BoneCount)
            {
                throw new ArgumentOutOfRangeException(nameof(boneIndex));
            }
        }
    }
}
