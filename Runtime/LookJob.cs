using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;

namespace LookAnimation
{
    /// <summary>
    /// The animation job that turns a character's head and upper body towards a target. It runs inside the look's own Playables graph,
    /// after the Animator's pose, so it sees the finished pose of every layer: it adds its turn to the animated rotation of each
    /// bone of the chain (in world space, root to head, each by its own angle). The main thread only writes the frame, the weight and each bone's two angles, once a frame.
    /// </summary>
    /// <remarks>
    /// A plain managed job (no Burst, no Unity.Mathematics): a handful of bones per character costs microseconds. It holds only
    /// blittable data. The arrays are <see cref="Allocator.Persistent"/> and belong to <see cref="LookAnimator"/>, which disposes
    /// them after the graph is destroyed.
    /// </remarks>
    internal struct LookJob : IAnimationJob
    {
        private const float MinWeight = 0.0001f;

        /// <summary>The chain bones, root-most first, the head last.</summary>
        public NativeArray<TransformStreamHandle> Chain;

        /// <summary>Degrees to the right of the character's forward that each chain bone turns by itself (same length as <see cref="Chain"/>).</summary>
        public NativeArray<float> ChainYaw;

        /// <summary>Degrees above the horizon that each chain bone turns by itself, positive = up (same length as <see cref="Chain"/>).</summary>
        public NativeArray<float> ChainPitch;

        /// <summary>Scratch for the animated world rotations of the chain bones (same length as <see cref="Chain"/>).</summary>
        public NativeArray<Quaternion> ChainRotations;

        /// <summary>
        /// The share of any turn that each chain bone takes once the look has settled (same length as <see cref="Chain"/>, adds up
        /// to 1): the animated pose's own aim is taken out in these shares.
        /// </summary>
        public NativeArray<float> ChainShare;

        /// <summary>The head's forward in its own bone space (found while the character stands in its neutral pose).</summary>
        public Vector3 HeadAxis;

        /// <summary>The character's world rotation this frame; the angles are measured in it.</summary>
        public Quaternion Frame;

        /// <summary>0 = the animation untouched, 1 = the full turn.</summary>
        public float Weight;

        /// <inheritdoc />
        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        /// <inheritdoc />
        public void ProcessAnimation(AnimationStream stream)
        {
            if (!stream.isValid || Weight <= MinWeight || !Chain.IsCreated)
            {
                return;
            }

            // The animated world rotation of every chain bone, read before anything is written, so none of them can
            // be a value that an earlier write already moved.
            var count = Chain.Length;
            for (var i = 0; i < count; i++)
            {
                var handle = Chain[i];
                if (handle.IsValid(stream))
                {
                    ChainRotations[i] = handle.GetRotation(stream);
                }
            }

            // Root to head. Each bone turns by its own yaw (about the character's up) and pitch (about its right; Unity's positive
            // X rotation pitches down and Pitch is positive upwards, hence the minus), and carries the turn of every bone above
            // it, so the world rotation it is given is its animated one with all the turns up to it laid on top. A write converts
            // that world rotation using the parent's current pose, which is why the parent is written first.
            var inverseFrame = Quaternion.Inverse(Frame);

            // Where the animation itself points the head (a combat stance twists the body and tilts the head; the relaxed pose
            // points it straight ahead). The angles from the main thread are measured from "straight ahead", so this offset is
            // taken out again, and the head ends up on the target whatever the pose.
            var poseYaw = 0f;
            var posePitch = 0f;
            var last = count - 1;
            if (Chain[last].IsValid(stream) && HeadAxis.sqrMagnitude > 0.5f)
            {
                var aimed = inverseFrame * (ChainRotations[last] * HeadAxis);
                var flat = Mathf.Sqrt(aimed.x * aimed.x + aimed.z * aimed.z);
                if (flat > 0.2f)
                {
                    poseYaw = Mathf.Atan2(aimed.x, aimed.z) * Mathf.Rad2Deg;
                    posePitch = Mathf.Atan2(aimed.y, flat) * Mathf.Rad2Deg;
                }
            }

            var cumulative = Quaternion.identity;
            for (var i = 0; i < count; i++)
            {
                var handle = Chain[i];
                if (!handle.IsValid(stream))
                {
                    continue;
                }

                var yaw = ChainYaw[i] - poseYaw * ChainShare[i];
                var pitch = ChainPitch[i] - posePitch * ChainShare[i];
                var own = Frame * Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(-pitch, Vector3.right) * inverseFrame;
                cumulative = Quaternion.Slerp(Quaternion.identity, own, Weight) * cumulative;
                handle.SetRotation(stream, cumulative * ChainRotations[i]);
            }
        }
    }

    /// <summary>The small pure rules of the look, kept free of Unity objects.</summary>
    internal static class LookState
    {
        /// <summary>
        /// Limits an angle to <paramref name="limit"/> degrees with a soft end: unchanged up to <see cref="LookSettings.SoftStart"/>
        /// of the limit, then an asymptote that never quite reaches it. Symmetric (the sign is kept).
        /// </summary>
        public static float SoftClamp(float angle, float limit)
        {
            var magnitude = Mathf.Abs(angle);
            var knee = limit * LookSettings.SoftStart;
            if (magnitude <= knee)
            {
                return angle;
            }

            var range = limit - knee;
            if (range <= 0.0001f)
            {
                return angle < 0f ? -limit : limit;
            }

            var clamped = knee + range * (1f - Mathf.Exp(-(magnitude - knee) / range));
            return angle < 0f ? -clamped : clamped;
        }
    }
}
