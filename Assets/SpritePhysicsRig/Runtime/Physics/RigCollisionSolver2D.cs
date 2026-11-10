using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShizukuCity.SpritePhysicsRig
{
    public enum RigCollisionShape
    {
        BoneEndpoints,
        BoneCapsules
    }

    public struct RigCollisionParameters
    {
        public LayerMask LayerMask;
        public RigCollisionShape Shape;
        public bool IncludeTriggers;
        public float RadiusMultiplier;
        public float Friction;
        public float Bounciness;
        public float SkinWidth;
        public int CollisionIterations;
        public int ConstraintIterations;
    }

    public sealed class RigCollisionSolver2D : IDisposable
    {
        const int InitialResultCapacity = 32;
        const float Epsilon = 0.000001f;

        readonly Transform owner;
        readonly SpriteRenderer sourceRenderer;
        readonly SpriteRigAsset rigAsset;
        readonly HashSet<Collider2D> ignoredColliders = new HashSet<Collider2D>();
        readonly List<Collider2D> overlapResults = new List<Collider2D>(InitialResultCapacity);
        readonly List<RaycastHit2D> castResults = new List<RaycastHit2D>(InitialResultCapacity);

        GameObject probeObject;
        CircleCollider2D circleProbe;
        CapsuleCollider2D capsuleProbe;

        public int LastQueryCount { get; private set; }
        public int LastCorrectionCount { get; private set; }

        public RigCollisionSolver2D(
            Transform owner,
            SpriteRenderer sourceRenderer,
            SpriteRigAsset rigAsset)
        {
            this.owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            this.sourceRenderer = sourceRenderer != null ? sourceRenderer : throw new ArgumentNullException(nameof(sourceRenderer));
            this.rigAsset = rigAsset != null ? rigAsset : throw new ArgumentNullException(nameof(rigAsset));
            RefreshIgnoredColliders();
        }

        public void RefreshIgnoredColliders()
        {
            ignoredColliders.Clear();
            Collider2D[] colliders = owner.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                ignoredColliders.Add(colliders[i]);
            }

            Transform parent = owner.parent;
            if (parent != null)
            {
                Collider2D[] parentColliders = parent.GetComponentsInChildren<Collider2D>(true);
                for (int i = 0; i < parentColliders.Length; i++)
                {
                    ignoredColliders.Add(parentColliders[i]);
                }
            }
        }

        public bool Solve(
            BonePhysicsSolver physicsSolver,
            RigCollisionParameters parameters)
        {
            LastQueryCount = 0;
            LastCorrectionCount = 0;
            if (physicsSolver == null || sourceRenderer.sprite == null)
            {
                return false;
            }

            EnsureProbe();
            ContactFilter2D filter = new ContactFilter2D();
            filter.NoFilter();
            filter.SetLayerMask(parameters.LayerMask);
            filter.useTriggers = parameters.IncludeTriggers;

            bool anyCollision = false;
            int collisionIterations = Mathf.Clamp(parameters.CollisionIterations, 1, 8);
            for (int iteration = 0; iteration < collisionIterations; iteration++)
            {
                bool iterationCollision = false;
                for (int boneIndex = 0; boneIndex < physicsSolver.BoneCount; boneIndex++)
                {
                    BonePhysicsSettings boneSettings = physicsSolver.GetBoneSettings(boneIndex);
                    if (boneSettings.SimulationMode != BoneSimulationMode.Dynamic
                        || boneSettings.CollisionRadius <= 0f)
                    {
                        continue;
                    }

                    float radius = GetWorldRadius(
                        boneSettings.CollisionRadius * Mathf.Max(0f, parameters.RadiusMultiplier));
                    if (radius <= Epsilon)
                    {
                        continue;
                    }

                    if (iteration == 0)
                    {
                        iterationCollision |= SolveContinuousEndpoint(
                            physicsSolver,
                            boneIndex,
                            radius,
                            filter,
                            parameters);
                    }

                    iterationCollision |= parameters.Shape == RigCollisionShape.BoneCapsules
                        ? SolveCapsuleOverlap(physicsSolver, boneIndex, radius, filter, parameters)
                        : SolveEndpointOverlap(physicsSolver, boneIndex, radius, filter, parameters);
                }

                anyCollision |= iterationCollision;
                if (!iterationCollision)
                {
                    break;
                }

                physicsSolver.ResolveConstraintsAfterCollision(parameters.ConstraintIterations);
            }

            DisableProbes();
            return anyCollision;
        }

        public void Dispose()
        {
            if (probeObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(probeObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(probeObject);
            }

            probeObject = null;
            circleProbe = null;
            capsuleProbe = null;
        }

        bool SolveContinuousEndpoint(
            BonePhysicsSolver physicsSolver,
            int boneIndex,
            float radius,
            ContactFilter2D filter,
            RigCollisionParameters parameters)
        {
            Vector2 previousWorld = RigToWorld(physicsSolver.GetPreviousBoneEndpoint(boneIndex));
            Vector2 currentWorld = RigToWorld(physicsSolver.GetBoneEndpoint(boneIndex));
            Vector2 travel = currentWorld - previousWorld;
            float distance = travel.magnitude;
            if (distance <= Epsilon)
            {
                return false;
            }

            castResults.Clear();
            LastQueryCount++;
            Physics2D.CircleCast(
                previousWorld,
                radius,
                travel / distance,
                filter,
                castResults,
                distance);

            for (int i = 0; i < castResults.Count; i++)
            {
                RaycastHit2D hit = castResults[i];
                if (hit.collider == null || IsIgnored(hit.collider))
                {
                    continue;
                }

                Vector2 correctedWorld = hit.centroid + hit.normal * Mathf.Max(0f, parameters.SkinWidth);
                ApplyWorldCorrection(
                    physicsSolver,
                    boneIndex,
                    correctedWorld - currentWorld,
                    hit.normal,
                    parameters);
                LastCorrectionCount++;
                return true;
            }

            return false;
        }

        bool SolveEndpointOverlap(
            BonePhysicsSolver physicsSolver,
            int boneIndex,
            float radius,
            ContactFilter2D filter,
            RigCollisionParameters parameters)
        {
            Vector2 center = RigToWorld(physicsSolver.GetBoneEndpoint(boneIndex));
            ConfigureCircleProbe(center, radius);
            overlapResults.Clear();
            LastQueryCount++;
            Physics2D.OverlapCircle(center, radius, filter, overlapResults);

            bool collided = false;
            for (int i = 0; i < overlapResults.Count; i++)
            {
                Collider2D collider = overlapResults[i];
                if (collider == null || IsIgnored(collider))
                {
                    continue;
                }

                ColliderDistance2D distance = Physics2D.Distance(circleProbe, collider);
                if (!distance.isValid || !distance.isOverlapped)
                {
                    continue;
                }

                Vector2 correction = distance.normal
                    * (distance.distance - Mathf.Max(0f, parameters.SkinWidth));
                ApplyWorldCorrection(
                    physicsSolver,
                    boneIndex,
                    correction,
                    correction.normalized,
                    parameters);
                LastCorrectionCount++;
                center += correction;
                ConfigureCircleProbe(center, radius);
                collided = true;
            }

            return collided;
        }

        bool SolveCapsuleOverlap(
            BonePhysicsSolver physicsSolver,
            int boneIndex,
            float radius,
            ContactFilter2D filter,
            RigCollisionParameters parameters)
        {
            Vector2 start = RigToWorld(physicsSolver.GetBoneStart(boneIndex));
            Vector2 endpoint = RigToWorld(physicsSolver.GetBoneEndpoint(boneIndex));
            Vector2 delta = endpoint - start;
            float length = delta.magnitude;
            if (length <= Epsilon)
            {
                return SolveEndpointOverlap(physicsSolver, boneIndex, radius, filter, parameters);
            }

            Vector2 center = (start + endpoint) * 0.5f;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            Vector2 size = new Vector2(length + radius * 2f, radius * 2f);
            ConfigureCapsuleProbe(center, size, angle);
            overlapResults.Clear();
            LastQueryCount++;
            Physics2D.OverlapCapsule(
                center,
                size,
                CapsuleDirection2D.Horizontal,
                angle,
                filter,
                overlapResults);

            bool collided = false;
            for (int i = 0; i < overlapResults.Count; i++)
            {
                Collider2D collider = overlapResults[i];
                if (collider == null || IsIgnored(collider))
                {
                    continue;
                }

                ColliderDistance2D distance = Physics2D.Distance(capsuleProbe, collider);
                if (!distance.isValid || !distance.isOverlapped)
                {
                    continue;
                }

                Vector2 correction = distance.normal
                    * (distance.distance - Mathf.Max(0f, parameters.SkinWidth));
                ApplyWorldCorrection(
                    physicsSolver,
                    boneIndex,
                    correction,
                    correction.normalized,
                    parameters);
                LastCorrectionCount++;
                endpoint += correction;
                delta = endpoint - start;
                length = delta.magnitude;
                center = (start + endpoint) * 0.5f;
                angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                size = new Vector2(length + radius * 2f, radius * 2f);
                ConfigureCapsuleProbe(center, size, angle);
                collided = true;
            }

            return collided;
        }

        void ApplyWorldCorrection(
            BonePhysicsSolver physicsSolver,
            int boneIndex,
            Vector2 worldCorrection,
            Vector2 worldNormal,
            RigCollisionParameters parameters)
        {
            Vector2 rigCorrection = WorldVectorToRig(worldCorrection);
            Vector2 rigNormal = WorldVectorToRig(worldNormal);
            physicsSolver.ApplyCollisionCorrection(
                boneIndex,
                rigCorrection,
                rigNormal,
                parameters.Friction,
                parameters.Bounciness);
        }

        void EnsureProbe()
        {
            if (probeObject != null)
            {
                return;
            }

            probeObject = new GameObject("SpriteRigCollisionProbe")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = Physics2D.IgnoreRaycastLayer
            };
            Rigidbody2D rigidbody = probeObject.AddComponent<Rigidbody2D>();
            rigidbody.bodyType = RigidbodyType2D.Kinematic;
            rigidbody.simulated = true;
            circleProbe = probeObject.AddComponent<CircleCollider2D>();
            circleProbe.isTrigger = true;
            capsuleProbe = probeObject.AddComponent<CapsuleCollider2D>();
            capsuleProbe.isTrigger = true;
            DisableProbes();
            ignoredColliders.Add(circleProbe);
            ignoredColliders.Add(capsuleProbe);
        }

        void ConfigureCircleProbe(Vector2 center, float radius)
        {
            capsuleProbe.enabled = false;
            circleProbe.enabled = true;
            circleProbe.radius = radius;
            probeObject.transform.SetPositionAndRotation(center, Quaternion.identity);
            Physics2D.SyncTransforms();
        }

        void ConfigureCapsuleProbe(Vector2 center, Vector2 size, float angle)
        {
            circleProbe.enabled = false;
            capsuleProbe.enabled = true;
            capsuleProbe.direction = CapsuleDirection2D.Horizontal;
            capsuleProbe.size = size;
            probeObject.transform.SetPositionAndRotation(center, Quaternion.Euler(0f, 0f, angle));
            Physics2D.SyncTransforms();
        }

        void DisableProbes()
        {
            if (circleProbe != null)
            {
                circleProbe.enabled = false;
            }
            if (capsuleProbe != null)
            {
                capsuleProbe.enabled = false;
            }
        }

        bool IsIgnored(Collider2D collider)
        {
            return collider == circleProbe
                || collider == capsuleProbe
                || ignoredColliders.Contains(collider);
        }

        Vector2 RigToWorld(Vector2 rigPosition)
        {
            Vector3 local = SpriteRigCoordinateUtility.RigToSpriteLocal(
                rigPosition,
                sourceRenderer.sprite,
                sourceRenderer.flipX,
                sourceRenderer.flipY);
            return owner.TransformPoint(local);
        }

        Vector2 WorldVectorToRig(Vector2 worldVector)
        {
            Vector3 local = owner.InverseTransformVector(worldVector);
            return SpriteRigCoordinateUtility.SpriteLocalDeltaToRig(
                local,
                sourceRenderer.sprite,
                sourceRenderer.flipX,
                sourceRenderer.flipY);
        }

        float GetWorldRadius(float rigRadius)
        {
            float localRadius = rigRadius
                * sourceRenderer.sprite.rect.height
                / sourceRenderer.sprite.pixelsPerUnit;
            Vector3 scale = owner.lossyScale;
            return localRadius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        }
    }
}
