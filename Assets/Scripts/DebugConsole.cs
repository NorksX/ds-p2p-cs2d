using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// On-screen log viewer with per-tag filtering and duplicate collapsing.
///
/// It exists because clients are standalone builds with no Unity console, so during a live
/// demo this is the only way to show what a client is actually doing. Two things make a 30 Hz
/// log readable: consecutive duplicates collapse into one line with a counter, and each
/// "[Tag]" prefix becomes a toggle, so a single subsystem can be isolated while explaining it.
///
/// Tags are discovered from the messages themselves rather than hardcoded - a fixed category
/// list goes stale the moment someone adds a log line.
/// </summary>
public class DebugConsole : MonoBehaviour
{
    // Input System Key, not legacy KeyCode: this project switched active input handling to the
    // Input System package, so any UnityEngine.Input call throws every frame.
    [Tooltip("Key that shows/hides the whole overlay")]
    [SerializeField] private Key toggleKey = Key.F1;

    [Tooltip("Start hidden, so the overlay does not cover a normal play session")]
    [SerializeField] private bool startHidden = false;

    private const int MaxEntries = 400;
    private const string Untagged = "misc";

    private class Entry
    {
        public string tag;
        public string message;
        public LogType type;
        public int count = 1;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private readonly Dictionary<string, bool> tagEnabled = new Dictionary<string, bool>();
    private readonly List<string> tagOrder = new List<string>();

    // logMessageReceived is not guaranteed to be on the main thread, and OnGUI walks the list.
    private readonly object gate = new object();

    private Vector2 scroll;
    private bool show = true;
    private bool paused;
    private bool errorsOnly;
    private GUIStyle lineStyle;

    private void Awake()
    {
        show = !startHidden;
    }

    private void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private Key validatedKey = Key.F1;
    private Key lastCheckedKey = Key.None;

    private void Update()
    {
        // A serialized field keeps its raw integer across a type change, and a domain reload
        // restores it into the new type. KeyCode.F1 is 282, which is not a valid Input System
        // Key, and the Keyboard indexer throws on it every single frame. Validate rather than
        // trust the inspector value.
        if (toggleKey != lastCheckedKey)
        {
            lastCheckedKey = toggleKey;
            bool valid = toggleKey != Key.None && System.Enum.IsDefined(typeof(Key), toggleKey);
            validatedKey = valid ? toggleKey : Key.F1;

            if (!valid)
                Debug.LogWarning($"[DebugConsole] toggleKey {(int)toggleKey} is not a valid Input System Key - falling back to F1");
        }

        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard[validatedKey].wasPressedThisFrame)
            show = !show;
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        if (paused)
            return;

        string tag = ExtractTag(logString);

        lock (gate)
        {
            if (!tagEnabled.ContainsKey(tag))
            {
                tagEnabled[tag] = true;
                tagOrder.Add(tag);
                tagOrder.Sort(System.StringComparer.OrdinalIgnoreCase);
            }

            // Collapse a repeat of the immediately preceding line. Per-tick logs otherwise
            // scroll the interesting one off screen within a second.
            if (entries.Count > 0)
            {
                Entry last = entries[entries.Count - 1];

                if (last.message == logString && last.type == type)
                {
                    last.count++;
                    return;
                }
            }

            entries.Add(new Entry { tag = tag, message = logString, type = type });

            if (entries.Count > MaxEntries)
                entries.RemoveRange(0, entries.Count - MaxEntries);
        }
    }

    private static string ExtractTag(string message)
    {
        if (string.IsNullOrEmpty(message) || message[0] != '[')
            return Untagged;

        int close = message.IndexOf(']');

        if (close <= 1 || close > 40)
            return Untagged;

        return message.Substring(1, close - 1);
    }

    private void OnGUI()
    {
        if (lineStyle == null)
        {
            lineStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = false, fontSize = 12 };
        }

        GUILayout.BeginArea(new Rect(8, 8, Screen.width - 16, Screen.height - 16));

