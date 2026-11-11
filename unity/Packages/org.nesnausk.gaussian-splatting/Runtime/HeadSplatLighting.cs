using UnityEngine;
using UnityEngine.Rendering;

namespace GaussianSplatting.Runtime
{
    public partial class GaussianSplatRenderer
    {
        // Runtime-only: these values belong to the viewer, never to the capture asset.
        [System.NonSerialized] public bool headLightEnabled;
        [System.NonSerialized] public Vector3 headLightCenter, headLightRadii;
        [System.NonSerialized] public Vector4 headLightDirection;
        [System.NonSerialized] public Vector4 headLightColour;

        void BindHeadLighting(CommandBuffer cmd)
        {
            var cs=m_CSSplatUtilities;
            // Bind for every renderer so a head cannot leave its lighting on the house.
            cmd.SetComputeIntParam(cs,"_HeadLightEnabled",headLightEnabled?1:0);
            cmd.SetComputeVectorParam(cs,"_HeadLightCenter",headLightCenter);
            cmd.SetComputeVectorParam(cs,"_HeadLightRadii",headLightRadii);
            cmd.SetComputeVectorParam(cs,"_HeadLightDirection",headLightDirection);
            cmd.SetComputeVectorParam(cs,"_HeadLightColour",headLightColour);
        }
    }
}
