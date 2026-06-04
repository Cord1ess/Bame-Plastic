using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using UnityEngine.Serialization;

public class BusController : MonoBehaviour
{

    [FormerlySerializedAs("kartModel")]
    public Transform busModel;
    [FormerlySerializedAs("kartNormal")]
    public Transform busNormal;
    public Rigidbody sphere;

    public List<ParticleSystem> primaryParticles = new List<ParticleSystem>();
    public List<ParticleSystem> secondaryParticles = new List<ParticleSystem>();

    float speed, currentSpeed;
    float rotate, currentRotate;
    int driftDirection;
    float driftPower;
    int driftMode = 0;
    bool first, second, third;
    Color c;
    float driftCooldown = 0f;

    [Header("Bools")]
    public bool drifting;


    [Header("Parameters")]
    [Header("Steering Multipliers")]
    public float normalSteeringMultiplier = 1f;
    public float driftSteeringMultiplier = 0.5f;

    public float acceleration = 30f;
    public float steering = 80f;
    public float gravity = 10f;
    public LayerMask layerMask;

    [Header("Model Parts")]

    public Transform frontWheels;
    public Transform backWheels;

    [Header("Particles")]
    public Transform wheelParticles;
    public Transform flashParticles;
    public Color[] turboColors;

    public enum WheelAxis { X, Y, Z }

    [Header("Wheel Settings")]
    [Tooltip("The local axle the wheels spin around. X is standard for most imported vehicles, Z was used for the original model.")]
    public WheelAxis wheelSpinAxis = WheelAxis.Z;
    [Tooltip("Set to -1 to reverse the wheel spinning direction, or adjust to change spin speed!")]
    public float wheelSpinMultiplier = 1f;

    [Header("Boost Settings")]
    [Tooltip("How much the current speed is multiplied when boosting (default is 3).")]
    public float boostSpeedMultiplier = 3f;
    [Tooltip("Base duration of the boost in seconds, multiplied by the drift tier level (default is 0.3).")]
    public float boostDurationMultiplier = 0.3f;

    private float currentWheelSpin = 0f;
    private float currentZTilt = 0f;
    private float modelInitialYRotation = 90f;

    void Start()
    {
        sphere.transform.parent = null;

        if (busModel != null)
        {
            modelInitialYRotation = busModel.localEulerAngles.y;
        }

        for (int i = 0; i < wheelParticles.GetChild(0).childCount; i++)
        {
            primaryParticles.Add(wheelParticles.GetChild(0).GetChild(i).GetComponent<ParticleSystem>());
        }

        for (int i = 0; i < wheelParticles.GetChild(1).childCount; i++)
        {
            primaryParticles.Add(wheelParticles.GetChild(1).GetChild(i).GetComponent<ParticleSystem>());
        }

        foreach (ParticleSystem p in flashParticles.GetComponentsInChildren<ParticleSystem>())
        {
            secondaryParticles.Add(p);
        }
    }

    void Update()
    {
        //if (Input.GetKeyDown(KeyCode.Space))
        //{
        //    float time = Time.timeScale == 1 ? .2f : 1;
        //    Time.timeScale = time;
        //}

        //Follow Collider
        transform.position = sphere.transform.position - new Vector3(0, 0.4f, 0);

        // Accelerate with W key only (constant speed)
        if (Input.GetKey(KeyCode.W))
            speed = acceleration;

        // Brake/slowdown with S key
        if (Input.GetKey(KeyCode.S))
            speed = -acceleration * 0.5f;

        //Steer - whenever the vehicle is moving or receiving throttle/reverse input!
        if (Input.GetAxis("Horizontal") != 0 && (Mathf.Abs(currentSpeed) > 0.1f || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S) || drifting))
        {
            int dir = Input.GetAxis("Horizontal") > 0 ? 1 : -1;
            float amount = Mathf.Abs((Input.GetAxis("Horizontal")));
            float steerMult = drifting ? driftSteeringMultiplier : normalSteeringMultiplier;
            Steer(dir, amount * steerMult);
        }

        // Update drift cooldown
        if (driftCooldown > 0)
            driftCooldown -= Time.deltaTime;

