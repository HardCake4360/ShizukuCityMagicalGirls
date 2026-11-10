using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ShizukuCity.SpritePhysicsRig.Editor
{
    public static class SpriteRigEditorUtility
    {
        const float DistanceEpsilon = 0.0001f;

        public static void GenerateGrid(SpriteRigAsset asset, int columns, int rows)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            columns = Mathf.Clamp(columns, 1, 32);
            rows = Mathf.Clamp(rows, 1, 32);
            Undo.RecordObject(asset, "Generate Rig Grid");

            SerializedObject serializedAsset = new SerializedObject(asset);
            SerializedProperty vertices = serializedAsset.FindProperty("vertices");
            SerializedProperty edges = serializedAsset.FindProperty("edges");
            SerializedProperty triangles = serializedAsset.FindProperty("triangles");

            int vertexColumns = columns + 1;
            int vertexRows = rows + 1;
            vertices.arraySize = vertexColumns * vertexRows;
            for (int y = 0; y < vertexRows; y++)
            {
                for (int x = 0; x < vertexColumns; x++)
                {
                    SetVertex(
                        vertices.GetArrayElementAtIndex(y * vertexColumns + x),
                        new Vector2(x / (float)columns, y / (float)rows));
                }
            }

            List<EdgeData> generatedEdges = new List<EdgeData>();
            for (int y = 0; y < vertexRows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    bool boundary = y == 0 || y == rows;
                    generatedEdges.Add(new EdgeData(
                        y * vertexColumns + x,
                        y * vertexColumns + x + 1,
                        boundary));
                }
            }

            for (int x = 0; x < vertexColumns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    bool boundary = x == 0 || x == columns;
                    generatedEdges.Add(new EdgeData(
                        y * vertexColumns + x,
                        (y + 1) * vertexColumns + x,
                        boundary));
                }
            }

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int bottomLeft = y * vertexColumns + x;
                    int topRight = bottomLeft + vertexColumns + 1;
                    generatedEdges.Add(new EdgeData(bottomLeft, topRight, false));
                }
            }

            edges.arraySize = generatedEdges.Count;
            for (int i = 0; i < generatedEdges.Count; i++)
            {
                SetEdge(edges.GetArrayElementAtIndex(i), generatedEdges[i]);
            }

            triangles.arraySize = columns * rows * 6;
            int triangleWriteIndex = 0;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int bottomLeft = y * vertexColumns + x;
                    int bottomRight = bottomLeft + 1;
                    int topLeft = bottomLeft + vertexColumns;
                    int topRight = topLeft + 1;
                    SetTriangleIndex(triangles, ref triangleWriteIndex, bottomLeft);
                    SetTriangleIndex(triangles, ref triangleWriteIndex, bottomRight);
                    SetTriangleIndex(triangles, ref triangleWriteIndex, topRight);
                    SetTriangleIndex(triangles, ref triangleWriteIndex, bottomLeft);
                    SetTriangleIndex(triangles, ref triangleWriteIndex, topRight);
                    SetTriangleIndex(triangles, ref triangleWriteIndex, topLeft);
                }
            }

            EnsureAtLeastOneBone(serializedAsset);
            serializedAsset.ApplyModifiedProperties();
            asset.EnsureStableBoneIds();
            GenerateAutomaticWeights(asset);
            EditorUtility.SetDirty(asset);
        }

        public static bool GenerateFromSpriteGeometry(SpriteRigAsset asset, Sprite sprite, out string error)
        {
            error = string.Empty;
            if (asset == null || sprite == null)
            {
                error = "A rig asset and preview Sprite are required.";
                return false;
            }

            Vector2[] spriteVertices = sprite.vertices;
            ushort[] spriteTriangles = sprite.triangles;
            if (spriteVertices == null || spriteVertices.Length < 3 || spriteTriangles == null || spriteTriangles.Length < 3)
            {
                error = "The Sprite does not contain usable geometry.";
                return false;
            }

            Undo.RecordObject(asset, "Generate Rig From Sprite");
            SerializedObject serializedAsset = new SerializedObject(asset);
            SerializedProperty vertices = serializedAsset.FindProperty("vertices");
            SerializedProperty edges = serializedAsset.FindProperty("edges");
            SerializedProperty triangles = serializedAsset.FindProperty("triangles");

            vertices.arraySize = spriteVertices.Length;
            for (int i = 0; i < spriteVertices.Length; i++)
            {
                Vector2 pixel = spriteVertices[i] * sprite.pixelsPerUnit + sprite.pivot;
                Vector2 normalized = new Vector2(
                    pixel.x / sprite.rect.width,
                    pixel.y / sprite.rect.height);
                SetVertex(vertices.GetArrayElementAtIndex(i), normalized);
            }

            triangles.arraySize = spriteTriangles.Length;
            Dictionary<ulong, EdgeCounter> edgeCounts = new Dictionary<ulong, EdgeCounter>();
            int writeIndex = 0;
            for (int i = 0; i + 2 < spriteTriangles.Length; i += 3)
            {
                int a = spriteTriangles[i];
                int b = spriteTriangles[i + 1];
                int c = spriteTriangles[i + 2];
                Vector2 positionA = GetVertexPosition(vertices, a);
                Vector2 positionB = GetVertexPosition(vertices, b);
                Vector2 positionC = GetVertexPosition(vertices, c);
                if (Cross(positionB - positionA, positionC - positionA) < 0f)
                {
                    int swap = b;
                    b = c;
                    c = swap;
                }

                SetTriangleIndex(triangles, ref writeIndex, a);
                SetTriangleIndex(triangles, ref writeIndex, b);
                SetTriangleIndex(triangles, ref writeIndex, c);
                CountEdge(edgeCounts, a, b);
                CountEdge(edgeCounts, b, c);
                CountEdge(edgeCounts, c, a);
            }

            edges.arraySize = edgeCounts.Count;
            int edgeIndex = 0;
            foreach (EdgeCounter edge in edgeCounts.Values)
            {
                SetEdge(
                    edges.GetArrayElementAtIndex(edgeIndex++),
                    new EdgeData(edge.VertexA, edge.VertexB, edge.Count == 1));
            }

            EnsureAtLeastOneBone(serializedAsset);
            serializedAsset.ApplyModifiedProperties();
            asset.EnsureStableBoneIds();
            GenerateAutomaticWeights(asset);
            EditorUtility.SetDirty(asset);
            return true;
        }

        public static bool GenerateAutomaticWeights(SpriteRigAsset asset)
        {
            if (asset == null || asset.Vertices.Count == 0 || asset.Bones.Count == 0)
            {
                return false;
            }

            Undo.RecordObject(asset, "Generate Automatic Rig Weights");
            SerializedObject serializedAsset = new SerializedObject(asset);
            SerializedProperty weights = serializedAsset.FindProperty("weights");
            weights.arraySize = asset.Vertices.Count;

            CpuSpriteSkinner bindPose = new CpuSpriteSkinner(asset);
            BoneDistance[] distances = new BoneDistance[asset.Bones.Count];
            for (int vertexIndex = 0; vertexIndex < asset.Vertices.Count; vertexIndex++)
            {
                Vector2 vertex = asset.Vertices[vertexIndex].Position;
                for (int boneIndex = 0; boneIndex < asset.Bones.Count; boneIndex++)
                {
                    Matrix4x4 matrix = bindPose.GetPoseWorldMatrix(boneIndex);
                    Vector2 start = new Vector2(matrix.m03, matrix.m13);
                    Vector2 end = matrix.MultiplyPoint3x4(new Vector3(asset.Bones[boneIndex].Length, 0f, 0f));
                    distances[boneIndex] = new BoneDistance(
                        boneIndex,
                        DistanceToSegment(vertex, start, end));
                }

                Array.Sort(distances, (left, right) => left.Distance.CompareTo(right.Distance));
                SerializedProperty vertexWeight = weights.GetArrayElementAtIndex(vertexIndex);
                float sum = 0f;
                int influenceCount = Mathf.Min(SpriteRigAsset.MaximumInfluenceCount, distances.Length);
                float[] rawWeights = new float[SpriteRigAsset.MaximumInfluenceCount];
                for (int influence = 0; influence < influenceCount; influence++)
                {
                    float distance = Mathf.Max(DistanceEpsilon, distances[influence].Distance);
                    rawWeights[influence] = 1f / (distance * distance);
                    sum += rawWeights[influence];
                }

                for (int influence = 0; influence < SpriteRigAsset.MaximumInfluenceCount; influence++)
                {
                    int boneIndex = influence < influenceCount ? distances[influence].BoneIndex : 0;
                    float weight = influence < influenceCount ? rawWeights[influence] / sum : 0f;
                    vertexWeight.FindPropertyRelative($"boneIndex{influence}").intValue = boneIndex;
                    vertexWeight.FindPropertyRelative($"weight{influence}").floatValue = weight;
                }
            }

            serializedAsset.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            return true;
        }

        public static int AddBone(
            SpriteRigAsset asset,
            int parentIndex,
            Vector2 localPosition,
            float localRotation,
            float length)
        {
            if (asset == null)
            {
                return -1;
            }

            Undo.RecordObject(asset, "Add Rig Bone");
            SerializedObject serializedAsset = new SerializedObject(asset);
            SerializedProperty bones = serializedAsset.FindProperty("bones");
            int newIndex = bones.arraySize;
            bones.InsertArrayElementAtIndex(newIndex);
            SerializedProperty bone = bones.GetArrayElementAtIndex(newIndex);
            ConfigureBone(
                bone,
                $"Bone {newIndex}",
                Mathf.Clamp(parentIndex, -1, newIndex - 1),
                localPosition,
                localRotation,
                Mathf.Max(0.001f, length));
            serializedAsset.ApplyModifiedProperties();
            asset.EnsureStableBoneIds();
            EditorUtility.SetDirty(asset);
            return newIndex;
        }

        public static int AddVertex(SpriteRigAsset asset, Vector2 position)
        {
            if (asset == null)
            {
                return -1;
            }

            Undo.RecordObject(asset, "Add Rig Vertex");
            SerializedObject serializedAsset = new SerializedObject(asset);
            SerializedProperty vertices = serializedAsset.FindProperty("vertices");
            SerializedProperty weights = serializedAsset.FindProperty("weights");
            int newIndex = vertices.arraySize;
            vertices.InsertArrayElementAtIndex(newIndex);
            SetVertex(vertices.GetArrayElementAtIndex(newIndex), position);
            weights.InsertArrayElementAtIndex(newIndex);
            SerializedProperty weight = weights.GetArrayElementAtIndex(newIndex);
            for (int influence = 0; influence < SpriteRigAsset.MaximumInfluenceCount; influence++)
            {
                weight.FindPropertyRelative($"boneIndex{influence}").intValue = 0;
                weight.FindPropertyRelative($"weight{influence}").floatValue = influence == 0 ? 1f : 0f;
            }

            serializedAsset.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            return newIndex;
        }

        public static bool DeleteVertex(SpriteRigAsset asset, int vertexIndex)
        {
            if (asset == null || vertexIndex < 0 || vertexIndex >= asset.Vertices.Count || asset.Vertices.Count <= 3)
            {
                return false;
            }

            Undo.RecordObject(asset, "Delete Rig Vertex");
            SerializedObject serializedAsset = new SerializedObject(asset);
            SerializedProperty vertices = serializedAsset.FindProperty("vertices");
            SerializedProperty weights = serializedAsset.FindProperty("weights");
            SerializedProperty edges = serializedAsset.FindProperty("edges");
            SerializedProperty triangles = serializedAsset.FindProperty("triangles");

            for (int i = edges.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty edge = edges.GetArrayElementAtIndex(i);
                int vertexA = edge.FindPropertyRelative("vertexA").intValue;
                int vertexB = edge.FindPropertyRelative("vertexB").intValue;
                if (vertexA == vertexIndex || vertexB == vertexIndex)
                {
                    edges.DeleteArrayElementAtIndex(i);
                    continue;
                }

                if (vertexA > vertexIndex)
                {
                    edge.FindPropertyRelative("vertexA").intValue--;
                }
                if (vertexB > vertexIndex)
                {
                    edge.FindPropertyRelative("vertexB").intValue--;
                }
            }

            for (int i = triangles.arraySize - 3; i >= 0; i -= 3)
            {
                int a = triangles.GetArrayElementAtIndex(i).intValue;
                int b = triangles.GetArrayElementAtIndex(i + 1).intValue;
                int c = triangles.GetArrayElementAtIndex(i + 2).intValue;
                if (a == vertexIndex || b == vertexIndex || c == vertexIndex)
                {
                    triangles.DeleteArrayElementAtIndex(i + 2);
                    triangles.DeleteArrayElementAtIndex(i + 1);
                    triangles.DeleteArrayElementAtIndex(i);
                    continue;
                }

                if (a > vertexIndex) triangles.GetArrayElementAtIndex(i).intValue--;
                if (b > vertexIndex) triangles.GetArrayElementAtIndex(i + 1).intValue--;
                if (c > vertexIndex) triangles.GetArrayElementAtIndex(i + 2).intValue--;
            }

            vertices.DeleteArrayElementAtIndex(vertexIndex);
            if (vertexIndex < weights.arraySize)
            {
                weights.DeleteArrayElementAtIndex(vertexIndex);
            }
            serializedAsset.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            return true;
        }

        public static bool MirrorWeightsHorizontally(SpriteRigAsset asset)
        {
            if (asset == null || asset.Vertices.Count == 0 || asset.Weights.Count != asset.Vertices.Count)
            {
                return false;
            }

            Undo.RecordObject(asset, "Mirror Rig Weights Horizontally");
            VertexWeight4[] sourceWeights = new VertexWeight4[asset.Weights.Count];
            for (int i = 0; i < sourceWeights.Length; i++)
            {
                sourceWeights[i] = asset.Weights[i];
            }

            SerializedObject serializedAsset = new SerializedObject(asset);
            SerializedProperty weights = serializedAsset.FindProperty("weights");
            for (int destinationIndex = 0; destinationIndex < asset.Vertices.Count; destinationIndex++)
            {
                Vector2 mirroredPosition = asset.Vertices[destinationIndex].Position;
                mirroredPosition.x = 1f - mirroredPosition.x;
                int sourceIndex = FindNearestVertex(asset, mirroredPosition);
                SerializedProperty destination = weights.GetArrayElementAtIndex(destinationIndex);
                VertexWeight4 source = sourceWeights[sourceIndex];
                for (int influence = 0; influence < SpriteRigAsset.MaximumInfluenceCount; influence++)
                {
                    destination.FindPropertyRelative($"boneIndex{influence}").intValue = source.GetBoneIndex(influence);
                    destination.FindPropertyRelative($"weight{influence}").floatValue = source.GetWeight(influence);
                }
            }

            serializedAsset.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            return true;
        }

        public static bool DeleteBone(SpriteRigAsset asset, int boneIndex)
        {
            if (asset == null || boneIndex < 0 || boneIndex >= asset.Bones.Count || asset.Bones.Count <= 1)
            {
                return false;
            }

            Undo.RecordObject(asset, "Delete Rig Bone");
            SerializedObject serializedAsset = new SerializedObject(asset);
            SerializedProperty bones = serializedAsset.FindProperty("bones");
            int deletedParent = bones.GetArrayElementAtIndex(boneIndex).FindPropertyRelative("parentIndex").intValue;

            for (int i = 0; i < bones.arraySize; i++)
            {
                if (i == boneIndex)
                {
                    continue;
                }

                SerializedProperty parent = bones.GetArrayElementAtIndex(i).FindPropertyRelative("parentIndex");
                if (parent.intValue == boneIndex)
                {
                    parent.intValue = deletedParent;
                }
                else if (parent.intValue > boneIndex)
                {
                    parent.intValue--;
                }
            }

            bones.DeleteArrayElementAtIndex(boneIndex);
            SerializedProperty weights = serializedAsset.FindProperty("weights");
            for (int vertexIndex = 0; vertexIndex < weights.arraySize; vertexIndex++)
            {
                SerializedProperty vertexWeight = weights.GetArrayElementAtIndex(vertexIndex);
                for (int influence = 0; influence < SpriteRigAsset.MaximumInfluenceCount; influence++)
                {
                    SerializedProperty indexProperty = vertexWeight.FindPropertyRelative($"boneIndex{influence}");
                    if (indexProperty.intValue == boneIndex)
                    {
                        indexProperty.intValue = Mathf.Max(0, deletedParent);
                    }
                    else if (indexProperty.intValue > boneIndex)
                    {
                        indexProperty.intValue--;
                    }
                }
            }

            serializedAsset.ApplyModifiedProperties();
            asset.NormalizeAllVertexWeights();
            EditorUtility.SetDirty(asset);
            return true;
        }

        static void EnsureAtLeastOneBone(SerializedObject serializedAsset)
        {
            SerializedProperty bones = serializedAsset.FindProperty("bones");
            if (bones.arraySize > 0)
            {
                return;
            }

            bones.arraySize = 1;
            ConfigureBone(bones.GetArrayElementAtIndex(0), "Root", -1, new Vector2(0.5f, 0.5f), 0f, 0.5f);
        }

        static void ConfigureBone(
            SerializedProperty bone,
            string name,
            int parentIndex,
            Vector2 localPosition,
            float localRotation,
            float length)
        {
            bone.FindPropertyRelative("stableId").stringValue = string.Empty;
            bone.FindPropertyRelative("name").stringValue = name;
            bone.FindPropertyRelative("parentIndex").intValue = parentIndex;
            bone.FindPropertyRelative("localPosition").vector2Value = localPosition;
            bone.FindPropertyRelative("localRotation").floatValue = localRotation;
            bone.FindPropertyRelative("length").floatValue = length;
            SetDefaultPhysics(bone.FindPropertyRelative("physics"));
        }

        static void SetDefaultPhysics(SerializedProperty physics)
        {
            BonePhysicsSettings defaults = BonePhysicsSettings.Default;
            physics.FindPropertyRelative("simulationMode").enumValueIndex = (int)defaults.SimulationMode;
            physics.FindPropertyRelative("mass").floatValue = defaults.Mass;
            physics.FindPropertyRelative("gravityScale").floatValue = defaults.GravityScale;
            physics.FindPropertyRelative("stiffness").floatValue = defaults.Stiffness;
            physics.FindPropertyRelative("damping").floatValue = defaults.Damping;
            physics.FindPropertyRelative("inertia").floatValue = defaults.Inertia;
            physics.FindPropertyRelative("minimumAngle").floatValue = defaults.MinimumAngle;
            physics.FindPropertyRelative("maximumAngle").floatValue = defaults.MaximumAngle;
            physics.FindPropertyRelative("maximumAngularSpeed").floatValue = defaults.MaximumAngularSpeed;
            physics.FindPropertyRelative("collisionRadius").floatValue = defaults.CollisionRadius;
            physics.FindPropertyRelative("externalForceMultiplier").floatValue = defaults.ExternalForceMultiplier;
        }

        static void SetVertex(SerializedProperty vertex, Vector2 position)
        {
            vertex.FindPropertyRelative("position").vector2Value = position;
            vertex.FindPropertyRelative("hasCustomUv").boolValue = false;
            vertex.FindPropertyRelative("uv").vector2Value = position;
        }

        static Vector2 GetVertexPosition(SerializedProperty vertices, int index)
        {
            return vertices.GetArrayElementAtIndex(index).FindPropertyRelative("position").vector2Value;
        }

        static void SetEdge(SerializedProperty edge, EdgeData data)
        {
            edge.FindPropertyRelative("vertexA").intValue = data.VertexA;
            edge.FindPropertyRelative("vertexB").intValue = data.VertexB;
            edge.FindPropertyRelative("boundary").boolValue = data.Boundary;
        }

        static void SetTriangleIndex(SerializedProperty triangles, ref int writeIndex, int value)
        {
            triangles.GetArrayElementAtIndex(writeIndex++).intValue = value;
        }

        static void CountEdge(Dictionary<ulong, EdgeCounter> edges, int vertexA, int vertexB)
        {
            int minimum = Mathf.Min(vertexA, vertexB);
            int maximum = Mathf.Max(vertexA, vertexB);
            ulong key = ((ulong)(uint)minimum << 32) | (uint)maximum;
            if (edges.TryGetValue(key, out EdgeCounter edge))
            {
                edge.Count++;
                edges[key] = edge;
            }
            else
            {
                edges.Add(key, new EdgeCounter(minimum, maximum));
            }
        }

        static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= DistanceEpsilon)
            {
                return Vector2.Distance(point, start);
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * t);
        }

        static int FindNearestVertex(SpriteRigAsset asset, Vector2 position)
        {
            int nearestIndex = 0;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < asset.Vertices.Count; i++)
            {
                float distance = (asset.Vertices[i].Position - position).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestIndex = i;
                    nearestDistance = distance;
                }
            }
            return nearestIndex;
        }

        static float Cross(Vector2 left, Vector2 right)
        {
            return left.x * right.y - left.y * right.x;
        }

        readonly struct EdgeData
        {
            public readonly int VertexA;
            public readonly int VertexB;
            public readonly bool Boundary;

            public EdgeData(int vertexA, int vertexB, bool boundary)
            {
                VertexA = vertexA;
                VertexB = vertexB;
                Boundary = boundary;
            }
        }

        struct EdgeCounter
        {
            public int VertexA;
            public int VertexB;
            public int Count;

            public EdgeCounter(int vertexA, int vertexB)
            {
                VertexA = vertexA;
                VertexB = vertexB;
                Count = 1;
            }
        }

        readonly struct BoneDistance
        {
            public readonly int BoneIndex;
            public readonly float Distance;

            public BoneDistance(int boneIndex, float distance)
            {
                BoneIndex = boneIndex;
                Distance = distance;
            }
        }
    }
}
