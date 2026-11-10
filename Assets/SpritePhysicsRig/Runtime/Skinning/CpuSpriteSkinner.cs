using System;
using UnityEngine;

namespace ShizukuCity.SpritePhysicsRig
{
    public sealed class CpuSpriteSkinner
    {
        readonly SpriteRigAsset rigAsset;
        readonly Vector2[] localPositions;
        readonly float[] localRotations;
        readonly Matrix4x4[] bindWorldMatrices;
        readonly Matrix4x4[] inverseBindMatrices;
        readonly Matrix4x4[] poseWorldMatrices;
        readonly Matrix4x4[] skinningMatrices;
        readonly byte[] traversalState;

        public int BoneCount => localPositions.Length;
        public int VertexCount => rigAsset.Vertices.Count;

        public CpuSpriteSkinner(SpriteRigAsset rigAsset)
        {
            this.rigAsset = rigAsset != null ? rigAsset : throw new ArgumentNullException(nameof(rigAsset));

            int boneCount = rigAsset.Bones.Count;
            localPositions = new Vector2[boneCount];
            localRotations = new float[boneCount];
            bindWorldMatrices = new Matrix4x4[boneCount];
            inverseBindMatrices = new Matrix4x4[boneCount];
            poseWorldMatrices = new Matrix4x4[boneCount];
            skinningMatrices = new Matrix4x4[boneCount];
            traversalState = new byte[boneCount];

            ResetToBindPose();
            CalculateWorldMatrices(bindWorldMatrices);
            for (int i = 0; i < boneCount; i++)
            {
                inverseBindMatrices[i] = bindWorldMatrices[i].inverse;
            }

            RecalculatePoseMatrices();
        }

        public void ResetToBindPose()
        {
            for (int i = 0; i < rigAsset.Bones.Count; i++)
            {
                RigBone bone = rigAsset.Bones[i];
                localPositions[i] = bone.LocalPosition;
                localRotations[i] = bone.LocalRotation;
            }
        }

        public void SetLocalPose(int boneIndex, Vector2 localPosition, float localRotation)
        {
            ValidateBoneIndex(boneIndex);
            localPositions[boneIndex] = localPosition;
            localRotations[boneIndex] = localRotation;
        }

        public void SetLocalPoseOffset(int boneIndex, Vector2 positionOffset, float rotationOffset)
        {
            ValidateBoneIndex(boneIndex);
            RigBone bindBone = rigAsset.Bones[boneIndex];
            localPositions[boneIndex] = bindBone.LocalPosition + positionOffset;
            localRotations[boneIndex] = bindBone.LocalRotation + rotationOffset;
        }

        public Vector2 GetLocalPosition(int boneIndex)
        {
            ValidateBoneIndex(boneIndex);
            return localPositions[boneIndex];
        }

        public float GetLocalRotation(int boneIndex)
        {
            ValidateBoneIndex(boneIndex);
            return localRotations[boneIndex];
        }

        public Matrix4x4 GetPoseWorldMatrix(int boneIndex)
        {
            ValidateBoneIndex(boneIndex);
            return poseWorldMatrices[boneIndex];
        }

        public void RecalculatePoseMatrices()
        {
            CalculateWorldMatrices(poseWorldMatrices);
            for (int i = 0; i < skinningMatrices.Length; i++)
            {
                skinningMatrices[i] = poseWorldMatrices[i] * inverseBindMatrices[i];
            }
        }

        public void Deform(Vector2[] outputRigPositions)
        {
            if (outputRigPositions == null || outputRigPositions.Length != VertexCount)
            {
                throw new ArgumentException("Output buffer must match the rig vertex count.", nameof(outputRigPositions));
            }

            for (int vertexIndex = 0; vertexIndex < VertexCount; vertexIndex++)
            {
                Vector3 bindPosition = rigAsset.Vertices[vertexIndex].Position;
                VertexWeight4 vertexWeight = rigAsset.Weights[vertexIndex];
                Vector3 deformed = Vector3.zero;

                for (int influence = 0; influence < SpriteRigAsset.MaximumInfluenceCount; influence++)
                {
                    float weight = vertexWeight.GetWeight(influence);
                    if (weight <= 0f)
                    {
                        continue;
                    }

                    int boneIndex = vertexWeight.GetBoneIndex(influence);
                    deformed += skinningMatrices[boneIndex].MultiplyPoint3x4(bindPosition) * weight;
                }

                outputRigPositions[vertexIndex] = new Vector2(deformed.x, deformed.y);
            }
        }

        void CalculateWorldMatrices(Matrix4x4[] output)
        {
            Array.Clear(traversalState, 0, traversalState.Length);
            for (int i = 0; i < output.Length; i++)
            {
                CalculateBoneWorldMatrix(i, output);
            }
        }

        Matrix4x4 CalculateBoneWorldMatrix(int boneIndex, Matrix4x4[] output)
        {
            if (traversalState[boneIndex] == 2)
            {
                return output[boneIndex];
            }

            if (traversalState[boneIndex] == 1)
            {
                throw new InvalidOperationException("Cannot calculate matrices for a cyclic bone hierarchy.");
            }

            traversalState[boneIndex] = 1;
            Matrix4x4 localMatrix = Matrix4x4.TRS(
                localPositions[boneIndex],
                Quaternion.Euler(0f, 0f, localRotations[boneIndex]),
                Vector3.one);

            int parentIndex = rigAsset.Bones[boneIndex].ParentIndex;
            output[boneIndex] = parentIndex >= 0
                ? CalculateBoneWorldMatrix(parentIndex, output) * localMatrix
                : localMatrix;

            traversalState[boneIndex] = 2;
            return output[boneIndex];
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
