using UnityEngine;
using System.Collections;
using System.Linq;
using Unity.VisualScripting;

public class FlyCamera : MonoBehaviour
{

    /*
    Writen by Windexglow 11-13-10.  Use it, edit it, steal it I don't care.  
    Converted to C# 27-02-13 - no credit wanted.
    Simple flycam I made, since I couldn't find any others made public.  
    Made simple to use (drag and drop, done) for regular keyboard layout  
    wasd : basic movement
    shift : Makes camera accelerate
    space : Moves camera on X and Z axis only.  So camera doesn't gain any height*/


    public float mainSpeed = 100.0f; //regular speed

    public const float YMin = -85.0f;
    public const float YMax = 85.0f;
    public float camSens = 1; //How sensitive it with mouse
    float currY = 0;
    float currX = 0;

    public bool pause = false;

    private void Start()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;
    }

    void LateUpdate()
    {
        if (Input.GetKeyUp(KeyCode.P))
        {
            pause = !pause;
            Cursor.visible = !Cursor.visible;
            Cursor.lockState = Cursor.lockState != CursorLockMode.None ? 
                CursorLockMode.None : CursorLockMode.Confined;
        }

        if (pause) { return; }

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;

        float xRot = Input.GetAxis("Mouse X") * camSens;
        float yRot = Input.GetAxis("Mouse Y") * camSens;
        currY = Mathf.Clamp(currY + yRot, YMin, YMax);
        currX += xRot;
        
        Vector3 direction = Quaternion.Euler(currY, 0, 0) * new Vector3(0, 0, -10);
        direction = Quaternion.Euler(0, currX, 0) * direction;

        transform.LookAt(transform.position + direction);

        //Keyboard commands
        Vector3 p = GetBaseInput();

        p = p * Time.deltaTime;
        Vector3 newPosition = transform.position;
        transform.Translate(p * mainSpeed);
    }
    

    private Vector3 GetBaseInput()
    { //returns the basic values, if it's 0 than it's not active.
        Vector3 p_Velocity = new Vector3();
        if (Input.GetKey(KeyCode.W))
        {
            p_Velocity += new Vector3(0, 0, 1);
        }
        if (Input.GetKey(KeyCode.S))
        {
            p_Velocity += new Vector3(0, 0, -1);
        }
        if (Input.GetKey(KeyCode.A))
        {
            p_Velocity += new Vector3(-1, 0, 0);
        }
        if (Input.GetKey(KeyCode.D))
        {
            p_Velocity += new Vector3(1, 0, 0);
        }
        if (Input.GetKey(KeyCode.E))
        {
            p_Velocity += new Vector3(0, 0.5f, 0);
        }
        if (Input.GetKey(KeyCode.Q))
        {
            p_Velocity += new Vector3(0, -0.5f, 0);
        }
        if (Input.GetKey(KeyCode.LeftShift)) 
        {
            p_Velocity *= 2;
        }
        return p_Velocity;
    }
}