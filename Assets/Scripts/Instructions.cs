using UnityEngine;

public class Instructions : MonoBehaviour
{
    public Canvas instructionCanvas;
    public void Hide()
    {
        if (instructionCanvas != null)
        {
            instructionCanvas.enabled = false;
        }
    }
    
    public void Show()
    {
        if (instructionCanvas != null)
        {
            instructionCanvas.enabled = true;
        }
    }   
}
