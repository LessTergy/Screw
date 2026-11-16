using System;
using System.Collections.Generic;
using UnityEngine;

namespace Screw.Levels
{
    internal sealed class PartPreview
    {
        private readonly List<(MeshRenderer Renderer, Material[] Materials)> _originalMaterials
            = new List<(MeshRenderer Renderer, Material[] Materials)>();

        public void Show(Transform root, Material material)
        {
            if (material == null)
            {
                throw new InvalidOperationException("Assign the part preview material in OrbitCamera.");
            }

            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                var original = renderer.sharedMaterials;
                var preview = new Material[original.Length];
                for (var index = 0; index < original.Length; index++)
                {
                    if (original[index] != null)
                    {
                        preview[index] = material;
                    }
                }

                _originalMaterials.Add((renderer, original));
                renderer.sharedMaterials = preview;
            }
        }

        public void Clear()
        {
            foreach (var (renderer, materials) in _originalMaterials)
            {
                if (renderer != null)
                {
                    renderer.sharedMaterials = materials;
                }
            }

            _originalMaterials.Clear();
        }
    }
}
