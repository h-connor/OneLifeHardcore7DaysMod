using System.IO;
using System.Collections.Generic;

// Manages the reads and writes of the users Storage since the mod has been used
public class FileSystemStorageMOD
{
	private readonly string filePathInterestingItems = GamePrefs.GetString(EnumGamePrefs.SaveGameFolder) +
		'/' + GamePrefs.GetString(EnumGamePrefs.GameWorld) + 
		'/' + GamePrefs.GetString(EnumGamePrefs.GameName) + "/storageHardcoreDeadIsDead.txt";
	private readonly string filePathToBeDeleted = GamePrefs.GetString(EnumGamePrefs.SaveGameFolder) +
	'/' + GamePrefs.GetString(EnumGamePrefs.GameWorld) +
	'/' + GamePrefs.GetString(EnumGamePrefs.GameName) + "/deletionHardcoreDeadIsDead.txt";

	private const string INIT_STRING = "Do not touch this file!\n";

	public FileSystemStorageMOD(){
		InitFileContents();
	}

	public void WriteListOverwriteToBeDeleted(LinkedList<BlockInfo> list)
    {
		using (StreamWriter writer = new StreamWriter(filePathToBeDeleted))
        {
			writer.Write(INIT_STRING);
			
			foreach (BlockInfo blockPos in list)
            {
				writer.Write(blockPos.ToString() + '\n');
            }
        }
    }

	public void WriteToBeDeleted(BlockInfo block)
	{
		using (StreamWriter sr = File.AppendText(filePathToBeDeleted))
		{
			sr.Write(block.ToString() + '\n');
		}
	}

	public void WriteInterestingBlocks(BlockInfo block)
    {
		using (StreamWriter sr = File.AppendText(filePathInterestingItems))
		{
			sr.Write(block.ToString() + '\n');
		}
	}

	// Was to lazy to make an iterator
	// Returns the file path, so we can read the file elsewhere
	public string GetFilePathInterestingItems()
    {
		return filePathInterestingItems;
    }

	public string GetFilePathToBeDeleted()
	{
		return filePathToBeDeleted;
	}

	// Clear the files contents entirely [reset]
	// Only clears interesting items!
	public void ClearInteresting()
    {
		using(FileStream fs = File.Open(filePathInterestingItems, FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
			lock(fs)
            {
				fs.SetLength(0);
            }
        }

		InitFileContents(true, false);
	}

	// Initialize the file for storage
	private void InitFileContents(bool overrideExistCheckInteresting = false, bool overrideExistCheckDeleted = false)
    {
		if (overrideExistCheckInteresting || !File.Exists(filePathInterestingItems))
			using (StreamWriter sr = new StreamWriter(filePathInterestingItems))
			{
				sr.Write(INIT_STRING);
			}

		if (overrideExistCheckDeleted || !File.Exists(filePathToBeDeleted))
			using (StreamWriter sr = new StreamWriter(filePathToBeDeleted))
			{
				sr.Write(INIT_STRING);
			}
	}
}
