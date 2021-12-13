using UnityEngine;
using InControl;
using System.Xml;
using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.IO;
using System.Collections;
using System.Collections.Generic;

/* This file contains all classes related to inventory tracking */

public class MinEventActionInventoryTracker : MinEventActionRemoveBuff
{
    public override void Execute(MinEventParams _params)
    {
        Log.Warning("ONELIFE: Active!");

        if (_params.Self is EntityPlayerLocal)
        {
            new TrackingManagerMOD((EntityPlayerLocal) _params.Self);
            Log.Out((_params.Self == null).ToString());
        }
        else
        {
            ConsoleMessageMOD.ErrorMessage("Could not get the local players instance.");
            TrackingManagerMOD.Refresh();
        }
    }

    public override bool ParseXmlAttribute(XmlAttribute _attribute)
    {
        return base.ParseXmlAttribute(_attribute);
    }
}

// Enforces that only one TrackingSystemMOD is placed on the player
// A singleTon though its re-created whenever the player joins a game
public class TrackingManagerMOD
{
    static TrackingManagerMOD singleInstance = null;
    static TrackingSystemMOD system = null;

    public TrackingManagerMOD(EntityPlayerLocal selfPlayer)
    {
        // Player may have exited and re-joined a world
        // Could be the same world or a different one
        if (system != null)
        {
            Refresh();
        }

        system = (selfPlayer.gameObject.AddComponent<TrackingSystemMOD>()).Initialize(selfPlayer);
        singleInstance = this;
    }

    public static void Refresh()
    {
        if (singleInstance != null)
        {
            if (system != null)
                GameObject.Destroy(system);

            singleInstance = null;
            system = null;
        }
    }

    public static TrackingManagerMOD GetInstance()
    {
        return singleInstance;
    }

    // Callback, called when the player dies
    // Delete all the storage items they have placed
    public static void DeleteStorage()
    {
        if (singleInstance == null || system == null)
        {
            ConsoleMessageMOD.ErrorMessage();
        }
        else
        {
            system.EraseWorldParts();
        }
    }
}

// Tracks the players block placements
// When a player places "special" blocks (Blocks we want to destroy on death)
// Their coordinates are written to a file.
// Destroy that block once they are near it if the player dies
// Note that the 'special' blocks will remain in the file even if it is destroyed.

// This method is unfortunantely very inefficient, sorry if its an eye soar! Will be difficult for you to modify. It was a pain to have to do so many work-arounds due to source code limitations.
// I couldn't get a more efficient way due to how object deletion works on parts of the map that aren't loaded
// On the bright side, its still way more efficient than looking through the entire map
// I decided to use coroutines so that certain checks don't run every frame, and instead only run every x seconds
// The x is arbitary to be honest, but its not very important what it is since you don't load new areas of the map quickly
public class TrackingSystemMOD : MonoBehaviour
{
    // ~~~~~~~~~~~~~~~~~~~~~~~ \\
    // ~~~~ Global Fields  ~~~~ \\
    // ~~~~~~~~~~~~~~~~~~~~~ ~~~ \\
    private const BindingFlags _NonPublicFlags = BindingFlags.NonPublic | BindingFlags.Instance;
    int[] deleteItemsIDs;
    EntityPlayerLocal selfPlayer;
    FieldInfo reflectionBindingsField = null;
    FieldInfo reflectionDeviceField = null;
    ItemInventoryData lastHeldBlock = null;
    FileSystemStorageMOD storageFile = null;
    LinkedList<BlockInfo> interestingBlocks = new LinkedList<BlockInfo>();
    LinkedList<BlockInfo> BlocksToBeDestroyed = new LinkedList<BlockInfo>();
    bool runningBlockDestroyer = false;
    BlockInfo INVALID_BLOCK;

    // ~~~~~~~~~~~~~~~~~~~~~~~~~ \\
    // ~~~~ Public functions ~~~~ \\
    // ~~~~~~~~~~~~~~~~~~~~~~~~~~~ \\

