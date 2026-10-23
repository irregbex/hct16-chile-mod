using System;
using System.Collections;
using System.Collections.Generic;
using Aquiris.HorizonChase.NewUI;
using System.IO;
using Aquiris.HorizonChase.Data;
using UnityEngine;
using UnityEngine.Networking;

namespace HCT15
{
    /// <summary>
    /// Plays the player's own music instead of the game's - inside the mod's content only (InModContent).
    ///
    /// Folder layout, created on first run next to the executable:
    ///   Game\HCT15Music\Menu\   played in the title and menus
    ///   Game\HCT15Music\Race\   played during races
    ///   Game\HCT15Music\        files placed here play everywhere
    /// Formats: .ogg (Vorbis) and .wav - the two the Unity 2018 Windows player can decode at run
    /// time. MP3 is not supported by the engine and is skipped with a warning.
    ///
    /// Every race has its own track (2.0.6): the race's *.race.json "Music", else its campaign round's
    /// "Music" (campaign.json), else a fixed automatic pick (AutoTrack: the same track every time for the
    /// same race id). The race opens with it and repeats it to the finish. F11 leaves it behind for that race.
    ///
    /// Integration: AudioManager (the game's music system) asks ForRace()/ForMenu() before it plays
    /// a clip and gets a custom clip back when one is available, so fades, snapshots, pause ducking
    /// and the volume slider keep working. When a custom track ends the next one in the list is
    /// handed to AudioManager. F11 skips, F12 toggles custom music, both saved to settings.
    /// Clips are streamed from disk (no full decode into memory).
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        public const string FolderName = "HCT15Music";
        private const float EndMargin = 0.35f;

        private class Track
        {
            public string Path;
            public string Rel;      // path under HCT15Music, '/' separated - how campaign.json names a track
            public string Title;    // parsed title (file name without number and artist)
            public string Artist = "";
            public string Album = ""; // the sub-folder the file sits in ("My Album")
            public string Number = "";
            public string Label;    // the whole file name, for logs
            public AudioClip Clip;
            public bool Failed;
            public bool Streamed;   // 2.0.17: played from the disk (WavStream), not held in memory
            public bool IsRace;    // 2.0.15: collected from HCT15Music\Race (a campaign's RaceMusicOnly applies to these)
        }

        // the "now playing" card
        private Track _np;
        private float _npUntil;
        private bool _npWaitForRace;
        private Texture2D _npBg;
        private GUIStyle _npSmall, _npTitle, _npArtist;

        private readonly List<Track> _menu = new List<Track>();
        private readonly List<Track> _race = new List<Track>();
        private readonly List<Track> _all = new List<Track>();   // every track, whichever folder it sits in

        // a race that names its own music in campaign.json
        private Track _pin;
        private string _pinFor;           // the race id _pin was resolved for
        private bool _pinResolved;        // _pin belongs to _pinFor (the id may legitimately be null)
        private bool _pinSkipped;         // F11 during that race: back to the normal rotation
        private string _pinWhy = "";      // where _pin came from, for the log
        // 2.0.23: a "Music" naming a folder ("Race/My Album"): one of its songs per run, F11 and a song's end move along it
        private List<Track> _pinAlbum;
        private int _pinAlbumAt;
        private static readonly Dictionary<string, int> AlbumTurn = new Dictionary<string, int>();   // race id -> last song played
        private int _menuIndex = -1;
        private int _raceIndex = -1;
        private Track _active;
        private bool _activeIsRace;
        private float _toast;
        private string _toastText = "";
        private bool _loading;
        private float _posLogAt;

        public static MusicPlayer Instance { get; private set; }

        public static string Root
        {
            get
            {
                string game = SavePaths.GameRoot();
                return game == null ? null : Path.Combine(game, FolderName);
            }
        }

        private void Awake()
        {
            Instance = this;
            try
            {
                string root = Root;
                if (root != null)
                {
                    Directory.CreateDirectory(Path.Combine(root, "Menu"));
                    Directory.CreateDirectory(Path.Combine(root, "Race"));
                    WriteReadme(root);
                    Scan(root);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HCT15] Music folder could not be prepared: " + ex.Message);
            }
            Debug.Log("[HCT15] Music: " + _menu.Count + " menu track(s), " + _race.Count + " race track(s) in " + Root);
            if (_menu.Count + _race.Count > 0) StartCoroutine(LoadAll());
        }

