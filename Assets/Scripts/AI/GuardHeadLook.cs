using System;
using UnityEngine;

namespace AI
{
    /// <summary>
    /// What a guard's head does in each state, as one asset shared by
    /// every guard (read by GuardHeadAim). Create one with Assets > Create
    /// > TeaLeaf > Guard Head Look (it starts with Reset()'s rules).
    ///
    /// A list of rules, each naming a state, whether it applies standing,
    /// moving or both, and what the head does then: plays the clip as it
    /// is (Animated), faces straight ahead of the body with no turn to
    /// the side but the clip's nod kept (Forward), or turns towards
    /// something (Look At). The first rule that matches wins; a state
    /// with no rule plays the clip as it is.
    ///
    /// Play Mode edits to the asset are kept, so it can be tuned while
    /// watching a guard.
    /// </summary>
    [CreateAssetMenu(fileName = "GuardHeadLook", menuName = "TeaLeaf/Guard Head Look")]
    public class GuardHeadLook : ScriptableObject
    {
        /// When in a state a rule applies.
        public enum Movement { Any, Standing, Moving }

        /// What the head does.
        public enum Mode { Animated, Forward, LookAt }

        /// What a Look At rule turns the head towards.
        public enum Target { Stimulus, Player }

        /// <summary>
        /// One rule: in this state (and movement), the head does this.
        /// </summary>
        [Serializable]
        public struct Rule
        {
            [Tooltip("The guard state this rule is for, e.g. Patrol.")]
            public GuardState state;
            [Tooltip("When in that state it applies: Standing = the guard is still (slower than Guard.StandingSpeed, turning on the spot included), Moving = walking or running, Any = both.")]
            public Movement movement;
            [Tooltip("What the head does: Animated = the clip as it is; Forward = straight ahead of the body, no turn to the side, the clip's nod kept; Look At = turned towards Target, within Max Yaw / Max Pitch.")]
            public Mode mode;
            [Tooltip("For Look At only: Stimulus = where the player was last seen or a noise last heard from (raised by Look Height); Player = the player's head (with no player, the clip plays as it is).")]
            public Target target;

            public Rule(GuardState state, Movement movement, Mode mode, Target target = Target.Stimulus)
            {
                this.state = state;
                this.movement = movement;
                this.mode = mode;
                this.target = target;
            }
        }

        [Tooltip("The rules, first match wins. A state (and movement) no rule matches plays the clip's head as it is.")]
        [SerializeField] private Rule[] rules = Array.Empty<Rule>();

        [Header("Turning")]
        // How fast the head moves to a new aim, and back to the clip,
        // so a change of rule never snaps it.
        [Tooltip("Degrees a second the head turns towards a new aim, and back to the clip when a rule ends. e.g. 150 = a quarter turn in about 0.6s. Higher = snappier; lower = slower, lazier turns.")]
        [SerializeField] private float turnSpeed = 150f;
        // The neck takes this share of each turn, the head the rest.
        [Tooltip("Share of each turn the neck takes; the head takes the rest. e.g. 0.4 = neck 40%, head 60%. Higher = more of the turn low in the neck; 0 = the head turns alone.")]
        [Range(0f, 1f)]
        [SerializeField] private float neckShare = 0.4f;

        [Header("Look At")]
        [Tooltip("Furthest, in degrees, Look At turns the gaze to either side of the body's facing. e.g. 70 = most of a person's head turn. Higher = looks further round; lower = stops nearer straight ahead.")]
        [SerializeField] private float maxYaw = 70f;
        [Tooltip("Furthest, in degrees, Look At tips the gaze up above level. e.g. 30. Higher = looks further up.")]
        [SerializeField] private float maxPitchUp = 30f;
        [Tooltip("Furthest, in degrees, Look At tips the gaze down below level. e.g. 40. Higher = looks further down.")]
        [SerializeField] private float maxPitchDown = 40f;
        // The stimulus is a spot on the ground; the head looks at a
        // point this high above it, as at a person standing there.
        [Tooltip("Metres above the stimulus (a spot on the ground) the head looks at, e.g. 1.5 = about where a standing person's head would be. Lower = looks further down at the spot itself.")]
        [SerializeField] private float lookHeight = 1.5f;

        public float TurnSpeed => turnSpeed;
        public float NeckShare => neckShare;
        public float MaxYaw => maxYaw;
        public float MaxPitchUp => maxPitchUp;
        public float MaxPitchDown => maxPitchDown;
        public float LookHeight => lookHeight;

        /// <summary>
        /// The first rule matching this state and movement. False if none
        /// does (the head plays the clip).
        /// </summary>
        public bool TryGetRule(GuardState state, bool isStanding, out Rule rule)
        {
            foreach (Rule candidate in rules) {
                if (candidate.state != state) {
                    continue;
                }

                if (candidate.movement == Movement.Any
                    || (candidate.movement == Movement.Standing) == isStanding) {
                    rule = candidate;
                    return true;
                }
            }

            rule = default;
            return false;
        }

        /// <summary>
        /// Starting rules: standing on patrol the head faces straight
        /// ahead, suspicious it looks at the stimulus, chasing at the
        /// player; everything else plays the clip.
        /// </summary>
        private void Reset()
        {
            rules = new[]
            {
                new Rule(GuardState.Patrol, Movement.Standing, Mode.Forward),
                new Rule(GuardState.Suspicious, Movement.Any, Mode.LookAt, Target.Stimulus),
                new Rule(GuardState.Chase, Movement.Any, Mode.LookAt, Target.Player)
            };
            turnSpeed = 150f;
            neckShare = 0.4f;
            maxYaw = 70f;
            maxPitchUp = 30f;
            maxPitchDown = 40f;
            lookHeight = 1.5f;
        }
    }
}
