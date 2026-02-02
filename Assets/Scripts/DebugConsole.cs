using UnityEngine;

/// <summary>
/// Debug console
/// </summary>
public class DebugConsole : MonoBehaviour
{
    private string log = "";
    private Vector2 scrollPosition;
    private bool show = true;
    
    private void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }
    
    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }
    
    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        string color = type == LogType.Error || type == LogType.Exception ? "red" : "white";
        log += $"<color={color}>{logString}</color>\n";
        
        if (log.Length > 5000)
        {
            log = log.Substring(log.Length - 5000);
        }
    }
    
    private void OnGUI()
    {
        if (GUILayout.Button("Toggle Console", GUILayout.Height(30), GUILayout.Width(120)))
        {
            show = !show;
        }
        
        if (!show) return;
        
        GUILayout.BeginArea(new Rect(10, 50, Screen.width - 20, Screen.height / 2));
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);
        GUILayout.Label(log);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}
