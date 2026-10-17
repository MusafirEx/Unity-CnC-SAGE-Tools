using UnityEngine;

namespace MusafirEx.CncSage
{
    /// <summary>
    /// Links an editable Project prefab to its original, imported W3X source.
    /// Unity Editor resolves source GUID to its current path during export.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CncSageEditableSource : MonoBehaviour
    {
        [SerializeField] private string sourceGuid;
        [SerializeField] private string originalW3xPath;

        public string SourceGuid => sourceGuid;
        public string OriginalW3xPath => originalW3xPath;

        public void SetSource(string guid, string path)
        {
            sourceGuid = guid ?? "";
            originalW3xPath = path ?? "";
        }
    }
}
