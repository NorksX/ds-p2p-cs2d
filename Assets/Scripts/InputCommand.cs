using UnityEngine;

[System.Serializable]
public struct InputCommand
{
    public int tick;

    public Vector2 move; 
    public Vector2 aimDir; 

    public bool fireHeld;
    public bool firePressed;

    public InputCommand(
        int tick,
        Vector2 move,
        Vector2 aimDir,
        bool fireHeld,
        bool firePressed)
    {
        this.tick = tick;
        this.move = move;
        this.aimDir = aimDir;
        this.fireHeld = fireHeld;
        this.firePressed = firePressed;
    }
}
