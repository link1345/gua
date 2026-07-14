using System;
using System.Collections;
using System.Collections.Generic;
using Gua.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class GuaUnityFixture
{
    private static readonly Color Background = new(0.30f, 0.30f, 0.30f, 1f);
    private static readonly Color ButtonNormal = new(0.16f, 0.16f, 0.16f, 1f);
    private static readonly Color ButtonHighlighted = new(0.22f, 0.22f, 0.22f, 1f);
    private static Font legacyFont;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Build()
    {
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
        Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var events = new GameObject("EventSystem");
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();
        }

        var cameraObject = new GameObject("BackgroundCamera", typeof(Camera));
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Background;
        camera.cullingMask = 0;
        camera.depth = -100;

        var inactiveScreen = new GameObject("InactiveScreenMarker", typeof(GuaScreen));
        inactiveScreen.GetComponent<GuaScreen>().Value = "inactive-screen";
        inactiveScreen.SetActive(false);

        var canvasObject = new GameObject("GuaUnitySample", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(GuaScreen));
        canvasObject.GetComponent<GuaScreen>().Value = "title";
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;

        var background = Rect("Background", canvasObject.transform, Vector2.zero, new Vector2(1280, 720));
        Stretch(background);
        background.gameObject.AddComponent<UnityEngine.UI.Image>().color = Background;

        var titleScreen = Rect("TitleScreen", background, Vector2.zero, new Vector2(360, 300));
        Title("Title", titleScreen, "Gua Unity Sample", new Vector2(0, 104), new Vector2(360, 56), 26);
        var start = Button("StartButton", "start", titleScreen, "Start Game", new Vector2(0, 20));
        var settings = Button("SettingsButton", "settings", titleScreen, "Settings", new Vector2(0, -52));

        var hint = Text("Hint", titleScreen, "Connected through Gua", new Vector2(0, -118), new Vector2(360, 28), 14);
        hint.color = new Color(0.78f, 0.78f, 0.78f, 1f);

        var loadingScreen = Rect("LoadingScreen", background, Vector2.zero, new Vector2(360, 120));
        loadingScreen.gameObject.AddComponent<GuaId>().Value = "loading-screen";
        var loading = Title("Loading", loadingScreen, "Loading...", Vector2.zero, new Vector2(360, 56), 28);
        loading.gameObject.AddComponent<GuaId>().Value = "loading";
        loadingScreen.gameObject.SetActive(false);

        start.onClick.AddListener(() =>
        {
            canvasObject.GetComponent<GuaScreen>().Value = "loading";
            titleScreen.gameObject.SetActive(false);
            loadingScreen.gameObject.SetActive(true);
        });
        settings.onClick.AddListener(() => { });
        if (string.Equals(Environment.GetEnvironmentVariable("GUA_UNITY_HOST_CLICK"), "1", StringComparison.Ordinal))
            settings.gameObject.AddComponent<GuaUnityHostClickDriver>().Button = settings;

        BuildInactiveCoverageControls(canvasObject.transform);
    }

    private static void BuildInactiveCoverageControls(Transform parent)
    {
        var coverage = new GameObject("InactiveCoverage", typeof(RectTransform));
        coverage.transform.SetParent(parent, false);

        var inputObject = new GameObject("NameInput", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(InputField), typeof(GuaId));
        inputObject.transform.SetParent(coverage.transform, false);
        inputObject.GetComponent<GuaId>().Value = "sample-input";
        var inputRect = inputObject.GetComponent<RectTransform>();
        inputRect.anchoredPosition = new Vector2(-420, 260);
        inputRect.sizeDelta = new Vector2(220, 44);
        var inputText = Text("Text", inputObject.transform, "pilot", Vector2.zero, inputRect.sizeDelta, 16);
        Stretch(inputText.rectTransform);
        var input = inputObject.GetComponent<InputField>();
        input.textComponent = inputText;
        input.text = "pilot";
        input.caretPosition = input.text.Length;

        var sliderRect = Rect("SampleSlider", coverage.transform, new Vector2(-420, 200), new Vector2(220, 32));
        sliderRect.gameObject.AddComponent<GuaId>().Value = "sample-slider";
        var slider = sliderRect.gameObject.AddComponent<UnityEngine.UI.Slider>();
        slider.minValue = 0;
        slider.maxValue = 10;
        slider.value = 5;

        var tmpButtonRect = Rect("TmpButton", coverage.transform, new Vector2(-420, 140), new Vector2(220, 44));
        tmpButtonRect.gameObject.AddComponent<UnityEngine.UI.Image>().color = ButtonNormal;
        tmpButtonRect.gameObject.AddComponent<GuaId>().Value = "tmp-button";
        tmpButtonRect.gameObject.AddComponent<UnityEngine.UI.Button>();
        var tmpButtonLabel = Title("TmpButtonLabel", tmpButtonRect, "TMP Launch", Vector2.zero, tmpButtonRect.sizeDelta, 18);
        Stretch(tmpButtonLabel.rectTransform);

        var tmpInputRect = Rect("TmpInput", coverage.transform, new Vector2(-420, 80), new Vector2(220, 44));
        tmpInputRect.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.white;
        tmpInputRect.gameObject.AddComponent<GuaId>().Value = "tmp-input";
        var tmpInput = tmpInputRect.gameObject.AddComponent<TMP_InputField>();
        var tmpViewport = Rect("Text Area", tmpInputRect, Vector2.zero, tmpInputRect.sizeDelta);
        Stretch(tmpViewport);
        var tmpInputText = Title("Text", tmpViewport, "bravo", Vector2.zero, tmpInputRect.sizeDelta, 16);
        Stretch(tmpInputText.rectTransform);
        tmpInput.textViewport = tmpViewport;
        tmpInput.textComponent = tmpInputText;
        tmpInput.text = "bravo";
        tmpInput.caretPosition = tmpInput.text.Length;

        var documentObject = new GameObject("ToolkitDocument", typeof(UIDocument));
        documentObject.transform.SetParent(coverage.transform, false);
        var document = documentObject.GetComponent<UIDocument>();
        document.panelSettings = Resources.Load<PanelSettings>("GuaFixturePanelSettings");
        var root = document.rootVisualElement;
        root.name = "toolkit-root";
        root.style.width = 640;
        root.style.height = 360;
        var scaledBox = new VisualElement { name = "scaled-box" };
        scaledBox.style.position = Position.Absolute;
        scaledBox.style.left = 100;
        scaledBox.style.top = 60;
        scaledBox.style.width = 200;
        scaledBox.style.height = 40;
        root.Add(scaledBox);
        var list = new ListView { name = "fixture-list", itemsSource = new List<string> { "One", "Two", "Three" }, fixedItemHeight = 24 };
        list.makeItem = () => new UnityEngine.UIElements.Label();
        list.bindItem = (element, index) => ((UnityEngine.UIElements.Label)element).text = (string)list.itemsSource[index];
        list.style.position = Position.Absolute;
        list.style.left = 100;
        list.style.top = 130;
        list.style.width = 200;
        list.style.height = 80;
        root.Add(list);

        var integerSlider = new SliderInt("integer-slider", 0, 10) { name = "integer-slider", value = 3 };
        root.Add(integerSlider);

        var tabView = new TabView { name = "fixture-tabs" };
        tabView.Add(new Tab("First") { name = "first-tab" });
        tabView.Add(new Tab("Second") { name = "second-tab" });
        root.Add(tabView);

        var firstBranch = new VisualElement { name = "first-branch" };
        firstBranch.Add(new UnityEngine.UIElements.Button { name = "duplicate-button", text = "Duplicate A" });
        root.Add(firstBranch);
        var secondBranch = new VisualElement { name = "second-branch" };
        secondBranch.Add(new UnityEngine.UIElements.Button { name = "duplicate-button", text = "Duplicate B" });
        root.Add(secondBranch);

        var disabledCanvasObject = new GameObject("DisabledCanvas", typeof(RectTransform), typeof(Canvas));
        disabledCanvasObject.transform.SetParent(coverage.transform, false);
        disabledCanvasObject.GetComponent<Canvas>().enabled = false;
        Button("DisabledCanvasButton", "disabled-canvas-button", disabledCanvasObject.transform, "Hidden by Canvas", Vector2.zero);

        coverage.SetActive(string.Equals(Environment.GetEnvironmentVariable("GUA_UNITY_COVERAGE"), "1", StringComparison.Ordinal));
    }

    private static UnityEngine.UI.Button Button(string name, string id, Transform parent, string label, Vector2 position)
    {
        var rect = Rect(name, parent, position, new Vector2(256, 56));
        rect.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.white;
        rect.gameObject.AddComponent<GuaId>().Value = id;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        var colors = button.colors;
        colors.normalColor = ButtonNormal;
        colors.highlightedColor = ButtonHighlighted;
        colors.pressedColor = new Color(0.11f, 0.11f, 0.11f, 1f);
        colors.selectedColor = ButtonHighlighted;
        button.colors = colors;

        var labelText = Text("Label", rect, label, Vector2.zero, new Vector2(256, 56), 18);
        Stretch(labelText.rectTransform);
        return button;
    }

    private static TextMeshProUGUI Title(string name, Transform parent, string value, Vector2 position, Vector2 size, float fontSize)
    {
        var rect = Rect(name, parent, position, size);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        return text;
    }

    private static Text Text(string name, Transform parent, string value, Vector2 position, Vector2 size, int fontSize)
    {
        var rect = Rect(name, parent, position, size);
        var text = rect.gameObject.AddComponent<Text>();
        text.text = value;
        text.font = legacyFont;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        return text;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        var rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

public sealed class GuaUnityHostClickDriver : MonoBehaviour
{
    public UnityEngine.UI.Button Button { get; set; }

    private IEnumerator Start()
    {
        yield return null;
        yield return null;
        Button.onClick.Invoke();
        Destroy(this);
    }
}
