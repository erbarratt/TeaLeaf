using UnityEngine;
using Player;

/// <summary>
/// Draws each hand's interaction ray in the Scene view (and Game view, if
/// its Gizmos toggle is on) during Play Mode, so PlayerHandInteraction's
/// leftHandRayAngleOffset/rightHandRayAngleOffset can be tuned visually
/// instead of by guesswork.
///
/// Editor-only visualization - Debug.DrawRay never renders in a build, so
/// there's no need to strip this out for release.
/// </summary>
public class HandRayDebug : MonoBehaviour
{
    [SerializeField] private PlayerHandInteraction playerHandInteraction;

    [SerializeField] private Color leftRayColor = Color.red;
    [SerializeField] private Color rightRayColor = Color.blue;

    private void Update()
    {
        Debug.DrawRay(
            playerHandInteraction.LeftRayOrigin,
            playerHandInteraction.LeftRayDirection * playerHandInteraction.RayLength,
            leftRayColor);

        Debug.DrawRay(
            playerHandInteraction.RightRayOrigin,
            playerHandInteraction.RightRayDirection * playerHandInteraction.RayLength,
            rightRayColor);
    }
}
