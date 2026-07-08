using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using RosSharp.RosBridgeClient;

/// Attached to ModeToggle in PersistentLayer/Header.
/// BtnReal/BtnSim used to load a separate AN5_workbench scene for the real
/// robot; that scene is gone, so both buttons now just pin the RosConnector
/// to the right ROS2 endpoint (Eje. Real -> physical robot controller IP,
/// Simulacion -> localhost/mock) and stay on this same scene.
public class ModeToggleController : MonoBehaviour
{
    const string RosUrlReal = "ws://192.168.58.3:9090";
    const string RosUrlSim  = "ws://localhost:9090";

    static readonly Color ActiveBg          = new Color(0.000f, 0.831f, 0.667f, 1.000f);
    static readonly Color ActiveHighlighted = new Color(0.000f, 0.900f, 0.720f, 1.000f);
    static readonly Color ActivePressed     = new Color(0.000f, 0.650f, 0.520f, 1.000f);
    static readonly Color ActiveText        = new Color(0.000f, 0.000f, 0.000f, 1.000f);

    static readonly Color IdleBg            = new Color(0.000f, 0.000f, 0.000f, 0.000f);
    static readonly Color IdleHighlighted   = new Color(0.000f, 0.831f, 0.667f, 0.300f);
    static readonly Color IdlePressed       = new Color(0.000f, 0.831f, 0.667f, 0.500f);
    static readonly Color IdleText          = new Color(0.353f, 0.388f, 0.439f, 1.000f);

    Button _btnReal, _btnSim;
    Image  _bgReal,  _bgSim;
    Text   _txtReal, _txtSim;

    bool _realModeActive;

    IEnumerator Start()
    {
        _btnReal = transform.Find("BtnReal")?.GetComponent<Button>();
        _btnSim  = transform.Find("BtnSim")?.GetComponent<Button>();
        _bgReal  = transform.Find("BtnReal")?.GetComponent<Image>();
        _bgSim   = transform.Find("BtnSim")?.GetComponent<Image>();
        _txtReal = transform.Find("BtnReal/T")?.GetComponent<Text>();
        _txtSim  = transform.Find("BtnSim/T")?.GetComponent<Text>();

        if (_btnReal) _btnReal.onClick.AddListener(SetRealMode);
        if (_btnSim)  _btnSim.onClick.AddListener(SetSimMode);

        // Wait one frame so the Canvas finishes its first layout pass
        // before we apply colors — prevents Button.CrossFadeColor from
        // overwriting our state on the very first frame.
        yield return null;

        // Default to Simulacion on launch — matches this scene's own
        // RosConnector.RosBridgeServerUrl (localhost), so no reconnect fires.
        _realModeActive = false;
        SetHighlight(_realModeActive);
        ApplyRosUrl(_realModeActive);
    }

    public void SetRealMode()
    {
        _realModeActive = true;
        SetHighlight(_realModeActive);
        ApplyRosUrl(_realModeActive);
    }

    public void SetSimMode()
    {
        _realModeActive = false;
        SetHighlight(_realModeActive);
        ApplyRosUrl(_realModeActive);
    }

    void ApplyRosUrl(bool realIsActive)
    {
        var rosConnector = FindObjectOfType<RosConnector>();
        if (rosConnector == null) return;

        string targetUrl = realIsActive ? RosUrlReal : RosUrlSim;
        if (rosConnector.RosBridgeServerUrl == targetUrl)
            return;

        Debug.Log($"[ModeToggleController] Pinning RosConnector to {(realIsActive ? "Eje. Real" : "Simulacion")}: {targetUrl}");
        rosConnector.RosBridgeServerUrl = targetUrl;
        rosConnector.ReconnectNow();

        // Keep the Configuracion panel's IP/Puerto fields in sync so they
        // don't keep showing whatever was there before this mode switch.
        FindObjectOfType<SecConfigController>()?.RefreshFromRosConnector();
    }

    void SetHighlight(bool realIsActive)
    {
        ApplyState(_bgReal, _btnReal, _txtReal, realIsActive);
        ApplyState(_bgSim,  _btnSim,  _txtSim,  !realIsActive);
    }

    void ApplyState(Image bg, Button btn, Text txt, bool active)
    {
        var bgColor = active ? ActiveBg : IdleBg;
        if (bg != null) bg.color = bgColor;
        if (btn != null)
        {
            var cb = btn.colors;
            cb.normalColor      = bgColor;
            cb.highlightedColor = active ? ActiveHighlighted : IdleHighlighted;
            cb.pressedColor     = active ? ActivePressed     : IdlePressed;
            cb.selectedColor    = bgColor;
            cb.fadeDuration     = 0.05f;
            btn.colors = cb;
            // Force the button to immediately snap to Normal state
            btn.targetGraphic.CrossFadeColor(bgColor, 0f, true, true);
        }
        if (txt != null) txt.color = active ? ActiveText : IdleText;
    }
}
