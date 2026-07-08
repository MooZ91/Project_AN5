using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class AddSaveTxtButtonEditor
{
    [MenuItem("Tools/Robot Arm/Add 'Save TXT' Button")]
    static void AddButton()
    {
        int added = 0;

        foreach (var ctrl in Resources.FindObjectsOfTypeAll<SecCoordQueueController>())
        {
            // Skip prefab assets that are not part of an open scene
            if (!ctrl.gameObject.scene.IsValid()) continue;

            Transform body = ctrl.transform.Find("Body");
            if (body == null)
            {
                Debug.LogWarning($"[AddSaveTxtButton] 'Body' not found under {ctrl.name}");
                continue;
            }

            if (body.Find("BtnRow_SaveTxt") != null)
            {
                Debug.Log($"[AddSaveTxtButton] BtnRow_SaveTxt already exists under {ctrl.name}/Body");
                continue;
            }

            Transform srcRow = body.Find("BtnRow_Send");
            if (srcRow == null)
            {
                Debug.LogWarning($"[AddSaveTxtButton] BtnRow_Send not found under {ctrl.name}/Body");
                continue;
            }

            // Duplicate BtnRow_Send
            GameObject newRow = Object.Instantiate(srcRow.gameObject, body);
            Undo.RegisterCreatedObjectUndo(newRow, "Add Save TXT Button");
            newRow.name = "BtnRow_SaveTxt";

            // Rename inner button
            Transform innerBtn = newRow.transform.GetChild(0);
            if (innerBtn != null)
            {
                innerBtn.name = "Btn_SaveTxt";

                // Update label text
                Text label = innerBtn.GetComponentInChildren<Text>();
                if (label != null)
                {
                    Undo.RecordObject(label, "Add Save TXT Button");
                    label.text = "Save TXT";
                }

                // Recolor to match the UI orange palette
                Color orange      = new Color(1f, 0.62f, 0.1f, 1f);
                Color orangeFaint = new Color(1f, 0.62f, 0.1f, 0.3f);

                var img = innerBtn.GetComponent<Image>();
                if (img != null) { Undo.RecordObject(img, "Add Save TXT Button"); img.color = orange; }

                var outline = innerBtn.GetComponent<Outline>();
                if (outline != null) { Undo.RecordObject(outline, "Add Save TXT Button"); outline.effectColor = orangeFaint; }

                // Clear any persistent onClick listeners from the duplicate
                Button btn = innerBtn.GetComponent<Button>();
                if (btn != null)
                {
                    Undo.RecordObject(btn, "Add Save TXT Button");
                    btn.onClick.RemoveAllListeners();
                }
            }

            EditorUtility.SetDirty(ctrl.gameObject);
            added++;
            Debug.Log($"[AddSaveTxtButton] Added BtnRow_SaveTxt under {ctrl.name}/Body");
        }

        if (added > 0)
        {
            foreach (var scene in new[]
            {
                UnityEngine.SceneManagement.SceneManager.GetActiveScene()
            })
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[AddSaveTxtButton] Done — added {added} button(s). Save the scene to persist.");
        }
        else
        {
            Debug.LogWarning("[AddSaveTxtButton] No buttons were added. Is the scene with SecCoordQueueController open?");
        }
    }

    [MenuItem("Tools/Robot Arm/Add 'Save TXT' Button", true)]
    static bool Validate() => !Application.isPlaying;

    [MenuItem("Tools/Robot Arm/Attach SecTrajController")]
    static void AttachSecTrajController()
    {
        int attached = 0;
        foreach (var rt in Resources.FindObjectsOfTypeAll<RectTransform>())
        {
            if (!rt.gameObject.scene.IsValid()) continue;
            if (rt.name != "SecTraj") continue;
            if (rt.GetComponent<SecTrajController>() != null)
            {
                Debug.Log($"[SecTraj] SecTrajController already on {rt.name}");
                continue;
            }
            Undo.AddComponent<SecTrajController>(rt.gameObject);
            EditorUtility.SetDirty(rt.gameObject);
            attached++;
            Debug.Log($"[SecTraj] Attached SecTrajController to {rt.name}");
        }
        if (attached > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        else
            Debug.LogWarning("[SecTraj] No SecTraj object found in open scenes.");
    }

    [MenuItem("Tools/Robot Arm/Recolor 'Save TXT' Button (orange)")]
    static void RecolorButton()
    {
        Color orange      = new Color(1f, 0.62f, 0.1f, 1f);
        Color orangeFaint = new Color(1f, 0.62f, 0.1f, 0.3f);
        int recolored = 0;

        foreach (var ctrl in Resources.FindObjectsOfTypeAll<SecCoordQueueController>())
        {
            if (!ctrl.gameObject.scene.IsValid()) continue;

            Transform btn = ctrl.transform.Find("Body/BtnRow_SaveTxt/Btn_SaveTxt");
            if (btn == null) continue;

            var img = btn.GetComponent<Image>();
            if (img != null) { Undo.RecordObject(img, "Recolor Save TXT"); img.color = orange; }

            var outline = btn.GetComponent<Outline>();
            if (outline != null) { Undo.RecordObject(outline, "Recolor Save TXT"); outline.effectColor = orangeFaint; }

            EditorUtility.SetDirty(btn.gameObject);
            recolored++;
        }

        if (recolored > 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log($"[AddSaveTxtButton] Recolored {recolored} button(s). Save the scene.");
        }
        else
            Debug.LogWarning("[AddSaveTxtButton] Btn_SaveTxt not found. Run 'Add Save TXT Button' first.");
    }
}
