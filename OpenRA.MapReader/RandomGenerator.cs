namespace OpenRA.MapReader
{
    /// <summary>
    /// A seedable random number generator
    /// </summary>
    public class RandomGenerator
    {
        private readonly Random _random;

        public RandomGenerator(int seed)
        {
            _random = new Random(seed);
        }

        /// <summary>
        /// Returns a random integer between 0 (inclusive) and max (exclusive)
        /// </summary>
        public int Next(int max)
        {
            return _random.Next(max);
        }

        /// <summary>
        /// Returns a random integer between min (inclusive) and max (exclusive)
        /// </summary>
        public int Next(int min, int max)
        {
            return _random.Next(min, max);
        }

        /// <summary>
        /// Returns a random float between 0.0 and 1.0
        /// </summary>
        public float NextFloat()
        {
            return (float)_random.NextDouble();
        }

        /// <summary>
        /// Picks a random item from the array
        /// </summary>
        public T Pick<T>(T[] items)
        {
            return items[_random.Next(items.Length)];
        }

        /// <summary>
        /// Picks a random item from the list
        /// </summary>
        public T Pick<T>(List<T> items)
        {
            return items[_random.Next(items.Count)];
        }

        /// <summary>
        /// Picks a random item based on weights
        /// </summary>
        public T PickWeighted<T>(T[] items, float[] weights)
        {
            float totalWeight = weights.Sum();
            float value = NextFloat() * totalWeight;

            float currentWeight = 0;
            for (int i = 0; i < items.Length; i++)
            {
                currentWeight += weights[i];
                if (value <= currentWeight)
                    return items[i];
            }

            return items[items.Length - 1];
        }

        /// <summary>
        /// Shuffles an array in-place
        /// </summary>
        public void ShuffleInPlace<T>(T[] array)
        {
            int n = array.Length;
            while (n > 1)
            {
                n--;
                int k = _random.Next(n + 1);
                (array[k], array[n]) = (array[n], array[k]);
            }
        }

        /// <summary>
        /// Shuffles part of an array in-place
        /// </summary>
        public void ShuffleInPlace<T>(T[] array, int count)
        {
            int n = Math.Min(count, array.Length);
            while (n > 1)
            {
                n--;
                int k = _random.Next(n + 1);
                (array[k], array[n]) = (array[n], array[k]);
            }
        }
    }
}
