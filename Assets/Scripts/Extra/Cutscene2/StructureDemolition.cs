// StructureDemolition.cs — takes the target structure apart when the nuke goes off.
//
// Two ways to get fragments, because OpenFracture supports both and they suit
// different structures:
//
//   A. Runtime — a Fracture component on the structure, cut the moment the blast
//      lands. Cheapest to author, most expensive to run: the mesh is cut while
//      the shot is playing. Fine for one modest building, not for a facility.
//
//   B. Prefractured — Prefracture run at design time (its ComputeFracture bails
//      out in play mode by design), leaving frozen fragments in the scene. Costs
//      nothing at runtime beyond the physics. This is the one to use for the
//      structure the missile hits.
//
// Either way the fragments need a push, and neither path gives them one:
// OpenFracture cuts the mesh and stops. UnfreezeFragment does release the
// prefractured ones, but only on a collision or a trigger — nothing is going to
// collide with a building a kilometre from the camera, so the constraints are
// cleared here instead.
using System.Collections;
using UnityEngine;

public class StructureDemolition : MonoBehaviour
{
    [Header("Intact Version")]
    [Tooltip("Switched off once the fragments exist. Leave empty on the runtime " +
             "path — Fracture already deactivates its own object.")]
    public GameObject intactStructure;

    [Header("Option A — Runtime Fracture")]
    [Tooltip("A Fracture component on the structure. Cut when the blast lands.")]
    public Fracture runtimeFracture;

    [Header("Option B — Prefractured Fragments")]
    [Tooltip("Root of the fragments left behind by Prefracture at design time — " +
             "the object named '<structure>Fragments'.")]
    public Transform prefracturedFragments;

    [Header("Blast")]
    public float explosionForce = 6000f;    // newtons at ground zero
    public float explosionRadius = 200f;    // meters — fragments outside this are untouched
    public float upwardsModifier = 0.8f;    // lifts the debris rather than only shoving it out
    public float torque = 200f;             // random tumble, so chunks don't fly flat

    [Header("Physics Budget")]
    // Fragments are cut from one solid, so every one of them starts overlapping
    // its neighbours. The solver resolving N interpenetrating convex hulls on a
    // single frame is what tips Physics.Simulate past the fixed timestep and
    // starts the catch-up spiral.
    [Tooltip("Layer to move the fragments onto. Set the collision matrix so this " +
             "layer does NOT collide with itself — debris-vs-debris contacts are " +
             "invisible at cutscene distance and cost N-squared. Empty = leave alone.")]
    public string fragmentLayer = "";

    [Tooltip("Per-body solver iterations. The project default is fine for gameplay " +
             "and wasted on debris nobody is looking at closely.")]
    public int solverIterations = 4;
    public int solverVelocityIterations = 1;

    [Tooltip("Caps how fast overlapping fragments shove each other apart on the " +
             "first frame. Unity's default of 10 m/s makes them burst outward and " +
             "gives the solver far more to chase.")]
    public float maxDepenetrationVelocity = 3f;

    [Header("Timing")]
    [Tooltip("Seconds to wait before pushing. Fracture with asynchronous " +
             "enabled has not produced its fragments on the same frame.")]
    public float forceDelay = 0.1f;

    [Header("Cleanup")]
    [Tooltip("Seconds before the fragment root is destroyed. 0 keeps it forever. " +
             "The shot only runs about 11s, so debris outliving that is wasted physics.")]
    public float fragmentLifetime = 20f;

    private bool _demolished;

    /// Convenience overload for wiring into a UnityEvent in the inspector, which
    /// cannot pass a Vector3.
    public void Demolish() => Demolish(BlastOriginFallback());

    /// Ground zero when nobody hands one over. Prefers the structure itself over
    /// this transform: the component may well be sitting on a manager object
    /// nowhere near the target, and an explosion centred there pushes nothing.
    private Vector3 BlastOriginFallback()
    {
        if (intactStructure != null) return intactStructure.transform.position;
        if (runtimeFracture != null) return runtimeFracture.transform.position;
        if (prefracturedFragments != null) return prefracturedFragments.position;
        return transform.position;
    }

    /// Fires the demolition on its own, without sitting through the four shots
    /// in front of it. Right-click the component header in play mode.
    [ContextMenu("Demolish Now")]
    private void DemolishFromContextMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("StructureDemolition: enter Play Mode first. The push runs " +
                             "on a coroutine and the debris needs physics to step, neither " +
                             "of which happens in edit mode.", this);
            return;
        }

        Demolish();
    }

    /// Blast origin comes from the detonation shot, which gets it from wherever
    /// the missile actually ended up rather than an authored guess.
    public void Demolish(Vector3 blastOrigin)
    {
        if (_demolished) return;
        _demolished = true;

        StartCoroutine(Run(blastOrigin));
    }

    IEnumerator Run(Vector3 blastOrigin)
    {
        Transform fragmentRoot = prefracturedFragments;

        if (runtimeFracture != null)
        {
            runtimeFracture.CauseFracture();

            // Fracture parents its output under a "<name>Fragments" object
            // alongside the original and deactivates the original itself. With
            // asynchronous fracturing on, that root is still empty this frame.
            yield return new WaitForSeconds(Mathf.Max(0f, forceDelay));

            fragmentRoot = FindFragmentRoot(runtimeFracture);
        }

        if (intactStructure != null) intactStructure.SetActive(false);

        if (fragmentRoot == null)
        {
            Debug.LogWarning("StructureDemolition: no fragments to push — assign " +
                             "either a Fracture component or a prefractured root.", this);
            yield break;
        }

        int layer = string.IsNullOrEmpty(fragmentLayer) ? -1 : LayerMask.NameToLayer(fragmentLayer);
        if (!string.IsNullOrEmpty(fragmentLayer) && layer < 0)
            Debug.LogWarning($"StructureDemolition: no layer named '{fragmentLayer}'.", this);

        foreach (var body in fragmentRoot.GetComponentsInChildren<Rigidbody>())
        {
            if (body == null) continue;

            // Prefractured fragments ship frozen by constraints, waiting on an
            // UnfreezeFragment trigger that will never come out here.
            body.constraints = RigidbodyConstraints.None;
            body.isKinematic = false;

            // Trimmed before the push, not after: these all apply to how the
            // first simulated frame resolves, and that is the frame that costs.
            if (layer >= 0) body.gameObject.layer = layer;
            body.solverIterations = Mathf.Max(1, solverIterations);
            body.solverVelocityIterations = Mathf.Max(1, solverVelocityIterations);
            if (maxDepenetrationVelocity > 0f)
                body.maxDepenetrationVelocity = maxDepenetrationVelocity;

            body.AddExplosionForce(explosionForce, blastOrigin, explosionRadius, upwardsModifier);

            if (torque > 0f)
                body.AddTorque(Random.insideUnitSphere * torque, ForceMode.Impulse);
        }

        if (fragmentLifetime > 0f)
            Destroy(fragmentRoot.gameObject, fragmentLifetime);
    }

    private Transform FindFragmentRoot(Fracture fracture)
    {
        string wanted = fracture.name + "Fragments";

        Transform parent = fracture.transform.parent;
        if (parent != null) return parent.Find(wanted);

        GameObject found = GameObject.Find(wanted);
        return found != null ? found.transform : null;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 at = intactStructure != null ? intactStructure.transform.position : transform.position;
        CutsceneGizmos.Ring(at, explosionRadius, CutsceneGizmos.Shot5 * 0.8f,
                            "blast radius " + CutsceneGizmos.Metres(explosionRadius));
    }
}
