// SPDX-License-Identifier: MIT
using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace GaussianSplatting.Runtime
{
    // Both asset formats are Morton ordered. Compressed assets include bounds;
    // FP32 assets can derive equivalent visibility-only bounds without changing
    // their positions, shading, scales, or the GPU's data decoding format.
    // Keep one depth sort across all visible blocks, shared by both eyes.
    sealed class VisibleSplatChunks : IDisposable
    {
        readonly Bounds[] bounds;
        readonly float[] radius;
        readonly int[] ids;
        readonly Plane[] left = new Plane[6], right = new Plane[6];
        readonly GraphicsBuffer buffer;
        int blockCount;
        public bool Compact {get; private set;}
        public int Count {get; private set;}
        public bool KeysChanged {get; private set;}

        public VisibleSplatChunks(GaussianSplatAsset asset, bool enabled)
        {
            Count=asset.splatCount;
            long positionBytes=(long)asset.splatCount*12;
            bool raw=enabled && !asset.chunkData &&
                asset.posFormat==GaussianSplatAsset.VectorFormat.Float32 &&
                asset.scaleFormat==GaussianSplatAsset.VectorFormat.Float32 &&
                asset.posData && asset.otherData &&
                (asset.posData.dataSize==positionBytes || asset.posData.dataSize==((positionBytes+7)&~7L)) &&
                asset.otherData.dataSize==(long)asset.splatCount*16;
            int n=enabled && asset.chunkData ? (int)(asset.chunkData.dataSize / 64) :
                raw ? (asset.splatCount+GaussianSplatAsset.kChunkSize-1)/GaussianSplatAsset.kChunkSize : 0;
            ids=new int[n];
            bounds=new Bounds[n];
            radius=new float[n];
            buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Math.Max(1,n),sizeof(int));
            if (n==0) return;
            if(raw)
            {
                // The importer pads the buffer to eight bytes. An odd count of
                // 12-byte positions therefore cannot be viewed as float3[].
                var positions=asset.posData.GetData<float>();
                // The first four bytes are the packed quaternion. The remaining
                // three floats are linear Gaussian scales (not logarithms/roots).
                var other=asset.otherData.GetData<float4>();
                for(int i=0;i<n;i++)
                {
                    float3 minimum=new float3(float.PositiveInfinity);
                    float3 maximum=new float3(float.NegativeInfinity);
                    float maximumScale=0;
                    int end=Math.Min(asset.splatCount,(i+1)*GaussianSplatAsset.kChunkSize);
                    for(int j=i*GaussianSplatAsset.kChunkSize;j<end;j++)
                    {
                        int offset=j*3;
                        var position=new float3(positions[offset],positions[offset+1],positions[offset+2]);
                        minimum=math.min(minimum,position);
                        maximum=math.max(maximum,position);
                        maximumScale=math.max(maximumScale,math.cmax(math.abs(other[j].yzw)));
                    }
                    bounds[i].SetMinMax(new Vector3(minimum.x,minimum.y,minimum.z),new Vector3(maximum.x,maximum.y,maximum.z));
                    radius[i]=4.5f*maximumScale+0.00001f;
                    // Malformed bounds must stay visible rather than silently
                    // remove geometry. Normal, finite assets take the fast path.
                    if(!math.all(math.isfinite(minimum)) || !math.all(math.isfinite(maximum)) || !float.IsFinite(radius[i]))
                        radius[i]=float.PositiveInfinity;
                }
                return;
            }
            var info=asset.chunkData.GetData<GaussianSplatAsset.ChunkInfo>();
            for(int i=0;i<n;i++)
            {
                var c=info[i];
                bounds[i].SetMinMax(new Vector3(c.posX.x,c.posY.x,c.posZ.x),new Vector3(c.posX.y,c.posY.y,c.posZ.y));
                float maximum=Mathf.Max(math.f16tof32(c.sclX>>16),Mathf.Max(math.f16tof32(c.sclY>>16),math.f16tof32(c.sclZ>>16)));
                // Stored scales are eighth roots. Cover more than the rendered
                // Gaussian footprint and allow for half-float quantization.
                radius[i]=4.5f*Mathf.Pow(maximum,8)+0.00001f;
            }
        }

        public void Prepare(Camera cam, Matrix4x4 localToWorld, int total, float splatScale, bool enabled, int[] roomMasks, int roomIndex, CommandBuffer cmd, bool[] movingChunks)
        {
            bool use=enabled && ids.Length>0;
            KeysChanged=use!=Compact;
            Compact=use;
            Count=total;
            if(!use) return;
            bool stereo=cam.stereoEnabled;
            if(stereo)
            {
                GeometryUtility.CalculateFrustumPlanes(cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left)*cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left)*localToWorld,left);
                GeometryUtility.CalculateFrustumPlanes(cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right)*cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right)*localToWorld,right);
            }
            else GeometryUtility.CalculateFrustumPlanes(cam.cullingMatrix*localToWorld,left);
            int n=0;
            Count=0;
            for(int i=0;i<ids.Length;i++)
            {
                if(roomMasks!=null && (roomMasks[i] & (1<<roomIndex))==0) continue;
                Bounds b=bounds[i];
                b.Expand(2*radius[i]*splatScale);
                if((movingChunks==null || !movingChunks[i]) && float.IsFinite(radius[i]) && !GeometryUtility.TestPlanesAABB(left,b) && !(stereo && GeometryUtility.TestPlanesAABB(right,b))) continue;
                KeysChanged|=ids[n]!=i;
                ids[n++]=i;
                Count+=Math.Min(GaussianSplatAsset.kChunkSize,total-i*GaussianSplatAsset.kChunkSize);
            }
            KeysChanged|=n!=blockCount;
            blockCount=n;
            if(KeysChanged && n>0) cmd.SetBufferData(buffer,ids,0,0,n);
        }

        public void Bind(CommandBuffer cmd, ComputeShader shader, int kernel)
        {
            cmd.SetComputeIntParam(shader,"_UseVisibleChunks",Compact?1:0);
            cmd.SetComputeIntParam(shader,"_RebuildSortKeys",KeysChanged?1:0);
            cmd.SetComputeIntParam(shader,"_VisibleSplatCount",Count);
            cmd.SetComputeBufferParam(shader,kernel,"_VisibleChunks",buffer);
        }
        public void Dispose(){buffer.Dispose();}
    }
}
