using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// Attached to SecCartInput in Panel_trayectorias. Lets the user type a
/// cartesian target (X/Y/Z/Rx/Ry/Rz) and, on Enter, requests inverse
/// kinematics from the mock (input_cartesian_position -> output_joint_position,
/// see mock_cmd_server.py's _on_input_cartesian_position) and applies the
/// resulting joint angles to the SAME SecJoints sliders SecCoordQueueController
/// reads from -- this both updates the fr5v6 model (ControlArticular already
/// listens on those sliders) and lets the existing Add/Send queue pick up the
/// new pose without any changes to that flow.
///
/// Failsafe for unreachable targets: if the mock's IK solver fails (out of
/// workspace, singularity, no convergence), it now publishes "ERROR:<reason>"
/// on the same topic instead of staying silent -- see this file's
/// ApplyJointAngles(). A local timeout also covers the case where nothing
/// comes back at all (ROS disconnected, mock down), so the boxes never wait
/// forever with no feedback.
///
/// SIMULATION-ONLY today: input_cartesian_position/output_joint_position are
/// implemented exclusively in an5_mock_sim/mock_cmd_server.py -- grep the whole
/// ROS workspace and that's the only place either topic name appears. real.launch.py
/// never runs the mock, and fr_ros2/ROS_API.cpp (the real driver) has no subscriber
/// for these topics at all, so with the real robot this box would just time out with
/// no response, not fail with a specific reason. This is a DIFFERENT limitation from
/// SecTrajController's CARTPoint/MoveL(CART...)/SplinePTP(CART...) path, which does
/// have a real-robot equivalent (the controller's onboard GetInverseKin) but that one
/// has been failing on real hardware. If cartesian jog is ever needed on the real
/// robot, the existing mechanism for it is StartJOG(...) with cartesian axis
/// parameters via /api_command (ROS_API.cpp forwards it straight to the controller,
/// which jogs with its own firmware) -- not input_cartesian_position.
public class SecCartInputController : MonoBehaviour
{
    public Ros2CommandSender ros2CommandSender;
    public InverseKinematicsSubscriber ikSubscriber;
    public SecCoordQueueController secCoordQueueController;
    public CartesianPositionSubscriber cartesianPositionSubscriber;

    public InputField xInput, yInput, zInput, rxInput, ryInput, rzInput;

    public float ikTimeoutSeconds = 3f;
    static readonly Color ErrorColor  = new Color(0.85f, 0.25f, 0.25f, 1f);
    Color[] _normalColors;

    // Mirrors CartesianStateWriterNew.UpdateInputFieldsContinuously(), which
    // used to keep these boxes tracking the robot's live cartesian position --
    // that component is now disabled (see SendROS2/CartesianStateWriterNew)
    // because its "isManualEditing" flag latched true permanently the first
    // time any box got focus and never reset, so the boxes went stale as soon
    // as this controller started making them interactive. Refresh here
    // instead, with the same 0.5s cadence and "skip while focused" rule, but
    // without the latch bug.
    const float LiveRefreshInterval = 0.5f;
    float _nextLiveRefreshTime;

    // InverseKinematicsSubscriber.ReceiveMessage() (and thus
    // OnInverseKinematicsResultReceived) fires on RosSharp's websocket network
    // thread, not Unity's main thread -- same hazard JointPositionSubscriber
    // already works around. Writing Slider/Image objects directly from that
    // thread is unsafe, so the raw payload is only queued here; the actual
    // work happens in Update().
    private readonly object _pendingLock = new object();
    private string _pendingData;
    private bool _hasPendingData;

    private bool _awaitingResponse;
    private float _requestTime;

    InputField[] AllInputs => new[] { xInput, yInput, zInput, rxInput, ryInput, rzInput };

