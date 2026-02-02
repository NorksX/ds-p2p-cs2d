using UnityEngine;

[System.Serializable]
public struct InputCommand
{
    public int tick;
    public string playerId;

    public Vector2 move; 
    public Vector2 aimDir; 

    public bool fireHeld;
    public bool firePressed;

    public InputCommand(
        int tick,
        Vector2 move,
        Vector2 aimDir,
        bool fireHeld,
        bool firePressed,
        string playerId = "")
    {
        this.tick = tick;
        this.playerId = playerId;
        this.move = move;
        this.aimDir = aimDir;
        this.fireHeld = fireHeld;
        this.firePressed = firePressed;
    }
}
