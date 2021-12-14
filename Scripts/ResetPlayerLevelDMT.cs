using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using System;
using System.Diagnostics;

public class MinEventActionResetPlayerLevel : MinEventActionRemoveBuff
{
    int firstLevelXP = 11024; 

    public override void Execute(MinEventParams _params)
    {
        Log.Out("ONELIFE: Death.");

        if (IsErasingWorld())
            for (int i = 0; i < this.targets.Count; i++)
            {
                EntityPlayerLocal entity = this.targets[i] as EntityPlayerLocal;
                if (entity != null)
                {
                    try
                    {
                        ClearEntity(entity);

                        // Force SAVE the world.
                        // Note that, this does not save the player's new state
                        GameManager.Instance.SaveWorld();
                        GameManager.Instance.SaveLocalPlayerData();
                        Log.Out("ONELIFE: Force saved the world.");
                    }
                    catch (Exception e)
                    {
                        Log.Error(e.ToString());
                        Log.Error("Line: " + (((new StackTrace(e, true)).GetFrame(0)).GetFileLineNumber().ToString()));
                    }
                }
            }
    }

    private void ClearEntity(EntityPlayerLocal player)
    {
        // --- Level/Skill/Xp Clearing --- \\
        player.Progression.Level = 1;
        player.Progression.ExpDeficit = 0; // Why is this stuff in EntityAlive and not EntityPlayer?
        player.Progression.ResetProgression(); // Encase this runs before the XML [Not familiar with ordering]
        player.Progression.SkillPoints = 0;
        player.Progression.ResetProgression(true); // For writing the 0 skill points
        player.Progression.ExpToNextLevel = firstLevelXP;

        // --- Quest Clearing --- \\

        // Clear all active quests
        if (player.QuestJournal != null && player.QuestJournal.quests != null)
        {
            var type = typeof(Quest).BaseType;
            for (int l = player.QuestJournal.quests.Count - 1; l >= 0; l--)
            {
                if (player.QuestJournal.quests[l].CurrentState == Quest.QuestState.InProgress || player.QuestJournal.quests[l].CurrentState == Quest.QuestState.ReadyForTurnIn)
                {
                    Quest quest = player.QuestJournal.quests[l];
                    quest.ResetObjectives();

                    // Need to stop the coroutine that gives the rewards for the quest at a later time

                    quest.ResetToRallyPointObjective();
                    player.QuestJournal.RemoveQuest(quest);
                }
            }
        }

        // Just some extra re-assurance
        if (player.QuestJournal.quests != null)
        {
            player.QuestJournal.quests.Clear();
        }

        player.QuestJournal.RemoveAllSharedQuests();

        if (player.QuestJournal.QuestFactionPoints != null)
        {
            player.QuestJournal.QuestFactionPoints.Clear();
        }

        // Give back starting quest
        // This has an issue as a result of how the game is built
        // A Coroutine for the rewards may continue to run in the background 
        // If this occurs, upon completion of the tutorial, players may receive 8 or even 12 skill points instead of 4
        // So, we will instead give them back all the stuff they would do in the tutorial instead
        /*
        if (player.QuestJournal != null)
        {
            const string startingQuest = "quest_BasicSurvival1";
            Quest myQuest = QuestClass.CreateQuest(startingQuest);
            myQuest.QuestGiverID = -1;
            player.QuestJournal.AddQuest(myQuest);
            player.QuestJournal.ActiveQuest = myQuest;
            player.QuestJournal.TrackedQuest = myQuest;
        }
        */

        // --- Spawns Clearing --- \\
        // Encase somehow they placed spawns
        if (player.SpawnPoints != null)
        {
            player.SpawnPoints.Clear();
            player.selectedSpawnPointKey = -1L;
        }

        // --- Journal Clearing --- \\
        if (player.PlayerJournal != null && player.PlayerJournal.List != null)
        {
            player.PlayerJournal.List.Clear();
        }

        // --- Waypoint Clearing --- \\
        if (player.Waypoints != null)
        {
            // Manually reset the waypoints so they don't remain on map
            foreach (Waypoint i in player.Waypoints.List)
            {
                i.icon = string.Empty;
                i.name = string.Empty;
            }

            player.Waypoints.List.Clear();
        }

        if (player.WaypointInvites != null)
        {
            player.WaypointInvites.Clear();
        }

        // --- Stat reset --- \\
        player.Stats.ResetStats();
        player.Stats.Health.OriginalValue = 100;
        player.Stats.Health.OriginalMax = 100;
        player.Stats.Health.ResetAll();
        player.Stats.CoreTemp.ResetAll();
        player.Stats.Stamina.OriginalValue = 75;
        player.Stats.Stamina.OriginalMax = 100;
        player.Stats.Stamina.ResetAll();
        player.Stats.Water.OriginalValue = 75;
        player.Stats.Water.OriginalMax = 100;
        player.Stats.Water.ResetAll();
        player.Stats.Food.OriginalValue = 75;
        player.Stats.Food.OriginalMax = 100;
        player.Stats.Food.ResetAll();

        // FIXME doesn't do anything? Might be for the map itself
        player.world.ObjectOnMapRemove(EnumMapObjectType.Backpack); // force remove backpack
        player.world.ObjectOnMapRemove(EnumMapObjectType.LandClaim); // force remove landclaims
        player.world.ObjectOnMapRemove(EnumMapObjectType.StartPoint); // force remove starting point
        player.world.ObjectOnMapRemove(EnumMapObjectType.MapMarker); // force remove starting point
        player.world.ObjectOnMapRemove(EnumMapObjectType.MapQuickMarker); // force remove starting point

        // -- Refresh Inventory --- \\

        // All starting items aside from land claim
        string[] startingGear = { "drinkJarBoiledWater", "foodCanChili", "medicalFirstAidBandage", "meleeToolTorch", "noteDuke01" };
        foreach (string i in startingGear)
        {
            if (!player.inventory.AddItem(new ItemStack(ItemClass.GetItem(i), 1)))
            {
                ConsoleMessageMOD.ErrorMessage("OneLife: Could not add item to inventory.");
            }
        }

        // Add the resources the player would get from doing the tutorial to their bag
        string[] bagItems = { "resourceYuccaFibers", "resourceWood", "resourceRockSmall", "resourceFeather" };
        int[] quantityItems = { 25, 23, 8, 1 };
        for (int i = 0; i < bagItems.Length; i++)
        {
            string item = bagItems[i];
            int quantity = quantityItems[i];

            if (!player.bag.AddItem(new ItemStack(ItemClass.GetItem(item), quantity)))
            {
                ConsoleMessageMOD.ErrorMessage("OneLife: Could not add item to inventory.");
            }
        }

        // Handle XP from tutorial
        int[] tutXp = { 50, 50, 50, 50, 50, 50, 50, 50 };
        int tot_xp_bonus = 0;

        foreach (int i in tutXp)
            tot_xp_bonus += i;

        player.Progression.ExpToNextLevel -= tot_xp_bonus;

        // Rewarding the skill points from tutorial
        player.Progression.SkillPoints = 4;
        player.Progression.ResetProgression(true); // For saving the result

        // Land claim block
        if (!player.inventory.AddItem(new ItemStack(new ItemValue(
            Block.nameIdMapping.GetIdForName("keystoneBlock")
            ), 1)))
        {
            ConsoleMessageMOD.ErrorMessage("OneLife: Could not add item to inventory.");
        }

        player.saveInventory = player.inventory;

        // --- Block Clearing --- \\

        // --- Misc Clearing --- \\
        player.SetDroppedBackpackPosition(Vector3i.zero);

        // Player starts game bleeding
        player.Buffs.AddBuff("triggerBleeding");

        // --- Turrets and Vehicles --- \\
        // Note: Fairly inefficient
        // Does not work
        // This likely only works if you spawn near the vehicles or turrets (Hence why im leaving it here)
        // The problem may be that the entities aren't loaded in the map if you aren't near them
        string playerID = Platform.UserProfiles.PrimaryUser.PlayerId;
        // Log.Out("Player: " + playerID);
        for (int i = 0; i < GameManager.Instance.World.Entities.list.Count; i++)
        {
            Entity entity = GameManager.Instance.World.Entities.list[i];

            if (entity is EntityVehicle entityVehicle) // Steam ID
            {
                // Log.Out("Vehicle: " + entityVehicle.GetOwner());
                // Log.Out("Vehicle: " + entityVehicle.GetVehicle().OwnerId);
                if (entityVehicle.IsOwner(playerID))
                {
                    entityVehicle.transform.Translate(0, -5000, 0); // Didn't see an easy way to remove the inventory, so now the dropped loot is just unaquirable until it despawns
                    entityVehicle.Kill();
                }
            }
            else if (entity is EntityTurret entityTurret)
            {
                // Log.Out("Vehicle: " + entityTurret.OwnerID);
                if (entityTurret.OwnerID == playerID)
                {
                    entityTurret.AmmoCount = 0;
                    entityTurret.InitInventory(); // Removes the previous inventory
                    entityTurret.Kill(DamageResponse.New(true));
                }
            }
        }

        // Finally, the map clear
        // This is by far the most expensive, so we do this last
        if (player.ChunkObserver != null && player.ChunkObserver.mapDatabase != null)
        {
            ClearMapChunkDatabase(player.ChunkObserver.mapDatabase);
            TrackingManagerMOD.DeleteStorage();
        }
    }

