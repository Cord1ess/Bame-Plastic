using UnityEngine;
using UnityEditor;

/// Creates the persistent, hand-editable "CollisionBox" child on the bus — a full-bus BoxCollider trigger
/// (+ BusTag + kinematic Rigidbody) that traffic collides with (the small sphere just drives). It auto-fits
/// to the bus model as a STARTING SIZE; then you select it and drag the BoxCollider handles in the Scene
/// view to get it right, and save the BusPlayer prefab. Runtime reuses whatever's there.
///
/// Menu: Bame Plastic ▸ Bus ▸ Add Collision Box
public static class AddBusCollisionBox
{
    [MenuItem("Bame Plastic/Bus/Add Collision Box")]
    static void Add()
    {
        BusController bus = FindBus();
        if (bus == null)
        {
            EditorUtility.DisplayDialog("Add Collision Box",
                "No BusController found. Select the BusPlayer (or have it in the scene) and try again.", "OK");
            return;
        }

        Transform existing = bus.transform.Find("CollisionBox");
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            Debug.Log("[AddBusCollisionBox] CollisionBox already exists — selected it. Resize via the BoxCollider handles, then save the prefab.");
            return;
        }

        GameObject box = BusController.BuildCollisionBox(bus.transform, bus.busModel, bus.collisionBoxSize, bus.collisionBoxCenter);
        Undo.RegisterCreatedObjectUndo(box, "Add Bus Collision Box");
        EditorUtility.SetDirty(bus);
        Selection.activeGameObject = box;
        EditorGUIUtility.PingObject(box);
        Debug.Log("[AddBusCollisionBox] Created CollisionBox (auto-fit to the model). Now drag the BoxCollider " +
                  "handles in the Scene view to size it to the bus, then SAVE the BusPlayer prefab so it persists.");
    }

    static BusController FindBus()
    {
        if (Selection.activeGameObject != null)
        {
            var b = Selection.activeGameObject.GetComponentInParent<BusController>();
            if (b != null) return b;
        }
#if UNITY_2022_2_OR_NEWER
        return Object.FindFirstObjectByType<BusController>();
#else
        return Object.FindObjectOfType<BusController>();
#endif
    }
}
