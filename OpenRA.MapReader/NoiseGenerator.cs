namespace OpenRA.MapReader
{
    /// <summary>
    /// Generates Perlin noise for terrain generation
    /// </summary>
    public class NoiseGenerator
    {
        private readonly RandomGenerator _random;

        public NoiseGenerator(RandomGenerator random)
        {
            _random = random;
        }

        /// <summary>
        /// Generates 2D Perlin noise
        /// </summary>
        public float[] GeneratePerlinNoise2D(int size)
        {
            var noise = new float[size * size];
            Array.Fill(noise, 0.0f);

            var vecX = new float[size * size];
            var vecY = new float[size * size];

            // Unit length divided by number of dot products to do
            float D = 1.0f / 4.0f;

            for (int y = 0; y <= size; y++)
            {
                for (int x = 0; x <= size; x++)
                {
                    float phase = 2 * MathF.PI * _random.NextFloat();
                    float vx = MathF.Cos(phase);
                    float vy = MathF.Sin(phase);

                    if (x > 0 && y > 0)
                    {
                        noise[(y - 1) * size + (x - 1)] += vx * -D + vy * -D;
                    }
                    if (x < size && y > 0)
                    {
                        noise[(y - 1) * size + x] += vx * D + vy * -D;
                    }
                    if (x > 0 && y < size)
                    {
                        noise[y * size + (x - 1)] += vx * -D + vy * D;
                    }
                    if (x < size && y < size)
                    {
                        noise[y * size + x] += vx * D + vy * D;
                    }
                }
            }

            return noise;
        }

        /// <summary>
        /// Generate fractal noise using multiple octaves of Perlin noise
        /// </summary>
        public float[] GenerateFractalNoise2D(int size, float wavelengthScale = 1.0f)
        {
            var noise = new float[size * size];
            Array.Fill(noise, 0.0f);

            // Create wavelengths for different octaves
            int octaveCount = (int)Math.Log2(size);
            var wavelengths = new float[octaveCount];
            for (int i = 0; i < octaveCount; i++)
            {
                wavelengths[i] = (1 << i) * wavelengthScale;
            }

            // Amplitude function (similar to JS version)
            float AmpFunc(float wavelength) => wavelength / size / wavelengths.Length;

            foreach (float wavelength in wavelengths)
            {
                float amps = AmpFunc(wavelength);
                int subSize = ((int)((size / wavelength)) | 0) + 2;
                float[] subNoise = GeneratePerlinNoise2D(subSize);

                // Offsets should align to grid
                int offsetX = (int)(_random.NextFloat() * wavelength);
                int offsetY = (int)(_random.NextFloat() * wavelength);

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        noise[y * size + x] += amps * Interpolate2D(
                            subNoise,
                            subSize,
                            subSize,
                            (offsetX + x) / wavelength,
                            (offsetY + y) / wavelength
                        );
                    }
                }
            }

            return noise;
        }

        /// <summary>
        /// Generate fractal noise with symmetry options
        /// </summary>
        public float[] GenerateFractalNoiseWithSymmetry(int size, int rotations = 2, int mirror = 0, float wavelengthScale = 1.0f)
        {
            // Generate raw noise on a larger template to allow for rotation
            int templateSize = size * 2 + 2;
            var template = GenerateFractalNoise2D(templateSize, wavelengthScale);

            var noise = new float[size * size];
            
            // Center offset for rotation
            float o = (size - 1) / 2.0f;
            float to = templateSize / 2.0f;

            // Apply rotational symmetry
            for (int rotation = 0; rotation < rotations; rotation++)
            {
                float angle = rotation * 2 * MathF.PI / rotations;
                float cosAngle = MathF.Cos(angle);
                float sinAngle = MathF.Sin(angle);

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float mtx = (x - o) * MathF.Sqrt(2);
                        float mty = (y - o) * MathF.Sqrt(2);
                        float tx = (mtx * cosAngle - mty * sinAngle) + to;
                        float ty = (mtx * sinAngle + mty * cosAngle) + to;

                        noise[y * size + x] += Interpolate2D(
                            template,
                            templateSize,
                            templateSize,
                            tx,
                            ty
                        ) / rotations;
                    }
                }
            }

            // Apply mirror symmetry if needed
            if (mirror != 0)
            {
                var unmirrored = (float[])noise.Clone();
                Array.Fill(noise, 0.0f);

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var (tx, ty) = MirrorXY(x, y, size, mirror);
                        noise[y * size + x] = unmirrored[y * size + x] + unmirrored[ty * size + tx];
                    }
                }
            }

            return noise;
        }

        /// <summary>
        /// 2D interpolation helper
        /// </summary>
        private float Interpolate2D(float[] grid, int w, int h, float x, float y)
        {
            int xa = (int)Math.Floor(x);
            int xb = (int)Math.Ceiling(x);
            int ya = (int)Math.Floor(y);
            int yb = (int)Math.Ceiling(y);

            float xbw = x - xa;
            float ybw = y - ya;
            float xaw = 1.0f - xbw;
            float yaw = 1.0f - ybw;

            // Clamp to valid indices
            if (xa < 0)
            {
                xa = 0;
                xb = 0;
            }
            else if (xb > w - 1)
            {
                xa = w - 1;
                xb = w - 1;
            }

            if (ya < 0)
            {
                ya = 0;
                yb = 0;
            }
            else if (yb > h - 1)
            {
                ya = h - 1;
                yb = h - 1;
            }

            float naa = grid[ya * w + xa];
            float nba = grid[ya * w + xb];
            float nab = grid[yb * w + xa];
            float nbb = grid[yb * w + xb];

            return (naa * xaw + nba * xbw) * yaw + (nab * xaw + nbb * xbw) * ybw;
        }

        /// <summary>
        /// Mirror coordinates for symmetry operations
        /// </summary>
        private (int, int) MirrorXY(int x, int y, int size, int mirror)
        {
            float o = (size - 1) / 2.0f;
            
            // Apply appropriate mirror transformation
            return mirror switch
            {
                1 => (x, (int)(2 * o - y)), // Mirror horizontally
                2 => (y, x),                // Mirror across y = x
                3 => ((int)(2 * o - x), y), // Mirror vertically
                4 => ((int)(2 * o - y), (int)(2 * o - x)), // Mirror across y = -x
                _ => (x, y)
            };
        }
    }
}