    // Destroys the blocks
    // Check to see if any player has loaded the world chunk
    // If they have, then we can destroy it safely
    public IEnumerator BlockDestroyer()
    {
        // Need to be very careful not to keep adding to the stack. Keep local variables in function calls or outside loop
        runningBlockDestroyer = true;
        const int DESTROYING_INTERVAL = 20;
        WaitForSeconds waiter = new WaitForSeconds(DESTROYING_INTERVAL);
        List<EntityPlayer> playerList = GameManager.Instance.World.GetPlayers();
        List<BlockInfo> destroyedBlocks = new List<BlockInfo>();

        while (runningBlockDestroyer)
        {
            playerList = GameManager.Instance.World.GetPlayers(); // Encase more players have joined the game since

            // Checking for all players
            foreach (EntityPlayer player in playerList)
            {
                foreach (BlockInfo block in BlocksToBeDestroyed)
                {
                    if (player.world.GetChunkFromWorldPos(block.x, block.y, block.z) != null)
                    {
                        DeleteBlock(block.id, block.x, block.y, block.z, player.world);
                        destroyedBlocks.Add(block);
                    }
                }
            }

            if (destroyedBlocks.Count > 0)
            {
                // Remove the destroyed blocks
                foreach (BlockInfo destroyedBlock in destroyedBlocks)
                {
                    interestingBlocks.Remove(destroyedBlock);
                    BlocksToBeDestroyed.Remove(destroyedBlock);
                }

                destroyedBlocks.Clear();

                // Write the new results of whats left to be deleted
                storageFile.WriteListOverwriteToBeDeleted(BlocksToBeDestroyed);
            }

            yield return waiter;
        }
    }

    // Initialize fields
    public TrackingSystemMOD Initialize(EntityPlayerLocal selfPlayer)
    {
        const int ID_THAT_DOESNT_EXIST = -2;
        int INVALID_POS = -99999;
        INVALID_BLOCK = new BlockInfo(ID_THAT_DOESNT_EXIST, INVALID_POS, INVALID_POS, INVALID_POS);

        // Setup the items that we would like to delete when the player dies
        // We will use the block ID instead of the names for efficiency
        string[] deleteItems = { 
            // Craftable storage types
            "cntSecureStorageChest", "cntWoodFurnitureBlockVariantHelper",  "cntDeskSafe", "cntWallSafe", "cntGunSafe", "cntGreenDrawerSecure",

            // Craftable station types
            "campfire", "workbench", "forge", "chemistryStation", "cementMixer", "generatorbank",

            // Misc types
            "keystoneBlock"//, "autoTurret", "shotgunTurret"
        };
        int[] ids = new int[deleteItems.Length];

        int index = 0;
        foreach (String item in deleteItems)
        {
            // For some reason the block id mapping is unavailable for the chests and special blocks, so we grab the ID from the more general ItemClass instead
            ids[index++] = ItemClass.GetItemClass(item).GetBlock().blockID;
        }

        deleteItemsIDs = ids;
        this.selfPlayer = selfPlayer;
        SetupReflectionFields();

        storageFile = new FileSystemStorageMOD();

        // Start the deletion system
        this.EraseWorldParts(true);
        this.StartCoroutine(BlockDestroyer());

        return this;
    }

    // Erase all storage items
    // Chests, fires, etc .. [Anything that can have items in it]
    public void EraseWorldParts(bool initRun = false)
    {
        try
        {
            if (initRun)
            {
                writeFileToList(storageFile.GetFilePathToBeDeleted(), BlocksToBeDestroyed);
                writeFileToList(storageFile.GetFilePathInterestingItems(), interestingBlocks);
            }
            else
            {
                // Swap interesting blocks into blocks that will be destroyed
                writeFileToList(storageFile.GetFilePathInterestingItems(), BlocksToBeDestroyed);

                foreach (BlockInfo block in BlocksToBeDestroyed)
                {
                    storageFile.WriteToBeDeleted(block);
                }

                storageFile.ClearInteresting();
            }
        }
        catch (FileNotFoundException)
        {
            Log.Out("ONELIFE: First load");
        }
        catch (Exception e)
        {
            // There are a couple other possible exceptions that can be raised here
            // All of them will just break the deletion feature of the mod
            Log.Out(e.Message);
            ConsoleMessageMOD.ErrorMessage();
        }
    }