        //Drift
        if (Input.GetButtonDown("Jump") && !drifting && Input.GetAxis("Horizontal") != 0 && currentSpeed > 0.5f && driftCooldown <= 0)
        {
            drifting = true;
            driftDirection = Input.GetAxis("Horizontal") > 0 ? 1 : -1;
            driftPower = 0;
            driftMode = 0;
            first = false; second = false; third = false;

            // Show sparks instantly with first turbo color
            foreach (ParticleSystem p in primaryParticles)
            {
                var pmain = p.main;
                pmain.startColor = turboColors.Length > 0 ? turboColors[0] : Color.white;
                p.Play();
            }

            // Removed visual jump/hop for the massive bus!
            // busModel.parent.DOComplete();
            // busModel.parent.DOPunchPosition(transform.up * .2f, .3f, 5, 1);
        }

        if (drifting)
        {
            // Symmetric control for natural counter-steering during drift
            float driftSteerInput = Input.GetAxis("Horizontal");
            int steerDir = driftSteerInput > 0 ? 1 : -1;
            float steerAmount = Mathf.Abs(driftSteerInput);
            Steer(steerDir, steerAmount * driftSteeringMultiplier);

            float powerControl = (driftDirection == 1) ? ExtensionMethods.Remap(Input.GetAxis("Horizontal"), -1, 1, .2f, 1) : ExtensionMethods.Remap(Input.GetAxis("Horizontal"), -1, 1, 1, .2f);
            driftPower += powerControl;

            // Always update sparks color to match current drift rank
            ColorDrift();
        }

        if (Input.GetButtonUp("Jump") && drifting)
        {
            Boost();
            driftCooldown = 0.3f;
        }

        currentSpeed = Mathf.SmoothStep(currentSpeed, speed, Time.deltaTime * 12f); speed = 0f;
        currentRotate = Mathf.Lerp(currentRotate, rotate, Time.deltaTime * 4f); rotate = 0f;

        //Animations    

        //a) Bus Visual Body Roll & Drifting Slide Transitions
        float speedRatio = Mathf.Clamp01(sphere.linearVelocity.magnitude / 30f); // Normalize tilt based on a max speed reference of 30
        float steerInput = Input.GetAxis("Horizontal");

        // Dynamic centrifugal roll: steer right (positive) causes bus body to roll left (negative local Z tilt)
        float targetZTilt = -steerInput * speedRatio * 5.0f; // 5.0 degrees of heavy weight-transfer tilt!
        currentZTilt = Mathf.Lerp(currentZTilt, targetZTilt, Time.deltaTime * 4.0f);

        if (!drifting)
        {
            // Subtle steering tilt on Y-axis (reduced to 4 degrees) and roll on Z-axis using safe Quaternion Slerp!
            Quaternion targetRot = Quaternion.Euler(0f, modelInitialYRotation + (steerInput * 4f), currentZTilt);
            busModel.localRotation = Quaternion.Slerp(busModel.localRotation, targetRot, Time.deltaTime * 12.0f);
        }
        else
        {
            float control = (driftDirection == 1) ? ExtensionMethods.Remap(steerInput, -1, 1, .5f, 2) : ExtensionMethods.Remap(steerInput, -1, 1, 2, .5f);
            
            // Ultra-smooth Y rotation transition, aligned correctly to point in driftDirection
            float targetSlideY = (control * 5f) * driftDirection;
            float currentSlideY = Mathf.LerpAngle(busModel.parent.localEulerAngles.y, targetSlideY, Time.deltaTime * 5.0f);
            busModel.parent.localRotation = Quaternion.Euler(0, currentSlideY, 0);

            // Keep the roll tilt active during the power-slide using safe Quaternion Slerp!
            Quaternion targetRot = Quaternion.Euler(0f, modelInitialYRotation, currentZTilt);
            busModel.localRotation = Quaternion.Slerp(busModel.localRotation, targetRot, Time.deltaTime * 12.0f);
        }

        //b) Wheels
        if (Application.isPlaying)
        {
            // Accumulate spin rotation based on speed and delta time
            currentWheelSpin += (sphere.linearVelocity.magnitude / 2f) * wheelSpinMultiplier * Time.deltaTime * 60f;
        }

        if (frontWheels != null)
        {
            float steerAngle = Input.GetAxis("Horizontal") * 15f;
            // Always spin on X and steer on Y!
            frontWheels.localRotation = Quaternion.Euler(currentWheelSpin, steerAngle, 0f);
        }
        
