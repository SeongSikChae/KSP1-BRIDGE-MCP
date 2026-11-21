using KspBridge.Mod.Core;
using System;

namespace KspBridge.Mod.Modules.Debug
{
    /// <summary>
    /// Checks a save name for debug_save / debug_load (pure). A save is the file &lt;name&gt;.sfs in the current save folder.
    /// Only letters, digits, '-', '_' and spaces are allowed so the name never leaves the folder or gets rewritten by the game.
    /// </summary>
    public static class SaveName
    {
        public const string Default = "quicksave";
        public const int MaxLength = 64;

        /// <summary>The save name the game itself writes on every flight start; debug_save must not overwrite it.</summary>
        public const string Persistent = "persistent";

        /// <summary>Returns the name to use; bad input is bad_request. <paramref name="forSave"/> also rejects "persistent".</summary>
        public static string Parse(string raw, bool forSave)
        {
            if (raw == null)
                return Default;
            if (raw.Length == 0)
                throw BridgeException.BadRequest("'name' must not be empty");
            if (raw.Length > MaxLength)
                throw BridgeException.BadRequest("'name' must be at most " + MaxLength + " characters (got " + raw.Length + ")");
            foreach (char c in raw)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == ' ';
                if (!ok)
                    throw BridgeException.BadRequest("'name' may only contain letters, digits, '-', '_' and spaces (found '" + c + "')");
            }
            if (forSave && string.Equals(raw, Persistent, StringComparison.OrdinalIgnoreCase))
                throw BridgeException.BadRequest("'persistent' is the game's own save and cannot be written by debug_save");
            return raw;
        }
    }
}