    private void writeFileToList(string filePath, LinkedList<BlockInfo> list)
    {
        using (StreamReader reader = new StreamReader(filePath))
        {
            // Read through each line in the file
            // Delete every item
            string line;
            string firstLine;
            bool lineZero = true;
            while ((line = reader.ReadLine()) != null)
            {
                // Ignore the first line in the file
                if (lineZero)
                {
                    firstLine = line;
                    lineZero = false;
                    continue;
                }

                // Parse the line
                // Lines are of the format
                // objid,x,y,z
                string[] splitItems = line.Split(',');
                int id = int.Parse(splitItems[0]);
                int xPos = int.Parse(splitItems[1]);
                int yPos = int.Parse(splitItems[2]);
                int zPos = int.Parse(splitItems[3]);

                // Add these to the list of blocks that we should destroy
                list.AddFirst(new BlockInfo(id, xPos, yPos, zPos));
            }
        }
    }

    // ~~~~~~~~~~~~~~~~~~~~~~~~~~~ \\
    // ~~~~ Private functions ~~~~ \\
    // ~~~~~~~~~~~~~~~~~~~~~~~~~~~ \\

    // Shouldn't impact frame rate noticably, although this is pretty slow due to GetValue calls
    // GetField reflection is cached.

    // TODO: Add an additional stack change check
    // The PlaceInterestingBlock runs too many times before the block is actually placed
    private void Update()
    {
        if (lastHeldBlock != null && lastHeldBlock.item != null && lastHeldBlock.item.GetBlock() != null)
        {
            if (SecondaryPress())
            {
                // CanPlaceBlockAt in selfPlayer.world is interesting
                if (PlacedInterestingBlock() && selfPlayer.HitInfo != null)
                {
                    BlockInfo block = FindBlockOfInterest(selfPlayer.HitInfo.lastBlockPos);

                    // If the interesting block exists at that location, and we are not already looking for it, then lets delete it
                    if (block != INVALID_BLOCK)
                    {
                        // Writing to file
                        // This just saves our state for when we close the game or it crashes
                        storageFile.WriteInterestingBlocks(block);
                        interestingBlocks.AddFirst(block);

                        lastHeldBlock = null;
                    }
                }
            }
        }

        if (selfPlayer.inventory != null && selfPlayer.inventory.holdingItemData != null)
            lastHeldBlock = selfPlayer.inventory.holdingItemData;
    }

    // Given a 'rough area' of where a block is
    // Return the block
    // If none was found, INVALID_BLOCK is given
    private BlockInfo FindBlockOfInterest(Vector3i guessedPosition)
    {
        // First check if the guessed position was correct
        // If its not, then lets check for an 'off by x' (If it was slightly off)
        BlockValue blockVal = GameManager.Instance.World.GetBlock(guessedPosition.x, guessedPosition.y, guessedPosition.z);

        if (blockVal.Block == null)
            return INVALID_BLOCK;

        int guessID = blockVal.Block.blockID;
        BlockInfo firstGuess = IntestingAndNotContained(guessID, guessedPosition.x, guessedPosition.y, guessedPosition.z);
        if (firstGuess != INVALID_BLOCK)
            return firstGuess;

        // First check failed
        // Check for all blocks radius away from the guess position
        // Encase of an 'off by 1', the raycast from lastBlockPos was sometimes incorrect
        const int RADIUS = 1;
        for (int x = guessedPosition.x - RADIUS; x <= guessedPosition.x + RADIUS; x++)
        {
            for (int y = guessedPosition.y - RADIUS; y <= guessedPosition.y + RADIUS; y++)
            {
                for (int z = guessedPosition.z - RADIUS; z <= guessedPosition.z + RADIUS; z++)
                {
                    // We already checked this
                    if (x == guessedPosition.x && y == guessedPosition.y && z == guessedPosition.z)
                        continue;

                    int idMisc = GameManager.Instance.World.GetBlock(x, y, z).Block.blockID;
                    BlockInfo miscGuess = IntestingAndNotContained(idMisc, x, y, z);
                    if (miscGuess != INVALID_BLOCK)
                        return miscGuess;
                }
            }
        }

        return INVALID_BLOCK;
    }

    // Returns true if the block is both interesting and not contained in our InterestingBlocks list
    private BlockInfo IntestingAndNotContained(int id, int x, int y, int z)
    {
        if (IsInterestingBlock(id))
        {
            BlockInfo block = new BlockInfo(id, x, y, z);

            if (!interestingBlocks.Contains(block))
                return block;
        }

        return INVALID_BLOCK;
    }

