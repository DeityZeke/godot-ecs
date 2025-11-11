#nullable enable

using System;
using System.Runtime.CompilerServices;
using Godot;
using UltraSim.ECS.Components;

namespace Client.ECS.Rendering
{
    internal static class FrustumUtility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsVisible(in ChunkBounds bounds, Godot.Collections.Array<Plane> frustumPlanes, float padding = 0f)
        {
            var center = new Vector3(
                (bounds.MinX + bounds.MaxX) * 0.5f,
                (bounds.MinY + bounds.MaxY) * 0.5f,
                (bounds.MinZ + bounds.MaxZ) * 0.5f
            );

            var extents = new Vector3(
                (bounds.MaxX - bounds.MinX) * 0.5f,
                (bounds.MaxY - bounds.MinY) * 0.5f,
                (bounds.MaxZ - bounds.MinZ) * 0.5f
            );

            for (int i = 0; i < 6; i++)
            {
                var plane = frustumPlanes[i];
                var normal = plane.Normal;
                float r = extents.X * MathF.Abs(normal.X) +
                          extents.Y * MathF.Abs(normal.Y) +
                          extents.Z * MathF.Abs(normal.Z);

                float d = normal.Dot(center) + plane.D;

                if (d + r < 0)
                    return false; // Outside
            }
            return true;
        }

        /*
        public static bool IsVisible(in ChunkBounds bounds, Godot.Collections.Array<Plane> frustumPlanes, float padding = 0f)
        {
            var min = new Vector3(bounds.MinX, bounds.MinY, bounds.MinZ);
            var max = new Vector3(bounds.MaxX, bounds.MaxY, bounds.MaxZ);

            if (padding > 0f)
            {
                var paddingVec = new Vector3(padding, padding, padding);
                min -= paddingVec;
                max += paddingVec;
            }

            // Godot's frustum planes point INWARD (toward the inside of the frustum)
            // For an object to be visible, it must be on the POSITIVE side (or intersecting) ALL 6 planes.
            // If the object is on the NEGATIVE side of ANY plane, it's outside the frustum → culled.
            //
            // For each plane, we check the "negative vertex" (closest corner to the plane).
            // If even the negative vertex is on the negative side of the plane, the entire AABB is outside.
            foreach (Plane plane in frustumPlanes)
            {
                var normal = plane.Normal;

                // Find the negative vertex (closest point to the plane, opposite of normal direction)
                // If normal.X >= 0, plane faces +X, so closest point has minimum X
                var negativeVertex = new Vector3(
                    normal.X >= 0 ? min.X : max.X,
                    normal.Y >= 0 ? min.Y : max.Y,
                    normal.Z >= 0 ? min.Z : max.Z
                );

                // Check if the closest vertex is behind the plane (negative distance)
                // If even the closest vertex is behind, the entire AABB is outside this plane
                if (plane.DistanceTo(negativeVertex) < 0)
                {
                    return false; // Culled - AABB is completely outside this frustum plane
                }
            }

            return true; // Visible (inside or intersecting frustum)
        }
        */
    }
}