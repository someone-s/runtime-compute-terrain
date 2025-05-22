using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct SplineDescription
{
    public string type;
    public string name;
    public bool isInternal;
}

public enum SplineType { Project, Visual }

public readonly struct SplineProfile
{
    public readonly Mesh mesh;
    public readonly List<Material> materials;

    public readonly float spacing;
    public readonly bool vertical;
    public readonly int maxPointCount;
    public readonly float extends;

    public readonly ContinousSettings? continous;

    public readonly CastSettings? cast;


    public SplineProfile(
        Mesh mesh,
        List<Material> materials,
        float spacing = 1f,
        bool vertical = false,
        int maxPointCount = 64,
        float extends = 0f,
        ContinousSettings? continous = null,
        CastSettings? cast = null)
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

        this.cast = cast;
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
    
    public readonly struct CastSettings 
    {
        public readonly Vector3 origin;
        public readonly Vector3 direction;
        public readonly float maxOffset;

        public CastSettings(
            Vector3 origin,
            Vector3 direction,
            float maxOffset)
        {
            this.origin = origin;
            this.direction = direction;
            this.maxOffset = maxOffset;
        }
    }
}

