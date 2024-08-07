﻿using Project.Params;
using Project.Settings;
using Project.Utility;
using FSParam;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Project.Tasks;

public partial class Randomizer
{
    public SeedInfo SeedInfo { get; private set; }
    private readonly string _seed;
    private readonly Random _random;
    private Param _itemLotParam_map;
    private Param _itemLotParam_enemy;
    private Param _shopLineupParam;
    private Param _atkParam_Pc;
    private Param _equipMtrlSetParam;
    // Dictionaries
    private Dictionary<int, EquipParamWeapon> _weaponDictionary;
    private Dictionary<int, EquipParamWeapon> _customWeaponDictionary;
    private Dictionary<int, string> _weaponNameDictionary;
    private Dictionary<int, EquipParamGoods> _goodsDictionary;
    private Dictionary<int, Magic> _magicDictionary;
    private Dictionary<ushort, List<Param.Row>> _weaponTypeDictionary;
    private Dictionary<byte, List<Param.Row>> _armorTypeDictionary;
    private Dictionary<byte, List<Param.Row>> _magicTypeDictionary;
    public Task RandomizeRegulation()
    {
        _randomizerLog = new List<string>();
        randomizeStartingClassParams();
        _cancellationToken.ThrowIfCancellationRequested();
        randomizeItemLotParams();
        _cancellationToken.ThrowIfCancellationRequested();
        randomizeShopLineupParam();
        _cancellationToken.ThrowIfCancellationRequested();
        randomizeShopLineupParamMagic();
        _cancellationToken.ThrowIfCancellationRequested();
        patchAtkParam();
        _cancellationToken.ThrowIfCancellationRequested();
        patchSmithingStones();
        _cancellationToken.ThrowIfCancellationRequested();
        writeFiles();
        writeLog();
        SeedInfo = new SeedInfo(_seed, Util.GetShaRegulation256Hash());
        string seedJson = JsonSerializer.Serialize(SeedInfo);
        File.WriteAllText(Config.LastSeedPath, seedJson);
        return Task.CompletedTask;
    }
    private void randomizeStartingClassParams()
    {
        logItem("Starting Class Randomization");
        logItem($"Seed: {_seed}");
        logItem("Level estimate (x) appears if you cannot wield the weapon, assumes you are benefiting from two-handing.");

        List<Param.Row> staves = _weaponTypeDictionary[Const.StaffType];
        List<Param.Row> seals = _weaponTypeDictionary[Const.SealType];
        List<Param.Row> bows = _weaponTypeDictionary[Const.BowType];
        List<Param.Row> lightbows = _weaponTypeDictionary[Const.LightBowType];
        List<Param.Row> greatbows = _weaponTypeDictionary[Const.GreatbowType];
        List<Param.Row> ballistae = _weaponTypeDictionary[Const.BallistaType];
        List<Param.Row> crossbows = _weaponTypeDictionary[Const.CrossbowType];
        List<Param.Row> smallShields = _weaponTypeDictionary[Const.SmallShieldType];
        List<Param.Row> mediumShields = _weaponTypeDictionary[Const.MediumShieldType];
        List<Param.Row> greatShields = _weaponTypeDictionary[Const.GreatShieldType];
        List<Param.Row> claws = _weaponTypeDictionary[Const.ClawType];
        List<Param.Row> fists = _weaponTypeDictionary[Const.FistType];

        IEnumerable<int> remembranceItems = _shopLineupParam.Rows.Where(r => r.ID is >= 101900 and <= 101929)
            .Select(r => new ShopLineupParam(r).equipId);

        List<int> spells = _magicDictionary.Keys.Select(id => id).Distinct()
            .Where(id => remembranceItems.All(r => r != id))
            .Where(id => staves.All(s => s.ID != id) && seals.All(s => s.ID != id))
            .ToList();
        spells.Shuffle(_random);

        List<int> weapons = _weaponDictionary.Keys.Select(washWeaponLevels).Distinct()
            .Where(id => remembranceItems.All(i => i != id))
            .Where(id => staves.All(s => s.ID != id)
                && seals.All(s => s.ID != id)
                && bows.All(s => s.ID != id)
                && lightbows.All(s => s.ID != id)
                && greatbows.All(s => s.ID != id)
                && ballistae.All(s => s.ID != id)
                && smallShields.All(s => s.ID != id)
                && mediumShields.All(s => s.ID != id)
                && greatShields.All(s => s.ID != id)
                && claws.All(s => s.ID != id)
                && fists.All(s => s.ID != id)
            ).ToList();
        weapons.Shuffle(_random);

        for (int i = 0; i < Config.NumberOfClasses; i++)
        {
            Param.Row? row = _charaInitParam[Config.FirstClassId + i];
            if (row == null) { continue; }

            int index = _random.Next(Equipment.SideWeaponLists.Count);
            List<int> sideArms = Equipment.SideWeaponLists[index];
            index = _random.Next(Equipment.MainWeaponLists.Count);
            List<int> main = Equipment.MainWeaponLists[index];
            // List<int> main = Equipment.StartingWeaponIDs;

            CharaInitParam startingClass = new(row);
            randomizeEquipment(startingClass, weapons, sideArms);
            allocateStatsAndSpells(row.ID, startingClass, spells);
            logCharaInitEntry(startingClass, i + 288100);
            addDescriptionString(startingClass, Const.ChrInfoMapping[i]);
        }
    }
    private void randomizeItemLotParams()
    {
        OrderedDictionary categoryDictEnemy = new();
        OrderedDictionary categoryDictMap = new();

        IEnumerable<Param.Row> itemLotParamMap = _itemLotParam_map.Rows.Where(id => !Unk.unkItemLotParamMapWeapons.Contains(id.ID));
        IEnumerable<Param.Row> itemLotParamEnemy = _itemLotParam_enemy.Rows.Where(id => !Unk.unkItemLotParamEnemyWeapons.Contains(id.ID));

        foreach (Param.Row row in itemLotParamEnemy.Concat(itemLotParamMap))
        {   // going through enemy weapon drops
            Param.Column[] itemIds = row.Cells.Take(Const.ItemLots).ToArray();
            Param.Column[] categories = row.Cells.Skip(Const.CategoriesStart).Take(Const.ItemLots).ToArray();
            Param.Column[] chances = row.Cells.Skip(Const.ChanceStart).Take(Const.ItemLots).ToArray();
            int totalWeight = chances.Sum(a => (ushort)a.GetValue(row));
            for (int i = 0; i < Const.ItemLots; i++)
            {
                int category = (int)categories[i].GetValue(row);
                if (category != Const.ItemLotWeaponCategory && category != Const.ItemLotCustomWeaponCategory)
                { continue; }

                int id = (int)itemIds[i].GetValue(row);
                //> Should work if it were item lot entries

                //^ TODO revisit for longterm
                int sanitizedId = washWeaponLevels(id);
                if (category == Const.ItemLotWeaponCategory)
                {
                    if (!_weaponDictionary.TryGetValue(sanitizedId, out EquipParamWeapon? wep)) { continue; }
                    if ((wep.wepType is Const.StaffType or Const.SealType)) { continue; }

                    if (id != sanitizedId)
                    {
                        _weaponNameDictionary[id] = $"{_weaponNameDictionary[sanitizedId]} + {id - sanitizedId}";
                    }
                    ushort chance = (ushort)chances[i].GetValue(row);
                    if (chance == totalWeight)
                    {
                        // these are all category 2         key(weapontype), new ItemLotEntry(id, 2);
                        addToOrderedDict(categoryDictMap, wep.wepType, new ItemLotEntry(id, category)); // (IOrderedDictionary orderedDict, object key, T type)
                        break; // Break here because the entire item lot param is just a single entry.  // could just add DLC Weapons to DictMap
                    }
                    addToOrderedDict(categoryDictEnemy, wep.wepType, new ItemLotEntry(id, category));
                }
                else
                { // category == Const.ItemLotCustomWeaponCategory
                    if (!_customWeaponDictionary.TryGetValue(id, out EquipParamWeapon? wep)) { continue; }
                    if (wep.wepType is Const.StaffType or Const.SealType) { continue; }

                    ushort chance = (ushort)chances[i].GetValue(row);

                    if (chance == totalWeight)
                    {
                        addToOrderedDict(categoryDictMap, wep.wepType, new ItemLotEntry(id, category));
                        break;
                    }
                    addToOrderedDict(categoryDictEnemy, wep.wepType, new ItemLotEntry(id, category));
                }
            }
        }

        foreach (ItemLotWrapper item in Equipment.DlcWeaponItemLots)
        {
            addToOrderedDict(categoryDictMap, item.Type, item.Entry);
            addToOrderedDict(categoryDictEnemy, item.Type, item.Entry);
        }

        dedupeAndRandomizeVectors(categoryDictMap);
        dedupeAndRandomizeVectors(categoryDictEnemy);

        Dictionary<int, ItemLotEntry> guaranteedDropReplace = getReplacementHashmap(categoryDictMap);
        Dictionary<int, ItemLotEntry> chanceDropReplace = getReplacementHashmap(categoryDictEnemy);

        // Application now has weapons set to randomize
        // logItem(">> Item Replacements - all instances of item on left will be replaced with item on right");
        // logItem("## Guaranteed Weapons");
        // logReplacementDictionary(guaranteedDropReplace);
        // logItem("\n## Chance Weapons");
        // logReplacementDictionary(chanceDropReplace);
        logItem("");

        foreach (Param.Row row in _itemLotParam_enemy.Rows.Concat(_itemLotParam_map.Rows))
        {
            Param.Column[] itemIds = row.Cells.Take(Const.ItemLots).ToArray();
            Param.Column[] categories = row.Cells.Skip(Const.CategoriesStart).Take(Const.ItemLots).ToArray();
            for (int i = 0; i < Const.ItemLots; i++)
            {
                int category = (int)categories[i].GetValue(row);
                if (category != Const.ItemLotWeaponCategory && category != Const.ItemLotCustomWeaponCategory)
                { continue; }

                int id = (int)itemIds[i].GetValue(row);
                if (category == Const.ItemLotWeaponCategory)
                {
                    // if (!_weaponDictionary.TryGetValue(washWeaponLevels(id), out _))
                    // { continue; }

                    if (guaranteedDropReplace.TryGetValue(id, out ItemLotEntry entry))
                    {
                        itemIds[i].SetValue(row, entry.Id);
                        categories[i].SetValue(row, entry.Category);
                        break;
                    }
                    if (!chanceDropReplace.TryGetValue(id, out entry))
                    { continue; }

                    itemIds[i].SetValue(row, entry.Id);
                    categories[i].SetValue(row, entry.Category);
                }
                else
                { // category == Const.ItemLotCustomWeaponCategory
                    if (!_customWeaponDictionary.TryGetValue(id, out _))
                    { continue; }

                    if (guaranteedDropReplace.TryGetValue(id, out ItemLotEntry entry))
                    {
                        itemIds[i].SetValue(row, entry.Id);
                        categories[i].SetValue(row, entry.Category);
                    }
                    if (!chanceDropReplace.TryGetValue(id, out entry))
                    { continue; }

                    itemIds[i].SetValue(row, entry.Id);
                    categories[i].SetValue(row, entry.Category);
                }
            }
        }
    }
    private void randomizeShopLineupParam() //TODO add armor randomization, TODO randomize away from Carian Regal Scepter
    {
        string docPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); // TODO remove

