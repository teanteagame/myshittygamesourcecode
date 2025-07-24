using UnityEngine;

public class PlayerCamera : MonoBehaviour
{
    [Header("Camera Targets")]
    [SerializeField] private Transform target;
    [SerializeField] private Transform pivot;
    [SerializeField] private Transform cameraTransform;

    [Header("Rotation Settings")]
    [SerializeField] private float followSpeed = 20f;
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float minPitch = -30f;
    [SerializeField] private float maxPitch = 60f;

    [Header("Lock-On Settings")]
    [SerializeField] private float lockRotationSpeed = 8f;
    [SerializeField] private float lockOnRadius = 15f;
    [SerializeField] private float lockOnViewAngle = 90f;
    [SerializeField] private string enemyTag = "Enemy";
    [SerializeField] internal Vector3 lockTargetOffset = new Vector3(0, 1.5f, 0);

    [Header("Collision Settings")]
    [SerializeField] private float cameraSphereRadius = 0.2f;
    [SerializeField] private float cameraCollisionOffset = 0.2f;
    [SerializeField] private float minimumCollisionDistance = 0.5f;
    [SerializeField] private LayerMask collisionLayers = ~0;

    public bool isLockedOn { get; private set; }
    public Transform lockTarget { get; private set; }
    public Vector3 flatForward;
    public Vector3 flatRight;

    private PlayerInput input;
    private float yaw;
    private float pitch;
    private float defaultCameraZ;
    private float smoothedZ;
    private float cameraVelocityZ;

