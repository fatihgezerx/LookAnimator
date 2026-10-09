using UnityEngine;

namespace LookAnimation
{
    /// <summary>One bone of the look chain and the share of the body's turn it takes.</summary>
    [System.Serializable]
    public struct LookBone
    {
        /// <summary>The bone. Must be under the Animator's object.</summary>
        public Transform Bone;

        /// <summary>
        /// How the body's share of the turn is divided between the bones that are not the head (only the proportions count).
        /// </summary>
        [Range(0f, 1f)] public float Weight;
    }

    /// <summary>How a <see cref="LookAnimator"/> turns the head and the upper body.</summary>
    [System.Serializable]
    public sealed class LookSettings
    {
        /// <summary>The soft end of the limits starts at this share of the limit (the turn is linear before it).</summary>
        public const float SoftStart = 0.7f;

        /// <summary>Degrees below the stop angle the target must come back to before the look resumes.</summary>
        public const float StopHysteresis = 10f;

        [Range(0f, 1f)] [Tooltip("How much of the turn shows. 0 = the look does nothing, 1 = the full turn.")]
        public float Weight = 1f;

        [Range(20f, 120f)] [Tooltip("Farthest the character turns to the side, in degrees. The last 30 percent is a soft end.")]
        public float MaxYaw = 80f;

        [Range(10f, 60f)] [Tooltip("Farthest it looks up, in degrees.")]
        public float MaxPitchUp = 40f;

        [Range(10f, 60f)] [Tooltip("Farthest it looks down, in degrees.")]
        public float MaxPitchDown = 30f;

        [Range(100f, 180f)] [Tooltip("Past this angle from the front (target behind the character) the look goes back to forward.")]
        public float StopAngle = 140f;

        [Range(0f, 1f)] [Tooltip("How much of the turn the neck and the spine take once they have caught up; the head takes the rest. The head always starts the turn alone and the body follows later, with smaller angles. 0 = only the head turns.")]
        public float BodyTurn = 0.45f;

        [HideInInspector] [Range(60f, 720f)] [Tooltip("Fastest the head turns, in degrees per second (the body is half as fast at most).")]
        public float TurnSpeed = 300f;

        [HideInInspector] [Range(2f, 30f)] [Tooltip("How quickly the head catches up with the target (per second: higher = snappier).")]
        public float HeadFollow = 12f;

        [HideInInspector] [Range(0.5f, 10f)] [Tooltip("How quickly the neck and spine catch up (per second). The spine is the slowest, the neck the quickest of them.")]
        public float BodyFollow = 3f;

        [HideInInspector] [Range(0.05f, 1f)] [Tooltip("Seconds the look takes to fade in.")]
        public float FadeIn = 0.30f;

        [HideInInspector] [Range(0.05f, 1f)] [Tooltip("Seconds the look takes to fade out.")]
        public float FadeOut = 0.35f;
    }
}
