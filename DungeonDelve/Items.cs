namespace DungeonDelve;

public enum Rarity { Common, Uncommon, Rare, Epic }

public enum Slot { Weapon, Armor, Charm }

public sealed class Item
{
    public string Name = "";
    public Slot Slot;
    public Rarity Rarity;

    public int Power;          // weapon damage or armour value
    public int Hp;             // charm stats
    public float Speed;
    public float Crit;
    public float ManaRegen;

    /// <summary>Used to decide whether a found item beats the equipped one.</summary>
    public float Rating => Slot switch
    {
        Slot.Weapon => Power,
        Slot.Armor => Power * 3f,
        _ => Hp * 0.5f + Speed * 150f + Crit * 250f + ManaRegen * 5f,
    };

    public Color Color => Rarity switch
    {
        Rarity.Common => Color.FromArgb(205, 205, 205),
        Rarity.Uncommon => Color.FromArgb(90, 215, 100),
        Rarity.Rare => Color.FromArgb(90, 160, 255),
        _ => Color.FromArgb(200, 100, 245),
    };

    public string Describe()
    {
        var parts = new List<string>();
        if (Slot == Slot.Weapon) parts.Add($"+{Power} dmg");
        if (Slot == Slot.Armor) parts.Add($"+{Power} armor");
        if (Hp > 0) parts.Add($"+{Hp} HP");
        if (Speed > 0f) parts.Add($"+{Speed * 100f:0}% speed");
        if (Crit > 0f) parts.Add($"+{Crit * 100f:0}% crit");
        if (ManaRegen > 0f) parts.Add($"+{ManaRegen:0.#} mana/s");
        return string.Join(", ", parts);
    }
}

public static class ItemFactory
{
    private static readonly string[][] WeaponNames =
    {
        new[] { "Rusty Sword", "Dull Axe", "Chipped Club" },
        new[] { "Iron Blade", "Steel Mace", "Hunter's Axe" },
        new[] { "Runed Saber", "Flamebrand", "Frostfang" },
        new[] { "Dragonbone Edge", "Soulreaver", "Kingslayer" },
    };

    private static readonly string[][] ArmorNames =
    {
        new[] { "Tattered Vest", "Padded Tunic" },
        new[] { "Chain Mail", "Studded Leather" },
        new[] { "Knight's Plate", "Warden's Aegis" },
        new[] { "Dragonscale Armor", "Aegis of Ages" },
    };

    private static readonly string[][] CharmNames =
    {
        new[] { "Lucky Coin", "Bone Charm" },
        new[] { "Ember Amulet", "Wolf Fang" },
        new[] { "Sage's Ring", "Eagle Pendant" },
        new[] { "Heart of the Mountain", "Phoenix Feather" },
    };

    private static readonly float[] Mult = { 1f, 1.35f, 1.8f, 2.4f };

    public static Item Create(Random rng, int floor, Slot? forcedSlot = null, Rarity? minRarity = null)
    {
        Slot slot = forcedSlot ?? (Slot)rng.Next(3);

        float roll = rng.NextSingle();
        Rarity rarity =
            roll > 0.985f - floor * 0.004f ? Rarity.Epic :
            roll > 0.90f - floor * 0.01f ? Rarity.Rare :
            roll > 0.60f ? Rarity.Uncommon :
            Rarity.Common;
        if (minRarity.HasValue && rarity < minRarity.Value) rarity = minRarity.Value;

        float mult = Mult[(int)rarity];
        var item = new Item { Slot = slot, Rarity = rarity };

        switch (slot)
        {
            case Slot.Weapon:
                item.Name = Pick(WeaponNames[(int)rarity], rng);
                item.Power = (int)((3f + floor * 1.6f + rng.Next(0, 4)) * mult);
                break;

            case Slot.Armor:
                item.Name = Pick(ArmorNames[(int)rarity], rng);
                item.Power = (int)((1f + floor * 0.7f + rng.Next(0, 2)) * mult);
                break;

            default:
                item.Name = Pick(CharmNames[(int)rarity], rng);
                int kind = rng.Next(4);
                ApplyCharmStat(item, kind, mult, floor);
                if (rarity >= Rarity.Rare)
                    ApplyCharmStat(item, (kind + 1 + rng.Next(3)) % 4, mult * 0.6f, floor);
                break;
        }
        return item;
    }

    private static string Pick(string[] names, Random rng) => names[rng.Next(names.Length)];

    private static void ApplyCharmStat(Item it, int kind, float mult, int floor)
    {
        switch (kind)
        {
            case 0: it.Hp += (int)((8f + floor * 3f) * mult); break;
            case 1: it.Speed += 0.04f * mult; break;
            case 2: it.Crit += 0.04f * mult; break;
            default: it.ManaRegen += 2f * mult; break;
        }
    }
}

/// <summary>A choice offered on level-up.</summary>
public sealed record Perk(string Name, string Description, Action<Player> Apply);

public static class Perks
{
    public static readonly Perk[] All =
    {
        new("Brute Force", "+20% damage with everything.", p => p.DmgMul += 0.2f),
        new("Vitality", "+30 max HP and heal 30.", p => { p.PerkHp += 30; p.Hp += 30; }),
        new("Fleet Foot", "+12% movement speed.", p => p.SpeedBonus += 0.12f),
        new("Swift Blade", "Sword swings 15% faster.", p => p.MeleeCdMul *= 0.85f),
        new("Arcane Well", "+30 max mana.", p => p.PerkMana += 30),
        new("Mana Spring", "+3 mana regenerated per second.", p => p.PerkManaRegen += 3f),
        new("Vampirism", "Heal for 5% of the damage you deal.", p => p.LifeSteal += 0.05f),
        new("Critical Eye", "+8% critical hit chance.", p => p.PerkCrit += 0.08f),
        new("Thick Skin", "+2 armor.", p => p.PerkArmor += 2),
        new("Firestarter", "Fireballs deal 40% more damage.", p => p.FireMul += 0.4f),
        new("Shadow Step", "Dash recharges 30% faster.", p => p.DashCdMul *= 0.7f),
    };
}
