using UnityEditor;
using UnityEngine;

namespace LookAnimation
{
    /// <summary>
    /// The computed texts of one <see cref="LookAnimator"/> for its Inspector: the header state pill, the live strip and the rig checks.
    /// Texts are cached against what they were made from (the target's name, the rounded numbers), so the 200 ms polls allocate only
    /// when something changed. One instance belongs to one built Inspector item (it holds the component it was made for, never a static).
    /// </summary>
    internal sealed class LookAnimatorStatus
    {
        private readonly LookAnimator look;
        private string shownTargetName;
        private string pillLookingText = string.Empty;
        private int liveKey = int.MinValue;
        private string liveText = string.Empty;

        /// <summary>Makes the status of the component behind <paramref name="serializedObject"/>.</summary>
        /// <param name="serializedObject">The serialized Look Animator.</param>
        internal LookAnimatorStatus(SerializedObject serializedObject)
        {
            look = serializedObject.targetObject as LookAnimator;
            Diagnostics = new LookDiagnostics(serializedObject);
        }

        /// <summary>The rig checks (hints) of this component.</summary>
        internal LookDiagnostics Diagnostics { get; }

        /// <summary>The component this status describes (may be null once destroyed).</summary>
        internal LookAnimator Look => look;

        /// <summary>The Animator the look would use: on the object or under it.</summary>
        internal Animator Rig()
        {
            return LookDiagnostics.FindRig(look);
        }

        /// <summary>The header pill: ready or what is missing in the editor; what it is looking at (or not working / idle) while playing.</summary>
        internal (string text, string kind) PillState()
        {
            if (look == null)
            {
                return (string.Empty, "idle");
            }

            if (Application.isPlaying)
            {
                if (!look.isActiveAndEnabled || !look.IsAttached)
                {
                    return ("Not working", "error");
                }

                if (!look.IsLooking)
                {
                    return ("Idle", "idle");
                }

                string name = look.TargetName;
                if (!string.Equals(name, shownTargetName))
                {
                    shownTargetName = name;
                    pillLookingText = "Looking at " + name;
                }

                return (pillLookingText, "ok");
            }

            Animator rig = Rig();
            if (rig == null)
            {
                return ("Needs an Animator", "warn");
            }

            if (!rig.isHuman && !Diagnostics.HasAnyBone)
            {
                return ("Needs a head bone", "warn");
            }

            return ("Ready", "ok");
        }

        /// <summary>The live strip text (Play Mode only): the weight now and the angles to the target; rebuilt only when a rounded number changed.</summary>
        internal string LiveText()
        {
            if (look == null)
            {
                return string.Empty;
            }

            int weight = Mathf.RoundToInt(look.CurrentWeight * 100f);
            bool looking = look.IsLooking;
            int yaw = looking ? Mathf.RoundToInt(look.YawToTarget) : 0;
            int pitch = looking ? Mathf.RoundToInt(look.PitchToTarget) : 0;
            int key = (((weight * 2 + (looking ? 1 : 0)) * 721 + yaw + 360) * 361) + pitch + 180;
            if (key != liveKey)
            {
                liveKey = key;
                liveText = looking
                    ? "weight " + weight + "%   yaw " + yaw + " deg   pitch " + pitch + " deg"
                    : "nothing to look at   weight " + weight + "%";
            }

            return liveText;
        }
    }
}