    void Start()
    {
        if (xInput  == null) xInput  = transform.Find("Body/BoxX")?.GetComponentInChildren<InputField>();
        if (yInput  == null) yInput  = transform.Find("Body/BoxY")?.GetComponentInChildren<InputField>();
        if (zInput  == null) zInput  = transform.Find("Body/BoxZ")?.GetComponentInChildren<InputField>();
        if (rxInput == null) rxInput = transform.Find("Body/BoxRx")?.GetComponentInChildren<InputField>();
        if (ryInput == null) ryInput = transform.Find("Body/BoxRy")?.GetComponentInChildren<InputField>();
        if (rzInput == null) rzInput = transform.Find("Body/BoxRz")?.GetComponentInChildren<InputField>();

        if (ros2CommandSender == null)
            ros2CommandSender = FindObjectOfType<Ros2CommandSender>();
        if (ikSubscriber == null)
            ikSubscriber = FindObjectOfType<InverseKinematicsSubscriber>();
        if (secCoordQueueController == null)
            secCoordQueueController = FindObjectOfType<SecCoordQueueController>();
        if (cartesianPositionSubscriber == null)
            cartesianPositionSubscriber = FindObjectOfType<CartesianPositionSubscriber>();

        var inputs = AllInputs;
        _normalColors = new Color[inputs.Length];
        for (int i = 0; i < inputs.Length; i++)
        {
            var img = inputs[i]?.GetComponent<Image>();
            _normalColors[i] = img != null ? img.color : Color.white;
            inputs[i]?.onEndEdit.AddListener(OnCartesianInputChanged);
        }

        if (ikSubscriber != null)
            ikSubscriber.OnInverseKinematicsResultReceived += OnJointAnglesReceivedFromNetworkThread;
        else
            Debug.LogWarning("[SecCartInputController] InverseKinematicsSubscriber not found.");
    }

    void OnDestroy()
    {
        if (ikSubscriber != null)
            ikSubscriber.OnInverseKinematicsResultReceived -= OnJointAnglesReceivedFromNetworkThread;
    }

    void Update()
    {
        string data = null;
        lock (_pendingLock)
        {
            if (_hasPendingData)
            {
                data = _pendingData;
                _hasPendingData = false;
            }
        }

        if (data != null)
        {
            _awaitingResponse = false;
            if (data.StartsWith("ERROR:"))
                ShowError(data.Substring("ERROR:".Length));
            else
                ApplyJointAngles(data);
            return;
        }

        if (_awaitingResponse && Time.time - _requestTime > ikTimeoutSeconds)
        {
            _awaitingResponse = false;
            ShowError("sin respuesta del mock (revisar conexión ROS2)");
        }

        if (Time.time >= _nextLiveRefreshTime)
        {
            _nextLiveRefreshTime = Time.time + LiveRefreshInterval;
            RefreshLivePosition();
        }
    }

    // Keeps the boxes tracking the robot's actual cartesian position (from
    // current_cartesian_position) whenever the user isn't actively typing into
    // any of them -- restores the pre-existing "boxes move with the robot"
    // behavior that CartesianStateWriterNew used to provide.
    private void RefreshLivePosition()
    {
        if (cartesianPositionSubscriber == null) return;

        var inputs = AllInputs;
        foreach (var field in inputs)
            if (field != null && field.isFocused)
                return;

        float[] positions = cartesianPositionSubscriber.GetLastKnownCartesianPositions();
        if (positions == null || positions.Length != 6) return;

        for (int i = 0; i < inputs.Length; i++)
            if (inputs[i] != null)
                inputs[i].text = positions[i].ToString("F2", CultureInfo.InvariantCulture);
    }