        DrawTopBar();

        if (show)
        {
            DrawTagBar();
            DrawLog();
        }

        GUILayout.EndArea();
    }

    private void DrawTopBar()
    {
        GUILayout.BeginHorizontal();

        if (GUILayout.Button(show ? $"Hide log ({validatedKey})" : $"Show log ({validatedKey})", GUILayout.Height(26), GUILayout.Width(130)))
            show = !show;

        if (!show)
        {
            GUILayout.EndHorizontal();
            return;
        }

        GUI.color = paused ? Color.yellow : Color.white;
        if (GUILayout.Button(paused ? "PAUSED" : "Live", GUILayout.Height(26), GUILayout.Width(80)))
            paused = !paused;
        GUI.color = Color.white;

        if (GUILayout.Button("Clear", GUILayout.Height(26), GUILayout.Width(70)))
        {
            lock (gate) entries.Clear();
        }

        GUI.color = errorsOnly ? Color.red : Color.white;
        if (GUILayout.Button("Errors only", GUILayout.Height(26), GUILayout.Width(100)))
            errorsOnly = !errorsOnly;
        GUI.color = Color.white;

        if (GUILayout.Button("All tags", GUILayout.Height(26), GUILayout.Width(80)))
            SetAllTags(true);

        if (GUILayout.Button("No tags", GUILayout.Height(26), GUILayout.Width(80)))
            SetAllTags(false);

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    private void SetAllTags(bool enabled)
    {
        lock (gate)
        {
            foreach (string tag in tagOrder)
                tagEnabled[tag] = enabled;
        }
    }

    // One button per discovered tag. Clicking it alone (right-click) solos that tag, which is
    // the common demo move: show migration without input spam drowning it.
    private void DrawTagBar()
    {
        List<string> snapshot;
        lock (gate) snapshot = new List<string>(tagOrder);

        int perRow = Mathf.Max(1, (Screen.width - 40) / 190);

        for (int i = 0; i < snapshot.Count; i += perRow)
        {
            GUILayout.BeginHorizontal();

            for (int j = i; j < Mathf.Min(i + perRow, snapshot.Count); j++)
            {
                string tag = snapshot[j];
                bool enabled = tagEnabled[tag];
                int count = CountFor(tag);

                GUI.color = enabled ? Color.green : new Color(0.5f, 0.5f, 0.5f);

                if (GUILayout.Button($"{tag} ({count})", GUILayout.Height(22), GUILayout.Width(180)))
                {
                    if (Event.current.button == 1)
                    {
                        SetAllTags(false);
                        tagEnabled[tag] = true;   // right-click solos
                    }
                    else
                    {
                        tagEnabled[tag] = !enabled;
                    }
                }
            }

            GUI.color = Color.white;
            GUILayout.EndHorizontal();
        }
    }

    private int CountFor(string tag)
    {
        int n = 0;

        lock (gate)
        {
            foreach (Entry e in entries)
            {
                if (e.tag == tag) n++;
            }
        }

        return n;
    }

    private void DrawLog()
    {
        // Pinned to the bottom while live, so the newest line is always the one being read.
        if (!paused)
            scroll.y = float.MaxValue;

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Screen.height * 0.6f));

        lock (gate)
        {
            foreach (Entry e in entries)
            {
                if (!tagEnabled.TryGetValue(e.tag, out bool on) || !on)
                    continue;

                if (errorsOnly && e.type != LogType.Error && e.type != LogType.Exception)
                    continue;

                string colour = e.type == LogType.Error || e.type == LogType.Exception ? "#ff6b6b"
                              : e.type == LogType.Warning ? "#ffd166"
                              : "#e8e8e8";

                string repeat = e.count > 1 ? $" <color=#7fd1ff>x{e.count}</color>" : "";

                GUILayout.Label($"<color={colour}>{e.message}</color>{repeat}", lineStyle);
            }
        }

        GUILayout.EndScrollView();
    }
}
