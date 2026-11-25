namespace OpenFabric.Core.World;

/// <summary>Small deterministic hash + gradient noise. Pure integer hashing so results are stable across machines.</summary>
public static class Noise
{
    public static uint Hash(uint seed, int x, int y)
    {
        unchecked
        {
            uint h = seed ^ ((uint)x * 0x27d4eb2du) ^ ((uint)y * 0x165667b1u);
            h ^= h >> 15; h *= 0x85ebca6bu;
            h ^= h >> 13; h *= 0xc2b2ae35u;
            h ^= h >> 16;
            return h;
        }
    }

    /// <summary>Uniform [0,1) from hash.</summary>
    public static float Unit(uint seed, int x, int y) => (Hash(seed, x, y) & 0xFFFFFF) / 16777216f;

    static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    static float Grad(uint h, float x, float y) => (h & 7) switch
    {
        0 => x, 1 => -x, 2 => y, 3 => -y,
        4 => (x + y) * 0.7071f, 5 => (x - y) * 0.7071f,
        6 => (-x + y) * 0.7071f, _ => (-x - y) * 0.7071f,
    };

    /// <summary>Perlin noise in roughly [-1,1].</summary>
    public static float Perlin(uint seed, float x, float y)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        float u = Fade(fx), v = Fade(fy);
        float n00 = Grad(Hash(seed, x0, y0), fx, fy);
        float n10 = Grad(Hash(seed, x0 + 1, y0), fx - 1, fy);
        float n01 = Grad(Hash(seed, x0, y0 + 1), fx, fy - 1);
        float n11 = Grad(Hash(seed, x0 + 1, y0 + 1), fx - 1, fy - 1);
        return Lerp(Lerp(n00, n10, u), Lerp(n01, n11, u), v) * 1.4142f;
    }

    /// <summary>Fractal Brownian motion, normalised to roughly [-1,1].</summary>
    public static float Fbm(uint seed, float x, float y, int octaves, float lacunarity = 2f, float gain = 0.5f)
    {
        float sum = 0, amp = 1, norm = 0, freq = 1;
        for (int i = 0; i < octaves; i++)
        {
            sum += Perlin(seed + (uint)i * 0x9E3779B9u, x * freq, y * freq) * amp;
            norm += amp; amp *= gain; freq *= lacunarity;
        }
        return sum / norm;
    }
}
