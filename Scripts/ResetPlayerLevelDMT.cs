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
        Log.Out("A user has died.");
        Log.Out("[relog] A user has died.");
        Log.Out("[relog] A user has died.");

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
                        // TODO use gamemanager saves instead
                        //entity.world.Save();
                        //entity.world.SaveWorldState();
                        //GameManager.Instance.SaveAndCleanupWorld();
                        GameManager.Instance.SaveWorld();
                        Log.Out("Saved the world!");
                        GameManager.Instance.SaveLocalPlayerData();
                        Log.Out("Saved the player!");
                        // GamePrefs.Save ??
                        // SaveUserIni

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

        if (player.QuestJournal.quests != null)
        {
            player.QuestJournal.quests.Clear();
        
        }

        if (player.QuestJournal.sharedQuestEntries != null)
        {
            player.QuestJournal.sharedQuestEntries.Clear();
        }

        if (player.QuestJournal.QuestFactionPoints != null)
        {
            player.QuestJournal.QuestFactionPoints.Clear();
        }

        // Clear all active quests
        if (player.QuestJournal != null && player.QuestJournal.quests != null)
        {
            for (int l = player.QuestJournal.quests.Count - 1; l >= 0; l--)
            {
                if (player.QuestJournal.quests[l].CurrentState == Quest.QuestState.InProgress || player.QuestJournal.quests[l].CurrentState == Quest.QuestState.ReadyForTurnIn)
                {
                    Quest quest = player.QuestJournal.quests[l];

                    player.QuestJournal.RemoveQuest(quest);
                }
            }
        }

        // Give back starting quest
        if (player.QuestJournal != null)
        {
            const string startingQuest = "quest_BasicSurvival1";
            Quest myQuest = QuestClass.CreateQuest(startingQuest);
            myQuest.QuestGiverID = -1;
            player.QuestJournal.AddQuest(myQuest);
            player.QuestJournal.ActiveQuest = myQuest;
            player.QuestJournal.TrackedQuest = myQuest;
        }

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
        // This is mainly written in the XML files to assure the values are kept this way.
        player.Stats.ResetStats();
        player.Stats.Health = new Stat(player, 100, 100);
        player.Stats.Stamina = new Stat(player, 75, 100);
        player.Stats.Food = new Stat(player, 75, 100);
        player.Stats.Water = new Stat(player, 75, 100);

        // FIXME doesn't do anything? Might be for the map itself
        player.world.ObjectOnMapRemove(EnumMapObjectType.Backpack); // force remove backpack
        player.world.ObjectOnMapRemove(EnumMapObjectType.LandClaim); // force remove landclaims
        player.world.ObjectOnMapRemove(EnumMapObjectType.StartPoint); // force remove starting point
        //player.backpackNavObject.
        // player.world.objectsOnMap.Clear(); // private

        // -- Refresh Inventory --- \\

        // All starting items aside from land claim
        string[] startingGear = { "drinkJarBoiledWater", "foodCanChili", "medicalFirstAidBandage", "meleeToolTorch", "noteDuke01" };
        foreach (string i in startingGear)
        {
            if (!player.inventory.AddItem(new ItemStack(ItemClass.GetItem(i), 1)))
            {
                ConsoleMessageMOD.ErrorMessage("Could not add item to inventory.");
            }
        }

        // Land claim block
        if (!player.inventory.AddItem(new ItemStack(new ItemValue(
            Block.nameIdMapping.GetIdForName("keystoneBlock")
            ), 1)))
        {
            ConsoleMessageMOD.ErrorMessage("Could not add item to inventory.");
        }

        player.saveInventory = player.inventory;

        // --- Block Clearing --- \\

        // --- Misc Clearing --- \\
        if (player.Buffs.ActiveBuffs != null)
        {
            player.Buffs.ActiveBuffs.Clear();
        }

        // Player starts game bleeding
        player.Buffs.AddBuff("triggerBleeding");

        // Finally, the map clear
        // This is by far the most expensive, so we do this last
        if (player.ChunkObserver != null && player.ChunkObserver.mapDatabase != null)
        {
            ClearMapChunkDatabase(player.ChunkObserver.mapDatabase);
            TrackingManagerMOD.DeleteStorage();
        }
    }

    // Saves the players new stats to the game (Cannot rage quit)
    private void SaveStatReset()
    {
        // Crashes the game
        //XmlDocument doc = new XmlDocument();
       // doc.Load("/.../.../.../Data/Config/entityclasses.xml");

        //XmlReader reader = XmlReader.Create(new System.IO.StringReader(doc));
        //XmlNode entity_player = doc.SelectSingleNode("//entity_classes/entity_class[@name='playerMale']");

        /*if (entity_player == null)
        {
            Log.Out("Could not force save the game.");
            Log.Out("Please assure this modlet is in the correct folder path.");
            return;
        }

        foreach (XmlNode node in entity_player)
        {
          //  if (innerNode["effect_group"])
        }*/
    }

    // Obtained from Hardcore DMT mod by KhaineGB => Map Clear method
    private void ClearMapChunkDatabase(MapChunkDatabase mapDatabase)
    {
        const BindingFlags _NonPublicFlags = BindingFlags.NonPublic | BindingFlags.Instance;
        Log.Out("Clearing player map chunk database...");

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

        Log.Out("Player map chunk database cleared.");
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