﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using lib.remnant2.analyzer.Model;
using lib.remnant2.analyzer;
using lib.remnant2.analyzer.Enums;
using lib.remnant2.analyzer.SaveLocation;
using lib.remnant2.saves.Model.Memory;
using Newtonsoft.Json;
using Remnant2SaveAnalyzer.Properties;
using Remnant2SaveAnalyzer.Logging;
using Serilog;
using Log = Remnant2SaveAnalyzer.Logging.Log;

namespace Remnant2SaveAnalyzer;

public class RemnantSave
{
    private Dataset? _remnantDataset;
    public Dataset? Dataset => _remnantDataset;

    private readonly string? _savePath;
    private readonly string? _profileFile;
    private static readonly object LoadLock = new();

    public static readonly Guid FolderIdSavedGames = new(0x4C5C32FF, 0xBB9D, 0x43B0, 0xB5, 0xB4, 0x2D, 0x72, 0xE5, 0x4E, 0xAA, 0xA4);
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    static extern string SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken = default);

    public RemnantSave(string path, bool skipUpdate = false)
    {
        string? profileFile = SaveUtils.GetSavePath(path, "profile");
        if (profileFile == null) {
            return;
        }

        _profileFile = profileFile;
        _savePath = path;
        if (!skipUpdate)
        {
            UpdateCharacters();
        }
    }

    public string? SaveFolderPath => _savePath;

    public string? SaveProfilePath => _profileFile;

    public bool Valid => _profileFile != null;

    public static bool ValidSaveFolder(string folder)
    {

        return SaveUtils.GetSavePath(folder, "profile") != null;

    }

    public void UpdateCharacters()
    {
        lock (LoadLock)
        {
            bool first = _remnantDataset == null;

            try
            {
                Analyzer.CheckBuildNumber(_savePath);
                _remnantDataset = Analyzer.Analyze(_savePath, _remnantDataset);
            }
            catch (Exception ex)
            {
                Notifications.Error(ex.ToString());
                return;
            }

            if (first)
            {
                if (Settings.Default.ReportPlayerInfo)
                {
                    ReportPlayerInfo();
                }

                Log.StartUpFinished();

                if (Settings.Default.DumpAnalyzerJson)
                {
                    DumpAnalyzerJson();
                }
            }
        }
    }

    private void DumpAnalyzerJson()
    {
        JsonSerializer serializer = new()
        {
            Formatting = Formatting.Indented,
            ContractResolver = new Exporter.IgnorePropertiesResolver([
                "ParentDataset",
                "Parent",
                "ProfileSaveFile",
                "ProfileNavigator",
                "WorldSaveFile",
                "WorldNavigator",
                "ParentCharacter"
            ]),
            NullValueHandling = NullValueHandling.Ignore
        };

        using StreamWriter sw = new(@"analyzer.json");
        using JsonWriter writer = new JsonTextWriter(sw);
        serializer.Serialize(writer, _remnantDataset);
    }

    private void ReportPlayerInfo()
    {
        Debug.Assert(_remnantDataset != null, nameof(_remnantDataset) + " != null");

        ILogger logger = Log.Logger
            .ForContext<RemnantSave>()
            .ForContext(Log.Category, Log.PlayerInfo);
            

        logger.Information($"Active character save: save_{_remnantDataset.ActiveCharacterIndex}.sav");

        FileHeader fhp = _remnantDataset.ProfileSaveFile!.FileHeader;
        logger.Information($"Profile save file version: {fhp.Version}, game build: {fhp.BuildNumber}");

        // Account Awards ------------------------------------------------------------
        logger.Information("BEGIN Account Awards");
        foreach (string award in _remnantDataset.AccountAwards)
        {
            LootItem? lootItem = ItemDb.GetItemByIdOrDefault(award);
            if (lootItem == null)
            {
                logger.Warning($"  UnknownMarker account award: {award}");
            }
            else
            {
                logger.Information($"  Account award: {lootItem.Name}");
            }
        }
        foreach (Dictionary<string, string> m in ItemDb.Db.Where(x => x["Type"] == "award" && !_remnantDataset.AccountAwards.Exists(y => y == x["Id"])))
        {
            logger.Information($"  Missing {Utils.Capitalize(m["Type"])}: {m["Name"]}");
        }
        logger.Information("END Account Awards");

        for (int index = 0; index < _remnantDataset.Characters.Count; index++)
        {
            // Character ------------------------------------------------------------
            Character character = _remnantDataset.Characters[index];
            int acquired = character.Profile.AcquiredItems;
            int missing = character.Profile.MissingItems.Count;
            int total = acquired + missing;



            logger.Information($"Character {index+1} (save_{character.Index}), Acquired Items: {acquired}, Missing Items: {missing}, Total: {total}");
            FileHeader fh = character.WorldSaveFile!.FileHeader;
            logger.Information($"World save file version: {fh.Version}, game build: {fh.BuildNumber}");
            logger.Information($"Is Hardcore: {character.Profile.IsHardcore}");
            logger.Information($"Trait Rank: {character.Profile.TraitRank}");
            logger.Information($"Last Saved Trait Points: {character.Profile.LastSavedTraitPoints}");
            logger.Information($"Power Level: {character.Profile.PowerLevel}");
            logger.Information($"Item Level: {character.Profile.ItemLevel}");
            logger.Information($"Gender: {character.Profile.Gender}");
            logger.Information($"Relic Charges: {character.Profile.RelicCharges}");
            // Equipment------------------------------------------------------------
            logger.Information($"BEGIN Equipment, Character {index + 1} (save_{character.Index})");
            List<InventoryItem> equipped = character.Profile.Inventory.Where(x => x.IsEquipped).ToList();
            IOrderedEnumerable<InventoryItem> equipment1 = equipped.Where(x => !x.IsTrait).OrderBy(x => x.EquippedSlot);
            IOrderedEnumerable<InventoryItem> traits1 = equipped.Where(x => x.IsTrait).OrderBy(x => x.EquippedSlot);

            foreach (InventoryItem r in equipment1)
            {
                if (Enum.IsDefined(typeof(EquipmentSlot), r.EquippedSlot!))
                {
                    string level = r.Level is > 0 ? $" +{r.Level}" : "";
                    LootItem? item = ItemDb.GetItemByProfileId(r.ProfileId);
                    logger.Information(item == null
                        ? $"!!{r.ProfileId} not found in the database!"
                        : $"  {Utils.FormatCamelAsWords(r.EquippedSlot.ToString())}: {item.Name}{level}");

                    foreach(InventoryItem m in character.Profile.Inventory.Where(x => x.EquippedModItemId == r.Id))
                    {
                        if (m.LootItem == null) continue;
                        logger.Information($"  {Utils.FormatEquipmentSlot(r.EquippedSlot.ToString(),m.LootItem.Type,m.Level ?? 1,m.LootItem.Name)}");
                    }
                }
            }

            foreach (var r in traits1.Select(x => new { ItemDb.GetItemByProfileId(x.ProfileId)!.Name, Item = x }).OrderBy(x => x.Name))
            {
                logger.Information($"  Trait: {r.Name}, Level {r.Item.Level}");
            }
            logger.Information($"END Equipment, Character {index + 1} (save_{character.Index}),");

            // Loadouts ------------------------------------------------------------
            logger.Information($"BEGIN Loadouts, Character {index + 1} (save_{character.Index})");
            if (character.Profile.Loadouts == null)
            {
                logger.Information("This character has no loadouts");
            }
            else
            {
                for (int i = 0; i < character.Profile.Loadouts.Count; i++)
                {
                    List<LoadoutRecord> loadoutRecords = character.Profile.Loadouts[i];
                    if (loadoutRecords.Count == 0)
                    {
                        logger.Information($"Loadout {i+1}: empty");
                    }
                    else
                    {
                        logger.Information($"Loadout {i + 1}:");
                        IOrderedEnumerable<LoadoutRecord> equipment = loadoutRecords.Where(x => x.Type == LoadoutRecordType.Equipment).OrderBy(x => x.Slot);
                        IOrderedEnumerable<LoadoutRecord> traits = loadoutRecords.Where(x => x.Type == LoadoutRecordType.Trait).OrderBy(x => x.Slot);
                        List<LoadoutRecord> other = loadoutRecords.Where(x => x.Type != LoadoutRecordType.Equipment && x.Type != LoadoutRecordType.Trait).ToList();
                        
                        foreach (LoadoutRecord r in equipment)
                        {
                            LoadoutSlot slot = (LoadoutSlot)r.Slot;
                            logger.Information($"  {Utils.FormatEquipmentSlot(slot.ToString(),r.ItemType,r.Level,r.Name)}");
                        }
                        
                        foreach (LoadoutRecord r in traits)
                        {
                            switch (r.Slot)
                            {
                                case 0:
                                case 1:
                                    continue; // These are archetypes we already display them in the equipment section, they are the same
                                case 2:
                                    logger.Information($"  Trait: {r.Name}, Level {r.Level}");
                                    break;
                                default:
                                    logger.Warning($"  !!!Unknown Slot {r.Name}, {r.Type}, {r.Slot}, {r.Level}");
                                    break;
                            }
                        }

                        if (other.Count > 0)
                        {
                            foreach (LoadoutRecord r in other)
                            {
                                logger.Warning($"  !!!Unknown Type {r.Name}, {r.Type}, {r.Slot}, {r.Level}");
                            }
                        }
                    }
                }
            }
            logger.Information($"END Loadouts, Character {index + 1} (save_{character.Index})");

            // Inventory ------------------------------------------------------------
            logger.Information($"BEGIN Inventory, Character {index+1} (save_{character.Index})");


            List<IGrouping<string, InventoryItem>> itemTypes = [.. character.Profile.Inventory
                .GroupBy(x => x.LootItem?.Type)
                .OrderBy(x=> x.Key)];

            foreach (IGrouping<string, InventoryItem> type in itemTypes)
            {
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (type.Key == null)
                {
                    foreach (InventoryItem item in type)
                    {
                        if (!Utils.IsKnownInventoryItem(Utils.GetNameFromProfileId(item.ProfileId)))
                        {
                            logger.Warning($"  Inventory item not found in database: {item.ProfileId}");
                        }
                    }
                }
                else
                {
                    if (type.Key == "armorspecial") continue;
                    logger.Information("  " + Utils.Capitalize(type.Key) + ":");

                    bool hasOne = false;
                    foreach (InventoryItem item in type.OrderBy(x => x.LootItem!.Name))
                    {
                        if (item.Quantity is 0) continue;
                        hasOne = true;
                        
                        string name = item.LootItem!.Name;
                        string quantity = item.Quantity.HasValue ? $" x{item.Quantity.Value}" : "";
                        string level = item.Level.HasValue ? $" +{item.Level.Value}" : "";
                        string favorited = item.Favorited ? ", favorite" : "";
                        string @new = item.New ? ", new" : "";
                        string slotted = item.EquippedModItemId is >= 0 ? ", slotted" :"";
                        if (item.LootItem!.Type == "fragment")
                        {
                            name = Utils.FormatRelicFragmentLevel(item.LootItem!.Name, item.Level ?? 1);
                            level = item.Level.HasValue ? $" (lvl {item.Level.Value})" : "";
                        }
                        if (item.LootItem!.Type == "archetype" || item.LootItem!.Type == "trait")
                        {
                            level = item.Level.HasValue ? $", Level {item.Level.Value}" : "";
                        }
                        logger.Information("    " + name + quantity + level + favorited + @new + slotted);
                        if (item.Id != null)
                        {
                            foreach (InventoryItem slottedItem in character.Profile.Inventory.Where(x => x.EquippedModItemId == item.Id))
                            {
                                LootItem? li = slottedItem.LootItem;
                                if (li == null)
                                {
                                    logger.Warning($"!!!!!!Equipped item with profileId: '{slottedItem.ProfileId}' not found");
                                }
                                else
                                {
                                    logger.Information($"      {Utils.FormatEquipmentSlot(string.Empty, li.Type, slottedItem.Level ?? 1, li.Name)}");
                                }
                            }
                        }
                    }
                    if (!hasOne)
                    {
                        logger.Information("    None");
                    }

                }
            }

            logger.Information($"END Inventory, Character {index+1} (save_{character.Index})");

            // Equipment------------------------------------------------------------
            logger.Information($"BEGIN Quick slots, Character {index + 1} (save_{character.Index})");
            foreach (InventoryItem item in character.Profile.QuickSlots)
            {
                logger.Information($"  {item.LootItem?.Name}");
            }
            logger.Information($"END Quick slots, Character {index + 1} (save_{character.Index})");

            // Thaen fruit
            if (character.Save.ThaenFruit == null)
            {
                logger.Information("Thaen fruit data not found");
            }
            else
            {
                logger.Information("Thaen fruit data");
                foreach (KeyValuePair<string, string> pair in character.Save.ThaenFruit.StringifiedRawData)
                {
                    logger.Information($"  {pair.Key}: {pair.Value}");
                }
            }

            // Campaign ------------------------------------------------------------
            logger.Information($"Save play time: {Utils.FormatPlaytime(character.Save.Playtime)}");
            foreach (Zone z in character.Save.Campaign.Zones)
            {
                logger.Information($"Campaign story: {z.Story}");
            }
            logger.Information($"Campaign difficulty: {character.Save.Campaign.Difficulty}");
            logger.Information($"Campaign play time: {Utils.FormatPlaytime(character.Save.Campaign.Playtime)}");
            string respawnPoint = character.Save.Campaign.RespawnPoint == null ? "Unknown" : character.Save.Campaign.RespawnPoint.ToString();
            logger.Information($"Campaign respawn point: {respawnPoint}");

            // Blood Moon
            if (character.Save.Campaign.BloodMoon == null)
            {
                logger.Information("Blood moon data not found");
            }
            else
            {
                logger.Information("Blood moon data");
                foreach (KeyValuePair<string, string> pair in character.Save.Campaign.BloodMoon.StringifiedRawData)
                {
                    logger.Information($"  {pair.Key}: {pair.Value}");
                }
            }

            // Campaign Quest Inventory ------------------------------------------------------------
            logger.Information($"BEGIN Quest inventory, Character {index+1} (save_{character.Index}), mode: campaign");
            // TODO
            IEnumerable<LootItem> lootItems = character.Save.Campaign.QuestInventory.Select(x => ItemDb.GetItemByProfileId(x.ProfileId)).Where(x => x != null).OrderBy(x => x!.Name)!;
            IEnumerable<InventoryItem> unknown = character.Save.Campaign.QuestInventory.Where(x => ItemDb.GetItemByProfileId(x.ProfileId) == null);
            foreach (InventoryItem s in unknown)
            {
                logger.Warning($"  Quest item not found in database: {s.ProfileId}");
            }

            foreach (LootItem lootItem in lootItems)
            {
                logger.Information("  " + lootItem.Name);
            }
            logger.Information($"END Quest inventory, Character {index+1} (save_{character.Index}), mode: campaign");

            if (character.Save.Adventure != null)
            {
                // Adventure ------------------------------------------------------------
                logger.Information($"Adventure story: {character.Save.Adventure.Zones[0].Story}");
                logger.Information($"Adventure difficulty: {character.Save.Adventure.Difficulty}");
                logger.Information($"Adventure play time: {Utils.FormatPlaytime(character.Save.Adventure.Playtime)}");
                respawnPoint = character.Save.Adventure.RespawnPoint == null ? "Unknown" : character.Save.Adventure.RespawnPoint.ToString();
                logger.Information($"Adventure respawn point: {respawnPoint}");

                // Blood Moon
                if (character.Save.Adventure.BloodMoon == null)
                {
                    logger.Information("Blood moon information not found");
                }
                else
                {
                    logger.Information("Blood moon data");
                    foreach (KeyValuePair<string, string> pair in character.Save.Adventure.BloodMoon.StringifiedRawData)
                    {
                        logger.Information($"  {pair.Key}: {pair.Value}");
                    }
                }

                // Adventure Quest Inventory ------------------------------------------------------------
                logger.Information($"BEGIN Quest inventory, Character {index+1} (save_{character.Index}), mode: adventure");
                lootItems = character.Save.Adventure.QuestInventory.Select(x => ItemDb.GetItemByProfileId(x.ProfileId)).Where(x => x != null).OrderBy(x => x!.Name)!;
                unknown = character.Save.Adventure.QuestInventory.Where(x => ItemDb.GetItemByProfileId(x.ProfileId) == null);
                foreach (InventoryItem s in unknown)
                {
                    logger.Warning($"  Quest item not found in database: {s.ProfileId}");
                }

                foreach (LootItem lootItem in lootItems)
                {
                    logger.Information("  " + lootItem.Name);
                }

                logger.Information($"END Quest inventory, Character {index+1} (save_{character.Index}), mode: adventure");
            }

            // Cass shop ------------------------------------------------------------
            logger.Information($"BEGIN Cass shop, Character {index+1} (save_{character.Index})");
            foreach (LootItem lootItem in character.Save.CassShop)
            {
                logger.Information("  " + lootItem.Name);
            }
            logger.Information($"END Cass shop, Character {index+1} (save_{character.Index})");

            // Quest log ------------------------------------------------------------
            logger.Information($"BEGIN Quest log, Character {index+1} (save_{character.Index})");
            lootItems = character.Save.QuestCompletedLog
                .Select(x => ItemDb.GetItemByIdOrDefault($"Quest_{x}")).Where(x => x != null)!;
            IEnumerable<string> unknowns = character.Save.QuestCompletedLog.Where(x => ItemDb.GetItemByIdOrDefault($"Quest_{x}") == null);
            foreach (string s in unknowns)
            {
                logger.Warning($"  Quest not found in database: {s}");
            }
            foreach (LootItem lootItem in lootItems)
            {
                logger.Information($"  {lootItem.Name} ({lootItem.Properties["Subtype"]})");
            }
            logger.Information($"END Quest log, Character {index+1} (save_{character.Index})");

            // Achievements ------------------------------------------------------------
            logger.Information($"BEGIN Achievements for Character {index+1} (save_{character.Index})");
            foreach (ObjectiveProgress objective in character.Profile.Objectives)
            {
                if (objective.Type == "achievement")
                {
                    logger.Information($"  {Utils.Capitalize(objective.Type)}: {objective.Description} - {objective.Progress}");
                }
            }

            foreach (Dictionary<string, string> m in ItemDb.Db.Where(x => x["Type"] == "achievement" && !character.Profile.Objectives.Exists(y => y.Id == x["Id"])))
            {
                logger.Information($"  Missing {Utils.Capitalize(m["Type"])}: {m["Name"]}");
            }

            logger.Information($"END Achievements for Character {index+1} (save_{character.Index})");

            // Challenges ------------------------------------------------------------
            logger.Information($"BEGIN Challenges for Character {index+1} (save_{character.Index})");
            foreach (ObjectiveProgress objective in character.Profile.Objectives)
            {
                if (objective.Type == "challenge")
                {
                    logger.Information($"  {Utils.Capitalize(objective.Type)}: {objective.Description} - {objective.Progress}");
                }
            }
            foreach (Dictionary<string, string> m in ItemDb.Db.Where(x => x["Type"] == "challenge" && !character.Profile.Objectives.Exists(y => y.Id == x["Id"])))
            {
                logger.Information($"  Missing {Utils.Capitalize(m["Type"])}: {m["Name"]}");
            }
            logger.Information($"END Challenges for Character {index+1} (save_{character.Index})");
            logger.Information("-----------------------------------------------------------------------------");
        }
    }

    public static string DefaultSaveFolder()
    {
        string saveFolder = SHGetKnownFolderPath(FolderIdSavedGames, 0) + @"\Remnant2";
        if (Directory.Exists($@"{saveFolder}\Steam"))
        {
            saveFolder += @"\Steam";
            string[] userFolders = Directory.GetDirectories(saveFolder);
            if (userFolders.Length > 0)
            {
                return userFolders[0];
            }
        }
        else
        {
            string[] folders = Directory.GetDirectories(saveFolder);
            if (folders.Length > 0)
            {
                return folders[0];
            }
        }
        return saveFolder;
    }

    public static string GetSaveFolder()
    {
        string saveFolder = Properties.Settings.Default.SaveFolder;
        if (saveFolder.EndsWith("\\wgs"))
        {
            saveFolder = SaveUtils.GetWgsFolderFromWgsBaseFolder(saveFolder)!;
        }
        return saveFolder;
    }
}

public enum RemnantSaveType
{
    Normal,
    WindowsStore
}