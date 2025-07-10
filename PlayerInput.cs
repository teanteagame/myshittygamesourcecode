using UnityEngine;

public class PlayerInput : MonoBehaviour
{
    public static PlayerInput instance;

    [Header("Input Values")]
    public float vertical;
    public float horizontal;
    public float mouseX;
    public float mouseY;

    public bool dash;
    public bool sprint;
    public bool jump;
    public bool walk;

    public bool lockOnToggle;
    public bool switchTargetLeft;
    public bool switchTargetRight;

    private void Awake()
    {
        if (instance == null) instance = this;
        else Destroy(gameObject);
    }

    private void Update()
    {
        ReadMovementInputs();
        ReadActionInputs();
    }

    private void ReadMovementInputs()
    {
        vertical = Input.GetAxisRaw("Vertical");
        horizontal = Input.GetAxisRaw("Horizontal");
        mouseX = Input.GetAxis("Mouse X");
        mouseY = Input.GetAxis("Mouse Y");
    }

    private void ReadActionInputs()
    {
        dash = Input.GetKeyDown(KeyCode.Space);
        sprint = Input.GetKey(KeyCode.Space);
        jump = Input.GetKeyDown(KeyCode.F);
        walk = Input.GetKey(KeyCode.C);

        lockOnToggle = Input.GetKeyDown(KeyCode.Tab);
        switchTargetLeft = Input.GetKeyDown(KeyCode.Q);
        switchTargetRight = Input.GetKeyDown(KeyCode.E);
    }
}
