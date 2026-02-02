using UnityEngine;

/// <summary>
/// Automatically sets the game to windowed, resizable mode on startup
/// </summary>
public class WindowManager : MonoBehaviour
{
    [Header("Window Settings")]
    [SerializeField] private int defaultWidth = 1280;
    [SerializeField] private int defaultHeight = 720;
    [SerializeField] private bool startWindowed = true;
    [SerializeField] private bool resizable = true;

    private void Awake()
    {
        if (startWindowed)
        {
            // Set to windowed mode
            Screen.fullScreenMode = FullScreenMode.Windowed;
            
            // Set default size
            Screen.SetResolution(defaultWidth, defaultHeight, FullScreenMode.Windowed);
            
            Debug.Log($"[WindowManager] Set to windowed mode: {defaultWidth}x{defaultHeight}");
        }
    }
}
