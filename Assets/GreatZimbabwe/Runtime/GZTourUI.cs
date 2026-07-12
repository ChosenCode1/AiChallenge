using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Self-building overlay UI for the aerial tour: a question bar (bottom), a
/// streaming subtitle panel above it, and a status strip (top) that shows the
/// question lock. Everything is constructed in code at Awake so the scene
/// needs no authored UI assets; visuals of the NPC itself are out of scope.
/// Uses the Input System UI module (this project is new-Input-System only).
/// </summary>
public class GZTourUI : MonoBehaviour
{
    public enum StatusTone { Ready, Busy, Warn }

    [Header("Wiring (set by GZTourSetup)")]
    public GZTourDirector director;

    [Header("Style")]
    public Color panelColor = new Color(0.055f, 0.045f, 0.035f, 0.66f);
    public Color textColor = new Color(0.95f, 0.92f, 0.85f, 1f);
    public Color accentColor = new Color(0.91f, 0.65f, 0.24f, 1f);
    public Color readyColor = new Color(0.44f, 0.75f, 0.47f, 1f);
    public Color warnColor = new Color(0.85f, 0.42f, 0.32f, 1f);

    TMP_InputField _input;
    Button _askButton;
    TMP_Text _askLabel;
    TMP_Text _subtitle;
    TMP_Text _status;
    Image _statusDot;
    TMP_Text _destination;
    GameObject _subtitlePanel;
    bool _built;

    const string IntroText =
        "Welcome to Great Zimbabwe. Ask about the Hill Complex, the Great Enclosure, the Conical Tower, " +
        "the Valley Ruins, the village, or the cattle — the camera flies wherever your questions lead.";

    void Awake()
    {
        EnsureBuilt();
    }

    // ---------- public API (called by the director) ----------

    public void SetLocked(bool locked)
    {
        EnsureBuilt();
        _input.interactable = !locked;
        _askButton.interactable = !locked;
        _askLabel.color = locked ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
        ((TMP_Text)_input.placeholder).text = locked
            ? "The guide is speaking…"
            : "Ask the guide anything about Great Zimbabwe…";
        if (!locked)
        {
            _input.text = "";
            _input.ActivateInputField();
        }
    }

    public void SetStatus(string message, StatusTone tone)
    {
        EnsureBuilt();
        _status.text = message;
        _statusDot.color = tone == StatusTone.Ready ? readyColor : tone == StatusTone.Warn ? warnColor : accentColor;
    }

    public void SetDestination(string message)
    {
        EnsureBuilt();
        _destination.text = message;
    }

    public void ShowQuestion(string question)
    {
        EnsureBuilt();
        _subtitlePanel.SetActive(true);
        _subtitle.text = "<color=#" + ColorUtility.ToHtmlStringRGB(accentColor) + ">You:</color> " +
                         question + "\n\n";
    }

    public void AppendAnswer(string token)
    {
        EnsureBuilt();
        _subtitlePanel.SetActive(true);
        _subtitle.text += token;
    }

    // ---------- construction ----------

    void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("GZ_EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.transform.SetParent(transform, false);
        }

        var canvasGO = new GameObject("GZ_TourCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        BuildStatusStrip(canvas.transform);
        BuildSubtitlePanel(canvas.transform);
        BuildInputBar(canvas.transform);

        _subtitle.text = IntroText;
        _destination.text = "";
    }

    void BuildStatusStrip(Transform parent)
    {
        var strip = Panel(parent, "StatusStrip", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0, -14), new Vector2(760, 40));

        _statusDot = new GameObject("Dot", typeof(Image)).GetComponent<Image>();
        _statusDot.transform.SetParent(strip.transform, false);
        var dotRT = _statusDot.rectTransform;
        dotRT.anchorMin = dotRT.anchorMax = new Vector2(0f, 0.5f);
        dotRT.pivot = new Vector2(0f, 0.5f);
        dotRT.anchoredPosition = new Vector2(16, 0);
        dotRT.sizeDelta = new Vector2(14, 14);
        _statusDot.color = readyColor;

        _status = Text(strip.transform, "StatusText", 21, TextAlignmentOptions.MidlineLeft);
        var srt = _status.rectTransform;
        srt.anchorMin = new Vector2(0, 0);
        srt.anchorMax = new Vector2(1, 1);
        srt.offsetMin = new Vector2(42, 0);
        srt.offsetMax = new Vector2(-14, 0);

