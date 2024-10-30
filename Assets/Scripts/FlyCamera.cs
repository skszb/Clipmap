using UnityEngine;

public class FlyCamera : MonoBehaviour
{
    public const float YMin = -85.0f;
    public const float YMax = 85.0f;

    /*
    Writen by Windexglow 11-13-10.  Use it, edit it, steal it I don't care.
    Converted to C# 27-02-13 - no credit wanted.
    Simple flycam I made, since I couldn't find any others made public.
    Made simple to use (drag and drop, done) for regular keyboard layout
    wasd : basic movement
    shift : Makes camera accelerate
    space : Moves camera on X and Z axis only.  So camera doesn't gain any height*/


    public float mainSpeed = 100.0f; //regular speed
    public float maxSpeed = 200.0f;
    public float camSens = 1; //How sensitive it with mouse

    public bool pause;
    private float currX;
    private float currY;

    private void Start()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;
    }

    private void LateUpdate()
    {
        if (Input.GetKeyUp(KeyCode.P))
        {
            pause = !pause;
            Cursor.visible = !Cursor.visible;
            Cursor.lockState = Cursor.lockState != CursorLockMode.None ? CursorLockMode.None : CursorLockMode.Confined;
        }

        if (pause) return;

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;

        var xRot = Input.GetAxis("Mouse X") * camSens;
        var yRot = Input.GetAxis("Mouse Y") * camSens;
        currY = Mathf.Clamp(currY + yRot, YMin, YMax);
        currX += xRot;

        var direction = Quaternion.Euler(currY, 0, 0) * new Vector3(0, 0, -10);
        direction = Quaternion.Euler(0, currX, 0) * direction;

        transform.LookAt(transform.position + direction);

        //Keyboard commands
        var p = GetBaseInput();

        p = p * Time.deltaTime;
        var newPosition = transform.position;
        transform.Translate(p);
    }


    private Vector3 GetBaseInput()
    {
        //returns the basic values, if it's 0 than it's not active.
        var p_Velocity = new Vector3();
        if (Input.GetKey(KeyCode.W)) p_Velocity += new Vector3(0, 0, 1);
        if (Input.GetKey(KeyCode.S)) p_Velocity += new Vector3(0, 0, -1);
        if (Input.GetKey(KeyCode.A)) p_Velocity += new Vector3(-1, 0, 0);
        if (Input.GetKey(KeyCode.D)) p_Velocity += new Vector3(1, 0, 0);
        if (Input.GetKey(KeyCode.E)) p_Velocity += new Vector3(0, 0.5f, 0);
        if (Input.GetKey(KeyCode.Q)) p_Velocity += new Vector3(0, -0.5f, 0);
        if (Input.GetKey(KeyCode.LeftShift)) p_Velocity *= maxSpeed;
        else p_Velocity *= mainSpeed;
        return p_Velocity;
    }
}