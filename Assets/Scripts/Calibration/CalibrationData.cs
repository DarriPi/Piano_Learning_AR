using UnityEngine;

[System.Serializable]
public class CalibrationData
{
    /// <summary>
    /// Will be used to position and orient the virtual keyboard in AR mode. 
    /// The user will calibrate this by aligning a virtual keyboard with 
    /// their real piano during setup.
    /// </summary>
    public float posX, posY, posZ; // Position of the keyboard center in world space
    public float rotX, rotY, rotZ; // Rotation of the keyboard in Euler
    public float scaleX = 1f, scaleY = 1f, scaleZ = 1f; // Scale factors for the keyboard size

    public Vector3 Position => new Vector3(posX, posY, posZ);
    public Quaternion Rotation => Quaternion.Euler(rotX, rotY, rotZ);
    public Vector3 Scale => new Vector3(scaleX, scaleY, scaleZ);
}