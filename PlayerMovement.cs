using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 2f;
    [SerializeField] private float runSpeed = 4f;
    [SerializeField] private float sprintSpeed = 6f;
    [SerializeField] private float rotationSpeed = 10f;

    [Header("Jump & Roll")]
    [SerializeField] private float jumpForce = 10f;
    [SerializeField] private float jumpForwardForce = 10f;
    [SerializeField] private float shortLandThreshold = 4f;
    [SerializeField] private float normalLandThreshold = 8f;
    [SerializeField] private float highLandThreshold = 15f;

    [Header("Ground Detection")]
    [SerializeField] private float groundCheckDistance = 0.3f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float extraGravity = -30f;
    [SerializeField] private float stickToGroundForce = -100;
    [SerializeField] private float slopeLimit = 45f;
    [SerializeField] private float slideForce = 40f;
    [SerializeField] private float groundedGraceTime = 0.2f; // how long to wait before becoming airborne  

    [Header("References")]
    [SerializeField] private PlayerCamera playerCamera;
    [SerializeField] private PlayerAnimation anim;
    
    public bool IsGrounded { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsWalking { get; private set; }

    internal Rigidbody rb;
    private PlayerInput input;
    private CapsuleCollider col;

    private float currentSpeed;
    private float fallStartY;
    private float lastTimeGrounded;
    private Vector3 moveInput;
    private Vector3 moveDirection;
  
    private float slopeAngle;
    private bool isOnSteepSlope;
    private Vector3 groundNormal;

    public Vector3 GroundNormal => groundNormal;
    public float SlideForce => slideForce;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        input = PlayerInput.instance;
        col = GetComponent<CapsuleCollider>();
    }

    private void Update()
    {
        ProcessInput();
        HandleRotation();
        UpdateAnimator();
        HandleRoll();
        HandleJump();
    }

    private void FixedUpdate()
    {       
        HandleMovement();
        HandleGrounded();
    }

    private void ProcessInput()
    {
        moveInput = new Vector3(input.horizontal, 0f, input.vertical).normalized;

        Vector3 flatForward = playerCamera.flatForward;
        Vector3 flatRight = playerCamera.flatRight;
        moveDirection = (flatForward * moveInput.z + flatRight * moveInput.x).normalized;

        bool movingForward = Vector3.Dot(moveDirection, transform.forward) > 0.7f;
        IsSprinting = input.sprint && moveInput.magnitude > 0.1f && (!playerCamera.isLockedOn || movingForward);
        IsWalking = input.walk && moveInput.magnitude > 0.1f;      
    }

    private void HandleMovement()
    {
        currentSpeed = IsSprinting ? sprintSpeed : IsWalking ? walkSpeed : runSpeed;

        if (IsGrounded && !OnSlope())
        {
            Vector3 desiredVelocity = moveDirection * currentSpeed;
            desiredVelocity.y = rb.velocity.y;
            rb.velocity = desiredVelocity;            
        }
    }

    private void HandleRotation()
    {
        if (anim.IsInteracting) return;

        Quaternion targetRot = Quaternion.identity;

        if (playerCamera.isLockedOn && playerCamera.lockTarget)
        {
            Vector3 toTarget = playerCamera.lockTarget.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f) return;
            targetRot = Quaternion.LookRotation(toTarget);
        }
        else if (moveDirection.magnitude > 0.1f)
        {
            targetRot = Quaternion.LookRotation(moveDirection);
        }
        else return;

        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
    }

    private void HandleJump()
    {
        if (!IsGrounded || !IsSprinting || !input.jump || anim.IsInteracting ||isOnSteepSlope) return;

        if (isOnSteepSlope)
        {
            Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, groundNormal);
            float slopeDot = Vector3.Dot(moveDirection.normalized, slopeDir.normalized);
            if (slopeDot < 0.5f) return;
        }

        anim.PlayTargetAnimation("Jump", true);
        fallStartY = transform.position.y;
        rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);

        Vector3 jumpDirection = moveDirection;
        rb.AddForce(Vector3.up * jumpForce + jumpDirection * jumpForwardForce, ForceMode.VelocityChange);
        IsGrounded = false;
        input.jump = false;
    }

    private void HandleRoll()
    {
        if (!IsGrounded || anim.IsInteracting || !input.dash || isOnSteepSlope) return;

        if (isOnSteepSlope)
        {
            Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, groundNormal);
            float slopeDot = Vector3.Dot(moveDirection.normalized, slopeDir.normalized);
            if (slopeDot < 0.5f) return;
        }

        anim.PlayTargetAnimation(DetermineRollAnim(), true);
        input.dash = false;
    }

    private string DetermineRollAnim()
    {
        bool isStationary = moveInput.sqrMagnitude < 0.01f;

        if (playerCamera.isLockedOn && playerCamera.lockTarget)
        {
            if (isStationary) return "Backstep";
            if (moveInput.z < 0) return "Roll_Backward";
            if (moveInput.x > 0) return "Roll_Right";
            if (moveInput.x < 0) return "Roll_Left";
            return "Roll_Forward";
        }

        return isStationary ? "Backstep" : "Roll_Forward";
    }

    private void UpdateAnimator()
    {
        if (!anim) return;

        if (playerCamera.isLockedOn && playerCamera.lockTarget)
            anim.UpdateMovement(moveInput.z, moveInput.x, IsSprinting, IsGrounded, true);
        else
            anim.UpdateMovement(rb.velocity.magnitude / runSpeed, 0f, IsSprinting, IsGrounded, false);
    }

    private void HandleGrounded()
    {
        Vector3 center = col.bounds.center;
        float halfHeight = Mathf.Max(col.height * 0.5f - col.radius, 0);
        Vector3 point1 = center + Vector3.up * halfHeight;
        Vector3 point2 = center - Vector3.up * halfHeight;
        float castDistance = groundCheckDistance + 0.01f;

        bool groundHit = Physics.CapsuleCast(
            point1, point2,
            col.radius * 0.95f,
            Vector3.down,
            out RaycastHit hit,
            castDistance,
            groundLayer,
            QueryTriggerInteraction.Ignore
        );       

        if (groundHit)
        {
            groundNormal = hit.normal;
            slopeAngle = Vector3.Angle(groundNormal, Vector3.up);
            isOnSteepSlope = slopeAngle > slopeLimit;

            rb.velocity += stickToGroundForce * Time.fixedDeltaTime * Vector3.up;

            if (rb.velocity.y <= 0f)
            {
                rb.velocity = Vector3.ProjectOnPlane(rb.velocity, groundNormal);
            }

            if (!IsGrounded)
            {
                float fallDistance = fallStartY - transform.position.y;
                if (fallDistance > shortLandThreshold)
                {
                    if (fallDistance > normalLandThreshold)
                    {
                        if (fallDistance > highLandThreshold)
                        {
                            anim.PlayTargetAnimation("Land (high)", true);
                        }
                        else
                        {
                            anim.PlayTargetAnimation("Land (normal)", true);
                        }
                    }
                    else
                        anim.PlayTargetAnimation("Land (short)", true);
                }
               
            }

            lastTimeGrounded = Time.time;
            IsGrounded = true;
        }
        else
        {
            groundNormal = Vector3.up;
            slopeAngle = 0f;
            isOnSteepSlope = false;

            if (IsGrounded && Time.time - lastTimeGrounded > groundedGraceTime)
            {
                fallStartY = transform.position.y;
                IsGrounded = false;
            }

            if (!IsGrounded)
            {
                rb.velocity += extraGravity * Time.fixedDeltaTime * Vector3.up;
            }
        }
    }

    public bool OnSlope()
    {
        return IsGrounded && slopeAngle > slopeLimit && slopeAngle < 89f;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position + Vector3.up * 0.5f, GetComponent<Rigidbody>().velocity.normalized * 0.5f);       
    } 
}
