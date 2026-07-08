using UnityEngine;
using UnityEngine.UI;

/// Displays the live Cartesian position from CartesianPositionSubscriber
/// in the six Val texts of SecPosition (X, Y, Z, Rz, Ry, Rx).
public class SecPositionDisplay : MonoBehaviour
{
    CartesianPositionSubscriber _cartSub;

    Text _valX, _valY, _valZ, _valRz, _valRy, _valRx;

    void Start()
    {
        _valX  = transform.Find("Body/BoxX/Val")?.GetComponent<Text>();
        _valY  = transform.Find("Body/BoxY/Val")?.GetComponent<Text>();
        _valZ  = transform.Find("Body/BoxZ/Val")?.GetComponent<Text>();
        _valRz = transform.Find("Body/BoxRz/Val")?.GetComponent<Text>();
        _valRy = transform.Find("Body/BoxRy/Val")?.GetComponent<Text>();
        _valRx = transform.Find("Body/BoxRx/Val")?.GetComponent<Text>();

        _cartSub = Object.FindFirstObjectByType<CartesianPositionSubscriber>();
        if (_cartSub == null)
            Debug.LogWarning("[SecPositionDisplay] CartesianPositionSubscriber not found.");
    }

    void Update()
    {
        if (_cartSub == null) return;

        float[] p = _cartSub.GetLastKnownCartesianPositions();

        if (_valX  != null) _valX.text  = p[0].ToString("F2");
        if (_valY  != null) _valY.text  = p[1].ToString("F2");
        if (_valZ  != null) _valZ.text  = p[2].ToString("F2");
        if (_valRz != null) _valRz.text = p[3].ToString("F2");
        if (_valRy != null) _valRy.text = p[4].ToString("F2");
        if (_valRx != null) _valRx.text = p[5].ToString("F2");
    }
}
