using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;

namespace LookAnimation
{
    /// <summary>
    /// Turns a character's head, neck and spine towards a target, on top of whatever the Animator plays: the <see cref="Target"/>
    /// you set, a position given to <see cref="LookAt(Vector3)"/>, or what an <see cref="ILookSource"/> says. The head starts the
    /// turn alone and fast; the neck and the spine follow later and slower, with smaller angles (the body's share is
    /// <see cref="LookSettings.BodyTurn"/>). It works with any pose: a stance that twists the body or tilts the head is taken out of
    /// the turn, so the head ends up on the target.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Put it on the character's root (the object that faces forward) with an Animator on it or under it. The turn is an animation
    /// job in a playable graph of its own that writes to the same Animator after everything else, so it needs no other component and
    /// does not touch the Animator's controller or its parameters. Nothing runs per character per frame:
    /// <see cref="LookManager"/> runs one loop for all looks, which sleeps while nobody has anything to look at. A humanoid
    /// finds its bones by itself (<c>Automatic Setup</c> in the Inspector shows them); any other rig needs the Head and the
    /// body bones set by hand.
    /// </para>
    /// <para>Keep the Animator's Update Mode on Normal: with Animate Physics the job sees the angles up to one frame late.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Look Animator/Look Animator")]
    public sealed class LookAnimator : MonoBehaviour
    {
        // Above the Animator's own pose and above Animation Rigging (which uses 1000): the look has the last word.
        private const ushort OutputOrder = 2000;
        private const float Epsilon = 0.0005f;

        [SerializeField] private LookSettings _settings = new LookSettings();

        [SerializeField] [Tooltip("What to look at. Empty = it comes from code (LookAt) or from another system that plugs in.")]
        private Transform _target;

        [SerializeField] [Tooltip("The head bone: it turns the most. Found by itself on a humanoid.")]
        private Transform _head;

        [SerializeField] [Tooltip("The bones below the head that turn, root-most first (spine, chest, neck). The numbers say how the body's turn is divided between them. Empty = the head alone, or on a humanoid the spine, chest and neck.")]
        private LookBone[] _chain;

        private Animator _animator;
        private ILookSource _source;
        private bool _armed; // registered with the manager
        private bool _attached; // the graph is built
        private bool _playing; // the graph is running (it sleeps while the look has nothing to do)
        private bool _noBones; // the bones could not be resolved: not tried again until the component is enabled again
        private PlayableGraph _graph;
        private AnimationScriptPlayable _node;
        private NativeArray<TransformStreamHandle> _chainHandles;
        private NativeArray<float> _chainYaw;
        private NativeArray<float> _chainPitch;
        private NativeArray<Quaternion> _chainRotations;
        private NativeArray<float> _chainShare;
        private float[] _bodyShares; // of the bones before the head: their share of the whole turn once settled
        private Transform _originBone;
        private Transform _axisBone; // the head bone whose forward axis was found
        private Vector3 _headAxis; // that bone's forward in its own space, found in the neutral pose the first time it attaches
        private HashSet<string> _warned;

        private Vector3 _point;
        private bool _hasPoint;
        private float _yawDesired;
        private float _pitchDesired;
        private float _weight;
        private float _writtenWeight;
        private bool _behind;
        private bool _looking;

        /// <summary>How the character looks. Can be changed at run time.</summary>
        public LookSettings Settings => _settings;

        /// <summary>What the character looks at. Setting it replaces a position given to <see cref="LookAt(Vector3)"/>.</summary>
        public Transform Target
        {
            get => _target;
            set
            {
                _target = value;
                _hasPoint = false;
            }
        }

        /// <summary>How much of the turn shows right now, 0 to 1 (it fades in and out).</summary>
        public float CurrentWeight => _weight;

        /// <summary>Whether the graph is built (false while the look has no usable bones, no Animator, or is not enabled yet).</summary>
        public bool IsAttached => _attached;

        /// <summary>Whether there was something to look at at the last step.</summary>
        public bool IsLooking => _looking;

