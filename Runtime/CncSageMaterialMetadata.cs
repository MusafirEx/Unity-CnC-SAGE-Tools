using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MusafirEx.CncSage
{
    /// <summary>
    /// Round-trip metadata. The runtime material is only an approximation of
    /// the original FXShader, so retain its source constant string values.
    /// </summary>
    public sealed class CncSageMaterialMetadata : ScriptableObject
    {
        [SerializeField] private string originalShaderName;
        [SerializeField] private string[] propertyNames = new string[0];
        [SerializeField] private string[] propertyValues = new string[0];

        public string OriginalShaderName => originalShaderName;
        public string[] PropertyNames => propertyNames;
        public string[] PropertyValues => propertyValues;

        public void SetSource(string name, IDictionary<string, string> properties)
        {
            originalShaderName = name;
            if (properties == null)
            {
                propertyNames = new string[0];
                propertyValues = new string[0];
                return;
            }

            var pairs = properties.OrderBy(p => p.Key,
                StringComparer.OrdinalIgnoreCase).ToArray();
            propertyNames = pairs.Select(p => p.Key).ToArray();
            propertyValues = pairs.Select(p => p.Value).ToArray();
        }
    }
}
