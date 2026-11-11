using UnityEngine;
using UnityEngine.Rendering;

namespace GaussianSplatting.Runtime
{
    public partial class GaussianSplatRenderer
    {
        Transform pickup;
        Matrix4x4 pickupRestInverse;
        Vector3 restPosition;
        Quaternion restRotation;
        uint[] pickupWords;
        bool[] pickupChunks;
        GraphicsBuffer pickupBuffer;
        internal bool PickupMoved => pickup && (Vector3.Distance(pickup.position,restPosition)>.0015f ||
             Quaternion.Angle(pickup.rotation,restRotation)>.45f);

        public void SetPickup(Transform target, Matrix4x4 rest, int[] moving, int[] replaced)
        {
            pickup=target; pickupRestInverse=rest.inverse;restPosition=rest.GetColumn(3);restRotation=rest.rotation;
            pickupBuffer?.Dispose();pickupBuffer=null;
            pickupWords=new uint[(m_Asset.splatCount+15)/16];
            pickupChunks=new bool[(m_Asset.splatCount+255)/256];
            foreach(int id in moving)
            {
                pickupWords[id>>4]|=1u<<((id&15)*2);
                pickupChunks[id>>8]=true;
            }
            foreach(int id in replaced) pickupWords[id>>4]|=2u<<((id&15)*2);
        }
        void BindPickup(CommandBuffer cmd,int kernel)
        {
            bool moved=PickupMoved;
            if(moved && pickupBuffer==null)
            {
                pickupBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Raw,pickupWords.Length,4);
                pickupBuffer.SetData(pickupWords);
            }
            cmd.SetComputeBufferParam(m_CSSplatUtilities,kernel,"_PickupBits",pickupBuffer??m_GpuPosData);
            cmd.SetComputeIntParam(m_CSSplatUtilities,"_PickupMoved",moved?1:0);
            if(!moved)return;
            var change=transform.worldToLocalMatrix*pickup.localToWorldMatrix*pickupRestInverse*transform.localToWorldMatrix;
            var q=Quaternion.LookRotation(change.MultiplyVector(Vector3.forward),change.MultiplyVector(Vector3.up));
            cmd.SetComputeMatrixParam(m_CSSplatUtilities,"_PickupMatrix",change);
            cmd.SetComputeVectorParam(m_CSSplatUtilities,"_PickupQuaternion",new Vector4(q.x,q.y,q.z,q.w));
        }
    }
}