    // Returns true if the given block is interesting
    // TODO: Use hashing, this should be O(1)
    private bool IsInterestingBlock(int blockID)
    {
        foreach (int i in deleteItemsIDs)
        {
            if (blockID == i)
            {
                return true;
            }
        }

        return false;
    }

    // Returns true if the user placed an interesting block
    private bool PlacedInterestingBlock()
    {
        // Check if we are holding the right item as we press our secondary
        return IsInterestingBlock(lastHeldBlock.item.GetBlock().blockID);
    }

    // Returns true if the secondary action is pressed
    private bool SecondaryPress()
    {
        if (selfPlayer.playerInput.Activate != null)
        {
            if (reflectionBindingsField != null && reflectionDeviceField != null)
            {
                // Going through the bindings for the secondary action.
                ReadOnlyCollection<BindingSource> bindings = (ReadOnlyCollection<BindingSource>)((reflectionBindingsField).GetValue(selfPlayer.playerInput.Secondary));
                InputDevice deviceInput = (InputDevice)((reflectionDeviceField).GetValue(selfPlayer.playerInput.Secondary));

                foreach (BindingSource source in bindings)
                {
                    if (source.GetState(deviceInput))
                    {
                        return true;
                    }
                }
            }
            else
            {
                SetupReflectionFields();
            }
        }

        return false;
    }

    // Setup the fields that require reflection
    // Encase the fields have not yet been initialized in the game state
    // We call this again if they are null
    private void SetupReflectionFields()
    {
        // Setup reflection of private variables
        var type = typeof(PlayerAction); 
        if (reflectionBindingsField == null)
            reflectionBindingsField = type.GetField("bindings", _NonPublicFlags);
        if (reflectionDeviceField == null)
            reflectionDeviceField = type.GetField("activeDevice", _NonPublicFlags);
    }

    // Save the currently tracked items to a file for later reference when the game is loaded again
    private void Save()
    {
        storageFile.WriteListOverwriteToBeDeleted(BlocksToBeDestroyed);
    }

    private void OnDestroy ()
    {
        Save();
        this.StopAllCoroutines();
    }

    // Special thanks to Kanaverum & StompyNZ for the Discord aid on where to delete blocks
    private void DeleteBlock(int id, int x, int y, int z, World world)
    {
        Log.Out("ONELIFE: Deleting a block!");
        BlockValue block = new BlockValue(0); // Replace it with air
        List<BlockChangeInfo> blocks = new List<BlockChangeInfo>();

        // Storage items contents are stored as TileEntities in WorldChunks
        // The block itself does not map to this
        // So we need to get the world chunk and do this the hard way
        // Special thanks to Kanaverum, Soleil Plein, and mgreter for direction on this portion
        if (world.GetChunkFromWorldPos(new Vector3i(x, y, z)) is Chunk worldChunk)
        {
            TileEntity entity = world.GetTileEntity(worldChunk.ClrIdx, new Vector3i(x, y, z));
            if (entity != null)
            {
                if (entity is TileEntityLootContainer lootEntity)
                {
                    lootEntity.SetEmpty();
                }

                else if (entity is TileEntityForge forgeEntity)
                {
                    Array.Clear(forgeEntity.GetInput(), 0, forgeEntity.GetInput().Length);
                    Array.Clear(forgeEntity.GetFuel(), 0, forgeEntity.GetFuel().Length);
                    forgeEntity.GetOutput().Clear();
                    forgeEntity.GetMold().Clear();
                }

                else if (entity is TileEntityWorkstation stationEntity)
                {
                    Array.Clear(stationEntity.Tools, 0, stationEntity.Tools.Length);
                    Array.Clear(stationEntity.Input, 0, stationEntity.Tools.Length);
                    Array.Clear(stationEntity.Fuel, 0, stationEntity.Tools.Length);
                    Array.Clear(stationEntity.Output, 0, stationEntity.Tools.Length);
                }

                /*
                else if (entity is EntityTurret turretEntity)
                {
                    turretEntity.AmmoCount = 0;

                    if (turretEntity.inventory != null)
                        turretEntity.inventory.Clear();
                }
                    Turrets are players?!
                    NOTE: Turrets will drop the ammo atm
                    They are not TileEntities. Need to rework a bit for this special case
                */
            }
        }

        blocks.Add(new BlockChangeInfo(x, y, z, block, true));
        world.SetBlocksRPC(blocks);
    }
}