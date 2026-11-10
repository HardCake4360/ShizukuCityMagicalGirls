using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShizukuCity.SpritePhysicsRig
{
    [CreateAssetMenu(fileName = "SpriteRigAsset", menuName = "2D Rig/Sprite Rig Asset")]
    public sealed class SpriteRigAsset : ScriptableObject
    {
        public const int CurrentDataVersion = 1;
        public const int MaximumBoneCount = 64;
        public const int MaximumInfluenceCount = 4;

        const float Epsilon = 0.00001f;
        const float WeightTolerance = 0.001f;

        [SerializeField, HideInInspector] int dataVersion = CurrentDataVersion;
        [SerializeField] List<RigVertex> vertices = new List<RigVertex>();
        [SerializeField] List<RigEdge> edges = new List<RigEdge>();
        [SerializeField] List<int> triangles = new List<int>();
        [SerializeField] List<RigBone> bones = new List<RigBone>();
        [SerializeField] List<VertexWeight4> weights = new List<VertexWeight4>();

        public int DataVersion => dataVersion;
        public IReadOnlyList<RigVertex> Vertices => vertices;
        public IReadOnlyList<RigEdge> Edges => edges;
        public IReadOnlyList<int> Triangles => triangles;
        public IReadOnlyList<RigBone> Bones => bones;
        public IReadOnlyList<VertexWeight4> Weights => weights;

        public bool Validate(List<RigValidationIssue> issues)
        {
            if (issues == null)
            {
                throw new ArgumentNullException(nameof(issues));
            }

            issues.Clear();
            ValidateVersion(issues);
            ValidateGeometry(issues);
            ValidateBones(issues);
            ValidateWeights(issues);

            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Severity == RigValidationSeverity.Error)
                {
                    return false;
                }
            }

            return true;
        }

        public bool HasValidRuntimeData()
        {
            return Validate(new List<RigValidationIssue>());
        }

        [ContextMenu("Initialize Default Quad")]
        public void InitializeDefaultQuad()
        {
            if (vertices.Count > 0 || edges.Count > 0 || triangles.Count > 0 || bones.Count > 0 || weights.Count > 0)
            {
                Debug.LogWarning($"'{name}' already contains rig data. Default Quad initialization was cancelled.", this);
                return;
            }

            vertices.Add(new RigVertex(new Vector2(0f, 0f)));
            vertices.Add(new RigVertex(new Vector2(1f, 0f)));
            vertices.Add(new RigVertex(new Vector2(1f, 1f)));
            vertices.Add(new RigVertex(new Vector2(0f, 1f)));

            edges.Add(new RigEdge(0, 1));
            edges.Add(new RigEdge(1, 2));
            edges.Add(new RigEdge(2, 3));
            edges.Add(new RigEdge(3, 0));

            triangles.Add(0);
            triangles.Add(1);
            triangles.Add(2);
            triangles.Add(0);
            triangles.Add(2);
            triangles.Add(3);

            bones.Add(new RigBone("Root", -1, new Vector2(0.5f, 0.5f), 0f, 0.5f));
            for (int i = 0; i < vertices.Count; i++)
            {
                VertexWeight4 weight = default;
                weight.SetInfluence(0, 0, 1f);
                weights.Add(weight);
            }
        }

        public int EnsureStableBoneIds()
        {
            HashSet<string> usedIds = new HashSet<string>(StringComparer.Ordinal);
            int changedCount = 0;

            for (int i = 0; i < bones.Count; i++)
            {
                RigBone bone = bones[i];
                if (string.IsNullOrWhiteSpace(bone.StableId) || !usedIds.Add(bone.StableId))
                {
                    do
                    {
                        bone.AssignNewStableId();
                    }
                    while (!usedIds.Add(bone.StableId));

                    bones[i] = bone;
                    changedCount++;
                }
            }

            return changedCount;
        }

        [ContextMenu("Repair Stable Bone IDs")]
        void RepairStableBoneIds()
        {
            int changedCount = EnsureStableBoneIds();
            Debug.Log($"Repaired {changedCount} bone ID(s) in '{name}'.", this);
        }

        public bool NormalizeVertexWeights(int vertexIndex)
        {
            if (vertexIndex < 0 || vertexIndex >= weights.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexIndex));
            }

            VertexWeight4 source = weights[vertexIndex];
            List<Influence> merged = new List<Influence>(MaximumInfluenceCount);

            for (int i = 0; i < MaximumInfluenceCount; i++)
            {
                float weight = source.GetWeight(i);
                int boneIndex = source.GetBoneIndex(i);
                if (!IsFinite(weight) || weight <= 0f || boneIndex < 0 || boneIndex >= bones.Count)
                {
                    continue;
                }

                int existingIndex = -1;
                for (int j = 0; j < merged.Count; j++)
                {
                    if (merged[j].BoneIndex == boneIndex)
                    {
                        existingIndex = j;
                        break;
                    }
                }

                if (existingIndex >= 0)
                {
                    Influence influence = merged[existingIndex];
                    influence.Weight += weight;
                    merged[existingIndex] = influence;
                }
                else
                {
                    merged.Add(new Influence(boneIndex, weight));
                }
            }

            if (merged.Count == 0)
            {
                return false;
            }

            merged.Sort((left, right) => right.Weight.CompareTo(left.Weight));
            float sum = 0f;
            for (int i = 0; i < merged.Count; i++)
            {
                sum += merged[i].Weight;
            }

            VertexWeight4 normalized = default;
            for (int i = 0; i < MaximumInfluenceCount; i++)
            {
                normalized.SetInfluence(i, i < merged.Count ? merged[i].BoneIndex : 0, i < merged.Count ? merged[i].Weight / sum : 0f);
            }

            weights[vertexIndex] = normalized;
            return true;
        }

        public int NormalizeAllVertexWeights()
        {
            int normalizedCount = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                if (NormalizeVertexWeights(i))
                {
                    normalizedCount++;
                }
            }

            return normalizedCount;
        }

        [ContextMenu("Normalize All Vertex Weights")]
        void NormalizeAllWeightsFromContextMenu()
        {
            int normalizedCount = NormalizeAllVertexWeights();
            Debug.Log($"Normalized {normalizedCount} vertex weight(s) in '{name}'.", this);
        }

        void ValidateVersion(List<RigValidationIssue> issues)
        {
            if (dataVersion != CurrentDataVersion)
            {
                AddError(issues, RigValidationCode.UnsupportedDataVersion,
                    $"Data version {dataVersion} is not supported. Expected {CurrentDataVersion}.");
            }
        }

        void ValidateGeometry(List<RigValidationIssue> issues)
        {
            if (vertices.Count < 3)
            {
                AddError(issues, RigValidationCode.TooFewVertices, "At least three vertices are required.");
            }

            for (int i = 0; i < vertices.Count; i++)
            {
                RigVertex vertex = vertices[i];
                if (!IsFinite(vertex.Position))
                {
                    AddError(issues, RigValidationCode.NonFiniteVertex, "Vertex position must be finite.", i);
                }

                Vector2 uv = vertex.Uv;
                if (!IsFinite(uv) || uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
                {
                    AddError(issues, RigValidationCode.InvalidUv, "UV must be finite and inside [0, 1].", i);
                }
            }

            HashSet<ulong> uniqueEdges = new HashSet<ulong>();
            List<List<int>> adjacency = new List<List<int>>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                adjacency.Add(new List<int>());
            }

            for (int i = 0; i < edges.Count; i++)
            {
                RigEdge edge = edges[i];
                if (!IsValidIndex(edge.VertexA, vertices.Count) || !IsValidIndex(edge.VertexB, vertices.Count))
                {
                    AddError(issues, RigValidationCode.InvalidEdgeIndex, "Edge refers to a missing vertex.", i);
                    continue;
                }

                if (edge.VertexA == edge.VertexB)
                {
                    AddError(issues, RigValidationCode.SelfEdge, "An edge cannot connect a vertex to itself.", i);
                    continue;
                }

                int minimum = Mathf.Min(edge.VertexA, edge.VertexB);
                int maximum = Mathf.Max(edge.VertexA, edge.VertexB);
                ulong key = ((ulong)(uint)minimum << 32) | (uint)maximum;
                if (!uniqueEdges.Add(key))
                {
                    AddError(issues, RigValidationCode.DuplicateEdge, "Duplicate edge found.", i);
                }

                adjacency[edge.VertexA].Add(edge.VertexB);
                adjacency[edge.VertexB].Add(edge.VertexA);
            }

            ValidateConnectedGeometry(adjacency, issues);

            if (triangles.Count % 3 != 0)
            {
                AddError(issues, RigValidationCode.TriangleIndexCount, "Triangle index count must be divisible by three.");
            }

            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                int triangleIndex = i / 3;
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                if (!IsValidIndex(a, vertices.Count) || !IsValidIndex(b, vertices.Count) || !IsValidIndex(c, vertices.Count))
                {
                    AddError(issues, RigValidationCode.InvalidTriangleIndex, "Triangle refers to a missing vertex.", triangleIndex);
                    continue;
                }

                if (a == b || b == c || c == a)
                {
                    AddError(issues, RigValidationCode.DegenerateTriangle, "Triangle contains a repeated vertex.", triangleIndex);
                    continue;
                }

                float signedDoubleArea = Cross(vertices[b].Position - vertices[a].Position, vertices[c].Position - vertices[a].Position);
                if (Mathf.Abs(signedDoubleArea) <= Epsilon)
                {
                    AddError(issues, RigValidationCode.DegenerateTriangle, "Triangle has no area.", triangleIndex);
                }
                else if (signedDoubleArea < 0f)
                {
                    AddError(issues, RigValidationCode.ClockwiseTriangle, "Triangle winding must be counter-clockwise.", triangleIndex);
                }
            }
        }

        void ValidateConnectedGeometry(List<List<int>> adjacency, List<RigValidationIssue> issues)
        {
            if (vertices.Count == 0)
            {
                return;
            }

            bool[] visited = new bool[vertices.Count];
            Queue<int> pending = new Queue<int>();
            pending.Enqueue(0);
            visited[0] = true;

            while (pending.Count > 0)
            {
                int vertexIndex = pending.Dequeue();
                for (int i = 0; i < adjacency[vertexIndex].Count; i++)
                {
                    int neighbor = adjacency[vertexIndex][i];
                    if (!visited[neighbor])
                    {
                        visited[neighbor] = true;
                        pending.Enqueue(neighbor);
                    }
                }
            }

            for (int i = 0; i < visited.Length; i++)
            {
                if (!visited[i])
                {
                    AddError(issues, RigValidationCode.DisconnectedGeometry,
                        "MVP geometry must be a single connected polygon.", i);
                }
            }
        }

        void ValidateBones(List<RigValidationIssue> issues)
        {
            if (bones.Count == 0)
            {
                AddError(issues, RigValidationCode.MissingBone, "At least one bone is required.");
                return;
            }

            if (bones.Count > MaximumBoneCount)
            {
                AddError(issues, RigValidationCode.TooManyBones,
                    $"A rig supports at most {MaximumBoneCount} bones.");
            }

            HashSet<string> stableIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < bones.Count; i++)
            {
                RigBone bone = bones[i];
                if (string.IsNullOrWhiteSpace(bone.StableId))
                {
                    AddError(issues, RigValidationCode.MissingBoneId, "Bone has no stable ID.", i);
                }
                else if (!stableIds.Add(bone.StableId))
                {
                    AddError(issues, RigValidationCode.DuplicateBoneId, "Bone stable ID is duplicated.", i);
                }

                if (bone.ParentIndex < -1 || bone.ParentIndex >= bones.Count || bone.ParentIndex == i)
                {
                    AddError(issues, RigValidationCode.InvalidBoneParent, "Bone parent index is invalid.", i);
                }

                if (!IsFinite(bone.LocalPosition) || !IsFinite(bone.LocalRotation))
                {
                    AddError(issues, RigValidationCode.InvalidBoneTransform, "Bone bind transform must be finite.", i);
                }

                if (!IsFinite(bone.Length) || bone.Length <= Epsilon)
                {
                    AddError(issues, RigValidationCode.InvalidBoneLength, "Bone length must be finite and greater than zero.", i);
                }

                if (!IsValidPhysics(bone.Physics))
                {
                    AddError(issues, RigValidationCode.InvalidBonePhysics, "Bone physics settings are outside the supported range.", i);
                }
            }

            ValidateBoneCycles(issues);
        }

        void ValidateBoneCycles(List<RigValidationIssue> issues)
        {
            byte[] state = new byte[bones.Count];
            for (int i = 0; i < bones.Count; i++)
            {
                int current = i;
                while (current >= 0 && current < bones.Count)
                {
                    if (state[current] == 1)
                    {
                        AddError(issues, RigValidationCode.CyclicBoneHierarchy, "Bone hierarchy contains a cycle.", current);
                        break;
                    }

                    if (state[current] == 2)
                    {
                        break;
                    }

                    state[current] = 1;
                    current = bones[current].ParentIndex;
                }

                current = i;
                while (current >= 0 && current < bones.Count && state[current] == 1)
                {
                    state[current] = 2;
                    current = bones[current].ParentIndex;
                }
            }
        }

        void ValidateWeights(List<RigValidationIssue> issues)
        {
            if (weights.Count != vertices.Count)
            {
                AddError(issues, RigValidationCode.MissingWeights,
                    $"Weight count ({weights.Count}) must match vertex count ({vertices.Count}).");
            }

            int count = Mathf.Min(weights.Count, vertices.Count);
            for (int vertexIndex = 0; vertexIndex < count; vertexIndex++)
            {
                VertexWeight4 vertexWeight = weights[vertexIndex];
                float sum = 0f;
                HashSet<int> influencedBones = new HashSet<int>();

                for (int influence = 0; influence < MaximumInfluenceCount; influence++)
                {
                    float weight = vertexWeight.GetWeight(influence);
                    int boneIndex = vertexWeight.GetBoneIndex(influence);
                    if (!IsFinite(weight) || weight < 0f || weight > 1f)
                    {
                        AddError(issues, RigValidationCode.InvalidWeight, "Weight must be finite and inside [0, 1].", vertexIndex);
                        continue;
                    }

                    if (weight <= 0f)
                    {
                        continue;
                    }

                    if (!IsValidIndex(boneIndex, bones.Count))
                    {
                        AddError(issues, RigValidationCode.InvalidWeightBoneIndex, "Weight refers to a missing bone.", vertexIndex);
                    }
                    else if (!influencedBones.Add(boneIndex))
                    {
                        AddError(issues, RigValidationCode.DuplicateWeightBone, "Repeated bone influences must be merged.", vertexIndex);
                    }

                    sum += weight;
                }

                if (Mathf.Abs(sum - 1f) > WeightTolerance)
                {
                    AddError(issues, RigValidationCode.WeightSum, "Active vertex weights must sum to one.", vertexIndex);
                }
            }
        }

        static bool IsValidPhysics(BonePhysicsSettings physics)
        {
            return IsFinite(physics.Mass) && physics.Mass > 0f
                && IsFinite(physics.GravityScale)
                && IsFinite(physics.Stiffness) && physics.Stiffness >= 0f
                && IsFinite(physics.Damping) && physics.Damping >= 0f && physics.Damping <= 1f
                && IsFinite(physics.Inertia) && physics.Inertia >= 0f
                && IsFinite(physics.MinimumAngle)
                && IsFinite(physics.MaximumAngle)
                && physics.MinimumAngle <= physics.MaximumAngle
                && IsFinite(physics.MaximumAngularSpeed) && physics.MaximumAngularSpeed >= 0f
                && IsFinite(physics.CollisionRadius) && physics.CollisionRadius >= 0f
                && IsFinite(physics.ExternalForceMultiplier) && physics.ExternalForceMultiplier >= 0f;
        }

        static float Cross(Vector2 left, Vector2 right)
        {
            return left.x * right.y - left.y * right.x;
        }

        static bool IsValidIndex(int index, int count)
        {
            return index >= 0 && index < count;
        }

        static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static void AddError(
            List<RigValidationIssue> issues,
            RigValidationCode code,
            string message,
            int elementIndex = -1)
        {
            issues.Add(new RigValidationIssue(RigValidationSeverity.Error, code, message, elementIndex));
        }

        struct Influence
        {
            public int BoneIndex;
            public float Weight;

            public Influence(int boneIndex, float weight)
            {
                BoneIndex = boneIndex;
                Weight = weight;
            }
        }
    }
}