        private static void WriteReadme(string root)
        {
            string path = Path.Combine(root, "README.txt");
            if (File.Exists(path)) return;
            File.WriteAllText(path,
                "HCT 1.6 custom music\r\n" +
                "====================\r\n" +
                "Menu\\   tracks for the title screen and menus\r\n" +
                "Race\\   tracks for the races\r\n" +
                "Files directly in this folder play in both.\r\n" +
                "\r\n" +
                "Sub-folders are albums: Race\\My Album\\01 Artist - Song.wav\r\n" +
                "File names are read as 'NN - Artist - Title', 'NN Artist - Title', 'Artist - Title' or 'Title';\r\n" +
                "the card shown when a race starts (and on F11) prints title, artist, album and track number.\r\n" +
                "Formats: .ogg (Vorbis) or .wav. MP3 cannot be decoded by this Unity version -\r\n" +
                "convert with any audio tool (e.g. ffmpeg -i song.mp3 -q:a 5 song.ogg).\r\n" +
                "Tracks play in file-name order (prefix 01_, 02_, ... to control it), or shuffled\r\n" +
                "with \"ShuffleMusic\": true in HCT15Content\\hct15.settings.json.\r\n" +
                "\r\n" +
                "Every race has its own track and repeats it to the finish (F11 still skips out of it):\r\n" +
                "the race's \"Music\" in its *.race.json (the track editor's Music selector), else the\r\n" +
                "round's \"Music\" in the campaign's campaign.json, else a fixed automatic pick from Race\\.\r\n" +
                "Names are the path under this folder (\"Race/Agregados/Song.wav\") or just the file name.\r\n" +
                "\r\n" +
                "In game: F11 = next track, F12 = custom music on/off (saved to settings).\r\n" +
                "The game's own music volume slider still applies.\r\n");
        }

        private void Scan(string root)
        {
            _menu.Clear();
            _race.Clear();
            List<Track> both = Collect(root, false);   // files dropped straight into HCT15Music play everywhere
            _menu.AddRange(Collect(Path.Combine(root, "Menu")));
            _menu.AddRange(both);
            _race.AddRange(Collect(Path.Combine(root, "Race")));
            _race.AddRange(both);
            _all.Clear();
            foreach (Track t in _menu) if (!_all.Contains(t)) _all.Add(t);
            foreach (Track t in _race) if (!_all.Contains(t)) _all.Add(t);
            foreach (Track t in _all)
                t.Rel = t.Path.Length > root.Length ? t.Path.Substring(root.Length).Trim('\\', '/').Replace('\\', '/') : Path.GetFileName(t.Path);
            if (Settings.Data.ShuffleMusic)
            {
                Shuffle(_menu);
                Shuffle(_race);
            }
        }

