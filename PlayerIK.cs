using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerIK : MonoBehaviour
{
    [Header("Head Look At")]
    [SerializeField] private float lookAtWeight = 1f;
    [SerializeField] private float bodyWeight = 0.3f;
    [SerializeField] private float headWeight = 0.8f;
    [SerializeField] private float eyesWeight = 1f;
    [SerializeField] private float clampWeight = 0.5f;
    [SerializeField] private float headSmoothSpeed = 5f;

    [Header("Foot IK Settings")]
    [SerializeField] private float footRayLength = 0.5f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float footOffsetY = 0.1f;
    [SerializeField] private float footIKWeight = .5f;
    [SerializeField] private float footRotationWeight = 1f;
    private float leftFootWeight = 0f;
    private float rightFootWeight = 0f;
    [SerializeField] private float footBlendSpeed = 5f;


    private Animator animator;
    private PlayerCamera playerCamera;
    private Vector3 currentLookAtPos;
    private PlayerMovement movement;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        playerCamera = GetComponentInParent<PlayerCamera>();
        movement = GetComponentInParent<PlayerMovement>();
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (!animator) return;

        HandleHeadLook();
        bool isGrounded = movement.IsGrounded;
        bool isIdleOrWalking = movement.rb.velocity.magnitude < 0.2f;
        if (!(isGrounded && isIdleOrWalking)) return;

        HandleFootIK(AvatarIKGoal.LeftFoot);
        HandleFootIK(AvatarIKGoal.RightFoot);
    }

    private void HandleHeadLook()
    {
        if (playerCamera != null && playerCamera.isLockedOn && playerCamera.lockTarget)
        {
            Vector3 target = playerCamera.lockTarget.position + playerCamera.lockTargetOffset;
            currentLookAtPos = Vector3.Lerp(currentLookAtPos, target, Time.deltaTime * headSmoothSpeed);

            animator.SetLookAtWeight(lookAtWeight, bodyWeight, headWeight, eyesWeight, clampWeight);
            animator.SetLookAtPosition(currentLookAtPos);
        }
        else
        {
            animator.SetLookAtWeight(0f);
        }
    }

    private void HandleFootIK(AvatarIKGoal foot)
    {
        Transform footTransform = animator.GetBoneTransform(foot == AvatarIKGoal.LeftFoot ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
        Vector3 origin = footTransform.position + Vector3.up * 0.2f;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, footRayLength, groundLayer))
        {
            Vector3 footPosition = hit.point + Vector3.up * footOffsetY;
            Quaternion footRotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * transform.rotation;

            // Smooth blend
            if (foot == AvatarIKGoal.LeftFoot)
                leftFootWeight = Mathf.Lerp(leftFootWeight, footIKWeight, Time.deltaTime * footBlendSpeed);
            else
                rightFootWeight = Mathf.Lerp(rightFootWeight, footIKWeight, Time.deltaTime * footBlendSpeed);

            float blendWeight = foot == AvatarIKGoal.LeftFoot ? leftFootWeight : rightFootWeight;

            animator.SetIKPositionWeight(foot, blendWeight);
            animator.SetIKRotationWeight(foot, blendWeight);
            animator.SetIKPosition(foot, footPosition);
            animator.SetIKRotation(foot, footRotation);
        }
        else
        {
            if (foot == AvatarIKGoal.LeftFoot)
                leftFootWeight = Mathf.Lerp(leftFootWeight, 0f, Time.deltaTime * footBlendSpeed);
            else
                rightFootWeight = Mathf.Lerp(rightFootWeight, 0f, Time.deltaTime * footBlendSpeed);

            float blendWeight = foot == AvatarIKGoal.LeftFoot ? leftFootWeight : rightFootWeight;

            animator.SetIKPositionWeight(foot, blendWeight);
            animator.SetIKRotationWeight(foot, blendWeight);
        }
    }
}
