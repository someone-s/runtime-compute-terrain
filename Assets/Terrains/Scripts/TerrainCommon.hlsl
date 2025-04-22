
#define MANDATE_BIT 1
#define MINIMUM_BIT 2
#define MAXIMUM_BIT 4
#define MANDATE_APPLIED_BIT 8
#define MINIMUM_APPLIED_BIT 16
#define MAXIMUM_APPLIED_BIT 32
#define EPSILON 0.0000000001
#define DIVIDE9 0.1111111111
#define DIVIDE25 0.04
#define FLOAT_SIZE_IN_BYTES 4

struct Modify {
    uint mask;
    float mandate;
    float minimum;
    float maximum;
};

int _Stride;
int _PositionOffset;
int _NormalOffset;
int _BaseOffset;
int _ModifyOffset;

#ifdef QUAD_MODE
RWByteAddressBuffer _VerticesTL;
RWByteAddressBuffer _VerticesTR;
RWByteAddressBuffer _VerticesBL;
RWByteAddressBuffer _VerticesBR;
#else
RWByteAddressBuffer _Vertices;
#endif

float _Area;
uint _Size; // i.e. 1000
uint _QuadSize; // must be size * 2 + 1
uint _MeshSection; // if _Size is 1000, _MeshSection is 126 ceil((1000 * 2 + 1) / 32)

uint CalculateVertexIndex(uint x, uint y) {
    return (y * (_Size + 1) + x) * _Stride + _PositionOffset;
}
float3 LoadVertex(uint x, uint y) {
#ifdef QUAD_MODE
    if (x > _Size && y > _Size) {
        float3 value = asfloat(_VerticesTR.Load3(CalculateVertexIndex(x - _Size, y - _Size)));
        value.x += _Area;
        value.z += _Area;
        return value;
    }
    else if (x > _Size) {
        float3 value = asfloat(_VerticesBR.Load3(CalculateVertexIndex(x - _Size, y)));
        value.x += _Area;
        return value;
    }
    else if (y > _Size) {
        float3 value = asfloat(_VerticesTL.Load3(CalculateVertexIndex(x, y - _Size)));
        value.z += _Area;
        return value;
    }
    else {
        float3 value = asfloat(_VerticesBL.Load3(CalculateVertexIndex(x, y)));
        return value;
    }
#else 
    return asfloat(_Vertices.Load3(CalculateVertexIndex(x, y)));
#endif
}
void StoreVertex(uint x, uint y, float3 value) {
#ifdef QUAD_MODE
    if (x >= _Size && y >= _Size) {
        float3 valueCopy = value;
        valueCopy.x -= _Area;
        valueCopy.z -= _Area;
        _VerticesTR.Store3(CalculateVertexIndex(x - _Size, y - _Size), asuint(valueCopy));
    }

    if (x >= _Size && y <= _Size) {
        float3 valueCopy = value;
        valueCopy.x -= _Area;
        _VerticesBR.Store3(CalculateVertexIndex(x - _Size, y), asuint(valueCopy));
    }

    if (x <= _Size && y >= _Size) {
        float3 valueCopy = value;
        valueCopy.z -= _Area;
        _VerticesTL.Store3(CalculateVertexIndex(x, y - _Size), asuint(valueCopy));
    }

    if (x <= _Size && y <= _Size) {
        float3 valueCopy = value;
        _VerticesBL.Store3(CalculateVertexIndex(x, y), asuint(valueCopy));
    }
#else
    _Vertices.Store3(CalculateVertexIndex(x, y), asuint(value));
#endif
}
void StoreVertexY(uint x, uint y, float value) {
#ifdef QUAD_MODE
    if (x >= _Size && y >= _Size) 
        _VerticesTR.Store(CalculateVertexIndex(x - _Size, y - _Size) + FLOAT_SIZE_IN_BYTES, asuint(value));

    if (x >= _Size && y <= _Size) 
        _VerticesBR.Store(CalculateVertexIndex(x - _Size, y) + FLOAT_SIZE_IN_BYTES, asuint(value));

    if (x <= _Size && y >= _Size) 
        _VerticesTL.Store(CalculateVertexIndex(x, y - _Size) + FLOAT_SIZE_IN_BYTES, asuint(value));

    if (x <= _Size && y <= _Size) 
        _VerticesBL.Store(CalculateVertexIndex(x, y) + FLOAT_SIZE_IN_BYTES, asuint(value));
#else
    _Vertices.Store(CalculateVertexIndex(x, y) + FLOAT_SIZE_IN_BYTES, asuint(value));
#endif
}

uint CalculateNormalIndex(uint x, uint y) {
    return (y * (_Size + 1) + x) * _Stride + _NormalOffset;
}
void StoreNormal(uint x, uint y, float3 value) {
#ifdef QUAD_MODE
    if (x >= _Size && y >= _Size)
        _VerticesTR.Store3(CalculateNormalIndex(x - _Size, y - _Size), asuint(value));

    if (x >= _Size && y <= _Size)
        _VerticesBR.Store3(CalculateNormalIndex(x - _Size, y), asuint(value));

    if (x <= _Size && y >= _Size)
        _VerticesTL.Store3(CalculateNormalIndex(x, y - _Size), asuint(value));

    if (x <= _Size && y <= _Size)
        _VerticesBL.Store3(CalculateNormalIndex(x, y), asuint(value));
#else
    _Vertices.Store3(CalculateNormalIndex(x, y), asuint(value));
#endif
}

