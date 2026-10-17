using UnityEngine;

namespace MusafirEx.CncSage
{
    /// <summary>
    /// Retains original pivot index, parent and SAGE fixup data for future export.
    /// FixupMatrix is preserved, but not applied in Phase 1B.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CncSagePivotInfo : MonoBehaviour
    {
        [SerializeField] private int nameId;
        [SerializeField] private int pivotIndex;
        [SerializeField] private int parentIndex;
        [SerializeField, TextArea(1, 3)] private string originalFixupXml;

        public int NameId => nameId;
        public int PivotIndex => pivotIndex;
        public int ParentIndex => parentIndex;
        public string OriginalFixupXml => originalFixupXml;

        public void SetSource(int originalNameId, int index, int parent,
            string fixupXml)
        {
            nameId = originalNameId;
            pivotIndex = index;
            parentIndex = parent;
            originalFixupXml = fixupXml ?? "";
        }
    }
}
