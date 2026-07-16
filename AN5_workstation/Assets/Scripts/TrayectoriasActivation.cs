using UnityEngine;
using UnityEngine.UI;

/// Attach to Panel_trayectorias.
/// Populates the SecJoints sliders (and, via SecJointsSliderSync, their value
/// inputs) with the robot's current joint positions whenever this tab is shown,
/// and -- like MonitoreoActivation -- drives the fr5v6 model from ROS while this
/// panel is visible, so drag-teach moves are reflected in the 3D model here too.
public class TrayectoriasActivation : MonoBehaviour
{
    static readonly string[] JointNames =
    {
        "Joint_BASE", "Joint_SHOULDER", "Joint_ELBOW",
        "Joint_WRIST 1", "Joint_WRIST 2", "Joint_WRIST 3"
    };

    JointPositionSubscriber _jps;

    void Awake()
    {
        _jps = FindObjectOfType<JointPositionSubscriber>();
        if (_jps == null)
            Debug.LogWarning("[TrayectoriasActivation] JointPositionSubscriber not found.");
    }

    void OnEnable()
    {
        if (_jps == null) return;

        var body = transform.Find("CenterBottom/SecJoints/Body");
        if (body == null)
        {
            Debug.LogWarning("[TrayectoriasActivation] SecJoints/Body not found.");
            return;
        }

        float[] positions = _jps.GetLastKnownPositions();
        for (int i = 0; i < JointNames.Length && i < positions.Length; i++)
        {
            var slider = body.Find(JointNames[i] + "/S")?.GetComponent<Slider>();
            if (slider == null) continue;
            // SetValueWithoutNotify: this slider is shared with ControlArticular's jog
            // sliders, and a plain "slider.value =" here fires onValueChanged, which
            // ControlArticular.OnSliderValueChanged treats as the user starting a manual
            // edit — calling jointPositionSubscriber.StopUpdating() and freezing all
            // further live ROS position updates for the whole session.
            slider.SetValueWithoutNotify(positions[i]);
        }

        _jps.driveRobotModel = true;
        _jps.ApplyToModel();   // snap fr5v6 to current pose immediately
    }

    void OnDisable()
    {
        if (_jps != null) _jps.driveRobotModel = false;
    }
}
