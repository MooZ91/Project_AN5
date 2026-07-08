using UnityEngine;
        using UnityEngine.UI;
        
        public class JointSliderBinding : MonoBehaviour
        {
            public Slider slider;
            public Text   valText;
            public Button minusBtn;
            public Button plusBtn;
            public float  step = 1f;
        
            void Start()
            {
                // Resolve by direct child name — layout-agnostic
                if (slider   == null) slider   = GetComponentInChildren<Slider>(true);
                if (valText  == null) valText  = transform.Find("Val")?.GetComponent<Text>();
                if (minusBtn == null) minusBtn = transform.Find("BtnRow/M")?.GetComponent<Button>();
                if (plusBtn  == null) plusBtn  = transform.Find("BtnRow/P")?.GetComponent<Button>();
        
                if (slider == null || valText == null)
                {
                    Debug.LogWarning($"[JointSliderBinding] Missing refs on {name} — slider={slider} val={valText}");
                    return;
                }
        
                // Sync text immediately
                UpdateVal(slider.value);
        
                // Wire slider -> val
                slider.onValueChanged.RemoveAllListeners();
                slider.onValueChanged.AddListener(UpdateVal);
        
                // Wire +/- buttons
                if (minusBtn != null) { minusBtn.onClick.RemoveAllListeners(); minusBtn.onClick.AddListener(() => slider.value = Mathf.Max(slider.minValue, slider.value - step)); }
                if (plusBtn  != null) { plusBtn.onClick.RemoveAllListeners();  plusBtn.onClick.AddListener(()  => slider.value = Mathf.Min(slider.maxValue, slider.value + step)); }
        
                Debug.Log($"[JointSliderBinding] {name} wired — slider.value={slider.value:F1}");
            }
        
            void UpdateVal(float v) => valText.text = v.ToString("F1") + "\u00b0";
        
            void OnDestroy()
            {
                if (slider != null) slider.onValueChanged.RemoveListener(UpdateVal);
            }
        }
        