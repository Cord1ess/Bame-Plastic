using UnityEngine;

namespace BamePlastic.Net
{
    /// A billboard avatar for a REMOTE player (a crew member someone else controls). GameNet feeds it
    /// interpolated poses; this just smooths toward the latest target each frame so motion looks fluid even at
    /// the ~15Hz pose rate. Parented either to the world (C1 running around) or the bus cabin (driver / C2),
    /// per the pose mode — so a cabin avatar rides the moving (proxy) bus automatically.
    public class RemoteAvatar : MonoBehaviour
    {
        public enum Mode { World = 0, Cabin = 1 }

        BillboardCharacter _view;
        Transform _cabin;            // bus cabin transform (for Cabin-mode parenting)
        Mode _mode = Mode.World;

        // interpolation targets (local-or-world per mode)
        Vector3 _targetPos;
        bool _has;
        public float lerp = 14f;     // higher = snappier follow

        public BillboardCharacter View => _view;     // for assigning real sprite art

        public static RemoteAvatar Create(string name, Color color, float height, Transform cabin)
        {
            var bc = BillboardCharacter.Create(name, color, height, Vector3.zero);
            var ra = bc.gameObject.AddComponent<RemoteAvatar>();
            ra._view = bc;
            ra._cabin = cabin;
            bc.gameObject.SetActive(false);   // shown once the first pose arrives
            return ra;
        }

        /// Apply a fresh pose. `pos` is world-space for World mode, cabin-local for Cabin mode.
        public void SetPose(Vector3 pos, Mode mode)
        {
            if (mode != _mode)
            {
                _mode = mode;
                // re-parent so Cabin avatars ride the bus, World avatars are free
                transform.SetParent(mode == Mode.Cabin ? _cabin : null, false);
                _has = false;   // avoid a lerp across the parent change
            }
            _targetPos = pos;
            if (!_has)
            {
                if (_mode == Mode.Cabin) transform.localPosition = pos; else transform.position = pos;
                _has = true;
            }
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }

        void Update()
        {
            if (!_has) return;
            float t = 1f - Mathf.Exp(-lerp * Time.deltaTime);   // frame-rate independent smoothing
            if (_mode == Mode.Cabin)
                transform.localPosition = Vector3.Lerp(transform.localPosition, _targetPos, t);
            else
                transform.position = Vector3.Lerp(transform.position, _targetPos, t);
            if (_view != null) _view.ApplyHeight();
        }
    }
}
