#define QUAD_MODE
#include "TerrainCommon.hlsl"

void ApplyNormals(uint3 id) {

    // Recalculate Normal
    {
        for (uint x = id.x * _MeshSection > 0 ? id.x * _MeshSection : 1; x < (id.x + 1) * _MeshSection && x < (_Size * 2); x++) {
    
            for (uint y = id.y * _MeshSection ? id.y * _MeshSection : 1; y <  (id.y + 1) * _MeshSection && y < (_Size * 2); y++) {
    
                float3 vertex = LoadVertex(x, y);
    
                float3 selfPt   = LoadVertex(x, y);
                float3 leftDir  = normalize(selfPt - LoadVertex(x - 1,  y));
                float3 upDir    = normalize(selfPt - LoadVertex(x,      y + 1));
                float3 downDir  = normalize(selfPt - LoadVertex(x,      y - 1));
                float3 rightDir = normalize(selfPt - LoadVertex(x + 1,  y));
    
                float3 luNorm = cross(leftDir, upDir);
                float3 ldNorm = cross(downDir, leftDir);
                float3 rdNorm = cross(rightDir, downDir);
                float3 ruNorm = cross(upDir, rightDir);
    
                float3 avgNorm = normalize(
                    luNorm * 0.25 + 
                    ldNorm * 0.25 + 
                    rdNorm * 0.25 + 
                    ruNorm * 0.25);
            
                StoreNormal(x, y, avgNorm);
            }
        }
    }

}

void ApplySmooth(uint3 id) {

    // Recalculate Normal
    {
        for (uint x = id.x * _MeshSection > 0 ? id.x * _MeshSection : 1; x < (id.x + 1) * _MeshSection && x < (_Size * 2); x++) {
    
            for (uint y = id.y * _MeshSection ? id.y * _MeshSection : 1; y <  (id.y + 1) * _MeshSection && y < (_Size * 2); y++) {
    
                float total = 0.0;
                for (int dx = -1; dx <= 1; dx++) 
                    for (int dy = -1; dy <= 1; dy++) 
                        total += LoadVertex(x + dx, y + dy).y;

                StoreVertexY(x, y, total * DIVIDE9);
            }
        }
    }

}

void ApplyConstraints(uint3 id) {

    // Apply Modifiers
    {
        for (uint x = id.x * _MeshSection; x < (id.x + 1) * _MeshSection && x < _QuadSize; x++)
            for (uint y = id.y * _MeshSection; y <  (id.y + 1) * _MeshSection && y < _QuadSize; y++) {
                uint mask = LoadModifyMask(x, y) 
                            & ~MANDATE_APPLIED_BIT 
                            & ~MINIMUM_APPLIED_BIT 
                            & ~MAXIMUM_APPLIED_BIT;
                StoreModifyMask(x, y, mask);
            }
    }
    {
        for (uint x = id.x * _MeshSection; x < (id.x + 1) * _MeshSection && x < _QuadSize; x++) {
    
            for (uint y = id.y * _MeshSection; y <  (id.y + 1) * _MeshSection && y < _QuadSize; y++) {
    
                float3 vertex = LoadVertex(x, y);
                Modify modify = LoadModify(x, y);
                float base = LoadBase(x, y);
                
                if (modify.mask & MAXIMUM_BIT && base > modify.maximum) {
                    base = modify.maximum;
                    modify.mask |= MAXIMUM_APPLIED_BIT;
                }
                
                if (modify.mask & MINIMUM_BIT && base < modify.minimum) {
                    base = modify.minimum;
                    modify.mask |= MINIMUM_APPLIED_BIT;
                }
    
                if (modify.mask & MANDATE_BIT) {
                    base = modify.mandate;
                    modify.mask |= MANDATE_APPLIED_BIT;
                }
            
                StoreVertexY(x, y, base);
                StoreModifyMask(x, y, modify.mask);
            }
        }
    }

}