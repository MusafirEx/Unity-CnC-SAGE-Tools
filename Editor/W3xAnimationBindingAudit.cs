using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Phase 3A read-only audit of actual imported clip bindings and the
    /// SAGE pivot 0 root. This intentionally does not synthesize curves or
    /// modify source W3X, prefab assets, scenes, or animation clips.
    /// </summary>
    internal static class W3xAnimationBindingAudit
    {
        private const string Menu =
            "Tools/C&C SAGE/Audit Selected Animation Pivot Bindings";
        private const int MaxClipSources = 32;
        private const int MaxClips = 120;
        private const int MaxMissingPaths = 12;

        [MenuItem(Menu)]
        private static void Run()
        {
            try
            {
                GameObject target = null;
                var clips = new List<AnimationClip>();
                var seen = new HashSet<AnimationClip>();
                var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var selected in Selection.objects)
                {
                    if (selected == null)
                        continue;
                    var candidate = selected as GameObject;
                    if (candidate != null &&
                        candidate.GetComponentInChildren<CncSagePivotInfo>(true)
                            != null)
                    {
                        string candidatePath = AssetDatabase.GetAssetPath(candidate);
                        // Prefer the explicitly selected editable prefab
                        // over a source W3X root if both are selected.
                        if (target == null ||
                            string.Equals(Path.GetExtension(candidatePath),
                                ".prefab", StringComparison.OrdinalIgnoreCase))
                            target = candidate;
                    }
                    var clip = selected as AnimationClip;
                    if (clip != null && seen.Add(clip))
                        clips.Add(clip);

                    string path = AssetDatabase.GetAssetPath(selected);
                    if (!string.IsNullOrEmpty(path) &&
                        string.Equals(Path.GetExtension(path), ".w3x",
                            StringComparison.OrdinalIgnoreCase))
                        sources.Add(path);
                }

                // Importer-created AnimationClips are sub-assets of the W3X.
                // Accept either the W3X asset or its AnimationClip sub-asset.
                foreach (var source in sources.Take(MaxClipSources))
                    foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(source)
                        .OfType<AnimationClip>())
                        if (seen.Add(clip))
                            clips.Add(clip);

                var log = new StringBuilder();
                log.AppendLine("[C&C SAGE Phase 3A] Pivot Binding Audit");
                log.AppendLine("AnimationClips selected/found: " +
                    clips.Count);
                log.AppendLine("Source W3X assets selected: " +
                    sources.Count);
                log.AppendLine("Target model: " +
                    (target == null ? "(none selected)" : target.name));
                log.AppendLine("NOTE: SAGE_Pivots is an artificial Unity " +
                    "container. Pivot 0 (usually ROOTTRANSFORM) is the " +
                    "actual animatable SAGE root; it should be targeted " +
                    "through a path like SAGE_Pivots/ROOTTRANSFORM.");

                if (clips.Count == 0)
                {
                    log.AppendLine("No AnimationClips selected. Select " +
                        "the imported W3X AnimationClip sub-assets, or " +
                        "their parent W3X source assets, then rerun.");
                    Debug.LogWarning(log.ToString());
                    return;
                }

                Transform targetRoot = target == null ? null : target.transform;
                var pivotZero = target == null ? null :
                    target.GetComponentsInChildren<CncSagePivotInfo>(true)
                        .FirstOrDefault(p => p.PivotIndex == 0);
                string targetRootPath = pivotZero == null ? "(none)" :
                    AnimationUtility.CalculateTransformPath(
                        pivotZero.transform, targetRoot);
                log.AppendLine("Target SAGE pivot 0: " + targetRootPath);

                foreach (var clip in clips.Take(MaxClips))
                {
                    var bindings = AnimationUtility.GetCurveBindings(clip);
                    string clipSource = AssetDatabase.GetAssetPath(clip);
                    var trackPaths = bindings.Where(b =>
                        b.type == typeof(Transform))
                        .Select(b => b.path).Distinct().ToArray();

                    log.AppendLine("CLIP " + clip.name +
                        " | source=" + clipSource +
                        " | fps=" + clip.frameRate +
                        " | length=" + clip.length +
                        " | curves=" + bindings.Length +
                        " | animatedPaths=" + trackPaths.Length);

                    int rootCount = 0;
                    int holderCount = 0;
                    foreach (var binding in bindings)
                    {
                        if (binding.type != typeof(Transform))
                            continue;
                        if (binding.path == "SAGE_Pivots")
                            holderCount++;
                        if (binding.path.EndsWith("/ROOTTRANSFORM",
                                StringComparison.OrdinalIgnoreCase) ||
                            (targetRootPath != "(none)" &&
                             binding.path == targetRootPath))
                            rootCount++;
                    }
                    log.AppendLine("  ROOTTRANSFORM curves=" + rootCount +
                        ", SAGE_Pivots holder curves=" + holderCount);
                    if (holderCount > 0)
                        log.AppendLine("  NOTE: Clip animates the generated " +
                            "SAGE_Pivots holder. Verify this is intended.");

                    string sourceRootName, sourceRootPath, rawChannelSummary;
                    string originalSourcePath, originalAnimationId;
                    ResolveOriginalW3x(clip,
                        out originalSourcePath, out originalAnimationId);
                    DescribeOriginalPivotZero(
                        originalSourcePath, originalAnimationId,
                        out sourceRootName, out sourceRootPath,
                        out rawChannelSummary);
                    log.AppendLine("  XML pivot0=" + sourceRootName +
                        " | importedPath=" + sourceRootPath +
                        " | " + rawChannelSummary);
                    // Report whether BODY/B_TURRET received genuine source
                    // translation channels or synthesized position curves.
                    DescribeFocusPivotMotion(clip, bindings, log,
                        originalSourcePath, originalAnimationId);

                    int sourceRootCurves = bindings.Count(b =>
                        b.type == typeof(Transform) &&
                        b.path == sourceRootPath &&
                        sourceRootPath != "(unknown)");
                    if (rawChannelSummary.StartsWith("pivot0 raw channels=") &&
                        !rawChannelSummary.StartsWith(
                            "pivot0 raw channels=0 ") &&
                        sourceRootCurves == 0)
                        log.AppendLine("  WARNING: Source animation has " +
                            "pivot 0 channels, but the Unity clip has " +
                            "no curves on its imported pivot 0 path.");
                    else if (sourceRootCurves == 0)
                        log.AppendLine("  Pivot 0 is not animated by this " +
                            "clip, or its source channel format is unsupported.");

                    if (targetRoot != null)
                    {
                        int missing = CountMissingTransformPaths(
                            clip, targetRoot);
                        log.AppendLine("  Against " + target.name +
                            ": missing Transform bindings=" + missing +
                            " / " + bindings.Length + " curves");
                        foreach (var path in trackPaths.Where(path =>
                            !ExistsAtPath(targetRoot, path))
                            .Take(MaxMissingPaths))
                            log.AppendLine("    UNRESOLVED " + path);
                        if (sourceRootCurves > 0 && targetRootPath !=
                            "(none)" && sourceRootPath != targetRootPath)
                            log.AppendLine("  ROOT PATH MISMATCH: Source clip " +
                                "targets '" + sourceRootPath +
                                "', selected prefab has pivot 0 at '" +
                                targetRootPath + "'. The pivot0 animation " +
                                "will not bind without compatible paths.");
                        if (pivotZero == null)
                            log.AppendLine("  TARGET WARNING: No " +
                                "CncSagePivotInfo PivotIndex 0 on prefab.");
                    }
                    else
                        log.AppendLine("  Select GPPitbull_Editable.prefab " +
                            "together with these clips to check playback " +
                            "paths against the model.");
                    foreach (var path in trackPaths.Take(20))
                        log.AppendLine("    CURVE PATH " + path);
                }
                if (clips.Count > MaxClips || sources.Count > MaxClipSources)
                    log.AppendLine("WARNING: Analysis truncated for safety.");
                Debug.Log(log.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    "[C&C SAGE Phase 3A] Pivot binding audit failed: " + ex);
            }
        }

        internal static int CountMissingTransformPaths(
            AnimationClip clip, Transform modelRoot)
        {
            if (clip == null || modelRoot == null)
                throw new ArgumentNullException(
                    clip == null ? "clip" : "modelRoot");
            return AnimationUtility.GetCurveBindings(clip)
                .Where(b => b.type == typeof(Transform) &&
                    !ExistsAtPath(modelRoot, b.path))
                .Count();
        }

        internal static bool ExistsAtPath(Transform root, string path)
        {
            return root != null &&
                (string.IsNullOrEmpty(path) || root.Find(path) != null);
        }

        private static void DescribeFocusPivotMotion(AnimationClip clip,
            EditorCurveBinding[] bindings, StringBuilder log,
            string sourcePath, string sourceAnimationId)
        {
            var selectedPath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(sourcePath))
            {
                log.AppendLine("  Source axes unavailable: select the " +
                    "original W3X sub-asset or keep a same-named W3X " +
                    "beside any extracted .anim copy.");
                return;
            }

            try
            {
                XDocument original;
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };
                using (var reader = XmlReader.Create(sourcePath, settings))
                    original = XDocument.Load(reader);

                var node = original.Root == null ? null :
                    original.Root.Elements().FirstOrDefault(e =>
                        e.Name.LocalName == "W3DAnimation" &&
                        string.Equals((string)e.Attribute("id"),
                            sourceAnimationId, StringComparison.OrdinalIgnoreCase));
                if (node == null)
                {
                    log.AppendLine("  Source axes: no matching animation " +
                        sourceAnimationId + " in " + sourcePath);
                    return;
                }

                var doc = W3xReader.Read(sourcePath);
                var hierarchy = doc.Hierarchies.FirstOrDefault(h =>
                    string.Equals(h.Identifier,
                        (string)node.Attribute("Hierarchy"),
                        StringComparison.OrdinalIgnoreCase));
                if (hierarchy == null)
                {
                    log.AppendLine("  Source axes: matching hierarchy " +
                        "is unavailable.");
                    return;
                }
                var channelsRoot = node.Elements().FirstOrDefault(e =>
                    e.Name.LocalName == "Channels");
                var sourceChannels = channelsRoot == null ?
                    new XElement[0] : channelsRoot.Elements().ToArray();

                if (!string.Equals(sourcePath, selectedPath,
                    StringComparison.OrdinalIgnoreCase))
                    log.AppendLine("  NOTE: Selected .anim is an " +
                        "independent clip copy; reimporting W3X will NOT " +
                        "update this .anim automatically. XML comparison " +
                        "uses sibling source " + sourcePath);

                foreach (var item in hierarchy.Pivots
                    .Select((pivot, index) => new { pivot, index })
                    .Where(x =>
                        string.Equals(x.pivot.Name, "BODY",
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.pivot.Name, "B_TURRET",
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.pivot.Name, "ROOTTRANSFORM",
                            StringComparison.OrdinalIgnoreCase))
                    .Take(8))
                {
                    var parts = new List<string>();
                    var seen = new HashSet<int>();
                    int pivotIndex = item.index;
                    while (pivotIndex >= 0 &&
                        pivotIndex < hierarchy.Pivots.Count &&
                        seen.Add(pivotIndex))
                    {
                        var p = hierarchy.Pivots[pivotIndex];
                        parts.Add(p.Name);
                        pivotIndex = p.Parent;
                    }
                    parts.Reverse();
                    string path = "SAGE_Pivots/" +
                        string.Join("/", parts.ToArray());

                    string indexString = item.index.ToString();
                    var source = sourceChannels.Where(c =>
                        (string)c.Attribute("Pivot") == indexString).ToArray();
                    bool sx = SourceHasAxis(source, "XTranslation");
                    bool sy = SourceHasAxis(source, "YTranslation");
                    bool sz = SourceHasAxis(source, "ZTranslation");
                    bool cx = HasCurve(bindings, path, "m_LocalPosition.x");
                    bool cy = HasCurve(bindings, path, "m_LocalPosition.y");
                    bool cz = HasCurve(bindings, path, "m_LocalPosition.z");
                    bool rotation = bindings.Any(b =>
                        b.type == typeof(Transform) &&
                        b.path == path &&
                        b.propertyName.StartsWith("m_LocalRotation.",
                            StringComparison.Ordinal));

                    log.AppendLine("  FOCUS " + item.pivot.Name +
                        " pivot=" + item.index +
                        " | XML source translation X=" + sx +
                        " Y=" + sy + " Z=" + sz +
                        " (" + source.Length + " total channel nodes)" +
                        " | clip position X=" + cx + " Y=" + cy +
                        " Z=" + cz + " | clip rotation=" + rotation);
                    foreach (var sourceAxis in source.Where(c =>
                        c.Name.LocalName == "ChannelScalar" &&
                        ((string)c.Attribute("Type") == "XTranslation" ||
                         (string)c.Attribute("Type") == "YTranslation" ||
                         (string)c.Attribute("Type") == "ZTranslation")))
                    {
                        string type = (string)sourceAxis.Attribute("Type");
                        var parsedValues = new List<float>();
                        int total = 0;
                        foreach (var frame in sourceAxis.Elements().Where(
                            e => e.Name.LocalName == "Frame"))
                        {
                            total++;
                            float value;
                            if (float.TryParse(frame.Value,
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture, out value))
                                parsedValues.Add(value);
                        }
                        if (parsedValues.Count == 0)
                        {
                            log.AppendLine("    SOURCE " + type +
                                ": no parseable numeric frames");
                            continue;
                        }
                        float minimum = parsedValues.Min();
                        float maximum = parsedValues.Max();
                        bool zeroDelta = parsedValues.Count == total &&
                            parsedValues.All(v => Mathf.Abs(v) <= 0.000001f);
                        log.AppendLine("    SOURCE " + type +
                            " frames=" + parsedValues.Count + "/" + total +
                            " range=[" +
                            minimum.ToString("G6",
                                CultureInfo.InvariantCulture) + ", " +
                            maximum.ToString("G6",
                                CultureInfo.InvariantCulture) + "]" +
                            (zeroDelta ? " ZERO-ONLY (rest-pose delta)" : ""));
                    }
                    // Unity swaps source Y/Z. An extra curve on an
                    // absent source axis is importer-generated, not authored.
                    if (cx && !sx || cy && !sz || cz && !sy)
                        log.AppendLine("    WARNING: EXTRA POSITION CURVE " +
                            "not present in W3X for " + item.pivot.Name +
                            "; update to sparse-axis importer and reimport.");
                }
            }
            catch (Exception e)
            {
                log.AppendLine("  Source axes diagnostic unavailable: " +
                    e.Message);
            }
        }

        private static void ResolveOriginalW3x(AnimationClip clip,
            out string sourcePath, out string originalAnimationId)
        {
            sourcePath = null;
            originalAnimationId = clip == null ? "" : clip.name;
            if (clip == null)
                return;
            string selectedPath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrWhiteSpace(selectedPath))
                return;
            if (string.Equals(Path.GetExtension(selectedPath), ".w3x",
                StringComparison.OrdinalIgnoreCase))
            {
                sourcePath = selectedPath;
                return;
            }
            if (!string.Equals(Path.GetExtension(selectedPath), ".anim",
                StringComparison.OrdinalIgnoreCase))
                return;

            string folder = Path.GetDirectoryName(selectedPath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return;

            // Phase 3B keeps an explicit GUID-based source asset link
            // alongside each independent .anim. This remains valid
            // after either the source W3X or editor workspace is moved.
            foreach (string file in Directory.GetFiles(folder))
            {
                if (!string.Equals(Path.GetExtension(file), ".asset",
                    StringComparison.OrdinalIgnoreCase))
                    continue;
                var metadata = AssetDatabase.LoadAssetAtPath<
                    W3xEditableAnimationSource>(file.Replace('\\', '/'));
                if (metadata == null || metadata.EditableClip != clip ||
                    string.IsNullOrEmpty(metadata.SourceW3xGuid))
                    continue;
                string resolved = AssetDatabase.GUIDToAssetPath(
                    metadata.SourceW3xGuid);
                if (!string.IsNullOrEmpty(resolved) &&
                    string.Equals(Path.GetExtension(resolved), ".w3x",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(AssetDatabase.AssetPathToGUID(resolved),
                        metadata.SourceW3xGuid,
                        StringComparison.OrdinalIgnoreCase))
                {
                    sourcePath = resolved;
                    originalAnimationId = metadata.OriginalAnimationId;
                }
                return;
            }

            // Backward compatibility for older hand-extracted .anim
            // files. Only trust one same-named sibling .w3x file.
            string stem = Path.GetFileNameWithoutExtension(selectedPath);
            var matches = Directory.GetFiles(folder)
                .Where(p => string.Equals(Path.GetExtension(p), ".w3x",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetFileNameWithoutExtension(p),
                        stem, StringComparison.OrdinalIgnoreCase))
                .Take(2).ToArray();
            if (matches.Length == 1)
                sourcePath = matches[0];
        }

        private static bool SourceHasAxis(
            XElement[] source, string type)
        {
            return source.Any(e =>
                e.Name.LocalName == "ChannelScalar" &&
                string.Equals((string)e.Attribute("Type"), type,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasCurve(EditorCurveBinding[] bindings,
            string path, string property)
        {
            return bindings.Any(b => b.type == typeof(Transform) &&
                b.path == path && b.propertyName == property);
        }

        private static void DescribeOriginalPivotZero(
            string sourcePath, string clipName, out string name,
            out string expectedPath, out string summary)
        {
            name = "(unknown)";
            expectedPath = "(unknown)";
            summary = "Source channel XML unavailable";
            if (string.IsNullOrEmpty(sourcePath) ||
                !string.Equals(Path.GetExtension(sourcePath), ".w3x",
                    StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };
                XDocument document;
                using (var reader = XmlReader.Create(sourcePath, settings))
                    document = XDocument.Load(reader);

                var animation = document.Root == null ? null :
                    document.Root.Elements().FirstOrDefault(e =>
                        e.Name.LocalName == "W3DAnimation" &&
                        string.Equals((string)e.Attribute("id"),
                            clipName, StringComparison.OrdinalIgnoreCase));
                if (animation == null)
                {
                    summary = "No matching W3DAnimation declaration";
                    return;
                }

                var channelsNode = animation.Elements().FirstOrDefault(e =>
                    e.Name.LocalName == "Channels");
                var sourceChannels = channelsNode == null ?
                    new XElement[0] : channelsNode.Elements().ToArray();
                var rootChannels = sourceChannels.Where(ch =>
                    (string)ch.Attribute("Pivot") == "0").ToArray();
                summary = "pivot0 raw channels=" + rootChannels.Length +
                    " / " + sourceChannels.Length + " total; types=" +
                    (rootChannels.Length == 0 ? "(none)" :
                        string.Join(", ", rootChannels.Select(ch =>
                            ch.Name.LocalName + "/" +
                            ((string)ch.Attribute("Type") ?? "(none)"))
                            .Distinct().ToArray()));

                // Consult the importer's resolved companion hierarchy so
                // this also works for standalone _ATKA/_IDLA animation files.
                var decoded = W3xReader.Read(sourcePath);
                string hierarchyName = (string)animation.Attribute("Hierarchy");
                var hierarchy = decoded.Hierarchies.FirstOrDefault(h =>
                    string.Equals(h.Identifier, hierarchyName,
                        StringComparison.OrdinalIgnoreCase));
                if (hierarchy == null || hierarchy.Pivots.Count == 0)
                {
                    summary += "; matching SAGE hierarchy unavailable";
                    return;
                }

                name = hierarchy.Pivots[0].Name;
                var pivotParts = new List<string>();
                var visited = new HashSet<int>();
                int index = 0;
                while (index >= 0 && index < hierarchy.Pivots.Count &&
                    visited.Add(index))
                {
                    pivotParts.Add(hierarchy.Pivots[index].Name);
                    index = hierarchy.Pivots[index].Parent;
                }
                pivotParts.Reverse();
                expectedPath = "SAGE_Pivots/" +
                    string.Join("/", pivotParts.ToArray());
            }
            catch (Exception e)
            {
                summary += "; source read error: " + e.Message;
            }
        }
    }
}
