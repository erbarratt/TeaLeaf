using UnityEngine;
using UnityEngine.XR;

public class TurnInputTest : MonoBehaviour
{
    private void Update()
    {
        UnityEngine.XR.InputDevice rightController =
            InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        if (rightController.isValid)
        {
            if (rightController.TryGetFeatureValue(
                    CommonUsages.primary2DAxis,
                    out Vector2 stick))
            {
                if (stick.sqrMagnitude > 0.01f)
                {
                    Debug.Log($"RAW RIGHT STICK: {stick}");
                }
            }
        }
    }
}