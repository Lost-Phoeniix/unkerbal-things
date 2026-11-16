// Dread: where a monster may appear or disappear. Pure C# (no UnityEngine); tested offline.
//
// A body that is 60 m tall is seen over ridges that would hide a person, so every test is made with the whole body: its feet, its
// middle and its head (BodyHeight) against the terrain, and its bounding points against the camera frustum. A spot is hidden when
//   no sample point of the body has line of sight to the craft (terrain and the curvature of the planet in between), AND
//   no sample point of the body is inside the camera frustum (with a margin, so that a turning camera does not catch it).
// Both are required. Frame: x, y are metres on the ground plane, z is the altitude above the sea (or any common datum); the camera
// view is given in the same cartesian frame. In the game the camera frame is the world frame and the terrain is the HuntTerrain.
using System;

namespace UnkerbalThings.Dread
{
    /// <summary>A camera: position, orthonormal forward/up/right, and the half angles as tangents.</summary>
    public struct SpawnView
    {
        public double Px, Py, Pz, Fx, Fy, Fz, Ux, Uy, Uz, Rx, Ry, Rz, TanHalfV, TanHalfH;

        /// <summary>Camera at p looking along forward (any length) with up hint; vertical field of view in degrees, aspect = width / height.</summary>
        public static SpawnView Make(double px, double py, double pz, double fx, double fy, double fz, double upx, double upy, double upz, double vfovDeg, double aspect)
        {
            SpawnView v = new SpawnView();
            v.Px = px; v.Py = py; v.Pz = pz;
            double fl = Math.Sqrt(fx * fx + fy * fy + fz * fz);
            if (fl < 1e-12) { fx = 1; fy = 0; fz = 0; fl = 1; }
            fx /= fl; fy /= fl; fz /= fl;
            // right = forward x up
            double rx = fy * upz - fz * upy, ry = fz * upx - fx * upz, rz = fx * upy - fy * upx;
            double rl = Math.Sqrt(rx * rx + ry * ry + rz * rz);
            if (rl < 1e-9) { rx = 0; ry = 1; rz = 0; rl = 1; }
            rx /= rl; ry /= rl; rz /= rl;
            v.Fx = fx; v.Fy = fy; v.Fz = fz;
            v.Rx = rx; v.Ry = ry; v.Rz = rz;
            v.Ux = ry * fz - rz * fy; v.Uy = rz * fx - rx * fz; v.Uz = rx * fy - ry * fx;
            v.TanHalfV = Math.Tan(vfovDeg * Math.PI / 360.0);
            v.TanHalfH = v.TanHalfV * aspect;
            return v;
        }
    }

    public static class DreadSpawnRule
    {
        public const double BodyHeight = 65.0;     // feet to top of the head (the Moho creature stands about 40 m at the head, reaches 65 with its arms up)
        public const double BodyHalfWidth = 14.0;  // sideways reach of the body, hands included

        /// <summary>True when the point is inside the frustum, widened by margin (0.25 = 25% wider than the real view).</summary>
        public static bool PointInView(in SpawnView v, double x, double y, double z, double margin)
        {
            double dx = x - v.Px, dy = y - v.Py, dz = z - v.Pz;
            double f = dx * v.Fx + dy * v.Fy + dz * v.Fz;
            if (f <= 0.1) return false;
            double r = dx * v.Rx + dy * v.Ry + dz * v.Rz;
            double u = dx * v.Ux + dy * v.Uy + dz * v.Uz;
            double k = 1.0 + margin;
            return Math.Abs(r) <= f * v.TanHalfH * k && Math.Abs(u) <= f * v.TanHalfV * k;
        }