uint CalculateModifyIndex(uint x, uint y) {
    return (y * (_Size + 1) + x) * _Stride + _ModifyOffset;
}
Modify LoadModify(uint x, uint y) {
    uint4 bits;

#ifdef QUAD_MODE
    if (x > _Size && y > _Size)
        bits = _VerticesTR.Load4(CalculateModifyIndex(x - _Size, y - _Size));
    else if (x > _Size) 
        bits = _VerticesBR.Load4(CalculateModifyIndex(x - _Size, y       ));
    else if (y > _Size) 
        bits = _VerticesTL.Load4(CalculateModifyIndex(x,        y - _Size));
    else 
        bits = _VerticesBL.Load4(CalculateModifyIndex(x,        y       ));
#else
    bits = _Vertices.Load4(CalculateModifyIndex(x, y));
#endif

    Modify modify;
    modify.mask = bits.x;

    float4 vals = asfloat(bits);
    modify.mandate = vals.y;
    modify.minimum = vals.z;
    modify.maximum = vals.w;

    return modify;
}
void StoreModify(uint x, uint y, float mandate, float minimum, float maximum, uint mask) {

    uint4 bits;
    bits.x = mask;
    bits.y = asuint(mandate);
    bits.z = asuint(minimum);
    bits.w = asuint(maximum);

#ifdef QUAD_MODE
    if (x >= _Size && y >= _Size)
        _VerticesTR.Store4(CalculateModifyIndex(x - _Size, y - _Size), bits);

    if (x >= _Size && y <= _Size)
        _VerticesBR.Store4(CalculateModifyIndex(x - _Size, y        ), bits);

    if (x <= _Size && y >= _Size) 
        _VerticesTL.Store4(CalculateModifyIndex(x        , y - _Size), bits);

    if (x <= _Size && y <= _Size) 
        _VerticesBL.Store4(CalculateModifyIndex(x        , y        ), bits);
#else
    _Vertices.Store4(CalculateModifyIndex(x, y), bits);
#endif
}
uint LoadModifyMask(uint x, uint y) {
#ifdef QUAD_MODE
    if (x > _Size && y > _Size)
        return _VerticesTR.Load(CalculateModifyIndex(x - _Size, y - _Size));
    else if (x > _Size) 
        return _VerticesBR.Load(CalculateModifyIndex(x - _Size, y        ));
    else if (y > _Size) 
        return _VerticesTL.Load(CalculateModifyIndex(x,         y - _Size));
    else 
        return _VerticesBL.Load(CalculateModifyIndex(x,         y        ));
#else
    return _Vertices.Load(CalculateModifyIndex(x, y));
#endif
}
void StoreModifyMask(uint x, uint y, uint mask) {
#ifdef QUAD_MODE
    if (x >= _Size && y >= _Size)
        _VerticesTR.Store(CalculateModifyIndex(x - _Size, y - _Size), mask);

    if (x >= _Size && y <= _Size)
        _VerticesBR.Store(CalculateModifyIndex(x - _Size, y        ), mask);

    if (x <= _Size && y >= _Size) 
        _VerticesTL.Store(CalculateModifyIndex(x        , y - _Size), mask);

    if (x <= _Size && y <= _Size) 
        _VerticesBL.Store(CalculateModifyIndex(x        , y        ), mask);
#else
    _Vertices.Store(CalculateModifyIndex(x, y), mask);
#endif
}

uint CalculateBaseIndex(uint x, uint y) {
    return (y * (_Size + 1) + x) * _Stride + _BaseOffset;
}
float LoadBase(uint x, uint y) {
#ifdef QUAD_MODE
    if (x > _Size && y > _Size)
        return asfloat(_VerticesTR.Load(CalculateBaseIndex(x - _Size, y - _Size)));
    else if (x > _Size) 
        return asfloat(_VerticesBR.Load(CalculateBaseIndex(x - _Size, y        )));
    else if (y > _Size) 
        return asfloat(_VerticesTL.Load(CalculateBaseIndex(x,         y - _Size)));
    else 
        return asfloat(_VerticesBL.Load(CalculateBaseIndex(x,         y        )));
#else
    return asfloat(_Vertices.Load(CalculateBaseIndex(x, y)));
#endif
}
void StoreBase(uint x, uint y, float value) {
#ifdef QUAD_MODE
    if (x >= _Size && y >= _Size)
        _VerticesTR.Store(CalculateBaseIndex(x - _Size, y - _Size), asuint(value));

    if (x >= _Size && y <= _Size)
        _VerticesBR.Store(CalculateBaseIndex(x - _Size, y        ), asuint(value));

    if (x <= _Size && y >= _Size) 
        _VerticesTL.Store(CalculateBaseIndex(x        , y - _Size), asuint(value));

    if (x <= _Size && y <= _Size) 
        _VerticesBL.Store(CalculateBaseIndex(x        , y        ), asuint(value));
#else
    _Vertices.Store(CalculateBaseIndex(x, y), asuint(value));
#endif
}