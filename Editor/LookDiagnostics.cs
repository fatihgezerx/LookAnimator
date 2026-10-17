using UnityEditor;
using UnityEngine;

namespace LookAnimation
{
    /// <summary>
    /// Says in words what would stop a <see cref="LookAnimator"/> from working, from the serialized head and body bones and the
    /// Animator it would use. UI-free: the studio Inspector shows each answer as a hint row and the fallback Inspector as a help box.
    /// Every check returns the message, or null when there is nothing to say.
    /// </summary>
    internal sealed class LookDiagnostics
    {
        private readonly SerializedObject serializedObject;
        private readonly SerializedProperty head;
        private readonly SerializedProperty chain;

        /// <summary>Reads the head and the body bones of <paramref name="serializedObject"/> (a Look Animator).</summary>
        internal LookDiagnostics(SerializedObject serializedObject)
        {
            this.serializedObject = serializedObject;
            head = serializedObject.FindProperty("_head");
            chain = serializedObject.FindProperty("_chain");
        }

        /// <summary>The Animator the look would use: on the object or under it (the same search the component makes).</summary>
        internal static Animator FindRig(Component look) => look != null ? look.GetComponentInChildren<Animator>() : null;

        /// <summary>Whether any entry of the body bone list has a bone.</summary>
        internal static bool HasBones(SerializedProperty chain)
        {
            for (var i = 0; i < chain.arraySize; i++)
            {
                if (chain.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(LookBone.Bone)).objectReferenceValue != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether the head or any body bone is set.</summary>
        internal bool HasAnyBone
        {
            get
            {
                serializedObject.UpdateIfRequiredOrScript();
                return head.objectReferenceValue != null || HasBones(chain);
            }
        }

        /// <summary>No Animator on the object or under it.</summary>
        internal static string NoAnimator(Animator rig)
        {
            return rig == null ? "There is no Animator on this object or under it, so the look has no bones to turn." : null;
        }

        /// <summary>A humanoid with no bones set: they are found by themselves when the game starts.</summary>
        internal string HumanoidAutomatic(Animator rig)
        {
            if (rig == null || !rig.isHuman || HasAnyBone)
            {
                return null;
            }

            return "Nothing is set: the spine, chest, neck and head are found by themselves when the game starts. Press Automatic Setup to see and edit them.";
        }

        /// <summary>A generic rig with no bones set: the look would do nothing.</summary>
        internal string GenericNeedsBones(Animator rig)
        {
            if (rig == null || rig.isHuman || HasAnyBone)
            {
                return null;
            }

            return "This is a generic rig: set the Head and the body bones, otherwise the look does nothing.";
        }

        /// <summary>The first bone that is not under the Animator's object (and how many more), because it is skipped.</summary>
        internal string BoneNotUnderRig(Animator rig)
        {
            if (rig == null)
            {
                return null;
            }

            serializedObject.UpdateIfRequiredOrScript();
            string first = null;
            var count = 0;
            Check(rig, head.objectReferenceValue as Transform, "The head", ref first, ref count);
            for (var i = 0; i < chain.arraySize; i++)
            {
                var bone = chain.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(LookBone.Bone)).objectReferenceValue as Transform;
                Check(rig, bone, "The body bone", ref first, ref count);
            }

            if (count == 0)
            {
                return null;
            }

            return count == 1 ? first : first + " " + (count - 1) + " more bone(s) are skipped too.";
        }

        /// <summary>The Animator updates with the physics, so the look sees the angles up to one frame late.</summary>
        internal static string AnimatePhysics(Animator rig)
        {
            if (rig == null || rig.updateMode != AnimatorUpdateMode.Fixed)
            {
                return null;
            }

            return $"The Animator on '{rig.name}' updates with the physics (Animate Physics), so the look sees the angles up to one frame late. Set its Update Mode to Normal.";
        }

        private static void Check(Animator rig, Transform bone, string what, ref string first, ref int count)
        {
            if (bone == null || bone.IsChildOf(rig.transform))
            {
                return;
            }

            if (count == 0)
            {
                first = $"{what} '{bone.name}' is not under the Animator's object '{rig.name}', so it is skipped.";
            }

            count++;
        }
    }
}
