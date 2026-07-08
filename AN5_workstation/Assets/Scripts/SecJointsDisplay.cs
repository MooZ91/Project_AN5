using UnityEngine;
using UnityEngine.UI;

public class SecJointsDisplay : MonoBehaviour
{
    [Header("Data source")]
    public JointPositionSubscriber jointSubscriber;

    Text[] _valTexts;

    float[] _pending;
    bool    _hasPending;
    readonly object _lock = new object();

    void Start()
    {
        CollectFields();

        if (jointSubscriber == null)
            jointSubscriber = FindObjectOfType<JointPositionSubscriber>();

        if (jointSubscriber != null)
        {
            jointSubscriber.OnJointPositionsUpdated += OnPositionsReceived;
            UpdateDisplays(jointSubscriber.GetLastKnownPositions());
        }
        else
        {
            Debug.LogWarning("[SecJointsDisplay] JointPositionSubscriber not found.");
        }
    }

    void OnDestroy()
    {
        if (jointSubscriber != null)
            jointSubscriber.OnJointPositionsUpdated -= OnPositionsReceived;
    }

    void OnPositionsReceived(float[] positions)
    {
        lock (_lock)
        {
            _pending = (float[])positions.Clone();
            _hasPending = true;
        }
    }

    void Update()
    {
        float[] positions = null;
        lock (_lock)
        {
            if (_hasPending)
            {
                positions = _pending;
                _hasPending = false;
            }
        }
        if (positions != null)
            UpdateDisplays(positions);
    }

    void CollectFields()
    {
        var body = transform.Find("Body");
        if (body == null) { Debug.LogWarning("[SecJointsDisplay] 'Body' not found under " + name); return; }

        var texts = new System.Collections.Generic.List<Text>();
        foreach (Transform joint in body)
        {
            // Try ValBox/Val first (boxed layout), fall back to Val directly
            var valT = joint.Find("ValBox/Val") ?? joint.Find("Val");
            if (valT == null) { Debug.LogWarning($"[SecJointsDisplay] 'Val' not found under {joint.name}"); continue; }
            texts.Add(valT.GetComponent<Text>());
        }

        _valTexts = texts.ToArray();
        Debug.Log($"[SecJointsDisplay] Ready — {_valTexts.Length} joints tracked");
    }

    void UpdateDisplays(float[] positions)
    {
        for (int i = 0; i < positions.Length && i < _valTexts.Length; i++)
        {
            if (_valTexts[i] != null)
                _valTexts[i].text = positions[i].ToString("F1") + "°";
        }
    }
}
