using System;

namespace ShizukuCity.SpritePhysicsRig
{
    public enum RigValidationSeverity
    {
        Warning,
        Error
    }

    public enum RigValidationCode
    {
        UnsupportedDataVersion,
        TooFewVertices,
        TooManyBones,
        NonFiniteVertex,
        InvalidUv,
        InvalidEdgeIndex,
        SelfEdge,
        DuplicateEdge,
        DisconnectedGeometry,
        TriangleIndexCount,
        InvalidTriangleIndex,
        DegenerateTriangle,
        ClockwiseTriangle,
        MissingWeights,
        InvalidWeight,
        InvalidWeightBoneIndex,
        DuplicateWeightBone,
        WeightSum,
        MissingBone,
        MissingBoneId,
        DuplicateBoneId,
        InvalidBoneParent,
        CyclicBoneHierarchy,
        InvalidBoneTransform,
        InvalidBoneLength,
        InvalidBonePhysics
    }

    [Serializable]
    public sealed class RigValidationIssue
    {
        public RigValidationSeverity Severity { get; }
        public RigValidationCode Code { get; }
        public string Message { get; }
        public int ElementIndex { get; }

        public RigValidationIssue(
            RigValidationSeverity severity,
            RigValidationCode code,
            string message,
            int elementIndex = -1)
        {
            Severity = severity;
            Code = code;
            Message = message;
            ElementIndex = elementIndex;
        }

        public override string ToString()
        {
            string location = ElementIndex >= 0 ? $" [{ElementIndex}]" : string.Empty;
            return $"{Severity}: {Code}{location} - {Message}";
        }
    }
}
