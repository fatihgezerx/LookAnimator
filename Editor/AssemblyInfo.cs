using System.Runtime.CompilerServices;

// The studio-look Inspector lives in its own assembly (it needs Veneer, which is optional) but reuses the rig checks and
// the Automatic Setup of this one: LookDiagnostics and LookAutoSetup are internal.
[assembly: InternalsVisibleTo("LookAnimator.Editor.Studio")]
