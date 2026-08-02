using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Hackerzhuli.Code.Editor
{
    /// <summary>Camera selection and projection math shared by GameObject and ECS visual snapshots.</summary>
    internal static class VisualSnapshotUtility
    {
        private static readonly int[] BoxEdgeStarts = { 0, 2, 4, 6, 0, 1, 4, 5, 0, 1, 2, 3 };
        private static readonly int[] BoxEdgeEnds = { 1, 3, 5, 7, 2, 3, 6, 7, 4, 5, 6, 7 };

        internal static bool TryResolveCamera(string requested, out Camera camera, out string errorCode,
            out string error)
        {
            camera = null;
            errorCode = null;
            error = null;
            var candidates = Camera.allCameras
                .Where(candidate => candidate != null && candidate.targetTexture == null)
                .ToList();
            if (candidates.Count == 0)
            {
                errorCode = "no_camera";
                error = "No enabled camera renders to the screen.";
                return false;
            }

            if (!string.IsNullOrEmpty(requested))
            {
                camera = candidates.FirstOrDefault(candidate =>
                    string.Equals(candidate.name, requested, StringComparison.Ordinal));
                if (camera != null) return true;
                errorCode = "not_found";
                error = $"No enabled camera named '{requested}' renders to the screen. Cameras are " +
                        string.Join(", ", candidates.Select(candidate => $"'{candidate.name}'")) + ".";
                return false;
            }

            var main = Camera.main;
            camera = main != null && candidates.Contains(main)
                ? main
                : candidates.OrderBy(candidate => candidate.depth).First();
            return true;
        }

        internal static float ComputePixelsPerUnit(Camera camera)
        {
            var pixelHeight = camera.pixelHeight;
            if (camera.orthographic)
                return camera.orthographicSize > 0f ? pixelHeight / (2f * camera.orthographicSize) : 0f;
            var tangent = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return tangent > 0f ? pixelHeight / (2f * tangent) : 0f;
        }

        internal static bool TryProjectBounds(Bounds bounds, Matrix4x4 worldToCamera, Matrix4x4 projection,
            Rect pixelRect, float screenHeight, float nearClip, out Rect rect, out float eyeDepthNear,
            out float eyeDepthFar)
        {
            rect = default;
            eyeDepthNear = 0f;
            eyeDepthFar = 0f;
            var center = bounds.center;
            var extents = bounds.extents;
            var corners = new Vector3[8];
            for (var index = 0; index < corners.Length; index++)
                corners[index] = worldToCamera.MultiplyPoint3x4(new Vector3(
                    center.x + ((index & 1) == 0 ? -extents.x : extents.x),
                    center.y + ((index & 2) == 0 ? -extents.y : extents.y),
                    center.z + ((index & 4) == 0 ? -extents.z : extents.z)));

            var points = new List<Vector3>(24);
            for (var edge = 0; edge < BoxEdgeStarts.Length; edge++)
            {
                var start = corners[BoxEdgeStarts[edge]];
                var end = corners[BoxEdgeEnds[edge]];
                var startInFront = -start.z >= nearClip;
                var endInFront = -end.z >= nearClip;
                if (!startInFront && !endInFront) continue;
                if (startInFront) points.Add(start);
                if (endInFront) points.Add(end);
                if (startInFront == endInFront) continue;
                var inside = startInFront ? start : end;
                var outside = startInFront ? end : start;
                var travel = (inside.z + nearClip) / (inside.z - outside.z);
                points.Add(Vector3.LerpUnclamped(inside, outside, travel));
            }
            if (points.Count == 0) return false;

            var xMin = float.MaxValue;
            var yMin = float.MaxValue;
            var xMax = float.MinValue;
            var yMax = float.MinValue;
            var depthMin = float.MaxValue;
            var depthMax = float.MinValue;
            foreach (var point in points)
            {
                var clip = projection * new Vector4(point.x, point.y, point.z, 1f);
                if (clip.w == 0f) continue;
                var x = pixelRect.x + (clip.x / clip.w * 0.5f + 0.5f) * pixelRect.width;
                var y = screenHeight - (pixelRect.y + (clip.y / clip.w * 0.5f + 0.5f) * pixelRect.height);
                var depth = -point.z;
                xMin = Mathf.Min(xMin, x); yMin = Mathf.Min(yMin, y);
                xMax = Mathf.Max(xMax, x); yMax = Mathf.Max(yMax, y);
                depthMin = Mathf.Min(depthMin, depth); depthMax = Mathf.Max(depthMax, depth);
            }
            if (xMin == float.MaxValue) return false;
            rect = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
            eyeDepthNear = depthMin;
            eyeDepthFar = depthMax;
            return true;
        }

        internal static RectInt ToPixelRect(Rect rect)
        {
            var xMin = Mathf.RoundToInt(rect.xMin);
            var yMin = Mathf.RoundToInt(rect.yMin);
            var xMax = Mathf.RoundToInt(rect.xMax);
            var yMax = Mathf.RoundToInt(rect.yMax);
            return new RectInt(xMin, yMin, Mathf.Max(1, xMax - xMin), Mathf.Max(1, yMax - yMin));
        }

        internal static Rect Intersect(Rect first, Rect second)
        {
            var xMin = Mathf.Max(first.xMin, second.xMin);
            var yMin = Mathf.Max(first.yMin, second.yMin);
            var xMax = Mathf.Min(first.xMax, second.xMax);
            var yMax = Mathf.Min(first.yMax, second.yMax);
            return new Rect(xMin, yMin, Mathf.Max(0f, xMax - xMin), Mathf.Max(0f, yMax - yMin));
        }
    }
}
