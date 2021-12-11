// Places certain messages to the console
public static class ConsoleMessageMOD
{
	public static void ErrorMessage(string reason = null)
    {
        if (reason != null)
            Log.Error(reason);

        Log.Error("ONELIFE: Some mod functionality may be broken.");
        Log.Error("ONELIFE: Perhaps restart the mod & game. If all fails, please verify integrity of game files.");
    }
}
