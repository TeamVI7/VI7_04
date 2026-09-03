using UnityEngine;

/// <summary>
/// A stable per-object identity for the save system.
///
/// The problem this solves: to remember "this crate was already looted", a save has
/// to name the crate in a way that still means the same crate next session. Object
/// name is not unique, sibling index shifts the moment the level is re-arranged, and
/// InstanceID is regenerated on every load.
///
/// The id is authored once, in the editor, and then never changes:
///  - <see cref="Generate"/> stamps a GUID into the serialized field, which survives
///    being renamed, moved in the hierarchy, and duplicated-then-edited.
///  - If the field was never stamped, <see cref="Resolve"/> falls back to the object's
///    full hierarchy path. That keeps existing scene objects working without a manual
///    pass over every one of them, at the cost of breaking if that object is later
///    renamed or reparented.
///
/// Duplicating a GameObject in the editor copies its id too, which would make two
/// objects share one save entry. <see cref="OnValidate"/> cannot reliably detect that
/// on its own, so <see cref="SaveIdAuditor"/> reports collisions instead.
/// </summary>
[DisallowMultipleComponent]
public class SaveableId : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Stable identity used by the save system. Generated automatically — do not " +
             "edit by hand, and do not clear it on an object that has already shipped in a " +
             "save, or that object's saved state becomes unreachable.")]
    private string id = "";

    public string Id => string.IsNullOrEmpty(id) ? HierarchyPath(transform) : id;

    private void Reset() => Generate();

    private void OnValidate()
    {
        if (string.IsNullOrEmpty(id)) Generate();
    }

    private void Generate()
    {
        id = System.Guid.NewGuid().ToString("N");
    }

    /// <summary>The save id for any object: its <see cref="SaveableId"/> if it has one,
    /// otherwise its hierarchy path.</summary>
    public static string Resolve(Component component)
    {
        if (component == null) return "";

        SaveableId marker = component.GetComponent<SaveableId>();
        return marker != null ? marker.Id : HierarchyPath(component.transform);
    }

    /// <summary>"Level/Props/Crate_04" — unique within a scene as long as no two
    /// siblings share a name.</summary>
    public static string HierarchyPath(Transform t)
    {
        if (t == null) return "";

        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent)
            path = p.name + "/" + path;

        return path;
    }
}