        /// <summary>
        /// Every .ogg/.wav under the folder, sub-folders included (a sub-folder is an album, kept in
        /// order). The file name is read as "NN - Artist - Title", "NN Artist - Title" or "Artist -
        /// Title" or just "Title"; the number and the dashes are optional.
        /// </summary>
        private static List<Track> Collect(string dir, bool recursive = true)
        {
            List<Track> list = new List<Track>();
            if (!Directory.Exists(dir)) return list;
            SearchOption opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            List<string> files = new List<string>();
            files.AddRange(Directory.GetFiles(dir, "*.ogg", opt));
            files.AddRange(Directory.GetFiles(dir, "*.wav", opt));
            foreach (string mp3 in Directory.GetFiles(dir, "*.mp3", opt))
                Debug.LogWarning("[HCT15] Music: MP3 is not supported by this engine, skipping " + Path.GetFileName(mp3));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                Track t = new Track { Path = f, Label = Path.GetFileNameWithoutExtension(f), IsRace = string.Equals(Path.GetFileName(dir.TrimEnd('\\', '/')), "Race", StringComparison.OrdinalIgnoreCase) };
                string album = Path.GetDirectoryName(f);
                t.Album = album != null && album.Length > dir.Length ? album.Substring(dir.Length).Trim('\\', '/').Replace("\\", " / ") : "";
                ParseName(t.Label, t);
                list.Add(t);
            }
            var albums = new Dictionary<string, int>();
            foreach (Track t in list) { int n; albums.TryGetValue(t.Album, out n); albums[t.Album] = n + 1; }
            foreach (var kv in albums) Debug.Log("[HCT15] Music: " + kv.Value + " track(s) in " + Path.GetFileName(dir) + (kv.Key.Length > 0 ? " / " + kv.Key : ""));
            return list;
        }

        private static void ParseName(string name, Track t)
        {
            string s = name.Trim();
            // leading track number: "02 ", "02 - ", "02. ", "02_"
            int i = 0;
            while (i < s.Length && i < 3 && char.IsDigit(s[i])) i++;
            if (i > 0 && i < s.Length && (s[i] == ' ' || s[i] == '-' || s[i] == '.' || s[i] == '_'))
            {
                t.Number = s.Substring(0, i);
                s = s.Substring(i).TrimStart(' ', '-', '.', '_');
            }
            string[] parts = s.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                t.Artist = parts[0].Trim();
                t.Title = string.Join(" - ", parts, 1, parts.Length - 1).Trim();
            }
            else
            {
                t.Artist = "";
                t.Title = s.Trim();
            }
            if (t.Title.Length == 0) t.Title = name;
        }

        private static void Shuffle(List<Track> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                Track t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }

        private IEnumerator LoadAll()
        {
            _loading = true;
            List<Track> all = _all;
            foreach (Track t in all)
            {
                if (t.Clip != null || t.Failed) continue;
                // 2.0.17: a .wav plays from the disk (WavStream) - loading every song whole ran the 32-bit game out of memory
                if (t.Path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    try { t.Clip = WavStream.Open(t.Path, "HCT15:" + t.Title); }
                    catch (Exception ex) { Debug.LogWarning("[HCT15] Music: could not stream " + t.Title + ": " + ex.Message); }
                    if (t.Clip != null) { t.Streamed = true; continue; }
                }
                AudioType type = t.Path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? AudioType.WAV : AudioType.OGGVORBIS;
                string uri = "file:///" + t.Path.Replace('\\', '/');
                using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, type))
                {
                    ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = true;
                    yield return req.SendWebRequest();
                    if (req.isNetworkError || req.isHttpError)
                    {
                        t.Failed = true;
                        Debug.LogWarning("[HCT15] Music: could not load " + t.Title + ": " + req.error);
                        continue;
                    }
                    try
                    {
                        t.Clip = DownloadHandlerAudioClip.GetContent(req);
                        if (t.Clip != null) t.Clip.name = "HCT15:" + t.Title;
                        else t.Failed = true;
                    }
                    catch (Exception ex)
                    {
                        t.Failed = true;
                        Debug.LogWarning("[HCT15] Music: could not decode " + t.Title + ": " + ex.Message);
                    }
                }
            }
            _loading = false;
            Debug.Log("[HCT15] Music: loaded " + all.FindAll(x => x.Clip != null).Count + " of " + all.Count + " track(s), " + all.FindAll(x => x.Streamed).Count + " played from the disk");
            // If the menu is already up with the game's own music, switch to ours now.
            if (Active && AudioManager.Instance != null && !StaticData.IsInRace && Next(_menu, ref _menuIndex) != null)
            {
                _menuIndex--; // Next() advanced it; ForMenu will advance again
                AudioClip clip = ForMenu(null);
                if (clip != null) AudioManager.Instance.PlayMusicClip(clip, true, 1f);
            }
        }

        public static bool Enabled
        {
            get { return Settings.Data.CustomMusic; }
        }

        /// <summary>
        /// The mod's music belongs to the mod's content: it plays only while a mod campaign runs (its menus and its
        /// races) or a mod race is loaded. Everywhere else - title, main menus, World Tour, Adventures, Tournament,
        /// Endurance, the Senna DLC - the game's own music plays, untouched (2.0.4; before, every track in
        /// HCT15Music that no campaign claimed played over the whole retail game).
        /// </summary>
        public static bool InModContent
        {
            get
            {
                try
                {
                    if (ModCampaigns.IsAnyActive) return true;
                    string id = CurrentRaceId();
                    return StaticData.IsInRace && id != null && ContentRegistry.RacesById.ContainsKey(id);
                }
                catch (Exception) { return false; }
            }
        }

        /// <summary>
        /// 2.0.11: a race of the game's own campaigns (World Tour, Adventures, Tournament, Endurance...) with the mod's
        /// music switched on there (F11 in such a race; Settings RetailMusic). Its menus keep the game's music.
        /// </summary>
        private static bool RetailRace
        {
            get
            {
                try { return Settings.Data.RetailMusic && StaticData.IsInRace && !InModContent; }
                catch (Exception) { return false; }
            }
        }

        /// <summary>Custom music is on (F12) and the player is in the mod's content, or in a retail race that asked for it.</summary>
        private static bool Active { get { return Enabled && (InModContent || RetailRace); } }

        private static Track Next(List<Track> list, ref int index)
        {
            for (int tries = 0; tries < list.Count; tries++)
            {
                index = (index + 1) % list.Count;
                if (list[index].Clip != null && Allowed(list[index])) return list[index];
            }
            return null;
        }

        /// <summary>
        /// A track from an album a campaign has claimed ("MusicAlbums" in its campaign.json) is only in the
        /// rotation while the player is inside that campaign. Without this the retro campaign's albums played over
        /// the retail game, the title screen and every other campaign, because Menu\ and Race\ are collected
        /// whole and a round naming its own track only ever ADDS to the pool, it never fences it off.
        /// </summary>
        private static bool Allowed(Track t)
        {
            if (t == null) return false;
            try { return ModCampaigns.MusicAlbumAllowed(t.Album) && (!t.IsRace || ModCampaigns.RaceMusicAllowed(t.Album)); }
            catch (Exception) { return true; }
        }

        /// <summary>Called by AudioManager with the clip it was about to play for a race.</summary>
        public AudioClip ForRace(AudioClip original)
        {
            Track pin = Pinned();
            if (pin == null) return Pick(original, _race, ref _raceIndex, true);
            if (_active == pin && _activeIsRace) return pin.Clip;
            _active = pin;
            _activeIsRace = true;
            ShowNowPlaying(pin, true);
            Debug.Log("[HCT15] Music: race " + _pinFor + " plays its own track '" + pin.Rel + "' (" + _pinWhy + ") instead of '" + (original != null ? original.name : "none") + "'");
            return pin.Clip;
        }

        /// <summary>
        /// The race's own track: its *.race.json "Music", else its campaign round's "Music", else AutoTrack.
        /// Null when there is no race track at all, it has not loaded yet, or the player pressed F11 to leave it.
        /// Resolved once per race (Update forgets it when the race ends, so the next race starts on its track again).
        /// </summary>
        private Track Pinned()
        {
            if (!Active || RetailRace) return null;
            string id = CurrentRaceId();
            if (!_pinResolved || id != _pinFor)
            {
                _pinFor = id;
                _pinResolved = true;
                _pinSkipped = false;
                _pin = null;
                _pinAlbum = null;
                CustomRaceDef def;
                string own = id != null && ContentRegistry.RacesById.TryGetValue(id, out def) && def != null ? def.Music : null;
                if (!string.IsNullOrEmpty(own)) { _pinWhy = "race.json Music"; _pin = PinFrom(own, id); }
                if (_pin == null)
                {
                    string round = RoundMusic(id);
                    if (!string.IsNullOrEmpty(round)) { _pinWhy = "campaign round Music"; _pin = PinFrom(round, id); }
                }
                if (_pin == null) { _pin = AutoTrack(id); _pinWhy = "automatic, fixed for this race" + _autoWhy; }
            }
            return _pinSkipped || _pin == null || _pin.Clip == null ? null : _pin;
        }

        /// <summary>
        /// 2.0.23: the track a race's "Music" names. A folder under HCT15Music ("Race/My Album", or just "My Album") gives one
        /// of its songs, the next one each time the race is run (the first run of a session starts on a random one).
        /// </summary>
        private Track PinFrom(string spec, string raceId)
        {
            List<Track> album = AlbumTracks(spec);
            if (album.Count == 0) return Find(spec);
            string key = raceId ?? "";
            int last;
            int turn = AlbumTurn.TryGetValue(key, out last) ? last + 1 : UnityEngine.Random.Range(0, album.Count);
            _pinAlbumAt = turn % album.Count;
            AlbumTurn[key] = _pinAlbumAt;
            _pinAlbum = album;
            _pinWhy += ", folder '" + spec + "' song " + (_pinAlbumAt + 1) + " of " + album.Count;
            return album[_pinAlbumAt];
        }

        /// <summary>The songs in a folder under HCT15Music (or under its Race\), in path order; empty when it is no folder.</summary>
        private List<Track> AlbumTracks(string spec)
        {
            string want = spec.Trim().Replace('\\', '/').Trim('/');
            List<Track> list = new List<Track>();
            if (want.Length == 0) return list;
            foreach (string dir in new[] { want + "/", "Race/" + want + "/" })
            {
                list = _all.FindAll(t => !t.Failed && t.Rel != null && t.Rel.StartsWith(dir, StringComparison.OrdinalIgnoreCase));
                if (list.Count > 0) break;
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Rel.ToLowerInvariant(), b.Rel.ToLowerInvariant()));
            return list;
        }

        /// <summary>
        /// The fixed automatic track of a race that names none: FNV-1a of the race id over the race tracks this
        /// campaign allows, in path order - the same race always gets the same song, whatever the shuffle setting
        /// or the order the clips finished loading in. Adding or removing songs may move it; naming one does not.
        /// </summary>
        private Track AutoTrack(string raceId)
        {
            _autoWhy = "";
            if (string.IsNullOrEmpty(raceId)) return null;
            List<Track> pool = _race.FindAll(t => !t.Failed && t.Rel != null && Allowed(t));
            if (pool.Count == 0) return null;
            pool.Sort((a, b) => string.CompareOrdinal(a.Rel.ToLowerInvariant(), b.Rel.ToLowerInvariant()));
            // the campaign's preferred albums (Agregados unless it says otherwise): every other round, in round order,
            // gets the album's next song, so each song of it is heard somewhere in the campaign
            ModCampaign owner; int round;
            if (OwnerRound(raceId, out owner, out round) && round % 2 == 0)
            {
                List<string> prefer = owner.Def.MusicPrefer ?? ((owner.Def.MusicAlbums == null || owner.Def.MusicAlbums.Count == 0) ? new List<string> { "Agregados" } : new List<string>());
                List<Track> pref = pool.FindAll(t => InAlbums(t.Album, prefer));
                if (pref.Count > 0)
                {
                    _autoWhy = " (" + prefer[0] + " turn: round " + (round + 1) + " -> song " + ((round / 2) % pref.Count + 1) + " of " + pref.Count + ")";
                    return pref[(round / 2) % pref.Count];
                }
            }
            uint h = 2166136261;
            foreach (char c in raceId) { h ^= c; h *= 16777619; }
            return pool[(int)(h % (uint)pool.Count)];
        }

        private string _autoWhy = "";

        private static bool InAlbums(string album, List<string> albums)
        {
            if (string.IsNullOrEmpty(album) || albums == null) return false;
            foreach (string owned in albums)
            {
                if (string.IsNullOrEmpty(owned)) continue;
                string o = owned.Replace((char)92, (char)47).Trim('/', ' ').Replace("/", " / ");
                if (album.Equals(o, StringComparison.OrdinalIgnoreCase) || album.StartsWith(o + " / ", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>The mod campaign that owns this race id and the round's index in it.</summary>
        private static bool OwnerRound(string raceId, out ModCampaign owner, out int index)
        {
            owner = null; index = -1;
            try
            {
                foreach (ModCampaign c in ModCampaigns.All)
                {
                    if (c == null || c.Def == null || c.Def.Rounds == null) continue;
                    for (int i = 0; i < c.Def.Rounds.Count; i++)
                        if (c.Def.Rounds[i] != null && c.Def.Rounds[i].RaceId == raceId) { owner = c; index = i; return true; }
                }
            }
            catch (Exception) { }
            return false;
        }

        private static string CurrentRaceId()
        {
            try { return StaticData.Race != null ? StaticData.Race.ItemId : null; }
            catch (Exception) { return null; }
        }

        /// <summary>The "Music" of the campaign round that owns this race id, over every mod campaign.</summary>
        private static string RoundMusic(string raceId)
        {
            if (string.IsNullOrEmpty(raceId)) return null;
            try
            {
                foreach (ModCampaign c in ModCampaigns.All)
                {
                    if (c == null || c.Def == null || c.Def.Rounds == null) continue;
                    foreach (F1Round r in c.Def.Rounds)
                        if (r != null && r.RaceId == raceId && !string.IsNullOrEmpty(r.Music)) return r.Music;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] Music: could not read the race's own track: " + ex.Message); }
            return null;
        }

        /// <summary>
        /// A track by the name a campaign round gives it: a path under HCT15Music ("Race\Album\03 Song.wav"),
        /// with or without the extension, or just the file name. Slashes and case do not matter, and a name
        /// that still says .mp3 finds the .ogg/.wav the file was converted to.
        /// </summary>
        private Track Find(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return null;
            string want = spec.Trim().Replace('\\', '/').Trim('/');
            string wantNoExt = StripAudioExt(want);
            string leaf = wantNoExt;
            int slash = leaf.LastIndexOf('/');
            if (slash >= 0) leaf = leaf.Substring(slash + 1);
            Track byLeaf = null;
            foreach (Track t in _all)
            {
                if (t.Rel == null) continue;
                if (string.Equals(t.Rel, want, StringComparison.OrdinalIgnoreCase)) return t;
                if (string.Equals(StripAudioExt(t.Rel), wantNoExt, StringComparison.OrdinalIgnoreCase)) return t;
                if (byLeaf == null && string.Equals(Path.GetFileNameWithoutExtension(t.Path), leaf, StringComparison.OrdinalIgnoreCase)) byLeaf = t;
            }
            if (byLeaf == null) Debug.LogWarning("[HCT15] Music: the race asks for '" + spec + "', which is not in " + Root);
            return byLeaf;
        }

        private static string StripAudioExt(string s)
        {
            foreach (string ext in new[] { ".ogg", ".wav", ".mp3" })
                if (s.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return s.Substring(0, s.Length - ext.Length);
            return s;
        }

        /// <summary>Called by AudioManager with the clip it was about to play for a menu state.</summary>
        public AudioClip ForMenu(AudioClip original)
        {
            return Pick(original, _menu, ref _menuIndex, false);
        }

        /// <summary>
        /// Start a named menu track now (a campaign's "MenuMusic", e.g. the retro campaign's title theme when its screen
        /// opens). It becomes the running menu track, so the menu-state changes that follow keep it instead of
        /// skipping to the next of the rotation; the rotation resumes after it as usual.
        /// </summary>
        public void PlayMenuTrack(string spec)
        {
            try
            {
                if (!Active || AudioManager.Instance == null) return;
                Track t = Find(spec);
                if (t == null || t.Clip == null) { Debug.LogWarning("[HCT15] Music: menu track '" + spec + "' not found or not loaded yet"); return; }
                int at = _menu.IndexOf(t);
                if (at >= 0) _menuIndex = at;
                _active = t;
                _activeIsRace = false;
                ShowNowPlaying(t, false);
                AudioManager.Instance.PlayMusicClip(t.Clip, true, 0.5f);
                Debug.Log("[HCT15] Music: campaign menu track '" + t.Label + "' (" + spec + ")");
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] Music: menu track: " + ex.Message); }
        }

        private AudioClip Pick(AudioClip original, List<Track> list, ref int index, bool isRace)
        {
            if (!Active || list.Count == 0) { _active = null; return original; }
            // keep the current custom track running across menu-state changes instead of skipping
            if (_active != null && _activeIsRace == isRace && _active.Clip != null && list.Contains(_active) && Allowed(_active)) return _active.Clip;
            Track t = Next(list, ref index);
            if (t == null) { _active = null; return original; }
            _active = t;
            _activeIsRace = isRace;
            ShowNowPlaying(t, isRace);
            Debug.Log("[HCT15] Music: playing custom " + (isRace ? "race" : "menu") + " track '" + t.Label + "' (" + t.Artist + " / " + t.Title + " / " + t.Album + ") instead of '" + (original != null ? original.name : "none") + "'");
            return t.Clip;
        }

        private void Skip()
        {
            if (!Active || AudioManager.Instance == null) return;
            bool isRace = StaticData.IsInRace;
            if (isRace && !_pinSkipped && _pinAlbum != null && _pinAlbum.Count > 1 && NextInAlbum()) return;
            if (isRace) _pinSkipped = true;   // the player wants something else than the race's own track
            List<Track> list = isRace ? _race : _menu;
            if (list.Count == 0) return;
            Track t = isRace ? Next(_race, ref _raceIndex) : Next(_menu, ref _menuIndex);
            if (t == null) return;
            _active = t;
            _activeIsRace = isRace;
            ShowNowPlaying(t, isRace);
            AudioManager.Instance.PlayMusicClip(t.Clip, true, 0.5f);
            Debug.Log("[HCT15] Music: next " + (isRace ? "race" : "menu") + " track '" + t.Rel + "'");
        }

        /// <summary>2.0.23: the race's folder plays on - its next loaded song becomes the race's track (and the next run starts after it).</summary>
        private bool NextInAlbum()
        {
            for (int step = 1; step < _pinAlbum.Count; step++)
            {
                int at = (_pinAlbumAt + step) % _pinAlbum.Count;
                Track t = _pinAlbum[at];
                if (t.Clip == null) continue;
                _pinAlbumAt = at;
                AlbumTurn[_pinFor ?? ""] = at;
                _pin = t;
                _active = t;
                _activeIsRace = true;
                ShowNowPlaying(t, true);
                AudioManager.Instance.PlayMusicClip(t.Clip, true, 0.5f);
                Debug.Log("[HCT15] Music: next song of the race's folder '" + t.Rel + "' (" + (at + 1) + " of " + _pinAlbum.Count + ")");
                return true;
            }
            return false;
        }

        // "-hct15-musickeys F11@40,F12@70" (tests): F11 / F12 as if pressed that many seconds after start
        private List<KeyValuePair<float, KeyCode>> _testKeys;

        private bool Pressed(KeyCode k)
        {
            if (Input.GetKeyDown(k)) return true;
            if (_testKeys == null)
            {
                _testKeys = new List<KeyValuePair<float, KeyCode>>();
                try
                {
                    string[] a = Environment.GetCommandLineArgs();
                    for (int i = 0; i < a.Length - 1; i++)
                        if (a[i] == "-hct15-musickeys")
                            foreach (string part in a[i + 1].Split(','))
                            {
                                string[] kv = part.Split('@');
                                float at;
                                if (kv.Length == 2 && float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out at))
                                    _testKeys.Add(new KeyValuePair<float, KeyCode>(at, kv[0].Trim().ToUpperInvariant() == "F12" ? KeyCode.F12 : KeyCode.F11));
                            }
                }
                catch (Exception) { }
            }
            for (int i = 0; i < _testKeys.Count; i++)
                if (_testKeys[i].Value == k && Time.realtimeSinceStartup >= _testKeys[i].Key)
                {
                    Debug.Log("[HCT15] Music: test key " + k + " at " + Time.realtimeSinceStartup.ToString("0.0") + " s (in race: " + StaticData.IsInRace + ")");
                    _testKeys.RemoveAt(i);
                    return true;
                }
            return false;
        }

        private void Update()
        {
            bool f11 = Pressed(KeyCode.F11), f12 = Pressed(KeyCode.F12);
            if (f11)
            {
                if (!Active && RetailRaceOff) SetRetail(true);
                Skip();
            }
            if (f12 && (RetailRace || RetailRaceOff))
            {
                // in a race of the game's own campaigns F12 switches the mod's music there on / off, not everywhere
                SetRetail(!(RetailRace && Enabled));
                if (AudioManager.Instance != null)
                {
                    if (!Active) { _active = null; GameAccess.ReplayContextMusic(AudioManager.Instance); }
                    else Skip();
                }
            }
            else if (f12)
            {
                Settings.Data.CustomMusic = !Settings.Data.CustomMusic;
                Settings.Save();
                Toast(Settings.Data.CustomMusic ? "Custom music ON" : "Custom music OFF");
                if (AudioManager.Instance != null)
                {
                    if (!Settings.Data.CustomMusic) { _active = null; GameAccess.ReplayContextMusic(AudioManager.Instance); }
                    else Skip();
                }
            }
            if (_toast > 0f) _toast -= Time.unscaledDeltaTime;
            // out of the race: the next race resolves its own track again (and an F11 skip does not carry over)
            if (_pinResolved) { bool inRace = true; try { inRace = StaticData.IsInRace; } catch (Exception) { } if (!inRace) _pinResolved = false; }
            // a race track is announced once the race is on screen, not on the loading screen
            if (_npWaitForRace)
            {
                bool visible = false;
                try { visible = StaticData.IsInRace && !LoadingManager.IsLoadingScene && UIStateManager.IsStateActive("GAMEPLAY"); } catch (Exception) { }
                if (visible) { _npWaitForRace = false; _npUntil = Time.unscaledTime + 8f; }
            }

            // Track finished: hand the next one over before AudioManager restarts the same clip.
            if (_active != null && _active.Clip != null && AudioManager.Instance != null)
            {
                AudioSource src = GameAccess.MusicSource(AudioManager.Instance);
                // 2.0.18: where the song is, every 30 s (the 6-minute Agregados songs: proof that they play to the end)
                if (src != null && src.clip == _active.Clip && src.isPlaying && Time.unscaledTime >= _posLogAt)
                {
                    _posLogAt = Time.unscaledTime + 30f;
                    Debug.Log("[HCT15] Music: '" + _active.Title + "' at " + src.time.ToString("0") + " / " + _active.Clip.length.ToString("0") + " s");
                }
                if (src != null && src.clip == _active.Clip && src.isPlaying && src.time >= _active.Clip.length - EndMargin)
                {
                    List<Track> list = _activeIsRace ? _race : _menu;
                    Track pinned = Pinned();
                    if (_active == pinned && _pinAlbum != null && _pinAlbum.Count > 1) Skip();   // 2.0.23: the folder's next song
                    else if (list.Count > 1 && _active != pinned) Skip();
                    else src.time = 0f;
                }
            }
        }

        /// <summary>A retail race playing the game's own music (the mod's music not asked for there yet).</summary>
        private static bool RetailRaceOff
        {
            get
            {
                try { return StaticData.IsInRace && !InModContent && !(Settings.Data.RetailMusic && Enabled); }
                catch (Exception) { return false; }
            }
        }

        private void SetRetail(bool on)
        {
            Settings.Data.RetailMusic = on;
            if (on) Settings.Data.CustomMusic = true;
            Settings.Save();
            bool es = Loc.Lang == "es";
            Toast(on ? (es ? "Música del mod en las campañas del juego: SÍ  (F11 siguiente, F12 vuelve a la del juego)" : "Mod music in the game's campaigns: ON  (F11 next, F12 back to the game's)")
                     : (es ? "Música del juego en sus campañas  (F11 vuelve a la del mod)" : "The game's own music in its campaigns  (F11 back to the mod's)"));
            Debug.Log("[HCT15] Music: retail races " + (on ? "play the mod's music" : "keep the game's music"));
        }

        private void Toast(string text)
        {
            _toastText = text;
            _toast = 4f;
        }

        /// <summary>The "now playing" card: in a race it appears when the race is on screen and stays 7 s; in the menus 5 s.</summary>
        private void ShowNowPlaying(Track t, bool isRace)
        {
            // a campaign may keep the track names to itself ("NowPlaying": false - the retro campaign plays its own themes only)
            try { if (!ModCampaigns.NowPlayingAllowed) { _np = null; _npWaitForRace = false; return; } } catch (Exception) { }
            _np = t;
            if (isRace) { _npWaitForRace = true; _npUntil = 0f; }
            else { _npWaitForRace = false; _npUntil = Time.unscaledTime + 5f; }
        }

        private void DrawNowPlaying()
        {
            if (_np == null || Time.unscaledTime > _npUntil) return;
            if (_npBg == null)
            {
                _npBg = new Texture2D(1, 1); _npBg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.78f)); _npBg.Apply();
                _npSmall = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.017f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                _npSmall.normal.textColor = new Color(1f, 0.85f, 0.25f);
                _npTitle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.03f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                _npTitle.normal.textColor = Color.white;
                _npArtist = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.021f), alignment = TextAnchor.MiddleLeft };
                _npArtist.normal.textColor = new Color(0.9f, 0.9f, 0.95f);
            }
            float remain = _npUntil - Time.unscaledTime;
            float alpha = Mathf.Clamp01(remain / 0.8f);   // fade out over the last 0.8 s
            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            float w = Mathf.Min(Screen.width * 0.46f, 620f), h = Screen.height * 0.125f;
            float x = (Screen.width - w) / 2f, y = Screen.height * 0.075f;   // top centre, under the lap counter line
            GUI.depth = -8500;
            GUI.DrawTexture(new Rect(x, y, w, h), _npBg);
            GUI.DrawTexture(new Rect(x, y, 6f, h), Texture2D.whiteTexture);
            float pad = 16f;
            GUI.Label(new Rect(x + pad, y + 4f, w - 2 * pad, h * 0.26f), "♪  " + Loc.Get("now_playing") + (_np.Number.Length > 0 ? "   ·   #" + _np.Number : ""), _npSmall);
            GUI.Label(new Rect(x + pad, y + h * 0.27f, w - 2 * pad, h * 0.4f), _np.Title, _npTitle);
            string line = _np.Artist;
            if (_np.Album.Length > 0) line += (line.Length > 0 ? "   ·   " : "") + _np.Album;
            GUI.Label(new Rect(x + pad, y + h * 0.66f, w - 2 * pad, h * 0.3f), line, _npArtist);
            GUI.color = prev;
        }

        private void OnGUI()
        {
            DrawNowPlaying();
            if (_toast <= 0f && !_loading) return;
            string text = _toast > 0f ? _toastText : "♪ loading music...";
            GUI.Label(new Rect(10, Screen.height - 30, 700, 24), "<color=#FFDD00>" + text + "</color>");
        }
    }
}