    // Obtained from Hardcore DMT mod by KhaineGB => Map Clear method
    private void ClearMapChunkDatabase(MapChunkDatabase mapDatabase)
    {
        const BindingFlags _NonPublicFlags = BindingFlags.NonPublic | BindingFlags.Instance;

        // The map chunk data is in private instance fields in the base type
        var type = typeof(MapChunkDatabase).BaseType;

        var catalog = (DictionaryKeyList<int, int>)type.GetField("catalog", _NonPublicFlags).GetValue(mapDatabase);
        if (catalog != null)
            catalog.Clear();

        var database = (DictionarySave<int, ushort[]>)type.GetField("database", _NonPublicFlags).GetValue(mapDatabase);
        if (database != null)
            database.Clear();

        var dirty = (Dictionary<int, bool>)type.GetField("dirty", _NonPublicFlags).GetValue(mapDatabase);
        if (dirty != null)
            dirty.Clear();
    }

    public override bool ParseXmlAttribute(XmlAttribute _attribute)
    {
        bool flag = base.ParseXmlAttribute(_attribute);
        if (!flag)
        {
            string name = _attribute.Name;
            if (name != null)
            {
                if (name == "value" )
                {
                    int.TryParse(_attribute.Value, out firstLevelXP);
                    return true;
                }
            }
        }
        return flag;
    }

    // Check to see if we will erase the world from this death
    // Here we can try to remove any 'unfair' deaths
    // For example, If the frame rate dropped to 1fps
    private bool IsErasingWorld()
    {
        return true;
    }
}