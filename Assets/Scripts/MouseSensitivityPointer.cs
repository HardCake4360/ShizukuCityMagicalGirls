using UnityEngine;

/// <summary>Gameplay-only virtual pointer. UI and the operating system's cursor remain untouched.</summary>
public sealed class MouseSensitivityPointer
{
    private bool initialized;
    private Vector2 previousRawPosition;
    private Vector2 position;
    private int processedFrame = -1;

    public void Reset() => initialized = false;
    public Vector2 GetPosition(Vector2 rawPosition, float multiplier, Rect bounds, int frame,
        Vector2? inputDelta = null)
    {
        if (!initialized || Mathf.Approximately(multiplier, 1f))
        {
            initialized = true;
            position = rawPosition;
        }
        else if (frame != processedFrame)
            position += (inputDelta ?? (rawPosition - previousRawPosition)) * multiplier;
        previousRawPosition = rawPosition;
        processedFrame = frame;
        // Neutral sensitivity preserves the existing absolute-pointer behavior exactly.
        if (!Mathf.Approximately(multiplier, 1f))
            position = new Vector2(Mathf.Clamp(position.x, bounds.xMin, bounds.xMax),
                Mathf.Clamp(position.y, bounds.yMin, bounds.yMax));
        return position;
    }
}
