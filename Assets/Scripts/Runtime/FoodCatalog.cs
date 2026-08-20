using System;
using System.Collections.Generic;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Character-neutral effects and inventory rules for one food. Character
    /// preferences and reaction motions belong to the character care profile,
    /// not this shared catalog.
    /// </summary>
    public sealed class PetFoodDefinition
    {
        internal PetFoodDefinition(
            string id,
            string displayName,
            string shortName,
            float energyGain,
            int moodGainSteps,
            int maxStack)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A food ID is required.", "id");
            }
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException(
                    "A food display name is required.",
                    "displayName");
            }
            if (string.IsNullOrWhiteSpace(shortName))
            {
                throw new ArgumentException(
                    "A food short name is required.",
                    "shortName");
            }
            if (float.IsNaN(energyGain) || float.IsInfinity(energyGain) ||
                energyGain < 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    "energyGain",
                    "Food Energy gain must be finite and non-negative.");
            }
            if (moodGainSteps < 0 || moodGainSteps > 4)
            {
                throw new ArgumentOutOfRangeException(
                    "moodGainSteps",
                    "Food Mood gain must be between zero and four steps.");
            }
            if (maxStack <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "maxStack",
                    "A food stack must hold at least one item.");
            }

            Id = id;
            DisplayName = displayName;
            ShortName = shortName;
            EnergyGain = energyGain;
            MoodGainSteps = moodGainSteps;
            MaxStack = maxStack;
        }

        public string Id { get; private set; }

        public string DisplayName { get; private set; }

        public string ShortName { get; private set; }

        public float EnergyGain { get; private set; }

        public int MoodGainSteps { get; private set; }

        public int MaxStack { get; private set; }
    }

    /// <summary>
    /// Stable, shared food definitions. The IDs and effects are app data; any
    /// installed-game icon or prop lookup remains in an optional presenter.
    /// </summary>
    public static class FoodCatalog
    {
        public const string CarrotJellyId = "carrot-jelly";

        public static readonly PetFoodDefinition CarrotJelly =
            new PetFoodDefinition(
                CarrotJellyId,
                "Carrot Jelly",
                "Carrot Jelly",
                18.0f,
                1,
                99);

        private static readonly PetFoodDefinition[] CatalogItems =
        {
            CarrotJelly
        };

        public static IReadOnlyList<PetFoodDefinition> Items
        {
            get { return CatalogItems; }
        }

        public static bool TryGet(string id, out PetFoodDefinition food)
        {
            if (!string.IsNullOrEmpty(id))
            {
                for (int index = 0; index < CatalogItems.Length; index++)
                {
                    PetFoodDefinition candidate = CatalogItems[index];
                    if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
                    {
                        food = candidate;
                        return true;
                    }
                }
            }

            food = null;
            return false;
        }
    }
}
