using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// Attached to SecTrend in Panel_monitoreo/RightPanel/Content, below
/// "CONTROL DE TRAYECTORIAS". Plots setpoint vs actual cartesian position
/// (X/Y/Z, one mini-chart each) as a rolling trend line, to visualize
/// trajectory-tracking error over time.
public class SecTrendGraphController : MonoBehaviour
{
    public CartesianPositionSubscriber actualSub;
    public SetpointCartesianPositionSubscriber setpointSub;

    // Matches mock_cmd_server.py's joint_states_rate_hz default (see
    // _tick_joint_states, where both current_cartesian_position and
    // setpoint_cartesian_position are published) -- the same cadence fr5's
    // own model updates at, so this graph scrolls in step with the robot
    // instead of an independent, potentially-drifting timer.
    public float updateHz = 50f;
    public int maxSamples = 150; // ~3s of history at 50Hz

    public UITrendLine actualLineX, setpointLineX;
    public UITrendLine actualLineY, setpointLineY;
    public UITrendLine actualLineZ, setpointLineZ;

    public Text errorLabelX, errorLabelY, errorLabelZ;

    private readonly List<float> _actualX = new List<float>();
    private readonly List<float> _setpointX = new List<float>();
    private readonly List<float> _actualY = new List<float>();
    private readonly List<float> _setpointY = new List<float>();
    private readonly List<float> _actualZ = new List<float>();
    private readonly List<float> _setpointZ = new List<float>();

    private float _sampleInterval;
    private float _nextSampleTime;

    void Start()
    {
        if (actualSub == null)
            actualSub = FindObjectOfType<CartesianPositionSubscriber>();
        if (setpointSub == null)
            setpointSub = FindObjectOfType<SetpointCartesianPositionSubscriber>();

        _sampleInterval = updateHz > 0f ? 1f / updateHz : 0.02f;
    }

    void Update()
    {
        if (Time.time < _nextSampleTime) return;
        _nextSampleTime = Time.time + _sampleInterval;
        Sample();
    }

    private void Sample()
    {
        if (actualSub == null || setpointSub == null) return;

        float[] actual = actualSub.GetLastKnownCartesianPositions();
        float[] setpoint = setpointSub.GetLastKnownSetpoint();
        if (actual == null || actual.Length != 6 || setpoint == null || setpoint.Length != 6) return;

        Push(_actualX, actual[0]); Push(_setpointX, setpoint[0]);
        Push(_actualY, actual[1]); Push(_setpointY, setpoint[1]);
        Push(_actualZ, actual[2]); Push(_setpointZ, setpoint[2]);

        UpdateChart(_actualX, _setpointX, actualLineX, setpointLineX, errorLabelX, actual[0], setpoint[0]);
        UpdateChart(_actualY, _setpointY, actualLineY, setpointLineY, errorLabelY, actual[1], setpoint[1]);
        UpdateChart(_actualZ, _setpointZ, actualLineZ, setpointLineZ, errorLabelZ, actual[2], setpoint[2]);
    }

    private void Push(List<float> buffer, float value)
    {
        buffer.Add(value);
        if (buffer.Count > maxSamples)
            buffer.RemoveAt(0);
    }

    // Normalizes both series to a SHARED min/max (across setpoint+actual) so
    // the two lines in a mini-chart stay on the same vertical scale -- if
    // each line auto-scaled independently, a perfectly-tracking robot could
    // still show two visually different curves.
    private void UpdateChart(
        List<float> actualBuf, List<float> setpointBuf,
        UITrendLine actualLine, UITrendLine setpointLine, Text errorLabel,
        float actualNow, float setpointNow)
    {
        if (actualBuf.Count < 2) return;

        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < actualBuf.Count; i++)
        {
            if (actualBuf[i] < min) min = actualBuf[i];
            if (actualBuf[i] > max) max = actualBuf[i];
            if (setpointBuf[i] < min) min = setpointBuf[i];
            if (setpointBuf[i] > max) max = setpointBuf[i];
        }
        float range = max - min;
        if (range < 1f) range = 1f; // avoid a degenerate scale on a flat line
        float pad = range * 0.1f;
        min -= pad; max += pad; range = max - min;

        var normActual = new List<float>(actualBuf.Count);
        var normSetpoint = new List<float>(setpointBuf.Count);
        for (int i = 0; i < actualBuf.Count; i++)
        {
            normActual.Add((actualBuf[i] - min) / range);
            normSetpoint.Add((setpointBuf[i] - min) / range);
        }

        if (actualLine != null) actualLine.SetNormalizedPoints(normActual);
        if (setpointLine != null) setpointLine.SetNormalizedPoints(normSetpoint);

        if (errorLabel != null)
            errorLabel.text = $"Δ {Mathf.Abs(actualNow - setpointNow):F4}mm";
    }
}
