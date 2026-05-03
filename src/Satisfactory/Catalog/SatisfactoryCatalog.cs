using ERP.Domain;

namespace Satisfactory.Catalog;

public static class SatisfactoryCatalog
{
    public static class Items
    {
        public static readonly Item IronOre        = new(new("iron-ore"),       "Iron Ore");
        public static readonly Item IronIngot      = new(new("iron-ingot"),     "Iron Ingot");
        public static readonly Item IronPlate      = new(new("iron-plate"),     "Iron Plate");
        public static readonly Item IronRod        = new(new("iron-rod"),       "Iron Rod");
        public static readonly Item Screw          = new(new("screw"),          "Screw");
        public static readonly Item ReinforcedPlate = new(new("reinforced-iron-plate"), "Reinforced Iron Plate");
        public static readonly Item CopperOre      = new(new("copper-ore"),     "Copper Ore");
        public static readonly Item CopperIngot    = new(new("copper-ingot"),   "Copper Ingot");
        public static readonly Item Wire           = new(new("wire"),           "Wire");
        public static readonly Item Cable          = new(new("cable"),          "Cable");
        public static readonly Item Limestone      = new(new("limestone"),      "Limestone");
        public static readonly Item Concrete       = new(new("concrete"),       "Concrete");

        public static readonly IReadOnlyList<Item> All =
        [
            IronOre, IronIngot, IronPlate, IronRod, Screw, ReinforcedPlate,
            CopperOre, CopperIngot, Wire, Cable,
            Limestone, Concrete
        ];
    }

    public static class Buildings
    {
        public static readonly Building Smelter      = new(new("smelter"),      "Smelter",      4);
        public static readonly Building Constructor  = new(new("constructor"),  "Constructor",  4);
        public static readonly Building Assembler    = new(new("assembler"),    "Assembler",   15);
        public static readonly Building Miner        = new(new("miner-mk1"),    "Miner Mk.1",   5);

        public static readonly IReadOnlyList<Building> All =
        [
            Smelter, Constructor, Assembler, Miner
        ];
    }

    // Rates below are items per minute (the units the Satisfactory game shows).
    // Recipe.Duration is left at 1 minute so the per-minute math is direct.
    private static readonly TimeSpan PerMinute = TimeSpan.FromMinutes(1);

    public static class Recipes
    {
        public static readonly Recipe IronIngot = new(
            new("iron-ingot"), "Iron Ingot", Buildings.Smelter.Id,
            Inputs:  [new(Items.IronOre.Id,    30)],
            Outputs: [new(Items.IronIngot.Id,  30)],
            Duration: PerMinute);

        public static readonly Recipe IronPlate = new(
            new("iron-plate"), "Iron Plate", Buildings.Constructor.Id,
            Inputs:  [new(Items.IronIngot.Id,  30)],
            Outputs: [new(Items.IronPlate.Id,  20)],
            Duration: PerMinute);

        public static readonly Recipe IronRod = new(
            new("iron-rod"), "Iron Rod", Buildings.Constructor.Id,
            Inputs:  [new(Items.IronIngot.Id,  15)],
            Outputs: [new(Items.IronRod.Id,    15)],
            Duration: PerMinute);

        public static readonly Recipe Screw = new(
            new("screw"), "Screw", Buildings.Constructor.Id,
            Inputs:  [new(Items.IronRod.Id,    10)],
            Outputs: [new(Items.Screw.Id,      40)],
            Duration: PerMinute);

        public static readonly Recipe ReinforcedPlate = new(
            new("reinforced-iron-plate"), "Reinforced Iron Plate", Buildings.Assembler.Id,
            Inputs:  [new(Items.IronPlate.Id, 30), new(Items.Screw.Id, 60)],
            Outputs: [new(Items.ReinforcedPlate.Id, 5)],
            Duration: PerMinute);

        public static readonly Recipe CopperIngot = new(
            new("copper-ingot"), "Copper Ingot", Buildings.Smelter.Id,
            Inputs:  [new(Items.CopperOre.Id,   30)],
            Outputs: [new(Items.CopperIngot.Id, 30)],
            Duration: PerMinute);

        public static readonly Recipe Wire = new(
            new("wire"), "Wire", Buildings.Constructor.Id,
            Inputs:  [new(Items.CopperIngot.Id, 15)],
            Outputs: [new(Items.Wire.Id,        30)],
            Duration: PerMinute);

        public static readonly Recipe Cable = new(
            new("cable"), "Cable", Buildings.Constructor.Id,
            Inputs:  [new(Items.Wire.Id,        60)],
            Outputs: [new(Items.Cable.Id,       30)],
            Duration: PerMinute);

        public static readonly Recipe Concrete = new(
            new("concrete"), "Concrete", Buildings.Constructor.Id,
            Inputs:  [new(Items.Limestone.Id,  45)],
            Outputs: [new(Items.Concrete.Id,   15)],
            Duration: PerMinute);

        public static readonly IReadOnlyList<Recipe> All =
        [
            IronIngot, IronPlate, IronRod, Screw, ReinforcedPlate,
            CopperIngot, Wire, Cable,
            Concrete
        ];
    }

    public static Item? FindItem(ItemId id) =>
        Items.All.FirstOrDefault(i => i.Id == id);

    public static Building? FindBuilding(BuildingId id) =>
        Buildings.All.FirstOrDefault(b => b.Id == id);
}
