using UnityEngine;

/// Attach to Panel_monitoreo.
/// Drives fr5v6 from ROS while this panel is visible; freezes it when another tab is active.
public class MonitoreoActivation : MonoBehaviour
{
    JointPositionSubscriber _jps;

    void Awake()
    {
        _jps = Object.FindFirstObjectByType<JointPositionSubscriber>();
        if (_jps == null)
            Debug.LogWarning("[MonitoreoActivation] JointPositionSubscriber not found.");
    }

    void OnEnable()
    {
        if (_jps == null) return;
        _jps.driveRobotModel = true;
        _jps.ApplyToModel();   // snap fr5v6 to current pose immediately
    }

    void OnDisable()
    {
        if (_jps != null) _jps.driveRobotModel = false;
    }
}