    // Fires on Enter (or losing focus) in any of the 6 boxes. Reads all six
    // current values (not just the one edited) since IK needs the full pose.
    private void OnCartesianInputChanged(string _)
    {
        if (ros2CommandSender == null)
        {
            Debug.LogError("[SecCartInputController] Ros2CommandSender not assigned.");
            return;
        }

        ClearError();

        float x  = ParseOr(xInput, 0f);
        float y  = ParseOr(yInput, 0f);
        float z  = ParseOr(zInput, 0f);
        float rx = ParseOr(rxInput, 0f);
        float ry = ParseOr(ryInput, 0f);
        float rz = ParseOr(rzInput, 0f);

        string data = $"{x},{y},{z},{rx},{ry},{rz}";
        ros2CommandSender.SendCommandToTopic(ros2CommandSender.inverseInputTopic, data);
        Debug.Log($"[SecCartInputController] Solicitando IK para {data}");

        _awaitingResponse = true;
        _requestTime = Time.time;
    }

    private static float ParseOr(InputField field, float fallback)
    {
        if (field == null || string.IsNullOrEmpty(field.text))
            return fallback;
        return float.TryParse(field.text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
            ? v : fallback;
    }

    // Called on the network thread -- must not touch Slider/UI objects here.
    private void OnJointAnglesReceivedFromNetworkThread(string data)
    {
        lock (_pendingLock)
        {
            _pendingData = data;
            _hasPendingData = true;
        }
    }

    // Runs on the main thread (from Update()). Applies the IK result to the
    // shared SecJoints sliders -- the same Slider components ControlArticular
    // already listens on, so this updates the visual model (and the joint
    // value InputFields) exactly like dragging a SecJoints slider by hand, with
    // no separate model-writing code needed here.
    private void ApplyJointAngles(string data)
    {
        if (secCoordQueueController == null) return;

        string[] parts = data.Split(',');
        if (parts.Length != 6) return;

        float[] angles = new float[6];
        for (int i = 0; i < 6; i++)
        {
            // float.TryParse("NaN", ...) succeeds and yields float.NaN -- it does NOT
            // fail/return false, so the loop below alone can't catch MATLAB's failure
            // convention (inverse_kinematics.m publishes literal "NaN,NaN,NaN,NaN,NaN,NaN"
            // for an unreachable/unsolved pose instead of an explicit "ERROR:" prefix).
            // Without this explicit check, a NaN sailed straight into
            // Slider.value -> JointStateWriter.Write() -> Transform.localEulerAngles,
            // which Unity rejects at the native level ("Input rotation is
            // { NaN, NaN, NaN, NaN }") -- SecTrajController.WaitForIkResult already
            // guards against this same case, this mirrors it here.
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out angles[i])
                || float.IsNaN(angles[i]))
            {
                ShowError($"posición inalcanzable (IK devolvió NaN o datos inválidos: '{data}')");
                return;
            }
        }

        Slider[] sliders =
        {
            secCoordQueueController.j1Slider, secCoordQueueController.j2Slider, secCoordQueueController.j3Slider,
            secCoordQueueController.j4Slider, secCoordQueueController.j5Slider, secCoordQueueController.j6Slider,
        };
        for (int i = 0; i < 6; i++)
            if (sliders[i] != null)
                sliders[i].value = angles[i]; // Slider.value already clamps to [minValue, maxValue].
    }

    // Failsafe feedback: tints all 6 boxes red and logs the reason, so an
    // unreachable/failed target is visible instead of the panel just doing
    // nothing. Cleared on the next edit (ClearError) or the next successful
    // IK result (also via ClearError, called from OnCartesianInputChanged).
    private void ShowError(string reason)
    {
        Debug.LogWarning($"[SecCartInputController] IK fallo: {reason}");
        var inputs = AllInputs;
        for (int i = 0; i < inputs.Length; i++)
        {
            var img = inputs[i]?.GetComponent<Image>();
            if (img != null) img.color = ErrorColor;
        }
    }

    private void ClearError()
    {
        var inputs = AllInputs;
        for (int i = 0; i < inputs.Length; i++)
        {
            var img = inputs[i]?.GetComponent<Image>();
            if (img != null) img.color = _normalColors[i];
        }
    }
}
