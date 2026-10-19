using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Phase 3B Editor-only provenance for an editable .anim clone.
    /// Does NOT imply an animation W3X exporter exists yet.
    /// The source W3X GUID and original binding signatures are retained
    /// for future Phase 3C changed-channel analysis and safe validation.
    /// </summary>
    internal sealed class W3xEditableAnimationSource : ScriptableObject
    {
        [SerializeField] private string sourceW3xGuid;
        [SerializeField] private string sourceW3xPathAtCreation;
        [SerializeField] private string originalAnimationId;
        [SerializeField] private string originalHierarchyId;
        [SerializeField] private int originalFrameCount;
        [SerializeField] private float originalFrameRate;
        [SerializeField] private string modelAssetGuid;
        [SerializeField] private AnimationClip editableClip;
        [SerializeField] private string[] sourceFloatBindings;
        [SerializeField] private string[] sourceObjectBindings;

        internal string SourceW3xGuid => sourceW3xGuid;
        internal string OriginalAnimationId => originalAnimationId;
        internal string OriginalHierarchyId => originalHierarchyId;
        internal int OriginalFrameCount => originalFrameCount;
        internal float OriginalFrameRate => originalFrameRate;
        internal string ModelAssetGuid => modelAssetGuid;
        internal AnimationClip EditableClip => editableClip;
        internal string[] SourceFloatBindings => sourceFloatBindings;
        internal string[] SourceObjectBindings => sourceObjectBindings;

        internal void SetSource(
            string w3xGuid, string originalPath, string animationId,
            string hierarchyId, int frames, float frameRate,
            string targetGuid, AnimationClip clip,
            string[] floatBindings, string[] objectBindings)
        {
            sourceW3xGuid = w3xGuid ?? "";
            sourceW3xPathAtCreation = originalPath ?? "";
            originalAnimationId = animationId ?? "";
            originalHierarchyId = hierarchyId ?? "";
            originalFrameCount = frames;
            originalFrameRate = frameRate;
            modelAssetGuid = targetGuid ?? "";
            editableClip = clip;
            sourceFloatBindings = floatBindings ?? new string[0];
            sourceObjectBindings = objectBindings ?? new string[0];
        }
    }
}
