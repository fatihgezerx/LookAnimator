using UnityEditor;
using UnityEngine;

namespace LookAnimation
{
    /// <summary>
    /// The Inspector's Automatic Setup: fills the head and the body bones of a Look Animator from its humanoid rig, as one undo step.
    /// UI-free apart from its two dialogs, so the studio Inspector and the fallback Inspector share it.
    /// </summary>
    internal static class LookAutoSetup
    {
        private const string Title = "Automatic Setup";

        /// <summary>Whether Automatic Setup can run: it needs a humanoid rig.</summary>
        internal static bool CanRun(Animator rig) => rig != null && rig.isHuman;

        /// <summary>The tooltip of the Automatic Setup button.</summary>
        internal static string Tooltip(Animator rig)
        {
            return CanRun(rig)
                ? "Fills the head and the body bones (spine, chest, neck) from the character's humanoid rig. You can edit them afterwards."
                : "Needs a humanoid rig. On any other rig, set the bones by hand.";
        }

        /// <summary>
        /// Fills the Head and the body bones of the Look Animator behind <paramref name="serializedObject"/> from <paramref name="rig"/>.
        /// Asks before replacing bones that are already set, and says so when the rig has no head bone.
        /// </summary>
        internal static void Run(SerializedObject serializedObject, Animator rig)
        {
            if (!CanRun(rig))
            {
                return;
            }

            serializedObject.Update();
            var head = serializedObject.FindProperty("_head");
            var chain = serializedObject.FindProperty("_chain");

            if (LookDiagnostics.HasBones(chain) &&
                !EditorUtility.DisplayDialog(Title, "This replaces the head and the body bones with the ones found on the humanoid rig.", "Replace", "Cancel"))
            {
                return;
            }

            if (!LookDefaults.FillHumanoid(rig, out var found, out var bones))
            {
                EditorUtility.DisplayDialog(Title, "No head bone was found on this humanoid rig. Set the bones by hand.", "OK");
                return;
            }

            head.objectReferenceValue = found;
            chain.arraySize = bones.Length;
            for (var i = 0; i < bones.Length; i++)
            {
                var entry = chain.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative(nameof(LookBone.Bone)).objectReferenceValue = bones[i].Bone;
                entry.FindPropertyRelative(nameof(LookBone.Weight)).floatValue = bones[i].Weight;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
