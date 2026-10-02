using UnityEngine;

namespace Arena
{
    /// <summary>
    /// Shows a serialized field in the Inspector but greys it out. Used for live runtime state
    /// (current cell, bound device, step progress) that the code owns and designers only watch.
    /// Drawer: Assets/Scripts/Editor/InspectorReadOnlyDrawer.cs
    /// </summary>
    public sealed class InspectorReadOnlyAttribute : PropertyAttribute { }
}
