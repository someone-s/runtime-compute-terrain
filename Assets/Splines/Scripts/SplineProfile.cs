using System.Collections.Generic;
using UnityEngine;

public readonly struct SplineProfile
{
    public readonly Mesh mesh;
    public readonly List<Material> materials;

    public readonly float spacing;
    public readonly bool vertical;
    public readonly int maxPointCount;
    public readonly float extends;

    public readonly ContinousSettings? continous;
        


    public SplineProfile(
        Mesh mesh, 
        List<Material> materials, 
        float spacing = 1f,
        bool vertical = false, 
        int maxPointCount = 64, 
        float extends = 0f,
        ContinousSettings? continous = null)
    {
        this.mesh = mesh;
        this.materials = materials;
        
        this.spacing = spacing;
        this.vertical = vertical;
        this.maxPointCount = maxPointCount;
        if (this.maxPointCount < 2)
            this.maxPointCount = 2;
        this.extends = extends;

        this.continous = continous;
    }

    public readonly struct ContinousSettings 
    {
        public readonly int[] mapping;
        public readonly float stretch;

        public ContinousSettings(
            int[] mapping = null, 
            float stretch = 1f)
        {
            this.mapping = mapping;
            this.stretch = stretch;
        }
    }
}

