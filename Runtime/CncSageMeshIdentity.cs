using UnityEngine;

namespace MusafirEx.CncSage
{
    /// <summary>Preserves the original W3X mesh identifier for future exporters.</summary>
    [DisallowMultipleComponent]
    public sealed class CncSageMeshIdentity : MonoBehaviour
    {
        [SerializeField] private string sageIdentifier;

        public string SageIdentifier => sageIdentifier;

        public void SetSageIdentifier(string value)
        {
            sageIdentifier = value;
        }
    }
}
