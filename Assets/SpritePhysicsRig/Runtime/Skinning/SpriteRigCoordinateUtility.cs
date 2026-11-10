using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShizukuCity.SpritePhysicsRig
{
    public static class SpriteRigCoordinateUtility
    {
        public static Vector3 RigToSpriteLocal(Vector2 rigPosition, Sprite sprite, bool flipX, bool flipY)
        {
            if (sprite == null)
            {
                throw new ArgumentNullException(nameof(sprite));
            }

            Rect rect = sprite.rect;
            Vector2 pivot = sprite.pivot;
            float pixelsPerUnit = sprite.pixelsPerUnit;
            Vector2 local = new Vector2(
                (rigPosition.x * rect.width - pivot.x) / pixelsPerUnit,
                (rigPosition.y * rect.height - pivot.y) / pixelsPerUnit);

            if (flipX)
            {
                local.x = -local.x;
            }

            if (flipY)
            {
                local.y = -local.y;
            }

            return new Vector3(local.x, local.y, 0f);
        }

        public static Vector2 SpriteLocalDeltaToRig(
            Vector2 localDelta,
            Sprite sprite,
            bool flipX = false,
            bool flipY = false)
        {
            if (sprite == null)
            {
                throw new ArgumentNullException(nameof(sprite));
            }

            if (flipX)
            {
                localDelta.x = -localDelta.x;
            }

            if (flipY)
            {
                localDelta.y = -localDelta.y;
            }

            Rect rect = sprite.rect;
            return new Vector2(
                localDelta.x * sprite.pixelsPerUnit / Mathf.Max(1f, rect.width),
                localDelta.y * sprite.pixelsPerUnit / Mathf.Max(1f, rect.height));
        }

        public static Vector2 SpriteLocalToRig(Vector2 localPosition, Sprite sprite, bool flipX, bool flipY)
        {
            if (sprite == null)
            {
                throw new ArgumentNullException(nameof(sprite));
            }

            if (flipX)
            {
                localPosition.x = -localPosition.x;
            }

            if (flipY)
            {
                localPosition.y = -localPosition.y;
            }

            Rect rect = sprite.rect;
            return new Vector2(
                (localPosition.x * sprite.pixelsPerUnit + sprite.pivot.x) / Mathf.Max(1f, rect.width),
                (localPosition.y * sprite.pixelsPerUnit + sprite.pivot.y) / Mathf.Max(1f, rect.height));
        }

        public static bool TryBuildTextureUvs(
            Sprite sprite,
            IReadOnlyList<RigVertex> vertices,
            Vector2[] output,
            out string error)
        {
            error = string.Empty;
            if (sprite == null)
            {
                error = "A Sprite is required.";
                return false;
            }

            if (vertices == null || output == null || output.Length != vertices.Count)
            {
                error = "The UV output buffer must match the rig vertex count.";
                return false;
            }

            if (sprite.packed && sprite.packingRotation != SpritePackingRotation.None)
            {
                error = $"Rotated Sprite Atlas packing ({sprite.packingRotation}) is not supported yet.";
                return false;
            }

            Texture2D texture = sprite.texture;
            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                error = "The Sprite has no readable texture dimensions.";
                return false;
            }

            Rect textureRect;
            try
            {
                textureRect = sprite.textureRect;
            }
            catch (UnityException)
            {
                error = "Tight-packed Sprite Atlas regions are not supported yet. Use rectangular packing.";
                return false;
            }

            float inverseTextureWidth = 1f / texture.width;
            float inverseTextureHeight = 1f / texture.height;
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector2 uv = vertices[i].Uv;
                output[i] = new Vector2(
                    (textureRect.x + uv.x * textureRect.width) * inverseTextureWidth,
                    (textureRect.y + uv.y * textureRect.height) * inverseTextureHeight);
            }

            return true;
        }
    }
}