        List<ShopLineupParam> shopLineupParamRemembranceList = new();
        foreach (Param.Row row in _shopLineupParam.Rows)
        {
            /*  
                using (StreamWriter outputFile = new StreamWriter(Path.Combine(docPath, "WriteLines.txt"), true))
                {
                            outputFile.WriteLine($" id {row.ID}, value: {col.GetValue(row)} <>");
                }
            */

            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupWeaponCategory || (row.ID < 101900 || row.ID > 101980))
            { continue; } // assures only weapons are randomized TODO update for armor

            ShopLineupParam lot = new(new Param.Row(row));
            int sanitizedId = washWeaponLevels(lot.equipId);

            if (!_weaponDictionary.TryGetValue(sanitizedId, out _))
            { continue; }

            if (lot.equipId != sanitizedId)
            {
                _weaponNameDictionary[lot.equipId] = $"{_weaponNameDictionary[sanitizedId]} +{lot.equipId - sanitizedId}";
            }
            shopLineupParamRemembranceList.Add(lot);
        }

        // logItem("<> Shop Replacements - Random item selected from pool of all weapons (not including infused weapons). Remembrances are randomized amongst each-other.");

        List<int> RemembranceWeaponIDs = new List<int>()
        {
            3100000, 3140000, 4020000, 4050000, 6040000, 8100000, 9020000, 11150000,
            13030000, 15040000, 15110000, 17010000,  20060000, 21060000, 23050000,
            42000000,
            // 4530000, 4550000,  // Radahn's DLC swords
            // DLC
            3500000, 3510000, 8500000, 17500000, 18510000, 23510000, 23520000, 67520000,
        };

        List<List<int>> WeaponShopLists = new List<List<int>>() {
            Equipment.LightBowAndBowIDs, Equipment.SmallShieldIDs, Equipment.MediumShieldIDs, Equipment.TorchIDs,
            Equipment.ColossalWeaponIDs, Equipment.CurvedGreatSwordIDs, Equipment.HammerIDs, Equipment.StraightSwordIDs,
            Equipment.CurvedSwordIDs, Equipment.ReaperIDs, Equipment.TwinbladeIDs, Equipment.DaggerClawFistIDs,
            Equipment.GreatswordIDs, Equipment.KatanaIDs, Equipment.HeavyThrustingIDs, Equipment.AxeIDs,
            Equipment.DlcSideIDs,
        };

        foreach (Param.Row row in _shopLineupParam.Rows)
        {
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupWeaponCategory
                || row.ID > 101980
            ) // TODO find out what this row.ID is removes 20 lots
            { continue; }

            ShopLineupParam lot = new(row);

            if (!_weaponDictionary.TryGetValue(washWeaponLevels(lot.equipId), out EquipParamWeapon? wep))
            { continue; }

            if (lot.equipId == Const.CarianRegalScepter || !(wep.wepType is Const.StaffType or Const.SealType))
            {   // about 60 item shop allocations
                int index = _random.Next(WeaponShopLists.Count);
                List<int> weaponIndex = WeaponShopLists[index];

                replaceShopLineupParam(lot, weaponIndex, RemembranceWeaponIDs);
            }
        }
        List<int> baseHeadProtectors = new List<int>()
        {
            40000, 160000, 210000, 280000, 620000, 630000, 660000, 670000, 730000, 870000,
            880000, 890000, 1401000, 1500000, 1100000,
        };
        List<int> baseArmProtectors = new List<int>() // arm protectors found in shops
        {
            40200, 210200, 280200, 630200, 660200, 670200, 730200, 870200, 880200, 930200, 1500200,
        };
        List<int> baseBodyProtectors = new List<int>()
        {
            40100, 210100, 280100, 622100, 630100, 660100, 670100, 730100, 870100, 880100, 890100, 931100,
            962100, 1500100, 1102100, 1100100,
        };
        List<int> baseLegProtectors = new List<int>()
        {
            40300, 210300, 280300, 620300, 630300, 660300, 670300, 730300, 870300, 880300, 890300, 930300,
            960300, 1500300,
        };

        foreach (Param.Row row in _shopLineupParam.Rows)
        {
            if ((byte)row["equipType"]!.Value.Value == Const.ShopLineupArmorCategory)
            {
                if (row.ID > 101980) { continue; }

                ShopLineupParam lot = new(row);

                if (baseHeadProtectors.Contains(lot.equipId)) // chain coif (helmet)
                {
                    int index = _random.Next(Equipment.HeadArmorIDs.Count);
                    lot.equipId = Equipment.HeadArmorIDs[index];
                }
                if (baseBodyProtectors.Contains(lot.equipId)) // Chain armor
                {
                    int index = _random.Next(Equipment.BodyArmorIDs.Count);
                    lot.equipId = Equipment.BodyArmorIDs[index];
                }
                if (baseArmProtectors.Contains(lot.equipId))
                {
                    int index = _random.Next(Equipment.ArmsArmorIDs.Count);
                    lot.equipId = Equipment.ArmsArmorIDs[index];
                }
                if (lot.equipId == 1100300) // Chain Leggings
                {
                    int index = _random.Next(Equipment.LegsArmorIDs.Count);
                    lot.equipId = Equipment.LegsArmorIDs[index];
                }

            }
        }
    }
    private void randomizeShopLineupParamMagic()
    {
        OrderedDictionary magicCategoryDictMap = new();
        List<ShopLineupParam> shopLineupParamRemembranceList = new();
        List<ShopLineupParam> shopLineupParamDragonList = new();
        foreach (Param.Row row in _shopLineupParam.Rows)
        {
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupGoodsCategory || row.ID > 101980)
            { continue; }

            ShopLineupParam lot = new(new Param.Row(row));
            if (!_magicDictionary.TryGetValue(lot.equipId, out Magic? magic)) { continue; }

            if (row.ID < 101950)
            {
                if (lot.mtrlId == -1)
                {
                    addToOrderedDict(magicCategoryDictMap, magic.ezStateBehaviorType, lot.equipId);
                    continue;
                }
                shopLineupParamRemembranceList.Add(lot);
            }
            else
            { // Dragon Communion Shop 101950 - 101980 
                shopLineupParamDragonList.Add(lot);
            }
        }

        foreach (Param.Row row in _itemLotParam_enemy.Rows.Concat(_itemLotParam_map.Rows))
        {
            Param.Column[] itemIds = row.Cells.Take(Const.ItemLots).ToArray();
            Param.Column[] categories = row.Cells.Skip(Const.CategoriesStart).Take(Const.ItemLots).ToArray();
            Param.Column[] chances = row.Cells.Skip(Const.ChanceStart).Take(Const.ItemLots).ToArray();
            int totalWeight = chances.Sum(a => (ushort)a.GetValue(row));
            for (int i = 0; i < Const.ItemLots; i++)
            {
                int category = (int)categories[i].GetValue(row);
                if (category != Const.ItemLotGoodsCategory) { continue; }

                int id = (int)itemIds[i].GetValue(row);
                if (!_magicDictionary.TryGetValue(id, out Magic? magic)) { continue; }

                ushort chance = (ushort)chances[i].GetValue(row);
                if (chance == totalWeight)
                {
                    addToOrderedDict(magicCategoryDictMap, magic.ezStateBehaviorType, id);
                    break;
                }
                addToOrderedDict(magicCategoryDictMap, magic.ezStateBehaviorType, id);
            }
        }

        dedupeAndRandomizeShopVectors(magicCategoryDictMap);

        Dictionary<int, int> magicShopReplacement = getShopReplacementHashmap(magicCategoryDictMap);
        shopLineupParamRemembranceList.Shuffle(_random); // TODO investigate if thise matters
        shopLineupParamDragonList.Shuffle(_random); // TODO investigate if thise matters
                                                    // logItem("\n## All Magic Replacement.");
                                                    // logReplacementDictionaryMagic(magicShopReplacement);

        // logItem("\n~* Shop Magic Replacement.");
        foreach (Param.Row row in _shopLineupParam.Rows)
        {
            // logShopIdMagic(row.ID);
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupGoodsCategory || row.ID > 101980)
            { continue; }

            ShopLineupParam lot = new(row);
            if (!_magicDictionary.TryGetValue(lot.equipId, out _)) { continue; }

            if (row.ID < 101950)
            { replaceShopLineupParamMagic(lot, magicShopReplacement, shopLineupParamRemembranceList); }

            else
            {
                ShopLineupParam newDragonIncant = getNewId(lot.equipId, shopLineupParamDragonList);
                // logItem($"{_goodsFmg[lot.equipId]} -> {_goodsFmg[newDragonIncant.equipId]}");
                copyShopLineupParam(lot, newDragonIncant);
            }
        }

        foreach (Param.Row row in _itemLotParam_enemy.Rows.Concat(_itemLotParam_map.Rows))
        {
            Param.Column[] itemIds = row.Cells.Take(Const.ItemLots).ToArray();
            Param.Column[] categories = row.Cells.Skip(Const.CategoriesStart).Take(Const.ItemLots).ToArray();
            for (int i = 0; i < Const.ItemLots; i++)
            {
                int category = (int)categories[i].GetValue(row);
                if (category != Const.ItemLotGoodsCategory) { continue; }

                int id = (int)itemIds[i].GetValue(row);
                if (!_magicDictionary.TryGetValue(id, out Magic _)) { continue; }

                if (!magicShopReplacement.TryGetValue(id, out int entry)) { continue; }

                itemIds[i].SetValue(row, entry);
            }
        }
    }
    private void patchAtkParam()
    {
        Param.Row swarmOfFlies1 = _atkParam_Pc[72100] ?? throw new InvalidOperationException("Entry 72100 not found in AtkParam_Pc");
        Param.Row swarmOfFlies2 = _atkParam_Pc[72101] ?? throw new InvalidOperationException("Entry 72101 not found in AtkParam_Pc");

        AtkParam swarmAtkParam1 = new(swarmOfFlies1);
        AtkParam swarmAtkParam2 = new(swarmOfFlies2);
        patchSpEffectAtkPowerCorrectRate(swarmAtkParam1);
        patchSpEffectAtkPowerCorrectRate(swarmAtkParam2);
    }

    private void patchSmithingStones()
    {
        foreach (Param.Row row in _equipMtrlSetParam.Rows)
        {
            int id = (int)row["materialId01"]!.Value.Value;
            int category = (byte)row["materialCate01"]!.Value.Value;
            int numberRequired = (sbyte)row["itemNum01"]!.Value.Value;

            if (category == 4 && numberRequired > 1 && id >= 10100 && id < 10110)
            {
                row["itemNum01"].Value.SetValue(Const.SmithingCost);
                // if (numberRequired == 4) { } // if (numberRequired == 6) { } //  EquipMtrlSetParam param = new(row); //  param.itemNum01 = (sbyte)3;
            }
        }
    }
}