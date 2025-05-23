
float RandomNormal(float2 seed) {
    return frac(sin(dot(seed, float2(12.9898, 78.233)))*43758.5453);
}

// Below based on code from https://iquilezles.org/articles/morenoise/
// Under MIT License stated at https://iquilezles.org/articles/

// Returns 2D value noise and its 2 derivatives
float3 ValueNoise(float2 x)
{
    float2 p = floor(x);
    float2 w = frac(x);

    float2 u = w*w*w*(w*(w*6.0-15.0)+10.0);
    float2 du = 30.0*w*w*(w*(w-2.0)+1.0);
    
    float a = RandomNormal(p + float2(0,0));
    float b = RandomNormal(p + float2(1,0));
    float c = RandomNormal(p + float2(0,1));
    float d = RandomNormal(p + float2(1,1));
    
    float k0 = a;
    float k1 = b - a;
    float k2 = c - a;
    float k4 = a - b - c + d;

    return float3((k0 + k1*u.x + k2*u.y + k4*u.x*u.y),
                   du * float2(k1 + k4*u.y, k2 + k4*u.x));
}

#define SCALE 10
#define ITERATIONS 9

float ProceduralHeight(float2 p)
{
    float t = 0.0;
    float freq = 1.0;
    float amp = 1.0 / exp2(ITERATIONS);

    for( int i=0; i< ITERATIONS; i++)
    {
        t += ValueNoise(p * SCALE / freq).x * amp;
        freq *= 2;
        amp *= 2;
    }
    return t * 10;
}