        /// <summary>Degrees to the right of the character's forward that the target is, as of the last step (no limits applied).</summary>
        public float YawToTarget => _yawDesired;

        /// <summary>Degrees above the horizon that the target is, as of the last step (no limits applied).</summary>
        public float PitchToTarget => _pitchDesired;

        /// <summary>Name of what is looked at, or null.</summary>
        public string TargetName
        {
            get
            {
                if (!_looking)
                {
                    return null;
                }

                if (_target != null)
                {
                    return _target.name;
                }

                return _hasPoint ? "a point" : _source?.TargetName;
            }
        }

        /// <summary>Looks at <paramref name="target"/> (null = stops looking at anything it was given).</summary>
        public void LookAt(Transform target) => Target = target;

        /// <summary>Looks at a world position until <see cref="ClearTarget"/>, a new position or a new target.</summary>
        public void LookAt(Vector3 worldPosition)
        {
            _target = null;
            _point = worldPosition;
            _hasPoint = true;
        }

        /// <summary>Stops looking at what <see cref="Target"/> or <see cref="LookAt(Vector3)"/> gave; a plugged-in source takes over again.</summary>
        public void ClearTarget()
        {
            _target = null;
            _hasPoint = false;
        }

        #region Lifecycle

        private void OnEnable()
        {
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null)
            {
                Warn("no animator", $"[LookAnimator] '{name}' has no Animator on it or under it, so it cannot turn the head and does nothing.");
                return;
            }

            _source = FindSource();
            _noBones = false;
            _armed = true;
            LookManager.Add(this);
        }

        private void Start()
        {
            // Every Animator of the hierarchy is enabled by now; the manager keeps trying if not.
            if (_armed)
            {
                Attach();
            }
        }

        private void OnDisable()
        {
            if (_armed)
            {
                _armed = false;
                LookManager.Remove(this);
            }

            Detach();
            _source = null;
            _weight = 0f;
            _writtenWeight = 0f;
            _behind = false;
            _looking = false;
        }

        private void OnDestroy()
        {
            // Safety net: normally everything is already gone in OnDisable.
            DisposeArrays();
        }

#if UNITY_EDITOR
        // Adding the component to a humanoid fills the bones, so it works without further setup.
        private void Reset()
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator != null && LookDefaults.FillHumanoid(animator, out var head, out var chain))
            {
                _head = head;
                _chain = chain;
            }
        }
