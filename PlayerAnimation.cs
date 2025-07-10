

using UnityEngine;

public class PlayerAnimation : MonoBehaviour
{
    [Header("References")]
    public Animator animator;

    private PlayerMovement movement;

    // Hashes for animator parameters
    private static readonly int VerticalHash = Animator.StringToHash("Vertical");
    private static readonly int HorizontalHash = Animator.StringToHash("Horizontal");
    private static readonly int SprintHash = Animator.StringToHash("Sprint");
    private static readonly int LockOnHash = Animator.StringToHash("LockOn");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");
    private static readonly int InteractingHash = Animator.StringToHash("Interacting");

    public bool IsInteracting => animator.GetBool(InteractingHash);

    private void Awake()
    {
        animator = GetComponent<Animator>();
        movement = GetComponentInParent<PlayerMovement>();
    }

    public void UpdateMovement(float vertical, float horizontal, bool sprint, bool grounded, bool lockOn)
    {
        animator.SetFloat(VerticalHash, vertical, 0.1f, Time.deltaTime);
        animator.SetFloat(HorizontalHash, horizontal, 0.1f, Time.deltaTime);
        animator.SetBool(SprintHash, sprint);
        animator.SetBool(GroundedHash, grounded);
        animator.SetBool(LockOnHash, lockOn);
    }

    public void PlayTargetAnimation(string animName, bool isInteracting)
    {
        animator.applyRootMotion = isInteracting;
        animator.SetBool(InteractingHash, isInteracting);
        animator.CrossFadeInFixedTime(animName, 0.1f);
    }

    private void OnAnimatorMove()
    {
        if (!IsInteracting) return;

        float delta = Time.deltaTime;
        movement.rb.drag = 0f;

        Vector3 deltaPosition = animator.deltaPosition;
        Vector3 velocity = deltaPosition / delta;

        // Base velocity from root motion
        velocity.y = movement.rb.velocity.y;

        // Modify for steep slope behavior
        if (movement.OnSlope())
        {
            Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, movement.GroundNormal).normalized;

            // Cancel uphill root motion
            float uphillDot = Vector3.Dot(velocity.normalized, -slopeDir);
            if (uphillDot > 0.1f)
            {
                Vector3 uphillMotion = Vector3.Project(velocity, -slopeDir);
                velocity -= uphillMotion;
            }

            // Add slide force while in root motion
            velocity += slopeDir * movement.SlideForce * delta;
        }

        movement.rb.velocity = velocity;
    }
}
