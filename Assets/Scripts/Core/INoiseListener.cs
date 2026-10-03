using UnityEngine;

namespace Core
{
    /// <summary>
    /// Something that can hear noises: a guard. Register with
    /// NoiseSystem.Register(this) in OnEnable and Unregister(this) in
    /// OnDisable.
    /// </summary>
    public interface INoiseListener
    {
        /// Where this listener's ears are, in world space.
        Vector3 EarPosition { get; }

        /// <summary>
        /// Called for each noise that reaches this listener. loudness is
        /// how loud it is at the ears: 1 right at the noise, falling to 0
        /// when the route it took is as long as its radius. heardFrom is
        /// where it seems to come from: the noise itself in the same room,
        /// otherwise the doorway (portal) it came through - where a guard
        /// should turn to look.
        /// </summary>
        void OnNoiseHeard(in Noise noise, float loudness, Vector3 heardFrom);
    }
}
