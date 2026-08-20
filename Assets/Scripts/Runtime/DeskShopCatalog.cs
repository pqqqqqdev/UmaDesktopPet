using System;
using System.Collections.Generic;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// One permanent, app-owned desk reward definition. The stable ID is saved;
    /// installed game asset names stay inside the presenter that renders it.
    /// </summary>
    public sealed class DeskShopItem
    {
        internal DeskShopItem(
            string id,
            string displayName,
            string shortName,
            int cost)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A desk item ID is required.", "id");
            }
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException(
                    "A desk item display name is required.",
                    "displayName");
            }
            if (string.IsNullOrWhiteSpace(shortName))
            {
                throw new ArgumentException(
                    "A desk item short name is required.",
                    "shortName");
            }
            if (cost <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "cost",
                    "A desk item must cost at least one Moni.");
            }

            Id = id;
            DisplayName = displayName;
            ShortName = shortName;
            Cost = cost;
        }

        public string Id { get; private set; }

        public string DisplayName { get; private set; }

        public string ShortName { get; private set; }

        public int Cost { get; private set; }
    }

    /// <summary>
    /// Character-neutral permanent desk collection. New characters share the
    /// same wallet and collection; their attachment profile decides placement.
    /// </summary>
    public static class DeskShopCatalog
    {
        public const string CarrotCharmId = "carrot-charm";
        public const string TazunaRedPenId = "tazuna-red-pen";
        public const string DerbyTrophyId = "derby-trophy";

        public static readonly DeskShopItem CarrotCharm =
            new DeskShopItem(CarrotCharmId, "Carrot desk charm", "Carrot charm", 1);
        public static readonly DeskShopItem TazunaRedPen =
            new DeskShopItem(
                TazunaRedPenId,
                "Tazuna's red pen",
                "Red pen",
                2);
        public static readonly DeskShopItem DerbyTrophy =
            new DeskShopItem(DerbyTrophyId, "Derby trophy", "Derby trophy", 3);

        private static readonly DeskShopItem[] CatalogItems =
        {
            CarrotCharm,
            TazunaRedPen,
            DerbyTrophy
        };

        public static IReadOnlyList<DeskShopItem> Items
        {
            get { return CatalogItems; }
        }

        public static bool TryGet(string id, out DeskShopItem item)
        {
            if (!string.IsNullOrEmpty(id))
            {
                for (int index = 0; index < CatalogItems.Length; index++)
                {
                    DeskShopItem candidate = CatalogItems[index];
                    if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
                    {
                        item = candidate;
                        return true;
                    }
                }
            }

            item = null;
            return false;
        }

        public static int TotalCost(IEnumerable<string> itemIds)
        {
            if (itemIds == null)
            {
                return 0;
            }

            int total = 0;
            foreach (string id in itemIds)
            {
                DeskShopItem item;
                if (!TryGet(id, out item))
                {
                    throw new ArgumentException(
                        "Unknown desk item ID: " + (id ?? "<null>"),
                        "itemIds");
                }
                checked
                {
                    total += item.Cost;
                }
            }
            return total;
        }
    }
}
