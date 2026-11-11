// Merge two sorted splat streams by view depth. Each thread owns a distinct run of 16 output slots.
StructuredBuffer<uint> _MergeDepthA, _MergeDepthB, _MergeIndexA, _MergeIndexB;
RWStructuredBuffer<uint> _MergeOutIndex, _MergeOutDepth;
uint _MergeCountA, _MergeCountB, _MergeTagA, _MergeTagB, _MergeWriteDepth;

[numthreads(64, 1, 1)]
void CSMergeAvatarDepth(uint3 id : SV_DispatchThreadID)
{
    uint start = id.x * 16;
    uint total = _MergeCountA + _MergeCountB;
    if (start >= total) return;
    uint lo = start > _MergeCountB ? start - _MergeCountB : 0;
    uint hi = min(start, _MergeCountA);
    while (lo < hi)
    {
        uint i = (lo + hi) >> 1;
        uint j = start - i;
        // A wins equal-depth ties, including at thread boundaries.
        if (j > 0 && i < _MergeCountA && _MergeDepthA[i] <= _MergeDepthB[j-1]) lo = i+1;
        else hi = i;
    }
    uint a = lo, b = start - lo;
    for (uint slot = start; slot < min(total, start+16); ++slot)
    {
        uint da = a < _MergeCountA ? _MergeDepthA[a] : 0xffffffff;
        uint db = b < _MergeCountB ? _MergeDepthB[b] : 0xffffffff;
        uint key, depth;
        if (a < _MergeCountA && (b >= _MergeCountB || da <= db))
        {
            key = _MergeIndexA[a++] | _MergeTagA;
            depth = da;
        }
        else
        {
            key = _MergeIndexB[b++] | _MergeTagB;
            depth = db;
        }
        _MergeOutIndex[slot] = key;
        if (_MergeWriteDepth) _MergeOutDepth[slot] = depth;
    }
}