        /// <summary>True when any of the body's sample points (feet, knee, middle, shoulder, head; centre and both sides) is inside the widened frustum.
        /// (x, y, z) is the foot point, (ux, uy, uz) the unit vector away from the ground there (0, 0, 1 on a flat plane).</summary>
        public static bool BodyInView(in SpawnView v, double x, double y, double z, double ux, double uy, double uz, double margin)
        {
            double dx = x - v.Px, dy = y - v.Py, dz = z - v.Pz;
            // sideways unit vector: perpendicular to the way up and to the line from the camera to the body
            double sx = uy * dz - uz * dy, sy = uz * dx - ux * dz, sz = ux * dy - uy * dx;
            double sl = Math.Sqrt(sx * sx + sy * sy + sz * sz);
            if (sl > 1e-9) { sx /= sl; sy /= sl; sz /= sl; } else { sx = 1; sy = 0; sz = 0; }
            for (int h = 0; h < 14; h++)
            {
                double hh = BodyHeight * h / 13.0;
                for (int s = -1; s <= 1; s++)
                {
                    double o = s * BodyHalfWidth;
                    if (PointInView(in v, x + ux * hh + sx * o, y + uy * hh + sy * o, z + uz * hh + sz * o, margin)) return true;
                }
            }
            return false;
        }

        /// <summary>Line of sight between two points over the terrain, with the planet's curvature (radius in metres, 0 = flat). Samples every stepMetres.</summary>
        public static bool LineOfSight(IHuntTerrain terrain, double radius, double ax, double ay, double az, double bx, double by, double bz, double stepMetres)
        {
            double dx = bx - ax, dy = by - ay;
            double len = Math.Sqrt(dx * dx + dy * dy);
            int n = (int)Math.Min(400.0, Math.Max(8.0, Math.Ceiling(len / Math.Max(stepMetres, 5.0))));
            for (int i = 1; i < n; i++)
            {
                double t = (double)i / n;
                double px = ax + dx * t, py = ay + dy * t;
                // everything is measured on the plane that touches the planet below the first point: a point s metres away lies s2 / 2R lower
                double s = len * t;
                double endDrop = radius > 1.0 ? len * len / (2.0 * radius) : 0.0;
                double rayZ = az + (bz - endDrop - az) * t;
                double drop = radius > 1.0 ? s * s / (2.0 * radius) : 0.0;
                // blocked only when the terrain is clearly above the ray (3 m): a crest between two samples must not make a hidden spot out of a visible one
                if (terrain.Height(px, py) - drop >= rayZ + 3.0) return false;
            }
            return true;
        }

        /// <summary>The craft (a point 6 m above its ground) sees any sample point of the body standing at (x, y)?</summary>
        public static bool BodySeenFromCraft(IHuntTerrain terrain, double radius, double craftX, double craftY, double craftZ, double x, double y, double stepMetres)
        {
            double gz = terrain.Height(x, y);
            for (int h = 13; h >= 0; h--)
            {
                // the head first: it is the part that shows over a ridge
                if (LineOfSight(terrain, radius, craftX, craftY, craftZ, x, y, gz + BodyHeight * h / 13.0, stepMetres)) return true;
            }
            return false;
        }

        /// <summary>The body at (x, y) is hidden: nobody on the craft sees it and the camera does not have it in frame.</summary>
        public static bool IsHidden(IHuntTerrain terrain, double radius, in SpawnView view, double craftX, double craftY, double craftZ, double x, double y, double margin, double stepMetres)
        {
            double gz = terrain.Height(x, y);
            if (BodyInView(in view, x, y, gz, 0.0, 0.0, 1.0, margin)) return false;
            if (BodySeenFromCraft(terrain, radius, craftX, craftY, craftZ, x, y, stepMetres)) return false;
            return true;
        }

        /// <summary>The body is exposed: inside the frame with a line of sight to the craft. The test of a spawn or a despawn that must not be seen.</summary>
        public static bool IsExposed(IHuntTerrain terrain, double radius, in SpawnView view, double craftX, double craftY, double craftZ, double x, double y, double stepMetres)
        {
            double gz = terrain.Height(x, y);
            return BodyInView(in view, x, y, gz, 0.0, 0.0, 1.0, 0.0) && BodySeenFromCraft(terrain, radius, craftX, craftY, craftZ, x, y, stepMetres);
        }
    }
}
