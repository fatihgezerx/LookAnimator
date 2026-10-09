# LookAnimator

Look Animator is a component that animates the head, neck and spine bones procedurally, so a character looks at a chosen position or object the way a real body does: the head goes first, the neck and the spine follow.

## Overview

Add **Look Animator** to a character, give it something to look at, and the character turns its head towards it on top of whatever the Animator plays. The head starts the turn alone and fast; the neck and the spine follow a moment later, slower and with smaller angles, so the eyes stay on the target while the body settles in behind them. Limits keep the neck from turning farther than a body could, and when the target is behind the character the look eases back to forward instead of wringing the neck.

It works with any pose. A combat stance that twists the body or tilts the head is taken out of the turn, so the head ends up on the target in a stance as well as in a relaxed idle. The look needs no other component, does not touch your Animator Controller or its parameters, and costs nothing while the character has nothing to look at.

## Features

- Looks at a **Transform**, at a **world position** (`LookAt`), or at whatever a plugged-in system says (`ILookSource`)
- Head first, then neck and spine: a **Body Turn** setting says how much of the turn the body takes, and each body bone has its own share
- Limits for left and right, up and down, and a **Stop Angle** after which the look goes back to forward; the end of every limit is soft, not a wall
- Fades in and out, and has an overall **Weight**
- Works over any animation and any pose; a twisted stance does not pull the head off the target
- **Automatic Setup** finds the head, spine, chest and neck of a humanoid rig; on any other rig you set the bones by hand
- No `Update` anywhere: one UniTask loop runs every look, and sleeps while nobody is looking at anything; a look with nothing to do does not even evaluate its animation graph
- The Inspector shows the limits as two small diagrams that follow the sliders, and while playing shows what the look is doing

## Setup

### Requirements

- Unity 2022.3 LTS or newer (made with Unity 6)
- [UniTask](https://github.com/Cysharp/UniTask): the setup guard offers to install it
- A character with an Animator. A humanoid rig is found automatically; any other rig needs the Head and the body bones set by hand

### Installation

Copy the repository into your project's `Assets/` (e.g. `Assets/Scripts/LookAnimator`). `Editor/Setup/DependencyGuard.cs` runs once the scripts load: it sets the `HAS_UNITASK` and `HAS_LOOK_ANIMATOR` scripting symbols and, if UniTask is missing, offers to install it. Until it is installed, Look Animator is left out of compilation, so the project keeps compiling.

## Quick Start

1. Select the character's root object (the one that faces forward and has the Animator on it or under it) and **Add Component > Look Animator**.
2. On a humanoid the bones are filled in for you. Press **Automatic Setup** to see and edit them; on another rig set the **Head** and the **Body bones** (spine, chest, neck: root-most first).
3. Drag what to look at onto **Target**, or leave it empty and set it from code.

From code:

```csharp
using LookAnimation;

var look = character.GetComponent<LookAnimator>();

look.LookAt(player.transform);          // follow a Transform
look.LookAt(new Vector3(3f, 1.6f, 7f)); // or look at a point
look.ClearTarget();                     // stop looking
```

## Settings

| Setting | What it does |
|---|---|
| **Target** | What to look at. Empty = it comes from code or from a plugged-in source |
| **Weight** | How much of the turn shows. 0 = nothing, 1 = the full turn |
| **Max Yaw** | Farthest it turns to the side, in degrees (the last 30 percent is a soft end) |
| **Max Pitch Up / Down** | Farthest it looks up and down, in degrees |
| **Stop Angle** | Past this angle from the front (target behind the character) the look goes back to forward |
| **Head** | The head bone: it turns the most |
| **Body bones** | The bones below the head, root-most first. Each number is that bone's share of the body's turn (only the proportions count) |
| **Body Turn** | How much of the turn the neck and the spine take once they have caught up; the head takes the rest. 0 = only the head turns |

The speeds (how fast the head turns, how quickly the body catches up, the fade times) are hidden to keep the Inspector short; switch the Inspector to **Debug** mode to change them.

## Plugging in another system

A look without a target of its own asks an `ILookSource` what to look at. Implement it on a component of the same object, or register a factory so every look animator that belongs to your system gets a source without a component being added:

```csharp
using LookAnimation;
using UnityEngine;

sealed class DialogueLookSource : ILookSource
{
    public string TargetName => "the speaker";

    public bool TryGetTarget(double now, out Vector3 point, out float weightFactor)
    {
        point = Speaker.Current.HeadPosition;
        weightFactor = 1f;          // 0 to 1: how much of the look shows right now
        return Speaker.Current != null;
    }
}

[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void Register() => LookSources.Register(look => look.GetComponent<Talker>() != null ? new DialogueLookSource() : null);
```

The [Combat System](https://github.com/fatihgezerx/CombatSystem) does exactly this for its fighters (its `CombatSystem.Look` assembly): a fighter looks at its lock-on or brain target, and eases off while it dodges or is staggered.

## Notes

- The turn is an animation job in a **playable graph of its own**, with one output on the character's Animator that is evaluated after the Animator's own pose (the same way Animation Rigging works). It needs no other component and leaves the Animator Controller and its parameters alone. Its output order is 2000, above Animation Rigging's 1000.
- Keep the Animator's **Update Mode** on Normal: with Animate Physics the job sees the angles up to one frame late.
- To tell where the animation itself points the head, the look reads the head bone's forward axis **once**, the first time it attaches, while the model still stands in the pose it was made in (a T-pose faces forward). Add the component before any animation has played on the character, as is normal when it is on the prefab or in the scene.
- The look is registered with `LookManager`, one UniTask loop at the start of the late update (before the animation is evaluated). The loop only wakes a few times a second while no look has anything to do.
- Older setups kept the head as the last entry of the chain. That still works: when the Head field is empty, the last entry is the head. When Head is set, it is the head and the chain holds the bones below it.
- A bone has to be under the Animator's object. Missing or foreign bones are skipped, with one warning in the Console.

## License

[MIT License](LICENSE)
