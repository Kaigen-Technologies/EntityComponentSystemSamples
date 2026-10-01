using Boids;
using Unity.Entities;
using UnityEngine;

// Fish count controls, matching the HZ boids demo: bottom-center panel, -5k / count / +5k.
// Adding is blocked while the averaged FPS (FrameTimingDisplay) is below MIN_FPS.
// Creates itself at startup and stays hidden until the boids spawn system has run.
public class BoidCountControls : MonoBehaviour
{
    private const int STEP = 5000;
    private const int MAX_COUNT = 1000000;
    private const double MIN_FPS = 30.0;

    private const int FONT_SIZE = 26;
    private const int BUTTON_FONT_SIZE = 22;
    private const int WARNING_FONT_SIZE = 16;
    private const float PANEL_PADDING = 14f;
    private const float ROW_GAP = 14f;
    private const float LINE_GAP = 6f;
    private const float SCREEN_MARGIN = 16f;
    private const float BUTTON_WIDTH = 84f;
    private const float BUTTON_HEIGHT = 44f;
    private const float COUNT_WIDTH = 200f;
    private const int CORNER_RADIUS = 6;
    private static readonly Color PANEL_COLOR = new Color32(0, 0, 0, 250);
    private static readonly Color BORDER_COLOR = new Color32(255, 255, 255, 40);
    private static readonly Color WARNING_COLOR = new Color32(255, 170, 120, 255);

    private World _world;
    private EntityQuery _populationQuery;
    private FrameTimingDisplay _frameTiming;

    private GUIStyle _panelStyle, _buttonStyle, _countStyle, _warningStyle;
    private Texture2D _panelTex, _buttonTex, _buttonHoverTex, _buttonActiveTex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        var go = new GameObject(nameof(BoidCountControls));
        DontDestroyOnLoad(go);
        go.AddComponent<BoidCountControls>();
    }

    private bool TryGetPopulation(out BoidPopulation population)
    {
        population = default;
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;
        if (_world != world)
        {
            _world = world;
            _populationQuery = world.EntityManager.CreateEntityQuery(typeof(BoidPopulation));
        }
        if (_populationQuery.IsEmpty)
            return false;
        population = _populationQuery.GetSingleton<BoidPopulation>();
        return population.Started;
    }

    void OnGUI()
    {
        if (!TryGetPopulation(out var population))
            return;
        if (_frameTiming == null)
            _frameTiming = FindFirstObjectByType<FrameTimingDisplay>();
        if (_panelStyle == null)
            CreateStyles();

        bool fpsCapped = _frameTiming != null && _frameTiming.Fps > 0.0 && _frameTiming.Fps < MIN_FPS;
        bool canAdd = !fpsCapped && population.Count + STEP <= MAX_COUNT;
        bool canRemove = population.Count >= STEP;
        string countText = $"Fish: {population.Count}";
        const string warningText = "Below 30 FPS: adding is capped";

        float rowWidth = BUTTON_WIDTH * 2 + COUNT_WIDTH + ROW_GAP * 2;
        float warningHeight = fpsCapped ? _warningStyle.CalcSize(new GUIContent(warningText)).y + LINE_GAP : 0f;
        float width = rowWidth + PANEL_PADDING * 2;
        float height = BUTTON_HEIGHT + warningHeight + PANEL_PADDING * 2;
        Rect panel = new Rect((Screen.width - width) * 0.5f, Screen.height - SCREEN_MARGIN - height, width, height);
        if (Event.current.type == EventType.Repaint)
            _panelStyle.Draw(panel, false, false, false, false);

        float x = panel.x + PANEL_PADDING;
        float y = panel.y + PANEL_PADDING;
        int delta = 0;

        GUI.enabled = canRemove;
        if (GUI.Button(new Rect(x, y, BUTTON_WIDTH, BUTTON_HEIGHT), "-5k", _buttonStyle))
            delta = -STEP;
        x += BUTTON_WIDTH + ROW_GAP;

        GUI.enabled = true;
        GUI.Label(new Rect(x, y, COUNT_WIDTH, BUTTON_HEIGHT), countText, _countStyle);
        x += COUNT_WIDTH + ROW_GAP;

        GUI.enabled = canAdd;
        if (GUI.Button(new Rect(x, y, BUTTON_WIDTH, BUTTON_HEIGHT), "+5k", _buttonStyle))
            delta = STEP;
        GUI.enabled = true;

        if (fpsCapped)
            GUI.Label(new Rect(panel.x, y + BUTTON_HEIGHT + LINE_GAP, panel.width, warningHeight), warningText, _warningStyle);

        if (delta != 0)
        {
            population.Delta = delta;
            _populationQuery.SetSingleton(population);
        }
    }

    private void CreateStyles()
    {
        var slice = new RectOffset(CORNER_RADIUS + 1, CORNER_RADIUS + 1, CORNER_RADIUS + 1, CORNER_RADIUS + 1);

        _panelTex = FrameTimingDisplay.MakeRoundedPanel(CORNER_RADIUS, PANEL_COLOR, BORDER_COLOR);
        _panelStyle = new GUIStyle { border = slice };
        _panelStyle.normal.background = _panelTex;

        _buttonTex = FrameTimingDisplay.MakeRoundedPanel(CORNER_RADIUS, new Color32(40, 40, 40, 250), BORDER_COLOR);
        _buttonHoverTex = FrameTimingDisplay.MakeRoundedPanel(CORNER_RADIUS, new Color32(70, 70, 70, 250), BORDER_COLOR);
        _buttonActiveTex = FrameTimingDisplay.MakeRoundedPanel(CORNER_RADIUS, new Color32(25, 25, 25, 250), BORDER_COLOR);
        _buttonStyle = new GUIStyle
        {
            border = slice,
            alignment = TextAnchor.MiddleCenter,
            fontSize = BUTTON_FONT_SIZE,
            fontStyle = FontStyle.Bold
        };
        _buttonStyle.normal.background = _buttonTex;
        _buttonStyle.hover.background = _buttonHoverTex;
        _buttonStyle.active.background = _buttonActiveTex;
        _buttonStyle.normal.textColor = Color.white;
        _buttonStyle.hover.textColor = Color.white;
        _buttonStyle.active.textColor = Color.white;

        _countStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = FONT_SIZE,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(0, 0, 0, 0),
            wordWrap = false
        };
        _countStyle.normal.textColor = Color.white;

        _warningStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperCenter,
            fontSize = WARNING_FONT_SIZE,
            padding = new RectOffset(0, 0, 0, 0),
            wordWrap = false
        };
        _warningStyle.normal.textColor = WARNING_COLOR;
    }

    void OnDestroy()
    {
        if (_panelTex != null) Destroy(_panelTex);
        if (_buttonTex != null) Destroy(_buttonTex);
        if (_buttonHoverTex != null) Destroy(_buttonHoverTex);
        if (_buttonActiveTex != null) Destroy(_buttonActiveTex);
    }
}
