using System.Diagnostics;
using Unity.Entities;
using UnityEngine;

// FPS / frame timing overlay. Drop the FrameTimingDisplay prefab into any scene.
//
// To time the ECS update, this takes the default world out of the player loop while
// enabled and steps it manually (world.Update()) once per frame from Update(). The
// world is put back into the player loop when the component is disabled/destroyed,
// so scenes without this component are unaffected.
public class FrameTimingDisplay : MonoBehaviour
{
    private const int SAMPLE_COUNT = 20;

    private FrameTiming[] _frameTimings = new FrameTiming[1];

    private double[] _cpuSamples = new double[SAMPLE_COUNT];
    private double[] _gpuSamples = new double[SAMPLE_COUNT];
    private double[] _ecsSamples = new double[SAMPLE_COUNT];
    private double[] _frameTimeSamples = new double[SAMPLE_COUNT];
    private int _sampleIndex;

    private double _cpuTimeMs;
    private double _gpuTimeMs;
    private double _ecsTimeMs;
    private double _frameTimeMs;
    private double _fps;

    // averaged FPS shown on the panel; the boids controls gate on it
    public double Fps => _fps;

    private float _logTimer;
    private const float LOG_INTERVAL = 1.0f;

    private Stopwatch _ecsStopwatch = new Stopwatch();
    private GUIStyle _style;
    private Texture2D _backgroundTexture;

    private World _manualWorld;

    void Start()
    {
        // Note: For release builds, enable "Frame Timing Stats" in:
        // Edit > Project Settings > Player > Other Settings
        FrameTimingManager.CaptureFrameTimings();
    }

    void OnDisable()
    {
        if (_manualWorld != null && _manualWorld.IsCreated)
            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(_manualWorld);
        _manualWorld = null;
    }

