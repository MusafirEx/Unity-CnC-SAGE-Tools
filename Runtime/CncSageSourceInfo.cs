using UnityEngine;

namespace MusafirEx.CncSage
{
    /// <summary>
    /// Stores the top-level W3X declarations on imported source assets.
    /// Zero imported meshes means no rendered geometry. A source may still
    /// contain reconstructed pivot transforms; animation clips are not built.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CncSageSourceInfo : MonoBehaviour
    {
        [SerializeField] private string[] declarations = new string[0];
        [SerializeField] private int importedMeshCount;

        public string[] Declarations => declarations;
        public int ImportedMeshCount => importedMeshCount;
        public bool IsMetadataOnly => importedMeshCount == 0;

        public void SetSourceInfo(string[] sourceDeclarations, int meshCount)
        {
            declarations = sourceDeclarations ?? new string[0];
            importedMeshCount = meshCount;
        }
    }
}