        if (backWheels != null)
        {
            // Always spin on X!
            backWheels.localRotation = Quaternion.Euler(currentWheelSpin, 0f, 0f);
        }



    }

    private void FixedUpdate()
    {
        //Forward Acceleration
        sphere.AddForce(transform.forward * currentSpeed, ForceMode.Acceleration);

        //Gravity
        sphere.AddForce(Vector3.down * gravity, ForceMode.Acceleration);

        //Steering
        transform.eulerAngles = Vector3.Lerp(transform.eulerAngles, new Vector3(0, transform.eulerAngles.y + currentRotate, 0), Time.deltaTime * 5f);

        RaycastHit hitOn;
        RaycastHit hitNear;

        Physics.Raycast(transform.position + (transform.up * .1f), Vector3.down, out hitOn, 1.1f, layerMask);
        Physics.Raycast(transform.position + (transform.up * .1f), Vector3.down, out hitNear, 2.0f, layerMask);

        //Normal Rotation
        busNormal.up = Vector3.Lerp(busNormal.up, hitNear.normal, Time.deltaTime * 8.0f);
        busNormal.Rotate(0, transform.eulerAngles.y, 0);
    }

    public void Boost()
    {
        drifting = false;

        // Restore boost effect if driftMode > 0
        if (driftMode > 0 && busModel != null)
        {
            DOVirtual.Float(currentSpeed * boostSpeedMultiplier, currentSpeed, boostDurationMultiplier * driftMode, Speed);
            
            Transform tube1 = busModel.Find("Tube001");
            Transform tube2 = busModel.Find("Tube002");
            if (tube1 != null)
            {
                ParticleSystem ps = tube1.GetComponentInChildren<ParticleSystem>();
                if (ps != null) ps.Play();
            }
            if (tube2 != null)
            {
                ParticleSystem ps = tube2.GetComponentInChildren<ParticleSystem>();
                if (ps != null) ps.Play();
            }
        }

        driftPower = 0;
        driftMode = 0;
        first = false; second = false; third = false;

        foreach (ParticleSystem p in primaryParticles)
        {
            p.startColor = Color.clear;
            p.Stop();
        }

        if (busModel != null && busModel.parent != null)
        {
            busModel.parent.DOLocalRotate(Vector3.zero, .5f).SetEase(Ease.OutBack);
        }
    }

    /// Called by obstacles/hazards to penalise the bus on contact.
    /// speedMultiplier in [0,1]; e.g. 0.5 sheds half the current speed, 0 is a dead stop.
    /// Also cancels any active drift so a hit really interrupts you.
    public void ApplyImpact(float speedMultiplier)
    {
        currentSpeed *= Mathf.Clamp01(speedMultiplier);
        if (drifting)
        {
            drifting = false;
            driftPower = 0;
            driftMode = 0;
        }
    }

    public void Steer(int direction, float amount)
    {
        rotate = (steering * direction) * amount;
    }

    public void ColorDrift()
    {
        if (!first)
            c = Color.clear;

        if (driftPower > 50 && driftPower < 100 - 1 && !first)
        {
            first = true;
            c = turboColors[0];
            driftMode = 1;

            PlayFlashParticle(c);
        }

        if (driftPower > 100 && driftPower < 150 - 1 && !second)
        {
            second = true;
            c = turboColors[1];
            driftMode = 2;

            PlayFlashParticle(c);
        }

        if (driftPower > 150 && !third)
        {
            third = true;
            c = turboColors[2];
            driftMode = 3;

            PlayFlashParticle(c);
        }

        foreach (ParticleSystem p in primaryParticles)
        {
            var pmain = p.main;
            pmain.startColor = c;
        }

        foreach (ParticleSystem p in secondaryParticles)
        {
            var pmain = p.main;
            pmain.startColor = c;
        }
    }

    void PlayFlashParticle(Color c)
    {

        foreach (ParticleSystem p in secondaryParticles)
        {
            var pmain = p.main;
            pmain.startColor = c;
            p.Play();
        }
    }

    private void Speed(float x)
    {
        currentSpeed = x;
    }

    //private void OnDrawGizmos()
    //{
    //    Gizmos.color = Color.red;
    //    Gizmos.DrawLine(transform.position + transform.up, transform.position - (transform.up * 2));
    //}
}