    void Update()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated)
        {
            if (_manualWorld != world && ScriptBehaviourUpdateOrder.IsWorldInCurrentPlayerLoop(world))
            {
                ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(world);
                _manualWorld = world;
            }

            if (_manualWorld == world)
            {
                _ecsStopwatch.Restart();
                world.Update();
                _ecsStopwatch.Stop();
                _ecsSamples[_sampleIndex] = _ecsStopwatch.Elapsed.TotalMilliseconds;
            }
        }

        FrameTimingManager.CaptureFrameTimings();

        if (FrameTimingManager.GetLatestTimings(1, _frameTimings) > 0)
        {
            _cpuSamples[_sampleIndex] = _frameTimings[0].cpuMainThreadFrameTime + _frameTimings[0].cpuRenderThreadFrameTime;
            _gpuSamples[_sampleIndex] = _frameTimings[0].gpuFrameTime;
        }

        _frameTimeSamples[_sampleIndex] = Time.unscaledDeltaTime * 1000.0;

        _sampleIndex = (_sampleIndex + 1) % SAMPLE_COUNT;

        // Calculate averages
        _ecsTimeMs = Average(_ecsSamples);
        _cpuTimeMs = Average(_cpuSamples);
        _gpuTimeMs = Average(_gpuSamples);
        _frameTimeMs = Average(_frameTimeSamples);
        _fps = _frameTimeMs > 0 ? 1000.0 / _frameTimeMs : 0;

        _logTimer += Time.unscaledDeltaTime;
        if (_logTimer >= LOG_INTERVAL)
        {
            _logTimer = 0f;
            UnityEngine.Debug.Log($"CPU: {_cpuTimeMs:F2} ms | GPU: {_gpuTimeMs:F2} ms | ECS: {_ecsTimeMs:F2} ms | Frame: {_frameTimeMs:F1} ms | FPS: {_fps:F0}");
        }
    }

    private double Average(double[] samples)
    {
        double sum = 0;
        for (int i = 0; i < samples.Length; i++)
            sum += samples[i];
        return sum / samples.Length;
    }

    // matches the HZ boids HUD: top-left panel, ECS (green), CPU, Frame, FPS
    private const int FONT_SIZE = 26;
    private const float PANEL_PADDING = 14f;
    private const float LINE_GAP = 4f;
    private const float SCREEN_MARGIN = 16f;
    private const int CORNER_RADIUS = 6;
    private static readonly Color PANEL_COLOR = new Color32(0, 0, 0, 250);
    private static readonly Color BORDER_COLOR = new Color32(255, 255, 255, 40);
    private static readonly Color ECS_COLOR = new Color32(144, 238, 144, 255);

    private GUIStyle _ecsStyle;
    private GUIStyle _panelStyle;
    private readonly string[] _lines = new string[4];

    void OnGUI()
    {
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label);
            _style.fontSize = FONT_SIZE;
            _style.fontStyle = FontStyle.Bold;
            _style.normal.textColor = Color.white;
            _style.padding = new RectOffset(0, 0, 0, 0);
            _style.margin = new RectOffset(0, 0, 0, 0);
            _style.wordWrap = false;

            _ecsStyle = new GUIStyle(_style);
            _ecsStyle.normal.textColor = ECS_COLOR;

            _backgroundTexture = MakeRoundedPanel(CORNER_RADIUS, PANEL_COLOR, BORDER_COLOR);
            _panelStyle = new GUIStyle();
            _panelStyle.normal.background = _backgroundTexture;
            _panelStyle.border = new RectOffset(CORNER_RADIUS + 1, CORNER_RADIUS + 1, CORNER_RADIUS + 1, CORNER_RADIUS + 1);
        }

        _lines[0] = $"ECS: {_ecsTimeMs:F2} ms";
        _lines[1] = $"CPU: {_cpuTimeMs:F2} ms";
        _lines[2] = $"Frame: {_frameTimeMs:F2} ms";
        _lines[3] = $"FPS: {_fps:F0}";

        float width = 0f, height = 0f;
        for (int i = 0; i < _lines.Length; i++)
        {
            Vector2 size = (i == 0 ? _ecsStyle : _style).CalcSize(new GUIContent(_lines[i]));
            width = Mathf.Max(width, size.x);
            height += size.y + (i > 0 ? LINE_GAP : 0f);
        }

        Rect panel = new Rect(SCREEN_MARGIN, SCREEN_MARGIN,
                              width + PANEL_PADDING * 2, height + PANEL_PADDING * 2);
        if (Event.current.type == EventType.Repaint)
            _panelStyle.Draw(panel, false, false, false, false);

        float y = panel.y + PANEL_PADDING;
        for (int i = 0; i < _lines.Length; i++)
        {
            GUIStyle style = i == 0 ? _ecsStyle : _style;
            float lineHeight = style.CalcSize(new GUIContent(_lines[i])).y;
            GUI.Label(new Rect(panel.x + PANEL_PADDING, y, width, lineHeight), _lines[i], style);
            y += lineHeight + LINE_GAP;
        }
    }

    // 9-slice source: a rounded rect with a 1px outline, stretched by GUIStyle.border
    internal static Texture2D MakeRoundedPanel(int radius, Color fill, Color border)
    {
        int size = radius * 2 + 3;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // signed distance to the rounded-rect edge, pixel centers
                float px = Mathf.Abs(x + 0.5f - half) - (half - radius);
                float py = Mathf.Abs(y + 0.5f - half) - (half - radius);
                float outside = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                float coverage = Mathf.Clamp01(0.5f - outside);
                float borderMix = Mathf.Clamp01(outside + 1.5f);
                Color c = Color.Lerp(fill, new Color(Mathf.Lerp(fill.r, border.r, border.a), Mathf.Lerp(fill.g, border.g, border.a),
                                                     Mathf.Lerp(fill.b, border.b, border.a), fill.a), borderMix);
                c.a *= coverage;
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return tex;
    }

    void OnDestroy()
    {
        if (_backgroundTexture != null)
            Destroy(_backgroundTexture);
    }
}
