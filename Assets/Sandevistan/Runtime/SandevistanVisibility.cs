using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sandevistan
{
    // Build metadata only. Consumed and removed by the NDMF pass before upload.
    [AddComponentMenu("")]
    public sealed class SandevistanVisibility : MonoBehaviour, VRC.SDKBase.IEditorOnly
    {
        [Serializable] public sealed class Binding { public Renderer source; public SkinnedMeshRenderer proxy; public bool preserveMesh; public Material sourceMaterial; }
        [Serializable] public sealed class InstallationSettings
        {
            public int version=1;
            public int targetTriangles=8000,capacity=3;
            public float maxSpacing=0.5f,runThreshold=3.5f;
            public bool detailed,originalMesh;
            public Renderer[] sources=Array.Empty<Renderer>();
        }
        public InstallationSettings installation;
        public List<Binding> bindings = new List<Binding>();
    }
}
