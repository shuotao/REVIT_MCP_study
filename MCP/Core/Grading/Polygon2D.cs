using System;
using System.Collections.Generic;

namespace RevitMCP.Core.Grading
{
    public static class Polygon2D
    {
        public static bool Contains(
            IReadOnlyList<Point2D> polygon,
            Point2D point,
            double tolerance)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return false;
            }

            tolerance = Math.Abs(tolerance);
            if (IsOnBoundary(polygon, point, tolerance))
            {
                return true;
            }

            var inside = false;
            for (var i = 0; i < polygon.Count; i++)
            {
                var start = polygon[i];
                var end = polygon[(i + 1) % polygon.Count];
                var crossesRay = (start.Y > point.Y) != (end.Y > point.Y);
                if (!crossesRay)
                {
                    continue;
                }

                var intersectionX = start.X
                    + ((point.Y - start.Y) * (end.X - start.X) / (end.Y - start.Y));
                if (point.X < intersectionX)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        public static bool Overlaps(
            IReadOnlyList<Point2D> first,
            IReadOnlyList<Point2D> second,
            double tolerance)
        {
            if (first == null || second == null || first.Count < 3 || second.Count < 3)
            {
                return false;
            }

            tolerance = Math.Abs(tolerance);
            for (var firstIndex = 0; firstIndex < first.Count; firstIndex++)
            {
                var firstStart = first[firstIndex];
                var firstEnd = first[(firstIndex + 1) % first.Count];

                for (var secondIndex = 0; secondIndex < second.Count; secondIndex++)
                {
                    var secondStart = second[secondIndex];
                    var secondEnd = second[(secondIndex + 1) % second.Count];
                    if (StrictlyIntersects(
                        firstStart,
                        firstEnd,
                        secondStart,
                        secondEnd,
                        tolerance))
                    {
                        return true;
                    }
                }
            }

            return HasStrictlyContainedVertex(first, second, tolerance)
                || HasStrictlyContainedVertex(second, first, tolerance);
        }

        private static bool HasStrictlyContainedVertex(
            IReadOnlyList<Point2D> candidateVertices,
            IReadOnlyList<Point2D> polygon,
            double tolerance)
        {
            for (var i = 0; i < candidateVertices.Count; i++)
            {
                var candidate = candidateVertices[i];
                if (!IsOnBoundary(polygon, candidate, tolerance)
                    && Contains(polygon, candidate, tolerance))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsOnBoundary(
            IReadOnlyList<Point2D> polygon,
            Point2D point,
            double tolerance)
        {
            for (var i = 0; i < polygon.Count; i++)
            {
                if (IsOnSegment(
                    polygon[i],
                    polygon[(i + 1) % polygon.Count],
                    point,
                    tolerance))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsOnSegment(
            Point2D start,
            Point2D end,
            Point2D point,
            double tolerance)
        {
            var deltaX = end.X - start.X;
            var deltaY = end.Y - start.Y;
            var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
            if (lengthSquared == 0)
            {
                return DistanceSquared(start, point) <= tolerance * tolerance;
            }

            var projection = ((point.X - start.X) * deltaX + (point.Y - start.Y) * deltaY)
                / lengthSquared;
            var projectionTolerance = tolerance / Math.Sqrt(lengthSquared);
            if (projection < -projectionTolerance || projection > 1 + projectionTolerance)
            {
                return false;
            }

            var cross = Cross(start, end, point);
            return Math.Abs(cross) <= tolerance * Math.Sqrt(lengthSquared);
        }

        private static bool StrictlyIntersects(
            Point2D firstStart,
            Point2D firstEnd,
            Point2D secondStart,
            Point2D secondEnd,
            double tolerance)
        {
            var firstLength = Distance(firstStart, firstEnd);
            var secondLength = Distance(secondStart, secondEnd);
            if (firstLength == 0 || secondLength == 0)
            {
                return false;
            }

            var secondStartSide = Cross(firstStart, firstEnd, secondStart);
            var secondEndSide = Cross(firstStart, firstEnd, secondEnd);
            var firstStartSide = Cross(secondStart, secondEnd, firstStart);
            var firstEndSide = Cross(secondStart, secondEnd, firstEnd);

            return AreOnStrictOppositeSides(secondStartSide, secondEndSide, tolerance * firstLength)
                && AreOnStrictOppositeSides(firstStartSide, firstEndSide, tolerance * secondLength);
        }

        private static bool AreOnStrictOppositeSides(double first, double second, double threshold)
        {
            return (first > threshold && second < -threshold)
                || (first < -threshold && second > threshold);
        }

        private static double Cross(Point2D start, Point2D end, Point2D point)
        {
            return ((end.X - start.X) * (point.Y - start.Y))
                - ((end.Y - start.Y) * (point.X - start.X));
        }

        private static double Distance(Point2D first, Point2D second)
        {
            return Math.Sqrt(DistanceSquared(first, second));
        }

        private static double DistanceSquared(Point2D first, Point2D second)
        {
            var deltaX = second.X - first.X;
            var deltaY = second.Y - first.Y;
            return (deltaX * deltaX) + (deltaY * deltaY);
        }
    }
}
