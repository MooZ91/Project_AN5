#if UNITY_EDITOR
using UnityEngine;
        using UnityEngine.UI;
        using UnityEditor;
        using UnityEditor.SceneManagement;

        public static class RebuildSecJointsCmd
        {
            [MenuItem("Tools/Rebuild SecJoints 2Row")]
            public static void Execute()
            {
                Color BG, PANEL, BORDER, BORDER2, TEXT, ACCENT, BLUE;
                ColorUtility.TryParseHtmlString("#0c0e10", out BG);
                ColorUtility.TryParseHtmlString("#191d23", out PANEL);
                ColorUtility.TryParseHtmlString("#252b34", out BORDER);
                ColorUtility.TryParseHtmlString("#2e3540", out BORDER2);
                ColorUtility.TryParseHtmlString("#d4dae4", out TEXT);
                ColorUtility.TryParseHtmlString("#00d4aa", out ACCENT);
                ColorUtility.TryParseHtmlString("#4a9eff", out BLUE);
        
                var font = AssetDatabase.LoadAssetAtPath<Font>(
                    "Assets/Prefabs/UI/Fuentes/Sans-pro/SourceSansPro-Regular.ttf");
                if (font == null) { Debug.LogError("Font not found"); return; }
        
                var secJoints = GameObject.Find("Panels/CenterBottom/SecJoints");
                if (secJoints == null) { Debug.LogError("SecJoints not found"); return; }
        
                // ── Clear all children except Head ────────────────────────────────
                var kill = new System.Collections.Generic.List<GameObject>();
                foreach (Transform ch in secJoints.transform)
                    if (ch.name != "Head") kill.Add(ch.gameObject);
                foreach (var g in kill) Object.DestroyImmediate(g);
        
                // ── Resize SecJoints ──────────────────────────────────────────────
                // 2 rows × 100px card + spacing(6) + head(24) + pads(8+8) = 246px
                const float CARD_H    = 100f;
                const float CARD_SPACING = 6f;
                const float HEAD_H    = 24f;
                const float PAD       = 8f;
                float secH = HEAD_H + PAD + CARD_H * 2 + CARD_SPACING + PAD;  // 246
        
                var secRT = secJoints.GetComponent<RectTransform>();
                secRT.sizeDelta = new Vector2(secRT.sizeDelta.x, secH);
                var secLE = secJoints.GetComponent<LayoutElement>()
                          ?? secJoints.AddComponent<LayoutElement>();
                secLE.preferredHeight = secH;
        
                // ── Body: GridLayoutGroup 2×3 ─────────────────────────────────────
                var body = new GameObject("Body");
                body.transform.SetParent(secJoints.transform, false);
                var bodyRT = body.AddComponent<RectTransform>();
                bodyRT.anchorMin = Vector2.zero;
                bodyRT.anchorMax = Vector2.one;
                bodyRT.offsetMin = new Vector2(PAD, PAD);
                bodyRT.offsetMax = new Vector2(-PAD, -HEAD_H - 2f);
        
                // GridLayoutGroup: 2 rows, 3 columns, cells fill width
                // Available width = SecJoints.sizeDelta.x - 2*PAD - 2*spacing_between_cols
                // = 1350 - 16 - 12 = 1322 → cell_w = 1322/3 ≈ 440
                var gl = body.AddComponent<GridLayoutGroup>();
                gl.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
                gl.constraintCount = 3;
                gl.spacing         = new Vector2(CARD_SPACING, CARD_SPACING);
                gl.padding         = new RectOffset(0, 0, 0, 0);
                gl.childAlignment  = TextAnchor.UpperLeft;
                gl.startCorner     = GridLayoutGroup.Corner.UpperLeft;
                gl.startAxis       = GridLayoutGroup.Axis.Horizontal;
                // Cell width calculated to fill: (1350-16-12)/3 = 440.67 → use 440
                // Cell height = CARD_H
                gl.cellSize = new Vector2(440f, CARD_H);
        
                // ── Joint data ────────────────────────────────────────────────────
                string[] jN  = {"BASE","SHOULDER","ELBOW","WRIST 1","WRIST 2","WRIST 3"};
                string[] jT  = {"J1","J2","J3","J4","J5","J6"};
                float[]  jV  = {0f,-45.3f,32.1f,10.5f,85.2f,-15f};
                float[]  jMi = {-180f,-90f,-135f,-180f,-180f,-360f};
                float[]  jMa = {180f,90f,135f,180f,180f,360f};
        
                for (int i = 0; i < 6; i++)
                {
                    Color bc = i == 0 ? ACCENT : BORDER;
        
                    // Card root
                    var card = new GameObject("Joint_" + jN[i]);
                    card.transform.SetParent(body.transform, false);
                    // GridLayoutGroup controls size — no LayoutElement needed
                    card.AddComponent<Image>().color = PANEL;
                    var cOL = card.AddComponent<Outline>();
                    cOL.effectColor = bc; cOL.effectDistance = new Vector2(1, -1);
        
                    // Card inner layout: vertical
                    //   Row1: Info (name + badge) + value label    — 36px
                    //   Row2: − slider +                           — 38px
                    //   (padding 8px top/bottom, spacing 6px → total = 8+36+6+38+8 = 96 ≤ 100)
                    var cardVL = card.AddComponent<VerticalLayoutGroup>();
                    cardVL.padding = new RectOffset(10, 10, 8, 8);
                    cardVL.spacing = 6f;
                    cardVL.childControlHeight  = false;
                    cardVL.childControlWidth   = true;
                    cardVL.childForceExpandHeight = false;
                    cardVL.childForceExpandWidth  = true;
        
                    // ── Row 1: Info row (name+badge LEFT, value RIGHT) ────────────
                    var row1 = new GameObject("Row1");
                    row1.transform.SetParent(card.transform, false);
                    row1.AddComponent<RectTransform>().sizeDelta = new Vector2(0, 36f);
                    var r1HL = row1.AddComponent<HorizontalLayoutGroup>();
                    r1HL.spacing = 6f; r1HL.childAlignment = TextAnchor.MiddleLeft;
                    r1HL.childControlHeight = true; r1HL.childControlWidth = true;
                    r1HL.childForceExpandHeight = true; r1HL.childForceExpandWidth = false;
        
                    // Info sub-col: name stacked over badge
                    var info = new GameObject("Info");
                    info.transform.SetParent(row1.transform, false);
                    info.AddComponent<RectTransform>();
                    var inLE = info.AddComponent<LayoutElement>();
                    inLE.preferredWidth = 90f; inLE.minWidth = 90f;
                    var inVL = info.AddComponent<VerticalLayoutGroup>();
                    inVL.spacing = 3f; inVL.childControlHeight = false; inVL.childControlWidth = true;
                    inVL.childForceExpandHeight = false; inVL.childForceExpandWidth = true;
        
                    var nGO = new GameObject("N"); nGO.transform.SetParent(info.transform, false);
                    nGO.AddComponent<RectTransform>().sizeDelta = new Vector2(0, 18f);
                    var nT = nGO.AddComponent<Text>(); nT.font=font; nT.text=jN[i]; nT.fontSize=13;
                    nT.color=TEXT; nT.fontStyle=FontStyle.Bold; nT.alignment=TextAnchor.MiddleLeft; nT.raycastTarget=false;
        
                    var bBG = new GameObject("Badge"); bBG.transform.SetParent(info.transform, false);
                    bBG.AddComponent<RectTransform>().sizeDelta = new Vector2(0, 15f);
                    bBG.AddComponent<Image>().color = new Color(BLUE.r,BLUE.g,BLUE.b,.15f);
                    var bOL = bBG.AddComponent<Outline>(); bOL.effectColor=new Color(BLUE.r,BLUE.g,BLUE.b,.4f); bOL.effectDistance=new Vector2(1,-1);
                    var bTG = new GameObject("T"); bTG.transform.SetParent(bBG.transform, false);
                    var bTRT = bTG.AddComponent<RectTransform>(); bTRT.anchorMin=Vector2.zero; bTRT.anchorMax=Vector2.one; bTRT.offsetMin=bTRT.offsetMax=Vector2.zero;
                    var bT = bTG.AddComponent<Text>(); bT.font=font; bT.text=jT[i]; bT.fontSize=11;
                    bT.color=BLUE; bT.fontStyle=FontStyle.Bold; bT.alignment=TextAnchor.MiddleCenter; bT.raycastTarget=false;
        
                    // Spacer
                    var spacer = new GameObject("Spacer"); spacer.transform.SetParent(row1.transform, false);
                    spacer.AddComponent<RectTransform>(); spacer.AddComponent<LayoutElement>().flexibleWidth=1;
        
                    // Value label (right side, large)
                    var vGO = new GameObject("Val"); vGO.transform.SetParent(row1.transform, false);
                    vGO.AddComponent<RectTransform>();
                    var vLE = vGO.AddComponent<LayoutElement>(); vLE.preferredWidth=70f; vLE.minWidth=70f;
                    var vTxt = vGO.AddComponent<Text>(); vTxt.font=font; vTxt.text=jV[i].ToString("F1")+"\u00b0"; vTxt.fontSize=20;
                    vTxt.color=ACCENT; vTxt.fontStyle=FontStyle.Bold; vTxt.alignment=TextAnchor.MiddleRight; vTxt.raycastTarget=false;
        
                    // ── Row 2: [ − ][ Slider ][ + ] ──────────────────────────────
                    var row2 = new GameObject("Row2");
                    row2.transform.SetParent(card.transform, false);
                    row2.AddComponent<RectTransform>().sizeDelta = new Vector2(0, 38f);
                    var r2HL = row2.AddComponent<HorizontalLayoutGroup>();
                    r2HL.spacing = 6f; r2HL.childAlignment = TextAnchor.MiddleLeft;
                    r2HL.childControlHeight = true; r2HL.childControlWidth = true;
                    r2HL.childForceExpandHeight = true; r2HL.childForceExpandWidth = false;
        
                    // Minus button
                    var mG = new GameObject("M"); mG.transform.SetParent(row2.transform, false); mG.AddComponent<RectTransform>();
                    var mLE = mG.AddComponent<LayoutElement>(); mLE.preferredWidth=36f; mLE.minWidth=36f;
                    mG.AddComponent<Image>().color = BG;
                    var mOL = mG.AddComponent<Outline>(); mOL.effectColor=BORDER2; mOL.effectDistance=new Vector2(1,-1);
                    var mBtn = mG.AddComponent<Button>(); var mC=mBtn.colors; mC.normalColor=BG; mC.highlightedColor=BORDER2; mC.pressedColor=ACCENT; mBtn.colors=mC;
                    var mTG = new GameObject("T"); mTG.transform.SetParent(mG.transform, false);
                    var mTRT = mTG.AddComponent<RectTransform>(); mTRT.anchorMin=Vector2.zero; mTRT.anchorMax=Vector2.one; mTRT.offsetMin=mTRT.offsetMax=Vector2.zero;
                    var mTxt = mTG.AddComponent<Text>(); mTxt.font=font; mTxt.text="-"; mTxt.fontSize=22; mTxt.color=TEXT; mTxt.fontStyle=FontStyle.Bold; mTxt.alignment=TextAnchor.MiddleCenter; mTxt.raycastTarget=false;
        
                    // Slider
                    var sG = new GameObject("S"); sG.transform.SetParent(row2.transform, false); sG.AddComponent<RectTransform>(); sG.AddComponent<LayoutElement>().flexibleWidth=1;
                    // Track bg
                    var sBG2 = new GameObject("BG"); sBG2.transform.SetParent(sG.transform, false);
                    var sBGRT = sBG2.AddComponent<RectTransform>(); sBGRT.anchorMin=new Vector2(0,.4f); sBGRT.anchorMax=new Vector2(1,.6f); sBGRT.offsetMin=sBGRT.offsetMax=Vector2.zero;
                    sBG2.AddComponent<Image>().color = BORDER2;
                    // Fill area
                    var sFA = new GameObject("FA"); sFA.transform.SetParent(sG.transform, false);
                    var sFART = sFA.AddComponent<RectTransform>(); sFART.anchorMin=new Vector2(0,.4f); sFART.anchorMax=new Vector2(1,.6f); sFART.offsetMin=Vector2.zero; sFART.offsetMax=new Vector2(-8,0);
                    var sFill = new GameObject("F"); sFill.transform.SetParent(sFA.transform, false);
                    var sFRT = sFill.AddComponent<RectTransform>(); sFRT.anchorMin=Vector2.zero; sFRT.anchorMax=new Vector2(.5f,1); sFRT.offsetMin=sFRT.offsetMax=Vector2.zero;
                    sFill.AddComponent<Image>().color = ACCENT;
                    // Handle
                    var sHA = new GameObject("HA"); sHA.transform.SetParent(sG.transform, false);
                    var sHART = sHA.AddComponent<RectTransform>(); sHART.anchorMin=Vector2.zero; sHART.anchorMax=Vector2.one; sHART.offsetMin=sHART.offsetMax=Vector2.zero;
                    var sH = new GameObject("H"); sH.transform.SetParent(sHA.transform, false);
                    var sHRT = sH.AddComponent<RectTransform>(); sHRT.sizeDelta=new Vector2(18,18);
                    var sHImg = sH.AddComponent<Image>(); sHImg.color=ACCENT;
                    // Slider component
                    var sl = sG.AddComponent<Slider>();
                    sl.fillRect=sFRT; sl.handleRect=sHRT; sl.targetGraphic=sHImg;
                    sl.direction=Slider.Direction.LeftToRight;
                    sl.minValue=jMi[i]; sl.maxValue=jMa[i]; sl.value=jV[i];
        
                    // Plus button
                    var pG = new GameObject("P"); pG.transform.SetParent(row2.transform, false); pG.AddComponent<RectTransform>();
                    var pLE = pG.AddComponent<LayoutElement>(); pLE.preferredWidth=36f; pLE.minWidth=36f;
                    pG.AddComponent<Image>().color = BG;
                    var pOL = pG.AddComponent<Outline>(); pOL.effectColor=BORDER2; pOL.effectDistance=new Vector2(1,-1);
                    var pBtn = pG.AddComponent<Button>(); var pC=pBtn.colors; pC.normalColor=BG; pC.highlightedColor=BORDER2; pC.pressedColor=ACCENT; pBtn.colors=pC;
                    var pTG = new GameObject("T"); pTG.transform.SetParent(pG.transform, false);
                    var pTRT = pTG.AddComponent<RectTransform>(); pTRT.anchorMin=Vector2.zero; pTRT.anchorMax=Vector2.one; pTRT.offsetMin=pTRT.offsetMax=Vector2.zero;
                    var pTxt = pTG.AddComponent<Text>(); pTxt.font=font; pTxt.text="+"; pTxt.fontSize=22; pTxt.color=TEXT; pTxt.fontStyle=FontStyle.Bold; pTxt.alignment=TextAnchor.MiddleCenter; pTxt.raycastTarget=false;
        
                    // ── Wire interactions ─────────────────────────────────────────
                    var captSl = sl; var captVt = vTxt;
                    captSl.onValueChanged.AddListener((float v) => captVt.text = v.ToString("F1") + "\u00b0");
                    mBtn.onClick.AddListener(() => captSl.value = Mathf.Max(captSl.minValue, captSl.value - 1f));
                    pBtn.onClick.AddListener(() => captSl.value = Mathf.Min(captSl.maxValue, captSl.value + 1f));
        
                    Debug.Log("[SecJoints] " + jN[i] + " built");
                }
        
                // ── Update CenterBottom height to fit ─────────────────────────────
                var centerBottom = GameObject.Find("Panels/CenterBottom");
                if (centerBottom != null)
                {
                    var cbRT = centerBottom.GetComponent<RectTransform>();
                    // SecJoints(246) + SecVelocity(48) + VL_spacing(8) + pads(10+10) = 332
                    const float newH = 246f + 48f + 8f + 20f;
                    cbRT.offsetMin = new Vector2(cbRT.offsetMin.x, 44f);
                    cbRT.offsetMax = new Vector2(cbRT.offsetMax.x, 44f + newH);
                    Debug.Log($"CenterBottom height set to {newH}");
                }
        
                Debug.Log("[SecJoints] Done - 2 rows x 3 cols, cards 440x100");
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
        }
#endif
        