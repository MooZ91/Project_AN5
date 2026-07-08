using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// Lightweight polyline renderer for UI trend graphs (no package dependency:
/// a MaskableGraphic building its own quad-strip mesh in OnPopulateMesh).
/// Points are pre-normalized to 0..1 by the caller (SecTrendGraphController),
/// so two lines sharing the same plot area can be compared on the same
/// vertical scale.
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(CanvasRenderer))]
public class UITrendLine : MaskableGraphic
{
    public float lineThickness = 2f;

    private readonly List<float> _normalized = new List<float>();

    public void SetNormalizedPoints(List<float> points)
    {
        _normalized.Clear();
        if (points != null)
            _normalized.AddRange(points);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        int n = _normalized.Count;
        if (n < 2) return;

        Rect r = rectTransform.rect;
        float half = lineThickness * 0.5f;

        Vector2[] pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float x = r.xMin + r.width * i / (n - 1);
            float y = r.yMin + Mathf.Clamp01(_normalized[i]) * r.height;
            pts[i] = new Vector2(x, y);
        }

        for (int i = 0; i < n - 1; i++)
        {
            Vector2 p0 = pts[i];
            Vector2 p1 = pts[i + 1];
            Vector2 dir = p1 - p0;
            if (dir.sqrMagnitude < 1e-8f) dir = Vector2.right;
            dir.Normalize();
            Vector2 normal = new Vector2(-dir.y, dir.x) * half;

            int idx = vh.currentVertCount;
            vh.AddVert(p0 - normal, color, Vector2.zero);
            vh.AddVert(p0 + normal, color, Vector2.zero);
            vh.AddVert(p1 + normal, color, Vector2.zero);
            vh.AddVert(p1 - normal, color, Vector2.zero);
            vh.AddTriangle(idx, idx + 1, idx + 2);
            vh.AddTriangle(idx, idx + 2, idx + 3);
        }
    }
}