#endif

        // A component of the object that says what to look at, else a factory of a system that plugs in.
        private ILookSource FindSource()
        {
            return TryGetComponent<ILookSource>(out var own) ? own : LookSources.Create(this);
        }

        #endregion

        #region Graph

        /// <summary>
        /// Builds the bone handles, the job and a playable graph of its own that writes to the Animator after the animation. Does
        /// nothing when it is already built, when the Animator is not active yet, or when there is no usable bone (one warning).
        /// </summary>
        internal void Attach()
        {
            if (_attached || _noBones || _animator == null || !_animator.isActiveAndEnabled)
            {
                return;
            }

            if (!ResolveBones(_animator, out var chainBones, out var chainShares))
            {
                _noBones = true;
                Warn("no bones", $"[LookAnimator] '{name}' has no usable bone to turn (set the Head and the body bones, or press Automatic Setup on a humanoid; a bone must be under the Animator's object), so the look does nothing.");
                return;
            }

            var chainCount = chainBones.Length;
            _originBone = chainBones[chainCount - 1];

            // The handles are bound on the main thread, now that the Animator is active; the arrays live until Detach.
            DisposeArrays();
            _chainHandles = new NativeArray<TransformStreamHandle>(chainCount, Allocator.Persistent);
            _chainYaw = new NativeArray<float>(chainCount, Allocator.Persistent);
            _chainPitch = new NativeArray<float>(chainCount, Allocator.Persistent);
            _chainRotations = new NativeArray<Quaternion>(chainCount, Allocator.Persistent);
            _chainShare = new NativeArray<float>(chainCount, Allocator.Persistent);
            for (var i = 0; i < chainCount; i++)
            {
                _chainHandles[i] = _animator.BindStreamTransform(chainBones[i]);
                _chainRotations[i] = Quaternion.identity;
            }

            _chainShare[chainCount - 1] = 1f; // Step writes the real shares before the job has any weight

            // The body's shares: the bones before the head divide the body's part of the turn by their numbers (the same number
            // for all when none is set). The head takes the rest, so its entry is not used.
            _bodyShares = new float[chainCount];
            var bodySum = 0f;
            for (var i = 0; i < chainCount - 1; i++)
            {
                bodySum += chainShares[i];
            }

            for (var i = 0; i < chainCount - 1; i++)
            {
                _bodyShares[i] = bodySum > 0f ? chainShares[i] / bodySum : 1f / (chainCount - 1);
            }

            // Which way the head faces in its own bone space: read once, while the model still stands in the pose it was made in
            // (it faces the character's forward there). The first attach comes before any animation has played.
            var headBone = chainBones[chainCount - 1];
            if (_axisBone != headBone)
            {
                _axisBone = headBone;
                _headAxis = Quaternion.Inverse(headBone.rotation) * (transform.rotation * Vector3.forward);
            }

            // Pre-binds the handles, so the first frame does not hitch.
            _animator.ResolveAllStreamHandles();

            var job = new LookJob
            {
                Chain = _chainHandles,
                ChainYaw = _chainYaw,
                ChainPitch = _chainPitch,
                ChainRotations = _chainRotations,
                ChainShare = _chainShare,
                HeadAxis = _headAxis,
                Frame = transform.rotation,
                Weight = _weight
            };

            // A graph of its own with one output on the same Animator, evaluated after the Animator's own pose (the way Animation
            // Rigging does it): the job receives the finished pose and changes it. It does not touch the Animator's controller.
            _graph = PlayableGraph.Create($"Look Animator ({name})");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _node = AnimationScriptPlayable.Create(_graph, job);
            var output = AnimationPlayableOutput.Create(_graph, "Look", _animator);
            output.SetAnimationStreamSource(AnimationStreamSource.PreviousInputs);
            output.SetSortingOrder(OutputOrder);
            output.SetSourcePlayable(_node);
            _attached = true;
            _writtenWeight = _weight;
            _playing = false; // Step starts it as soon as the look has something to show
        }

        /// <summary>Destroys the graph and frees the native arrays. Safe to call twice.</summary>
        internal void Detach()
        {
            if (_graph.IsValid())
            {
                // The graph goes first; only then is nothing left that reads the arrays.
                _graph.Destroy();
            }

            _graph = default;
            _node = default;
            _attached = false;
            _playing = false;
            DisposeArrays();
        }

        // The one place the native memory is freed: idempotent, called from Detach, Attach (before new arrays) and OnDestroy.
        private void DisposeArrays()
        {
            if (_chainHandles.IsCreated) _chainHandles.Dispose();
            if (_chainYaw.IsCreated) _chainYaw.Dispose();
            if (_chainPitch.IsCreated) _chainPitch.Dispose();
            if (_chainRotations.IsCreated) _chainRotations.Dispose();
            if (_chainShare.IsCreated) _chainShare.Dispose();
        }

        // Which bones turn, root-most first and the head last, and the numbers that divide the body's turn between them. The
        // head is the Head field. An older setup kept the head as the last entry of the chain: then that entry is the head.
        private bool ResolveBones(Animator animator, out Transform[] bones, out float[] shares)
        {
            var list = new List<Transform>();
            var weights = new List<float>();
            var head = _head;
            if (_chain != null)
            {
                for (var i = 0; i < _chain.Length; i++)
                {
                    var bone = _chain[i].Bone;
                    if (bone != null && bone != head && UsableBone(bone, animator, "chain"))
                    {
                        list.Add(bone);
                        weights.Add(_chain[i].Weight);
                    }
                }
            }

            if (head == null)
            {
                if (list.Count > 0)
                {
                    head = list[list.Count - 1];
                    list.RemoveAt(list.Count - 1);
                    weights.RemoveAt(weights.Count - 1);
                }
                else if (animator.isHuman && LookDefaults.FillHumanoid(animator, out var defaultHead, out var defaultChain))
                {
                    head = defaultHead;
                    for (var i = 0; i < defaultChain.Length; i++)
                    {
                        list.Add(defaultChain[i].Bone);
                        weights.Add(defaultChain[i].Weight);
                    }
                }
            }

            if (head == null || !UsableBone(head, animator, "head"))
            {
                bones = null;
                shares = null;
                return false;
            }

            list.Add(head);
            weights.Add(1f);
            bones = list.ToArray();
            shares = weights.ToArray();
            return true;
        }

        private bool UsableBone(Transform bone, Animator animator, string what)
        {
            if (!bone.IsChildOf(animator.transform))
            {
                Warn(what + bone.name, $"[LookAnimator] '{name}': the {what} bone '{bone.name}' is not under the Animator's object '{animator.name}', so it is skipped.");
                return false;
            }

            return true;
        }

        private void Warn(string key, string message)
        {
            _warned ??= new HashSet<string>();
            if (_warned.Add(key))
            {
                Debug.LogWarning(message, this);
            }
        }

        #endregion

        #region Step

        /// <summary>
        /// Moves the look one step: finds the target, works out the angles, fades the weight, and hands the result to the job.
        /// Called by <see cref="LookManager"/>. Returns whether the look needs the per-frame rhythm (a target, or a weight still
        /// fading); false lets the manager sleep.
        /// </summary>
        internal bool Step(double now, float dt)
        {
            if (!_attached)
            {
                Attach(); // the Animator was not active yet, or the look was just enabled
                return false;
            }

            if (!_node.IsValid() || !_graph.IsValid())
            {
                Detach();
                return false;
            }

            var settings = _settings;

            // What: the target set by hand, else a position set by hand, else what the plugged-in source says.
            var point = default(Vector3);
            var factor = 1f;
            bool hasTarget;
            if (_target != null && _target.gameObject.activeInHierarchy)
            {
                point = _target.position;
                hasTarget = true;
            }
            else if (_hasPoint)
            {
                point = _point;
                hasTarget = true;
            }
            else
            {
                hasTarget = _source != null && _source.TryGetTarget(now, out point, out factor);
            }

            _looking = hasTarget;

            // The angles to the target, in the character's own frame, soft-limited and smoothed.
            var frame = transform.rotation;
            if (hasTarget)
            {
                var local = Quaternion.Inverse(frame) * (point - _originBone.position);
                if (local.sqrMagnitude >= 0.01f)
                {
                    _yawDesired = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                    _pitchDesired = Mathf.Atan2(local.y, Mathf.Sqrt(local.x * local.x + local.z * local.z)) * Mathf.Rad2Deg;
                }

                var away = Mathf.Abs(_yawDesired);
                if (_behind)
                {
                    if (away < settings.StopAngle - LookSettings.StopHysteresis)
                    {
                        _behind = false;
                    }
                }
                else if (away > settings.StopAngle)
                {
                    _behind = true;
                }

                var yawLimited = LookState.SoftClamp(_yawDesired, settings.MaxYaw);
                var pitchLimited = _pitchDesired >= 0f
                    ? LookState.SoftClamp(_pitchDesired, settings.MaxPitchUp)
                    : -LookState.SoftClamp(-_pitchDesired, settings.MaxPitchDown);
                FollowTarget(settings, yawLimited, pitchLimited, dt);
            }
            else
            {
                _behind = false;
            }

            // How much: the weight follows the wanted weight at the fade speeds.
            var targetWeight = hasTarget && !_behind ? settings.Weight * Mathf.Clamp01(factor) : 0f;
            _weight += Mathf.Clamp(targetWeight - _weight, -dt / Mathf.Max(0.01f, settings.FadeOut), dt / Mathf.Max(0.01f, settings.FadeIn));
            if (targetWeight <= 0f && _weight < Epsilon)
            {
                _weight = 0f;
            }

            // The job only needs new data while it has something to do (or just stopped having it); the graph sleeps otherwise.
            if (_weight > 0f || _writtenWeight > 0f)
            {
                var job = _node.GetJobData<LookJob>();
                job.Frame = frame;
                job.Weight = _weight;
                _node.SetJobData(job);
                _writtenWeight = _weight;
                if (!_playing)
                {
                    _graph.Play();
                    _playing = true;
                }
            }
            else if (_playing)
            {
                _graph.Stop();
                _playing = false;
            }

            return hasTarget || _weight > 0f;
        }

        // The head goes for the whole angle at once and fast. The bones before it each take a small share of the angle once they
        // have caught up, slowly (the spine the slowest, the neck the quickest), and the head gives back what they have taken: it
        // turns by the angle that is still missing, so the eyes stay on the target while the body settles in behind them.
        private void FollowTarget(LookSettings settings, float yawTotal, float pitchTotal, float dt)
        {
            var count = _chainYaw.Length;
            var head = count - 1;
            var bodyYaw = 0f;
            var bodyPitch = 0f;
            var bodyShare = 0f;
            for (var i = 0; i < head; i++)
            {
                var share = _bodyShares[i] * settings.BodyTurn;
                _chainShare[i] = share;
                bodyShare += share;
                var t = head > 1 ? i / (float)(head - 1) : 1f;
                var rate = settings.BodyFollow * (0.6f + 0.8f * t);
                var cap = settings.TurnSpeed * 0.5f * (0.4f + 0.6f * t);
                _chainYaw[i] = Follow(_chainYaw[i], yawTotal * share, rate, cap, dt);
                _chainPitch[i] = Follow(_chainPitch[i], pitchTotal * share, rate, cap, dt);
                bodyYaw += _chainYaw[i];
                bodyPitch += _chainPitch[i];
            }

            _chainShare[head] = 1f - bodyShare;
            _chainYaw[head] = Follow(_chainYaw[head], yawTotal - bodyYaw, settings.HeadFollow, settings.TurnSpeed, dt);
            _chainPitch[head] = Follow(_chainPitch[head], pitchTotal - bodyPitch, settings.HeadFollow, settings.TurnSpeed, dt);
        }

        // Moves an angle towards its target the way a spring would (quick when far, gentle when near), never faster than the cap.
        private static float Follow(float current, float target, float rate, float maxSpeed, float dt)
        {
            var step = (target - current) * (1f - Mathf.Exp(-rate * dt));
            var cap = maxSpeed * dt;
            return current + Mathf.Clamp(step, -cap, cap);
        }

        #endregion
    }

    /// <summary>Ready-made bone setups for <see cref="LookAnimator"/>.</summary>
    public static class LookDefaults
    {
        // Spine, chest, upper chest, neck: how the body's turn is divided between the bones before the head.
        private static readonly HumanBodyBones[] BodyBones =
        {
            HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck
        };

        private static readonly float[] BodyShares = { 0.10f, 0.15f, 0.15f, 0.25f };

        /// <summary>
        /// Fills the head and the body bones (spine 0.10, chest 0.15, upper chest 0.15, neck 0.25; bones the rig lacks are left
        /// out) from a humanoid Animator. Returns false on a generic rig or one without a head.
        /// </summary>
        public static bool FillHumanoid(Animator animator, out Transform head, out LookBone[] chain)
        {
            head = null;
            chain = new LookBone[0];
            if (animator == null || !animator.isHuman)
            {
                return false;
            }

            head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null)
            {
                return false;
            }

            var found = new Transform[BodyBones.Length];
            var count = 0;
            var sum = 0f;
            for (var i = 0; i < BodyBones.Length; i++)
            {
                found[i] = animator.GetBoneTransform(BodyBones[i]);
                if (found[i] != null)
                {
                    count++;
                    sum += BodyShares[i];
                }
            }

            chain = new LookBone[count];
            var written = 0;
            for (var i = 0; i < BodyBones.Length; i++)
            {
                if (found[i] != null)
                {
                    chain[written++] = new LookBone { Bone = found[i], Weight = BodyShares[i] / sum };
                }
            }

            return true;
        }
    }
}