        _destination = Text(parent, "Destination", 22, TextAlignmentOptions.Center);
        _destination.color = accentColor;
        _destination.fontStyle = FontStyles.SmallCaps | FontStyles.Bold;
        var drt = _destination.rectTransform;
        drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 1f);
        drt.pivot = new Vector2(0.5f, 1f);
        drt.anchoredPosition = new Vector2(0, -60);
        drt.sizeDelta = new Vector2(900, 32);
    }

    void BuildSubtitlePanel(Transform parent)
    {
        _subtitlePanel = Panel(parent, "SubtitlePanel", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(0, 118), new Vector2(1040, 100)).gameObject;

        var layout = _subtitlePanel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 14, 14);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = _subtitlePanel.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _subtitle = Text(_subtitlePanel.transform, "Subtitle", 25, TextAlignmentOptions.TopLeft);
        _subtitle.richText = true;
        _subtitle.lineSpacing = 6f;
    }

    void BuildInputBar(Transform parent)
    {
        var bar = new GameObject("InputBar", typeof(RectTransform));
        bar.transform.SetParent(parent, false);
        var rt = bar.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0, 30);
        rt.sizeDelta = new Vector2(1040, 66);

        // input field ----------------------------------------------------
        var fieldGO = new GameObject("QuestionField", typeof(Image), typeof(TMP_InputField));
        fieldGO.transform.SetParent(bar.transform, false);
        var fieldRT = fieldGO.GetComponent<RectTransform>();
        fieldRT.anchorMin = new Vector2(0, 0);
        fieldRT.anchorMax = new Vector2(1, 1);
        fieldRT.offsetMin = Vector2.zero;
        fieldRT.offsetMax = new Vector2(-160, 0);
        var fieldBG = fieldGO.GetComponent<Image>();
        fieldBG.color = panelColor;

        var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(fieldGO.transform, false);
        var areaRT = area.GetComponent<RectTransform>();
        areaRT.anchorMin = Vector2.zero;
        areaRT.anchorMax = Vector2.one;
        areaRT.offsetMin = new Vector2(20, 8);
        areaRT.offsetMax = new Vector2(-20, -8);

        var placeholder = Text(area.transform, "Placeholder", 25, TextAlignmentOptions.MidlineLeft);
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.color = new Color(textColor.r, textColor.g, textColor.b, 0.45f);
        placeholder.text = "Ask the guide anything about Great Zimbabwe…";
        Stretch(placeholder.rectTransform);

        var textComp = Text(area.transform, "Text", 25, TextAlignmentOptions.MidlineLeft);
        Stretch(textComp.rectTransform);

        _input = fieldGO.GetComponent<TMP_InputField>();
        _input.targetGraphic = fieldBG;
        _input.textViewport = areaRT;
        _input.textComponent = textComp;
        _input.placeholder = placeholder;
        _input.lineType = TMP_InputField.LineType.SingleLine;
        _input.caretColor = accentColor;
        _input.customCaretColor = true;
        _input.selectionColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.35f);
        _input.onSubmit.AddListener(_ => Submit());

        // ask button ------------------------------------------------------
        var buttonGO = new GameObject("AskButton", typeof(Image), typeof(Button));
        buttonGO.transform.SetParent(bar.transform, false);
        var brt = buttonGO.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(1, 0);
        brt.anchorMax = new Vector2(1, 1);
        brt.pivot = new Vector2(1, 0.5f);
        brt.offsetMin = new Vector2(-146, 0);
        brt.offsetMax = new Vector2(0, 0);
        var bImg = buttonGO.GetComponent<Image>();
        bImg.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.92f);
        _askButton = buttonGO.GetComponent<Button>();
        _askButton.targetGraphic = bImg;
        _askButton.onClick.AddListener(Submit);

        _askLabel = Text(buttonGO.transform, "Label", 26, TextAlignmentOptions.Center);
        _askLabel.fontStyle = FontStyles.Bold;
        _askLabel.color = Color.white;
        _askLabel.text = "ASK";
        Stretch(_askLabel.rectTransform);
    }

    void Submit()
    {
        if (director == null || _input == null) return;
        string q = _input.text;
        if (string.IsNullOrWhiteSpace(q)) return;
        if (!director.AskQuestion(q))
            return;  // locked: director refused, leave the text in place
        _input.text = "";
    }

    // ---------- tiny builders ----------

    Image Panel(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 pivot,
                Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = panelColor;
        var rt = img.rectTransform;
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return img;
    }

    TMP_Text Text(Transform parent, string name, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.color = textColor;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
