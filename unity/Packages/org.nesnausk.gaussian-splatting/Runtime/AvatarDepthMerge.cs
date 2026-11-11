using UnityEngine;
using UnityEngine.Rendering;

namespace GaussianSplatting.Runtime
{
    partial class GaussianSplatRenderSystem
    {
        GraphicsBuffer mergedIndices, actorIndices, actorDepths, unusedDepth;
        RenderItem combinedItem;
        bool hasCombinedItem;

        static void Reserve(ref GraphicsBuffer buffer, int size, string label)
        {
            if (buffer != null && buffer.count >= size) return;
            buffer?.Dispose();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size, 4) { name = label };
        }

        void ReleaseAvatarMerge()
        {
            foreach (var buffer in new[] { mergedIndices, actorIndices, actorDepths, unusedDepth })
                buffer?.Dispose();
            mergedIndices = actorIndices = actorDepths = unusedDepth = null;
            hasCombinedItem = false;
        }

        static void Merge(CommandBuffer cmd, ComputeShader cs, int kernel,
            GraphicsBuffer depthsA, GraphicsBuffer indexA, int countA, int tagA,
            GraphicsBuffer depthsB, GraphicsBuffer indexB, int countB, int tagB,
            GraphicsBuffer indices, GraphicsBuffer depths, bool writeDepth)
        {
            cmd.SetComputeBufferParam(cs,kernel,"_MergeDepthA",depthsA);
            cmd.SetComputeBufferParam(cs,kernel,"_MergeIndexA",indexA);
            cmd.SetComputeBufferParam(cs,kernel,"_MergeDepthB",depthsB);
            cmd.SetComputeBufferParam(cs,kernel,"_MergeIndexB",indexB);
            cmd.SetComputeBufferParam(cs,kernel,"_MergeOutIndex",indices);
            cmd.SetComputeBufferParam(cs,kernel,"_MergeOutDepth",depths);
            cmd.SetComputeIntParam(cs,"_MergeCountA",countA);
            cmd.SetComputeIntParam(cs,"_MergeCountB",countB);
            cmd.SetComputeIntParam(cs,"_MergeTagA",tagA);
            cmd.SetComputeIntParam(cs,"_MergeTagB",tagB);
            cmd.SetComputeIntParam(cs,"_MergeWriteDepth",writeDepth ? 1 : 0);
            cmd.DispatchCompute(cs,kernel,(countA+countB+1023)/1024,1,1);
        }

        void PrepareAvatarMerge(CommandBuffer cmd, PreparedRenderData data)
        {
            hasCombinedItem = false;
            if (data.renderItems.Count < 2 || data.renderItems.Count > 3) return;
            RenderItem house = default, first = default, second = default;
            foreach (var item in data.renderItems)
            {
                if (item.gs.m_RenderMode != GaussianSplatRenderer.RenderMode.Splats || item.gs.m_SortPasses != 4) return;
                if (item.gs.m_MergeSceneAvatars) house = item;
                else if (first.gs == null) first = item;
                else second = item;
            }
            if (house.gs == null || first.gs == null) return;
            int total = house.instanceCount + first.instanceCount + second.instanceCount;
            var cs = house.gs.m_CSSplatUtilities;
            int kernel = cs.FindKernel("CSMergeAvatarDepth");
            Reserve(ref mergedIndices, house.gs.splatCount + first.gs.splatCount + (second.gs ? second.gs.splatCount : 0), "Scene and avatar draw order");
            Reserve(ref unusedDepth, 1, "Unused merge depth");
            var indices = first.gs.m_GpuSortKeys;
            var depths = first.gs.m_GpuSortDistances;
            int tag = 1 << 30;
            if (second.gs)
            {
                int actorCapacity = first.gs.splatCount + second.gs.splatCount;
                Reserve(ref actorIndices, actorCapacity, "Avatar order");
                Reserve(ref actorDepths, actorCapacity, "Avatar depths");
                Merge(cmd,cs,kernel,depths,indices,first.instanceCount,tag,
                      second.gs.m_GpuSortDistances,second.gs.m_GpuSortKeys,second.instanceCount,unchecked((int)0x80000000),
                      actorIndices,actorDepths,true);
                indices = actorIndices; depths = actorDepths; tag = 0;
            }
            Merge(cmd,cs,kernel,house.gs.m_GpuSortDistances,house.gs.m_GpuSortKeys,house.instanceCount,0,
                  depths,indices,first.instanceCount+second.instanceCount,tag,mergedIndices,unusedDepth,false);
            house.mpb.SetBuffer(GaussianSplatRenderer.Props.OrderBuffer,mergedIndices);
            house.mpb.SetBuffer("_FirstAvatarView",first.gs.m_GpuView);
            house.mpb.SetBuffer("_SecondAvatarView",second.gs ? second.gs.m_GpuView : first.gs.m_GpuView);
            house.instanceCount = total;
            combinedItem = house;
            hasCombinedItem = true;
        }
    }
}