    public static PlayerCamera instance;

    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        input = PlayerInput.instance;
        defaultCameraZ = cameraTransform.localPosition.z;
        smoothedZ = defaultCameraZ;
    }

    private void Update()
    {
        if (!isLockedOn || lockTarget == null)
        {
            yaw += input.mouseX * mouseSensitivity;
            pitch -= input.mouseY * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        if (input.lockOnToggle)
        {
            ToggleLockOn(FindLockOnTarget());
            input.lockOnToggle = false;
        }

        if (!isLockedOn) return;

        if (input.switchTargetRightPressed)
        {
            SwitchTarget(toRight: true);
        }
        else if (input.switchTargetLeftPressed)
        {
            SwitchTarget(toRight: false);
        }
    }

    private void FixedUpdate()
    {
        if (!target) return;

        FollowTarget();
        HandleCameraCollision();

        if (isLockedOn && lockTarget)
        {
            if (!IsLockTargetValid(lockTarget))
            {
                ToggleLockOn(null);
                return;
            }

            RotateTowardLockTarget();
        }
        else
        {
            ApplyFreeRotation();
        }

        UpdateFlatDirections();
    }

    private void FollowTarget()
    {
        transform.position = Vector3.Lerp(transform.position, target.position, followSpeed * Time.fixedDeltaTime);
    }

    private void ApplyFreeRotation()
    {
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        pivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void RotateTowardLockTarget()
    {
        Vector3 flatDir = lockTarget.position - transform.position;
        flatDir.y = 0f;

        if (flatDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(flatDir.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, lockRotationSpeed * Time.fixedDeltaTime);
        }

        Vector3 toTarget = lockTarget.position - pivot.position;
        float angle = -Mathf.Atan2(toTarget.y, new Vector2(toTarget.x, toTarget.z).magnitude) * Mathf.Rad2Deg;
        pitch = Mathf.Lerp(pitch, angle, lockRotationSpeed * Time.fixedDeltaTime);
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        pivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void HandleCameraCollision()
    {
        Vector3 origin = pivot.position;
        Vector3 targetPos = pivot.position + pivot.rotation * new Vector3(0f, 0f, defaultCameraZ);
        Vector3 dir = (targetPos - origin).normalized;
        float maxDistance = Mathf.Abs(defaultCameraZ);
        float targetZ = defaultCameraZ;

        if (Physics.SphereCast(origin, cameraSphereRadius, dir, out RaycastHit hit, maxDistance, collisionLayers))
        {
            float adjustedDist = Vector3.Distance(origin, hit.point) - cameraCollisionOffset;
            targetZ = -Mathf.Clamp(adjustedDist, minimumCollisionDistance, maxDistance);
        }

        smoothedZ = Mathf.SmoothDamp(smoothedZ, targetZ, ref cameraVelocityZ, 0.05f);
        cameraTransform.localPosition = new Vector3(0, 0, smoothedZ);
    }

    private void UpdateFlatDirections()
    {
        flatForward = pivot.forward;
        flatForward.y = 0f;
        flatForward.Normalize();

        flatRight = pivot.right;
        flatRight.y = 0f;
        flatRight.Normalize();
    }

    public Transform FindLockOnTarget()
    {
        Collider[] hits = Physics.OverlapSphere(target.position, lockOnRadius);
        Transform best = null;
        float closestAngle = lockOnViewAngle;

        foreach (var hit in hits)
        {
            if (!hit.CompareTag(enemyTag) || hit.transform == target) continue;

            Vector3 toTarget = (hit.transform.position + Vector3.up * 1f) - transform.position;
            float angle = Vector3.Angle(transform.forward, toTarget);
            if (angle > lockOnViewAngle) continue;

            Vector3 rayOrigin = cameraTransform.position;
            Vector3 rayTarget = hit.transform.position + Vector3.up * 1.2f;

            if (Physics.Linecast(rayOrigin, rayTarget, out RaycastHit obstruction, collisionLayers))
            {
                if (!obstruction.transform.CompareTag(enemyTag)) continue;
            }

            if (angle < closestAngle)
            {
                best = hit.transform;
                closestAngle = angle;
            }
        }

        return best;
    }

    public void ToggleLockOn(Transform newTarget)
    {
        if (isLockedOn && lockTarget == newTarget)
        {
            isLockedOn = false;
            lockTarget = null;
            ResetCamera();
        }
        else if (newTarget != null)
        {
            isLockedOn = true;
            lockTarget = newTarget;
        }
        else
        {
            isLockedOn = false;
            lockTarget = null;
            ResetCamera();
        }
    }

    public void SwitchTarget(bool toRight)
    {
        if (!isLockedOn || lockTarget == null) return;

        Collider[] hits = Physics.OverlapSphere(target.position, lockOnRadius);
        Transform best = null;
        float bestScore = float.MaxValue;

        Vector3 camRight = toRight ? transform.right : -transform.right;

        foreach (var hit in hits)
        {
            if (!hit.CompareTag(enemyTag) || hit.transform == lockTarget || hit.transform == target) continue;

            Vector3 toCandidate = (hit.transform.position - lockTarget.position).normalized;
            float sideDot = Vector3.Dot(camRight, toCandidate);
            if (sideDot < 0.3f) continue;

            if (!IsLockTargetValid(hit.transform)) continue;

            Vector3 screenPoint = Camera.main.WorldToViewportPoint(hit.transform.position);
            float offset = Mathf.Abs(screenPoint.x - 0.5f);

            if (offset < bestScore)
            {
                bestScore = offset;
                best = hit.transform;
            }
        }

        if (best != null)
        {
            lockTarget = best;
        }
    }

    private bool IsLockTargetValid(Transform t)
    {
        if (!t) return false;

        if (Vector3.Distance(target.position, t.position) > lockOnRadius)
            return false;

        Vector3 from = cameraTransform.position;
        Vector3 to = t.position + Vector3.up * 1.2f;

        if (Physics.Linecast(from, to, out RaycastHit hit, collisionLayers) && hit.transform != t)
            return false;

        Vector3 screenPoint = Camera.main.WorldToViewportPoint(to);
        return screenPoint.z > 0f && screenPoint.x > 0f && screenPoint.x < 1f && screenPoint.y > 0f && screenPoint.y < 1f;
    }

    private void ResetCamera()
    {
        if (!target) return;

        yaw = target.eulerAngles.y;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        pivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }
}
