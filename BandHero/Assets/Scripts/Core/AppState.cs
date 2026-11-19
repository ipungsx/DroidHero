using System.Collections.Generic;

namespace BandHero.Core
{
    /// <summary>
    /// Session-scoped store so screens can hand data to each other without a scene graph.
    /// </summary>
    public static class AppState
    {
        public static List<SongEntry> Songs = new List<SongEntry>();
        public static SongEntry SelectedSong;
        public static string Difficulty = "Expert";
        public static string PendingLocalAudioPath;

        public static int LastScore;
        public static int LastMaxCombo;
        public static float LastAccuracy;
        public static int LastStars;
        public static int LastRank;
        public static string LastSongName = "";
        public static string LastSongArtist = "";
    }
}
