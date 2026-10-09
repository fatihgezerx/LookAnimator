# Changelog

## [1.0.0] - 2026-10-09

First release. It grew out of the Combat System's *Fighter Look* and now stands on its own.

### Added
- `LookAnimator` component: turns the head, neck and spine towards a `Target`, a world position (`LookAt(Vector3)`) or what an `ILookSource` says. The head starts the turn alone and fast, the neck and spine follow later with smaller angles (`Body Turn`).
- Limits for left / right (`Max Yaw`), up and down (`Max Pitch Up`, `Max Pitch Down`) with a soft end, a `Stop Angle` after which the look goes back to forward, an overall `Weight` and fade times.
- Works in any pose: the head's own aim in the animated pose (a combat stance twists the body and tilts the head) is taken out of the turn, so the head ends up on the target.
- The turn runs in a playable graph of its own, with one output on the character's Animator evaluated after the Animator's own pose (output order 2000). It needs no other component and leaves the Animator Controller and its parameters alone. The graph sleeps while the look has nothing to show.
- `ILookSource` and `LookSources` (register a factory): another system decides what a look animator looks at, without a component being added to the character.
- Inspector: a banner with the state (ready / what is missing, or what it is looking at and how strongly while playing), a Target card, a Limits card with two live diagrams, a Bones card with **Automatic Setup** (the head, spine, chest and neck of a humanoid), a reorderable list of body bones with their share in percent, and notes that say in words what would stop the look from working.
- `LookManager`: one UniTask loop for every look, which sleeps while nobody has anything to look at.
- `Editor/Setup/DependencyGuard.cs`: sets `HAS_UNITASK` and `HAS_LOOK_ANIMATOR` and offers to install UniTask; without UniTask the system is left out of compilation.

### Changed (from the Combat System's Fighter Look)
- The component is `LookAnimator` (namespace `LookAnimation`) and no longer needs a `Fighter` or a `FighterAnimator`. Saved components keep working: the settings, the head and the chain are read as before.
- The **Head** field is the head bone. The chain holds the bones below it (spine, chest, neck). An older setup whose chain ended with the head still works: when Head is empty, the last chain entry is the head.
- `Add humanoid chain` is now **Automatic Setup**.
- The Combat-only settings (*Attack Weight* and the search for something to look at) moved to the Combat System's own bridge, `CombatSystem.Look`.
