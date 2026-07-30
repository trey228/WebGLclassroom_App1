#if NORMCORE

using UnityEngine;
using Normal.Realtime;

/// <summary>
/// Lives on the NormcoreManager object (next to the Realtime component).
/// Modeled on Normcore's CubePlayerManager example.
///
/// When this client connects to the room, it spawns the player prefab for us
/// at the spawn point (near the door). Normcore handles replicating that
/// spawn to every other client so everyone sees everyone.
///
/// The prefab MUST live in a Resources folder so Normcore can find it by name
/// across the network.
/// </summary>
[RequireComponent(typeof(Realtime))]
public class ClassroomPlayerManager : MonoBehaviour {
    [SerializeField] private GameObject _prefab;

    [Header("Spawn placement (near the door)")]
    [Tooltip("Where new players appear. Door is around (8.2, 0, 14.3); a bit " +
             "inside the room works well.")]
    public Vector3 spawnPosition = new Vector3(6f, 0f, 11f);

    [Tooltip("Initial facing, in degrees (Y). 180 faces back toward the room/chalkboard.")]
    public float spawnYaw = 180f;

    [Tooltip("If true, slightly randomizes spawn X/Z so two players don't overlap.")]
    public bool jitterSpawn = true;
    public float jitterRadius = 1.0f;

    private Realtime _realtime;

    private void Awake() {
        _realtime = GetComponent<Realtime>();
        _realtime.didConnectToRoom += DidConnectToRoom;
    }

    private void DidConnectToRoom(Realtime realtime) {
        Vector3 pos = spawnPosition;
        if (jitterSpawn) {
            Vector2 r = Random.insideUnitCircle * jitterRadius;
            pos += new Vector3(r.x, 0f, r.y);
        }

        Quaternion rot = Quaternion.Euler(0f, spawnYaw, 0f);

        var options = new Realtime.InstantiateOptions {
            ownedByClient            = true,    // this client owns its RealtimeView
            preventOwnershipTakeover = true,    // others can't steal control of our player
            useInstance              = realtime // use the Realtime instance that connected
        };

        Realtime.Instantiate(_prefab.name, pos, rot, options);
    }
}

#endif
