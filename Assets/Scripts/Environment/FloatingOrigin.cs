// FloatingOrigin.cs
// Written by Peter Stirling
// 11 November 2010
// Uploaded to Unify Community Wiki on 11 November 2010
// Updated to Unity 5.x particle system by Tony Lovell 14 January, 2016
// fix to ensure ALL particles get moved by Tony Lovell 8 September, 2016
// URL: http://wiki.unity3d.com/index.php/Floating_Origin
using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;


[RequireComponent(typeof(Camera))]
public class FloatingOrigin : MonoBehaviour
{
    public float threshold = 100.0f;
    public LevelLayoutGenerator layoutGenerator;

    [Tooltip("OFF by default: the recenter teleport destabilises the physics bus and isn't needed for a ~10-min shift (the world stays small). Leave off until a bus-anchored, physics-safe version is built.")]
    public bool recenter = false;

    void LateUpdate()
    {
        if (!recenter) return;

        Vector3 cameraPosition = gameObject.transform.position;
        cameraPosition.y = 0f;

        if (cameraPosition.magnitude > threshold)
        {

            for (int z = 0; z < SceneManager.sceneCount; z++)
            {
                foreach (GameObject g in SceneManager.GetSceneAt(z).GetRootGameObjects())
                {
                    g.transform.position -= cameraPosition;
                }
            }

            // The shift above teleports everything. An interpolated Rigidbody would otherwise
            // smear from its old (far) pose to the new one for a frame, which looks like it's
            // clipping through the ground. Toggling interpolation off/on flushes that buffer so
            // each body snaps cleanly to the recentered position. (Cheap because recenters are rare.)
            Rigidbody[] bodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
            for (int i = 0; i < bodies.Length; i++)
            {
                RigidbodyInterpolation mode = bodies[i].interpolation;
                if (mode != RigidbodyInterpolation.None)
                {
                    bodies[i].interpolation = RigidbodyInterpolation.None;
                    bodies[i].interpolation = mode;
                }
            }

            Vector3 originDelta = Vector3.zero - cameraPosition;
            layoutGenerator.UpdateSpawnOrigin(originDelta);
            Debug.Log("recentering, origin delta = " + originDelta);
        }

    }
}


