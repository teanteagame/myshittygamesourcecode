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
    [SerializeField] private float landThreshold = 2f;

    [Header("Ground Detection")]
    [SerializeField] private float groundCheckDistance = 0.3f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float extraGravity = -30f;
    [SerializeField] private float slopeLimit = 45f;
    [SerializeField] private float slideForce = 40f;

    [Header("Step Handling")]
    [SerializeField] private float stepOffset = 0.3f;
    [SerializeField] private float stepCheckDistance = 0.5f;
    [SerializeField] private float stepHeight = 0.4f;
    [SerializeField] private float stepSmooth = 6f;

    [Header("References")]
    [SerializeField] private PlayerCamera playerCamera;
    [SerializeField] private PlayerAnimation anim;

    public bool IsGrounded { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsWalking { get; private set; }

    internal Rigidbody rb;
    private PlayerInput input;

    private float currentSpeed;
    private float fallStartY;
    private Vector3 moveInput;
    private Vector3 moveDirection;
    private bool wasGrounded;
    [SerializeField] private float groundedCoyoteThreshold = 0.15f; // seconds
    private float lastTimeGrounded;
    private float verticalVelocity;
    private float timeSinceLeftGround;
    [SerializeField] private float groundStickGraceTime = 0.1f;  // adjust as needed
    [SerializeField] private float maxStepDownHeight = 0.3f;     // tolerable edge height
    private bool shouldUseGroundedAnim;
    private float groundBufferTime = 0.1f; // time allowed off ground without falling anim
    private float groundedAnimTimer;

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

        if (!IsGrounded && anim.IsInteracting == false)
        {
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            Vector3 forward = rb.velocity.sqrMagnitude > 0.1f ? rb.velocity.normalized : transform.forward;

            if (Physics.Raycast(origin, forward, out RaycastHit hit, 0.6f, groundLayer))
            {
                float wallAngle = Vector3.Angle(hit.normal, Vector3.up);
                if (wallAngle > 75f)
                {
                    // Project velocity away from wall surface
                    Vector3 pushAway = Vector3.ProjectOnPlane(rb.velocity, hit.normal).normalized;
                    rb.velocity = pushAway * rb.velocity.magnitude;

                    // Add slight down force to keep sliding
                    rb.AddForce(Vector3.down * 20f, ForceMode.Acceleration);
                }
            }
        }


        if (OnSlope() && !anim.IsInteracting)
        {
            Vector3 slideDir = Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized;
            rb.velocity += slideDir * slideForce * Time.fixedDeltaTime;
        }


        if (!IsGrounded && Physics.Raycast(transform.position, moveDirection, out RaycastHit wallHit, 0.6f))
        {
            float wallAngle = Vector3.Angle(wallHit.normal, Vector3.up);
            if (wallAngle > 75f) // Only repel steep walls
            {
                rb.AddForce(wallHit.normal * 50f, ForceMode.Acceleration); // push away
            }
        }


        ApplyGravity();
        EvaluateGrounded();
        UpdateAnimatorGroundedState();
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

        if (input.lockOnToggle)
        {
            Transform foundTarget = playerCamera.FindLockOnTarget();
            playerCamera.ToggleLockOn(foundTarget);
        }

        if (playerCamera.isLockedOn)
        {
            if (input.switchTargetLeft)
                playerCamera.SwitchTarget(false);
            else if (input.switchTargetRight)
                playerCamera.SwitchTarget(true);
        }
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
        if (!IsGrounded || !IsSprinting || !input.jump || anim.IsInteracting) return;

        // Prevent jump uphill on steep slopes
        if (isOnSteepSlope)
        {
            Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, groundNormal);
            float slopeDot = Vector3.Dot(moveDirection.normalized, slopeDir.normalized);
            if (slopeDot < 0.5f) return;
        }

        anim.PlayTargetAnimation("Jump", true);

        // Optional: Zero Y to prevent carryover from slope velocity
        rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);

        Vector3 jumpDirection = moveDirection;

        // Detect walls and cancel forward momentum if too steep
        if (WallInFront())
        {
            Vector3 wallNormal = Physics.Raycast(transform.position + Vector3.up * 0.5f, moveDirection, out RaycastHit hit, 1f, groundLayer)
                ? hit.normal : -moveDirection;

            // Cancel jump forward if wall is steep
            if (Vector3.Angle(wallNormal, Vector3.up) > 60f)
                jumpDirection = Vector3.zero;
        }

        rb.AddForce(Vector3.up * jumpForce + jumpDirection * jumpForwardForce, ForceMode.VelocityChange);
        IsGrounded = false;
    }


    private void HandleRoll()
    {
        if (!IsGrounded || anim.IsInteracting || !input.dash) return;
        if (isOnSteepSlope)
        {
            Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, groundNormal);
            float slopeDot = Vector3.Dot(moveDirection.normalized, slopeDir.normalized);
            if (slopeDot < 0.5f) return; // cancel if player trying to climb slope
        }

        anim.PlayTargetAnimation(DetermineRollAnim(), true);
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
            return "Roll_Forward"; // fallback if needed
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

    private void ApplyGravity()
    {
        if (!IsGrounded)
        {
            rb.AddForce(Vector3.up * extraGravity, ForceMode.Acceleration);

            // Enhanced wall repel logic
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            Vector3 forward = moveDirection.sqrMagnitude > 0.1f ? moveDirection : transform.forward;

            if (Physics.Raycast(origin, forward, out RaycastHit wallHit, 0.5f, groundLayer))
            {
                float wallAngle = Vector3.Angle(wallHit.normal, Vector3.up);
                if (wallAngle > 75f)
                {
                    // Apply a small bounce outward + extra downward pull
                    Vector3 repelDir = wallHit.normal + Vector3.down;
                    rb.AddForce(repelDir.normalized * 80f, ForceMode.Acceleration);
                }
            }
        }
    }

    private void EvaluateGrounded()
    {
        CapsuleCollider col = GetComponent<CapsuleCollider>();
        Vector3 origin = transform.position + Vector3.up * (col.radius + 0.05f);
        float radius = col.radius * 0.9f;
        float castDistance = groundCheckDistance + 0.2f;

        bool wasActuallyGrounded = IsGrounded;
        IsGrounded = Physics.SphereCast(
            origin,
            radius,
            Vector3.down,
            out RaycastHit hit,
            castDistance,
            groundLayer,
            QueryTriggerInteraction.Ignore
        );

        verticalVelocity = rb.velocity.y;

        if (IsGrounded)
        {
            timeSinceLeftGround = 0f;

            groundNormal = hit.normal;
            slopeAngle = Vector3.Angle(groundNormal, Vector3.up);
            isOnSteepSlope = slopeAngle > slopeLimit;

            if (!wasActuallyGrounded)
            {
                float fallDistance = fallStartY - transform.position.y;
                if (fallDistance > landThreshold)
                    anim.PlayTargetAnimation("Land", true);
            }
        }
        else
        {
            timeSinceLeftGround += Time.deltaTime;

            // "Sticky ground" check: small drop but still near ground and falling slowly
            bool softGrounded = timeSinceLeftGround < groundStickGraceTime && verticalVelocity > -2f;

            if (softGrounded)
            {
                IsGrounded = true;
                isOnSteepSlope = false;
            }
            else
            {
                isOnSteepSlope = false;

                if (wasActuallyGrounded)
                    fallStartY = transform.position.y;
            }
        }

        wasGrounded = IsGrounded;
    }

    private bool TryStepUp(out Vector3 newPosition)
    {
        newPosition = transform.position;

        Vector3 forward = moveDirection.sqrMagnitude > 0.1f ? moveDirection : transform.forward;
        Vector3 origin = transform.position + Vector3.up * stepHeight;

        if (Physics.Raycast(origin, forward, out RaycastHit forwardHit, stepCheckDistance, groundLayer))
        {
            Vector3 stepOrigin = transform.position + forward * (forwardHit.distance + 0.1f);
            stepOrigin.y += stepHeight;

            if (!Physics.Raycast(stepOrigin, Vector3.down, out RaycastHit downHit, stepHeight + 0.1f, groundLayer))
                return false;

            float stepHeightDifference = transform.position.y - downHit.point.y;
            if (stepHeightDifference < stepOffset)
            {
                newPosition = new Vector3(transform.position.x, downHit.point.y, transform.position.z);
                return true;
            }
        }

        return false;
    }

    public bool OnSlope()
    {
        return IsGrounded && slopeAngle > slopeLimit && slopeAngle < 89f;
    }

    bool WallInFront()
    {
        CapsuleCollider col = GetComponent<CapsuleCollider>();
        Vector3 center = transform.position + Vector3.up * col.height * 0.5f;
        float castRadius = col.radius * 0.9f;
        float castDistance = 0.6f;

        return Physics.CapsuleCast(
            center,
            center,
            castRadius,
            moveDirection,
            out RaycastHit hit,
            castDistance,
            groundLayer
        );
    }

    private void UpdateAnimatorGroundedState()
    {
        if (IsGrounded)
        {
            groundedAnimTimer = groundBufferTime;
            shouldUseGroundedAnim = true;
        }
        else
        {
            groundedAnimTimer -= Time.deltaTime;
            shouldUseGroundedAnim = groundedAnimTimer > 0;
        }
    }


    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.2f, 0.3f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position + Vector3.up * 0.5f, GetComponent<Rigidbody>().velocity.normalized * 0.5f);
    }
}
