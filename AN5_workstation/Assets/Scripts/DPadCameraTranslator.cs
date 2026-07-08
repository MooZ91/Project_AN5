using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DPadCameraTranslator : MonoBehaviour
{
    [Header("Settings")]
    public float panSpeed = 2f;
    public float mouseSensitivity = 0.01f;

    DPadButton _btnUp, _btnDown, _btnLeft, _btnRight;
    bool _wasAnyPressed;

    void Start()
    {
        // Search by name so the path survives reparenting
        GameObject dpad = null;
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            if (t.name == "DPad_Translate" && t.gameObject.activeInHierarchy) { dpad = t.gameObject; break; }
        if (dpad == null) { Debug.LogError("[DPadTranslate] DPad_Translate not found"); return; }

        foreach (Transform child in dpad.transform)
        {
            var txt = child.GetComponentInChildren<Text>();
            if (txt == null) continue;
            var db = child.GetComponent<DPadButton>() ?? child.gameObject.AddComponent<DPadButton>();
            switch (txt.text.Trim())
            {
                case "↑": _btnUp    = db; break;
                case "↓": _btnDown  = db; break;
                case "←": _btnLeft  = db; break;
                case "→": _btnRight = db; break;
            }
        }
    }

    void Update()
    {
        bool isTyping = EventSystem.current != null
                     && EventSystem.current.currentSelectedGameObject != null
                     && EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null;

        bool kUp    = !isTyping && Input.GetKey(KeyCode.UpArrow);
        bool kDown  = !isTyping && Input.GetKey(KeyCode.DownArrow);
        bool kLeft  = !isTyping && Input.GetKey(KeyCode.LeftArrow);
        bool kRight = !isTyping && Input.GetKey(KeyCode.RightArrow);
        bool mouse  = Input.GetMouseButton(1) && !EventSystem.current.IsPointerOverGameObject();

        bool anyPressed = kUp || kDown || kLeft || kRight || mouse
                       || (_btnUp    != null && _btnUp.isPressed)
                       || (_btnDown  != null && _btnDown.isPressed)
                       || (_btnLeft  != null && _btnLeft.isPressed)
                       || (_btnRight != null && _btnRight.isPressed);

        _wasAnyPressed = anyPressed;
        if (!anyPressed) return;

        float dt = Time.deltaTime * panSpeed;
        Vector3 move = Vector3.zero;

        if (kUp    || (_btnUp    != null && _btnUp.isPressed))    move += transform.up;
        if (kDown  || (_btnDown  != null && _btnDown.isPressed))  move -= transform.up;
        if (kLeft  || (_btnLeft  != null && _btnLeft.isPressed))  move -= transform.right;
        if (kRight || (_btnRight != null && _btnRight.isPressed)) move += transform.right;

        if (move != Vector3.zero)
            transform.position += move.normalized * dt;

        if (mouse)
        {
            transform.position -= transform.right * (Input.GetAxis("Mouse X") * mouseSensitivity);
            transform.position -= transform.up    * (Input.GetAxis("Mouse Y") * mouseSensitivity);
        }
    }
}
