// Compute entry point for scalar fields and compact Forcecraft brick payloads.
// A terrain workgroup owns one 4^3 brick; only nonempty payloads reserve space.
layout(local_size_x=4, local_size_y=4, local_size_z=4) in;
layout(set=0,binding=0,std430) buffer Output { uint words[]; };
layout(push_constant) uniform Parameters {
    vec4 originStep;
    uvec4 extentMode;
    ivec4 algorithm; // seed, noise, fractal, octaves
    vec4 fractal; // frequency, lacunarity, gain, bounding
    vec4 shaping; // weight, pingpong, fade, unused
    vec4 terrain; // height, amplitude, vertical scale, unused
    uvec4 material; // color, sky, rotation, unused
} p;

float singleNoise(int seed, vec3 v, bool twoD) {
    if (p.algorithm.y == 0) return twoD ? OpenSimplex2Kernel_Sample2(seed,v.x,v.y) : OpenSimplex2Kernel_Sample3(seed,v.x,v.y,v.z);
    if (p.algorithm.y == 3) return twoD ? PerlinKernel_Sample2(seed,v.x,v.y) : PerlinKernel_Sample3(seed,v.x,v.y,v.z);
    return twoD ? ValueKernel_Sample2(seed,v.x,v.y) : ValueKernel_Sample3(seed,v.x,v.y,v.z);
}
vec3 transform(vec3 v, bool twoD) {
    v = vec3(Mul(v.x,p.fractal.x),Mul(v.y,p.fractal.x),Mul(v.z,p.fractal.x));
    if (twoD) {
        if (p.algorithm.y == 0) {
            float t = Mul(Add(v.x,v.y),0.3660253882408142);
            v.x=Add(v.x,t); v.y=Add(v.y,t);
        }
    } else if (p.material.z == 1u) {
        float xy=Add(v.x,v.y), s=Mul(xy,-0.211324865405187);
        v.z=Mul(v.z,0.577350269189626);
        v.x=Add(v.x,Sub(s,v.z)); v.y=Sub(Add(v.y,s),v.z);
        v.z=Add(v.z,Mul(xy,0.577350269189626));
    } else if (p.material.z == 2u) {
        float xz=Add(v.x,v.z), s=Mul(xz,-0.211324865405187);
        v.y=Mul(v.y,0.577350269189626);
        v.x=Add(v.x,Sub(s,v.y)); v.z=Add(v.z,Sub(s,v.y));
        v.y=Add(v.y,Mul(xz,0.577350269189626));
    } else if (p.algorithm.y == 0) {
        float r=Mul(Add(Add(v.x,v.y),v.z),0.6666666865348816);
        v=vec3(Sub(r,v.x),Sub(r,v.y),Sub(r,v.z));
    }
    return v;
}
float shape(float n) {
    if (p.algorithm.z==2) return Add(Mul(abs(n),-2.0),1.0);
    if (p.algorithm.z==3) return Mul(Sub(NoiseMath_PingPong(Mul(Add(n,1.0),p.shaping.y)),0.5),2.0);
    return n;
}
float noise(vec3 world, bool twoD) {
    vec3 v=transform(world,twoD);
    if (p.algorithm.z==0) return Mul(singleNoise(p.algorithm.x,v,twoD),p.shaping.z);
    if (p.algorithm.w<=1) return Mul(Mul(shape(singleNoise(p.algorithm.x,v,twoD)),p.fractal.w),p.shaping.z);
    float sum=0.0, amp=p.fractal.w;
    for(int o=0;o<p.algorithm.w;o++) {
        if (o==p.algorithm.w-1) amp=Mul(amp,p.shaping.z);
        float n=singleNoise(AddI(p.algorithm.x,o),v,twoD);
        float weight;
        if(p.algorithm.z==2) weight=Sub(1.0,abs(n));
        else if(p.algorithm.z==3) weight=NoiseMath_PingPong(Mul(Add(n,1.0),p.shaping.y));
        else weight=Mul(twoD ? min(Add(n,1.0),2.0) : Add(n,1.0),0.5);
        sum=Add(sum,Mul(shape(n),amp));
        amp=Mul(amp,NoiseMath_Lerp(1.0,weight,p.shaping.x));
        v=vec3(Mul(v.x,p.fractal.y),Mul(v.y,p.fractal.y),Mul(v.z,p.fractal.y));
        amp=Mul(amp,p.fractal.z);
    }
    return sum;
}
shared uint cells[64];
shared uint brickOffset;
shared uint brickCount;
void main() {
    uint mode=p.extentMode.w;
    uvec3 c=gl_GlobalInvocationID;
    if(mode==3u) {
        uint face=gl_LocalInvocationIndex;
        if(face<12u) {
            uint offset=words[0]+face*4u;
            words[offset]=face<6u ? 0u : p.material.x;
            words[offset+1u]=0u; words[offset+2u]=0u; words[offset+3u]=0u;
        }
        return;
    }
    if(any(greaterThanEqual(c,p.extentMode.xyz))) return;
    vec3 world=vec3(Add(p.originStep.x,Mul(float(c.x),p.originStep.w)),
        Add(p.originStep.y,Mul(float(c.y),p.originStep.w)),
        Add(p.originStep.z,Mul(float(c.z),p.originStep.w)));
    float n=noise(world,mode==0u);
    if(mode<2u) {
        words[c.x+p.extentMode.x*(c.y+p.extentMode.y*c.z)]=floatBitsToUint(n);
        return;
    }
    // Forcecraft is Z-fastest within both the directory and each brick.
    uint local=(gl_LocalInvocationID.x*4u+gl_LocalInvocationID.y)*4u+gl_LocalInvocationID.z;
    float density=Sub(Add(Mul(n,p.terrain.y),p.terrain.x),Mul(world.y,p.terrain.z));
    cells[local]=density>0.0 ? 0xff000001u : 0u;
    barrier();
    if(local==0u) {
        bool isUniform=true, empty=true;
        for(uint i=0u;i<64u;i++) { isUniform=isUniform && cells[i]==cells[0]; empty=empty && cells[i]==0u; }
        brickCount=empty ? 0u : (isUniform ? 1u : 64u);
        brickOffset=empty ? 0u : atomicAdd(words[0],brickCount*2u);
        uint side=p.extentMode.x/4u;
        uint directory=4u+(gl_WorkGroupID.x*side+gl_WorkGroupID.y)*side+gl_WorkGroupID.z;
        words[directory]=brickOffset | (brickCount==1u ? 0x80000000u : 0u);
    }
    barrier();
    if(local<brickCount) {
        words[brickOffset+local*2u]=cells[local];
        words[brickOffset+local*2u+1u]=cells[local]==0u ? 0u : 0xff000000u|(p.material.y<<16u);
    }
}

