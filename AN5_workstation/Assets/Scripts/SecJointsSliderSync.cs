using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// Wires each SecJoints slider to its InputField display.
/// Attach to the active SecJoints panel.
public class SecJointsSliderSync : MonoBehaviour
{
    static readonly string[] JointNames =
    {
        "Joint_BASE", "Joint_SHOULDER", "Joint_ELBOW",
        "Joint_WRIST 1", "Joint_WRIST 2", "Joint_WRIST 3"
    };
    static readonly string[] InputNames =
    {
        "J1ValInput", "J2ValInput", "J3ValInput",
        "J4ValInput", "J5ValInput", "J6ValInput"
    };

    [Tooltip("Degrees added/removed per click of a joint's +/- button.")]
    public float step = 1f;

    void Start()
    {
        var body = transform.Find("Body");
        if (body == null) { Debug.LogWarning("[SecJointsSliderSync] Body not found."); return; }

        for (int i = 0; i < JointNames.Length; i++)
        {
            var joint = body.Find(JointNames[i]);
            if (joint == null) continue;

            var slider = joint.Find("S")?.GetComponent<Slider>();
            var input  = joint.Find("Val/" + InputNames[i])?.GetComponent<InputField>();

            if (slider == null || input == null) continue;

            // Wire +/- buttons
            var minusBtn = joint.Find("BtnRow/M")?.GetComponent<Button>();
            var plusBtn  = joint.Find("BtnRow/P")?.GetComponent<Button>();
            var capturedSliderForBtns = slider;
            if (minusBtn != null)
            {
                minusBtn.onClick.RemoveAllListeners();
                minusBtn.onClick.AddListener(() =>
                    capturedSliderForBtns.value = Mathf.Max(capturedSliderForBtns.minValue, capturedSliderForBtns.value - step));
            }
            if (plusBtn != null)
            {
                plusBtn.onClick.RemoveAllListeners();
                plusBtn.onClick.AddListener(() =>
                    capturedSliderForBtns.value = Mathf.Min(capturedSliderForBtns.maxValue, capturedSliderForBtns.value + step));
            }

            // Seed the display with the current slider value
            input.text = slider.value.ToString("F1", CultureInfo.InvariantCulture);

            // Slider → InputField
            var capturedInput  = input;
            slider.onValueChanged.AddListener(v =>
                capturedInput.text = v.ToString("F1", CultureInfo.InvariantCulture));

            // InputField → Slider
            var capturedSlider = slider;
            input.onEndEdit.AddListener(text =>
            {
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                    capturedSlider.value = Mathf.Clamp(val, capturedSlider.minValue, capturedSlider.maxValue);
                else
                    capturedInput.text = capturedSlider.value.ToString("F1", CultureInfo.InvariantCulture);
            });
        }
    }
}
