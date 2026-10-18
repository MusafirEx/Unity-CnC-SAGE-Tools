using UnityEngine;

namespace MusafirEx.CncSage
{
    /// <summary>
    /// Optional authoring data for a newly added W3X static mesh.
    /// Selects an existing source FXShader and explicitly maps textures.
    /// No Unity shader graph or texture data is embedded in W3X.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("C&C SAGE/Material Identity")]
    public sealed class CncSageMaterialIdentity : MonoBehaviour
    {
        [Tooltip("Existing W3DMesh id to copy the SAGE FXShader from, e.g. GPPITBULL.NEWSKIN. Empty uses the legacy first source shader.")]
        [SerializeField] private string shaderTemplateMeshId = "";

        [Tooltip("If an override is empty, use the assigned MeshRenderer material's texture.")]
        [SerializeField] private bool useRendererTextures = true;

        [Tooltip("Optional diffuse texture in Assets, ideally beside exported W3X.")]
        [SerializeField] private Texture2D diffuseTexture;

        [SerializeField] private Texture2D normalTexture;
        [SerializeField] private Texture2D specularTexture;

        [Header("Optional texture names (match files beside exported W3X)")]
        [SerializeField] private string diffuseTextureName = "";
        [SerializeField] private string normalTextureName = "";
        [SerializeField] private string specularTextureName = "";

        public string ShaderTemplateMeshId => shaderTemplateMeshId;
        public bool UseRendererTextures => useRendererTextures;
        public Texture2D DiffuseTexture => diffuseTexture;
        public Texture2D NormalTexture => normalTexture;
        public Texture2D SpecularTexture => specularTexture;
        public string DiffuseTextureName => diffuseTextureName;
        public string NormalTextureName => normalTextureName;
        public string SpecularTextureName => specularTextureName;

        public void SetSource(string templateMeshId,
            Texture2D diffuse = null, Texture2D normal = null,
            Texture2D specular = null, bool useAssignedMaterial = true,
            string diffuseName = "", string normalName = "",
            string specularName = "")
        {
            shaderTemplateMeshId = templateMeshId ?? "";
            diffuseTexture = diffuse;
            normalTexture = normal;
            specularTexture = specular;
            useRendererTextures = useAssignedMaterial;
            diffuseTextureName = diffuseName ?? "";
            normalTextureName = normalName ?? "";
            specularTextureName = specularName ?? "";
        }
    }
}
