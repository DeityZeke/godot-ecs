#nullable enable

using System;
using Godot;
using UltraSim.ECS.Components;

namespace Client.ECS.Rendering
{
    internal static class FrustumUtility
    {
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

            var aabb = new Godot.Aabb(min, max - min);

            // Test each frustum plane
            // For each plane, check if AABB is completely on the "outside" side
            foreach (Plane plane in frustumPlanes)
            {
                // Use Godot's built-in intersection test
                // If AABB doesn't intersect the plane, it's completely on one side
                if (!aabb.IntersectsPlane(plane))
                {
                    // AABB is completely on one side of the plane
                    // Check which side using the center point
                    var center = aabb.GetCenter();
                    float dist = plane.DistanceTo(center);

                    // If distance is positive, center is on the "over" side (normal direction)
                    // For frustum planes pointing outward, positive = outside frustum
                    if (dist > 0)
                    {
                        return false; // Outside this plane = cull
                    }
                }
            }

            return true; // Inside or intersecting all planes = visible
        }
    }
}
