using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    [Serializable]
    public sealed class SageMaterialEntry
    {
        public string key;
        public string sourceName;
        public string rendererPath;
        public int slot;
        public Material generatedMaterial;
        public Texture texture;
    }

    // Persistent import-time material index; the Editor Inspector reads this,
    // while import/export logic remains independent from Inspector widgets.
    public sealed class SageMaterialManifest : ScriptableObject
    {
        public List<SageMaterialEntry> entries = new List<SageMaterialEntry>();
    }
}
