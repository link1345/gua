using TMPro;
using Gua.Core;
using Gua.Unity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;

public static class GuaRuntimeUiSample
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Build()
    {
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var events = new GameObject("EventSystem");
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();
        }
        var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var buttonObject = new GameObject("StartButton", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
        buttonObject.transform.SetParent(canvasObject.transform, false);
        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(buttonObject.transform, false);
        labelObject.GetComponent<Text>().text = "Start Game";
        var tmpObject = new GameObject("TmpLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        tmpObject.transform.SetParent(canvasObject.transform, false);
        tmpObject.GetComponent<TextMeshProUGUI>().text = "TMP Ready";

        var documentObject = new GameObject("ToolkitDocument", typeof(UIDocument));
        var document = documentObject.GetComponent<UIDocument>();
        document.rootVisualElement.Add(new UnityEngine.UIElements.Label("Toolkit Ready") { name = "toolkit-label" });
        document.rootVisualElement.Add(new UnityEngine.UIElements.Button { name = "toolkit-button", text = "Toolkit Start" });
        document.rootVisualElement.Add(new TextField("Callsign") { name = "toolkit-input", value = "alpha" });

        var doorObject = new GameObject("Door A");
        doorObject.transform.position = new Vector3(640, 180, 0);
        var door = doorObject.AddComponent<GuaWorldObject>();
        door.Id = "door-a";
        door.Kind = "door";
        door.Label = "Door A";
        door.Space = GuaWorldSpace.World2D;
        door.VisibleToPlayer = true;
        door.Tags = new[] { "east-corridor", "mission-critical" };
        door.SetState("open", false);
        door.SetState("locked", true);
    }
}
