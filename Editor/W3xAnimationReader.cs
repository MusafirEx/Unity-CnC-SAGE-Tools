using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    internal sealed class W3xAnimationData
    {
        public string Identifier;
        public string HierarchyName;
        public int NumFrames;
        public float FrameRate;
        public readonly List<W3xAnimationChannel> Channels =
            new List<W3xAnimationChannel>();
    }

    internal sealed class W3xAnimationChannel
    {
        public int PivotIndex;
        public int FirstFrame;
        public string Kind;
        public float[] Scalars;
        public Quaternion[] Quaternions;
    }

    /// <summary>
    /// Conservative W3DAnimation XML channel reader. Never silently
    /// guesses undocumented compression, Euler or visibility formats.
    /// Invalid/unsupported channels are logged and skipped independently.
    /// </summary>
    internal static class W3xAnimationReader
    {
        private const int MaxFrames = 10000;
        private const int MaxChannels = 2048;
        internal static W3xAnimationData Read(
            XElement node, IList<string> warnings)
        {
            string id = (string)node.Attribute("id");
            string hierarchy = (string)node.Attribute("Hierarchy");
            int frames;
            float rate;
            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(hierarchy) ||
                !int.TryParse((string)node.Attribute("NumFrames"),
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out frames) ||
                !float.TryParse((string)node.Attribute("FrameRate"),
                    NumberStyles.Float, CultureInfo.InvariantCulture,
                    out rate) ||
                frames < 1 || frames > MaxFrames ||
                rate <= 0 || rate > 240 || float.IsNaN(rate) ||
                float.IsInfinity(rate))
            {
                warnings.Add("Phase 3A: W3DAnimation has missing or invalid " +
                    "id/Hierarchy/NumFrames/FrameRate; metadata retained.");
                return null;
            }

            var data = new W3xAnimationData
            {
                Identifier = id.Trim(),
                HierarchyName = hierarchy.Trim(),
                NumFrames = frames,
                FrameRate = rate
            };
            var container = node.Elements().FirstOrDefault(e =>
                e.Name.LocalName == "Channels");
            if (container == null)
            {
                warnings.Add("Phase 3A: " + id +
                    " has no Channels; metadata retained.");
                return data;
            }

            var source = container.Elements().ToArray();
            if (source.Length > MaxChannels)
            {
                warnings.Add("Phase 3A: " + id +
                    " exceeds the channel safety limit; clips skipped.");
                return data;
            }
            foreach (var child in source)
            {
                try
                {
                    var channel = ReadChannel(child, data.NumFrames);
                    if (channel == null)
                    {
                        warnings.Add("Phase 3A: " + id +
                            " unsupported channel " + child.Name.LocalName +
                            " Type=" + ((string)child.Attribute("Type") ?? "") +
                            "; preserved in W3X source.");
                        continue;
                    }
                    data.Channels.Add(channel);
                }
                catch (InvalidDataException ex)
                {
                    warnings.Add("Phase 3A: " + id + ": " + ex.Message +
                        "; channel skipped.");
                }
            }
            return data;
        }

        private static W3xAnimationChannel ReadChannel(
            XElement node, int numFrames)
        {
            string tag = node.Name.LocalName;
            string type = ((string)node.Attribute("Type") ?? "").Trim();
            bool scalar = tag == "ChannelScalar";
            bool quaternion = tag == "ChannelQuaternion" ||
                tag == "ChannelQuat";
            if (scalar && type != "XTranslation" &&
                type != "YTranslation" && type != "ZTranslation")
                return null;
            if (quaternion && type != "" &&
                type != "Quaternion" && type != "Rotation")
                return null;
            if (!scalar && !quaternion)
                return null;

            int pivot, first;
            if (!int.TryParse((string)node.Attribute("Pivot"),
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out pivot) || pivot < 0 ||
                !int.TryParse((string)node.Attribute("FirstFrame") ?? "0",
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out first) || first < 0)
                throw new InvalidDataException(
                    "Channel has invalid Pivot/FirstFrame.");

            var frameNodes = node.Elements()
                .Where(e => e.Name.LocalName == "Frame").ToArray();
            if (frameNodes.Length == 0 ||
                (long)first + frameNodes.Length > numFrames)
                throw new InvalidDataException(
                    "Channel keyframes are empty or exceed NumFrames.");

            var result = new W3xAnimationChannel
            {
                PivotIndex = pivot,
                FirstFrame = first,
                Kind = scalar ? type : "Quaternion"
            };
            if (scalar)
            {
                result.Scalars = new float[frameNodes.Length];
                for (int i = 0; i < frameNodes.Length; i++)
                    result.Scalars[i] = Scalar(frameNodes[i].Value);
            }
            else
            {
                result.Quaternions = new Quaternion[frameNodes.Length];
                for (int i = 0; i < frameNodes.Length; i++)
                {
                    var f = frameNodes[i];
                    var q = new Quaternion(
                        Scalar((string)f.Attribute("X")),
                        Scalar((string)f.Attribute("Y")),
                        Scalar((string)f.Attribute("Z")),
                        Scalar((string)f.Attribute("W")));
                    if (q.sqrMagnitude <= 0.000001f)
                        throw new InvalidDataException(
                            "Zero-length quaternion frame.");
                    result.Quaternions[i] = q.normalized;
                }
            }
            return result;
        }

        private static float Scalar(string value)
        {
            float v;
            if (!float.TryParse(value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out v) ||
                float.IsNaN(v) || float.IsInfinity(v))
                throw new InvalidDataException(
                    "Channel contains an invalid scalar value.");
            return v;
        }
    }
}
