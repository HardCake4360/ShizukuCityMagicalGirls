using System;
using UnityEngine;

namespace ShizukuCity.SpritePhysicsRig
{
    public enum BoneSimulationMode
    {
        Fixed,
        Animated,
        Dynamic
    }

    [Serializable]
    public struct RigVertex
    {
        [SerializeField] Vector2 position;
        [SerializeField] bool hasCustomUv;
        [SerializeField] Vector2 uv;

        public Vector2 Position
        {
            get => position;
            set => position = value;
        }

        public bool HasCustomUv
        {
            get => hasCustomUv;
            set => hasCustomUv = value;
        }

        public Vector2 Uv
        {
            get => hasCustomUv ? uv : position;
            set
            {
                uv = value;
                hasCustomUv = true;
            }
        }

        public RigVertex(Vector2 position)
        {
            this.position = position;
            hasCustomUv = false;
            uv = position;
        }
    }

    [Serializable]
    public struct RigEdge
    {
        [SerializeField] int vertexA;
        [SerializeField] int vertexB;
        [SerializeField] bool boundary;

        public int VertexA
        {
            get => vertexA;
            set => vertexA = value;
        }

        public int VertexB
        {
            get => vertexB;
            set => vertexB = value;
        }

        public bool Boundary
        {
            get => boundary;
            set => boundary = value;
        }

        public RigEdge(int vertexA, int vertexB, bool boundary = true)
        {
            this.vertexA = vertexA;
            this.vertexB = vertexB;
            this.boundary = boundary;
        }
    }

    [Serializable]
    public struct BonePhysicsSettings
    {
        [SerializeField] BoneSimulationMode simulationMode;
        [SerializeField, Min(0.0001f)] float mass;
        [SerializeField] float gravityScale;
        [SerializeField, Min(0f)] float stiffness;
        [SerializeField, Range(0f, 1f)] float damping;
        [SerializeField, Min(0f)] float inertia;
        [SerializeField] float minimumAngle;
        [SerializeField] float maximumAngle;
        [SerializeField, Min(0f)] float maximumAngularSpeed;
        [SerializeField, Min(0f)] float collisionRadius;
        [SerializeField, Min(0f)] float externalForceMultiplier;

        public BoneSimulationMode SimulationMode { get => simulationMode; set => simulationMode = value; }
        public float Mass { get => mass; set => mass = value; }
        public float GravityScale { get => gravityScale; set => gravityScale = value; }
        public float Stiffness { get => stiffness; set => stiffness = value; }
        public float Damping { get => damping; set => damping = value; }
        public float Inertia { get => inertia; set => inertia = value; }
        public float MinimumAngle { get => minimumAngle; set => minimumAngle = value; }
        public float MaximumAngle { get => maximumAngle; set => maximumAngle = value; }
        public float MaximumAngularSpeed { get => maximumAngularSpeed; set => maximumAngularSpeed = value; }
        public float CollisionRadius { get => collisionRadius; set => collisionRadius = value; }
        public float ExternalForceMultiplier { get => externalForceMultiplier; set => externalForceMultiplier = value; }

        public static BonePhysicsSettings Default => new BonePhysicsSettings
        {
            simulationMode = BoneSimulationMode.Dynamic,
            mass = 1f,
            gravityScale = 1f,
            stiffness = 8f,
            damping = 0.86f,
            inertia = 0.75f,
            minimumAngle = -180f,
            maximumAngle = 180f,
            maximumAngularSpeed = 720f,
            collisionRadius = 0f,
            externalForceMultiplier = 1f
        };
    }

    [Serializable]
    public struct RigBone
    {
        [SerializeField, HideInInspector] string stableId;
        [SerializeField] string name;
        [SerializeField] int parentIndex;
        [SerializeField] Vector2 localPosition;
        [SerializeField] float localRotation;
        [SerializeField, Min(0.0001f)] float length;
        [SerializeField] BonePhysicsSettings physics;

        public string StableId => stableId;
        public string Name { get => name; set => name = value; }
        public int ParentIndex { get => parentIndex; set => parentIndex = value; }
        public Vector2 LocalPosition { get => localPosition; set => localPosition = value; }
        public float LocalRotation { get => localRotation; set => localRotation = value; }
        public float Length { get => length; set => length = value; }
        public BonePhysicsSettings Physics { get => physics; set => physics = value; }

        public RigBone(
            string name,
            int parentIndex,
            Vector2 localPosition,
            float localRotation,
            float length)
        {
            stableId = Guid.NewGuid().ToString("N");
            this.name = name;
            this.parentIndex = parentIndex;
            this.localPosition = localPosition;
            this.localRotation = localRotation;
            this.length = length;
            physics = BonePhysicsSettings.Default;
        }

        internal void AssignNewStableId()
        {
            stableId = Guid.NewGuid().ToString("N");
        }
    }

    [Serializable]
    public struct VertexWeight4
    {
        [SerializeField] int boneIndex0;
        [SerializeField] int boneIndex1;
        [SerializeField] int boneIndex2;
        [SerializeField] int boneIndex3;
        [SerializeField] float weight0;
        [SerializeField] float weight1;
        [SerializeField] float weight2;
        [SerializeField] float weight3;

        public int BoneIndex0 { get => boneIndex0; set => boneIndex0 = value; }
        public int BoneIndex1 { get => boneIndex1; set => boneIndex1 = value; }
        public int BoneIndex2 { get => boneIndex2; set => boneIndex2 = value; }
        public int BoneIndex3 { get => boneIndex3; set => boneIndex3 = value; }
        public float Weight0 { get => weight0; set => weight0 = value; }
        public float Weight1 { get => weight1; set => weight1 = value; }
        public float Weight2 { get => weight2; set => weight2 = value; }
        public float Weight3 { get => weight3; set => weight3 = value; }

        public int GetBoneIndex(int influence)
        {
            switch (influence)
            {
                case 0: return boneIndex0;
                case 1: return boneIndex1;
                case 2: return boneIndex2;
                case 3: return boneIndex3;
                default: throw new ArgumentOutOfRangeException(nameof(influence));
            }
        }

        public float GetWeight(int influence)
        {
            switch (influence)
            {
                case 0: return weight0;
                case 1: return weight1;
                case 2: return weight2;
                case 3: return weight3;
                default: throw new ArgumentOutOfRangeException(nameof(influence));
            }
        }

        public void SetInfluence(int influence, int boneIndex, float weight)
        {
            switch (influence)
            {
                case 0: boneIndex0 = boneIndex; weight0 = weight; break;
                case 1: boneIndex1 = boneIndex; weight1 = weight; break;
                case 2: boneIndex2 = boneIndex; weight2 = weight; break;
                case 3: boneIndex3 = boneIndex; weight3 = weight; break;
                default: throw new ArgumentOutOfRangeException(nameof(influence));
            }
        }
    }
}
