#if !HAS_VENEER
using UnityEditor;
using UnityEngine;

namespace LookAnimation
{
    /// <summary>
    /// The fallback Inspector of <see cref="LookAnimator"/>, used only while Veneer is not in the project: Unity's default
    /// drawing of the fields, the notes that say what would stop the look from working, and the Automatic Setup button. With the kit
    /// installed, <c>LookAnimatorStudioEditor</c> (the studio-look Inspector in the <c>Studio</c> folder) replaces it.
    /// </summary>
    [CustomEditor(typeof(LookAnimator))]
    internal sealed class LookAnimatorEditor : UnityEditor.Editor
    {
        private LookDiagnostics diagnostics;

        private void OnEnable()
        {
            diagnostics = new LookDiagnostics(serializedObject);
        }

        /// <inheritdoc />
        public override void OnInspectorGUI()
        {
            var rig = LookDiagnostics.FindRig((LookAnimator)target);

            DrawDefaultInspector();

            Note(LookDiagnostics.NoAnimator(rig), MessageType.Warning);
            Note(diagnostics.HumanoidAutomatic(rig), MessageType.Info);
            Note(diagnostics.GenericNeedsBones(rig), MessageType.Warning);
            Note(diagnostics.BoneNotUnderRig(rig), MessageType.Warning);
            Note(LookDiagnostics.AnimatePhysics(rig), MessageType.Info);

            using (new EditorGUI.DisabledScope(!LookAutoSetup.CanRun(rig)))
            {
                if (GUILayout.Button(new GUIContent("Automatic Setup", LookAutoSetup.Tooltip(rig))))
                {
                    LookAutoSetup.Run(serializedObject, rig);
                }
            }
        }

        private static void Note(string message, MessageType type)
        {
            if (message != null)
            {
                EditorGUILayout.HelpBox(message, type);
            }
        }
    }
}
#endif
