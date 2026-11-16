using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyResources
{
    /// <summary>Inclusive bounds in exact smallest units. The Inspector displays whole resource quantities.</summary>
    [Serializable]
    public struct ResourceRewardRange
    {
        [SerializeField, Tooltip("Resource awarded by this row. Repeated resources roll independently and are summed.")]
        private ResourceDefinition resource;
        [SerializeField, Tooltip("Minimum inclusive quantity, stored in smallest units. Zero is allowed.")]
        private long min;
        [SerializeField, Tooltip("Maximum inclusive quantity, stored in smallest units. Must be at least Min.")]
        private long max;

        public ResourceDefinition Resource => resource;
        public long Min => min;
        public long Max => max;

        public ResourceRewardRange(ResourceDefinition resource, long min, long max)
        {
            this.resource = resource;
            this.min = min;
            this.max = max;
            Validate();
        }

        /// <summary>Author bounds in whole quantities, e.g. 1.25m..1.75m for a two-decimal resource.</summary>
        public static ResourceRewardRange FromQuantity(ResourceDefinition resource, decimal min, decimal max)
        {
            if (resource == null)
                throw new ArgumentNullException(nameof(resource));
            resource.ValidateDefinition();
            return new ResourceRewardRange(resource, resource.ToUnits(min), resource.ToUnits(max));
        }

        internal void Validate()
        {
            if (resource == null)
                throw new ArgumentException("Assign a Resource to every reward row, or remove the empty row.");
            resource.ValidateDefinition();
            if (min < 0 || max < min)
                throw new ArgumentException("Reward bounds must satisfy 0 <= Min <= Max. Both bounds are included.");
        }
    }

    /// <summary>Reward configuration only. Each Roll returns an independent cost snapshot for one atomic grant.</summary>
    [CreateAssetMenu(menuName = "EasyGameFramework/Resources/Resource Reward", fileName = "New Resource Reward")]
    public sealed class ResourceRewardProfile : ScriptableObject, ISerializationCallbackReceiver
    {
        [SerializeField, Tooltip("The database owning every resource selected in this reward. Changing it never replaces rows automatically.")]
        private ResourceDatabase database;
        [SerializeField, Tooltip("All listed resources are awarded. Each inclusive range rolls in smallest-unit steps; duplicate rows add together.")]
        private ResourceRewardRange[] ranges = Array.Empty<ResourceRewardRange>();
        [NonSerialized] private IReadOnlyList<ResourceRewardRange> view;

        // No cached roll, paid flag or mutable wallet state belongs in the asset.
        public IReadOnlyList<ResourceRewardRange> Ranges => view ?? (view = Array.AsReadOnly(ranges));

        public ResourceDatabase Database
        {
            get => database;
            set { ValidateMembership(value); database = value; }
        }

        private void ValidateMembership(ResourceDatabase candidate)
        {
            if (candidate == null) throw new ArgumentException("Assign the Resource Database used by this reward.");
            candidate.ValidateConfiguration();
            if (ranges == null) throw new ArgumentException("Reward Ranges is missing.");
            for (var i = 0; i < ranges.Length; i++)
                if (!candidate.Contains(ranges[i].Resource))
                    throw new ArgumentException($"Reward row {i + 1} is missing or belongs to another database. Select a resource from {candidate.name}; existing rows were kept.");
        }

        private void OnValidate() => view = null;
        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() => view = null;

        public void ValidateConfiguration()
        {
            ValidateMembership(database);
            if (ranges == null)
                throw new ArgumentException("Reward Ranges is missing. Use an empty list for no reward.");
            for (var i = 0; i < ranges.Length; i++)
            {
                var row = ranges[i];
                row.Validate();
                long maximum = row.Max;
                for (var j = 0; j < i; j++)
                {
                    var earlier = ranges[j];
                    if (ReferenceEquals(earlier.Resource, row.Resource))
                    {
                        try { maximum = checked(maximum + earlier.Max); }
                        catch (OverflowException exception)
                        {
                            throw new OverflowException($"The sum of Max amounts for {row.Resource.DisplayName} exceeds Int64. Reduce the duplicate reward rows.", exception);
                        }
                    }
                    else if (earlier.Resource.Id == row.Resource.Id)
                        throw new ArgumentException("Different reward Resource assets share an ID. Give the duplicate asset a new ID.");
                }
            }
        }

        /// <summary>
        /// Roll once per reward event. Null uses Unity Random; pass a seeded System.Random for reproducible rolls.
        /// The returned snapshot allocates once per roll; retain it when retrying a failed grant. Main thread only.
        /// </summary>
        public ResourceCost Roll(System.Random random = null)
        {
            ValidateConfiguration(); // Complete validation precedes all RNG calls.
            var result = new ResourceAmount[ranges.Length];
            for (var i = 0; i < ranges.Length; i++)
            {
                var row = ranges[i];
                result[i] = new ResourceAmount(row.Resource, Sample(row.Min, row.Max, random));
            }
            return new ResourceCost(result);
        }

        private static long Sample(long min, long max, System.Random random)
        {
            if (min == max)
                return min;
            // Width can be 2^63 for the full nonnegative Int64 range. Never add one in signed arithmetic.
            var width = (ulong)(max - min) + 1UL;
            const ulong domain = 1UL << 63;
            var limit = domain - domain % width;
            ulong bits;
            do
            {
                bits = ((ulong)NextChunk(random) << 42) | ((ulong)NextChunk(random) << 21) | (uint)NextChunk(random);
            } while (bits >= limit);
            return min + (long)(bits % width);
        }

        private static int NextChunk(System.Random random)
        {
            const int exclusive = 1 << 21;
            var value = random == null ? UnityEngine.Random.Range(0, exclusive) : random.Next(exclusive);
            if ((uint)value >= exclusive)
                throw new InvalidOperationException("The supplied Random returned a value outside [0, maxValue).");
            return value;
        }

        public static ResourceRewardProfile Create(string name, ResourceDatabase database, params ResourceRewardRange[] ranges)
        {
            if (ranges == null)
                throw new ArgumentNullException(nameof(ranges));
            var profile = CreateInstance<ResourceRewardProfile>();
            profile.name = name;
            profile.database = database;
            profile.ranges = (ResourceRewardRange[])ranges.Clone();
            try { profile.ValidateConfiguration(); return profile; }
            catch { if (Application.isPlaying) Destroy(profile); else DestroyImmediate(profile); throw; }
        }
    }
}
