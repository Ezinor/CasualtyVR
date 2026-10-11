using UnityEngine;

namespace CasualtyVR
{
    /// <summary>
    /// The one mapping from tracking space (OpenXR LOCAL, converted to Unity axes, metres) to the game world,
    /// shared by the eye cameras, the controllers, the laser, the hands and the UI panels, so they always agree.
    ///
    /// * Diorama: the game plane is DioramaDistance metres in front of the seated origin, UnitsPerMetre world
    ///   units per metre, no rotation (world +z = away from the player).
    /// * First person (FirstPerson.cs): the seated origin is the character's eye, the player's forward is the
    ///   character's facing along world x (yaw ±90°), FirstPerson.Scale units per metre.
    /// </summary>
    internal static class VrSpace
    {
        public static bool FirstPerson { get; private set; }
        /// <summary>World units per metre.</summary>
        public static float Scale { get; private set; } = 25f;
        /// <summary>Tracking-to-world rotation.</summary>
        public static Quaternion Rotation { get; private set; } = Quaternion.identity;

        private static Vector3 trackOrigin, worldOrigin;

        public static void SetDiorama(Vector3 focus, Vector3 seatedOrigin)
        {
            FirstPerson = false;
            Scale = Plugin.UnitsPerMetre.Value;
            Rotation = Quaternion.identity;
            trackOrigin = seatedOrigin + new Vector3(0f, 0f, Plugin.DioramaDistance.Value);
            worldOrigin = focus;
        }

        public static void SetFirstPerson(Vector3 eyeWorld, Vector3 seatedOrigin, float yawDegrees, float scale)
        {
            FirstPerson = true;
            Scale = scale;
            Rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            trackOrigin = seatedOrigin;
            worldOrigin = eyeWorld;
        }

        /// <summary>The player's head (centre eye) in world space, this frame.</summary>
        public static Vector3 HeadPosition { get; private set; }
        public static Vector3 HeadForward { get; private set; } = Vector3.forward;

        public static void SetHead(Vector3 trackingPos, Quaternion trackingRot)
        {
            HeadPosition = Point(trackingPos);
            HeadForward = Rot(trackingRot) * Vector3.forward;
        }

        /// <summary>First person: the character's (smoothed) eye in world space, which the seated origin maps to.</summary>
        public static Vector3 Anchor => worldOrigin;

        public static Vector3 Point(Vector3 tracking) => worldOrigin + Rotation * (tracking - trackOrigin) * Scale;
        public static Quaternion Rot(Quaternion tracking) => Rotation * tracking;

        // OpenXR is right-handed with -Z forward; Unity is left-handed with +Z forward.
        public static Vector3 ToUnity(XrNative.Vec3 p) => new Vector3(p.x, p.y, -p.z);
        public static Quaternion ToUnity(XrNative.Quat q) => new Quaternion(-q.x, -q.y, q.z, q.w);
    }
}
