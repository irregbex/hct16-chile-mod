using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// Names for the opponents. The retail results board and podium label every rival with its CAR
    /// name ("Grinder", "Cruiser"...). A mod campaign can give them people's names instead:
    ///   Game\HCT15Content\campaigns\&lt;folder&gt;\drivers.txt   one name per line, '#' comments, UTF-8
    /// Each retail car gets one name for the whole session (deterministic per campaign, no two cars
    /// share one while the list is long enough), so a rival keeps its name from race to race. The
    /// Chile campaign is written with 48 Chilean names on first run; edit freely.
    /// The Grand Prix 2026 grid shows the driver ("Tomas Vidal") - the car IS the driver there.
    /// Players keep their own profile name. Hooks: ResultsBoardViewModel and PodiumViewModel, rivals only.
    /// </summary>
    public static class Drivers
    {
        private static readonly Dictionary<string, List<string>> _files = new Dictionary<string, List<string>>();
        private static Dictionary<string, string> _assigned;
        private static string _assignedFor;

        private static string ChileanDefaults()
        {
            return "# HCT 1.6 - names of the rival drivers in the Chile campaign (results board, podium)\r\n" +
                   "# One name per line, '#' starts a comment, UTF-8. Each car keeps one name for the whole session.\r\n" +
                   "# Common Chilean given names and surnames; no real people intended.\r\n\r\n" +
                   "Benjamín Soto\r\nMatías Araya\r\nVicente Rojas\r\nMartín Díaz\r\nJoaquín Muñoz\r\nSebastián González\r\n" +
                   "Tomás Pérez\r\nCristóbal Contreras\r\nNicolás Fuentes\r\nDiego Riquelme\r\nIgnacio Cortés\r\nMaximiliano Vera\r\n" +
                   "Lucas Pizarro\r\nAgustín Lagos\r\nGabriel Ortiz\r\nRodrigo Navarro\r\nPablo Medina\r\nAndrés Sandoval\r\n" +
                   "Esteban Aguilera\r\nCristian Torres\r\nBastián Olivares\r\nAlonso Godoy\r\nRenato Zúñiga\r\nEduardo Cáceres\r\n" +
                   "Álvaro Leiva\r\nPatricio Ramírez\r\nClaudio Bustos\r\nMarcelo Alarcón\r\nHernán Cárdenas\r\nRafael Montecinos\r\n" +
                   "Catalina Rojas\r\nAntonia Vargas\r\nFrancisca Morales\r\nJaviera Castillo\r\nValentina Herrera\r\nFernanda Núñez\r\n" +
                   "Constanza Reyes\r\nIsidora Tapia\r\nCamila Espinoza\r\nMartina Figueroa\r\nSofía Sepúlveda\r\nJosefa Carrasco\r\n" +
                   "Florencia Bravo\r\nTrinidad Silva\r\nMaite Gutiérrez\r\nEmilia Salazar\r\nAmanda Vidal\r\nPaula Henríquez\r\n";
        }

        /// <summary>The names file of a campaign folder (written with defaults for chile when missing); null when there is none.</summary>
        private static List<string> For(string folder)
        {
            List<string> names;
            if (_files.TryGetValue(folder ?? "", out names)) return names;
            names = null;
            try
            {
                ContentRegistry.EnsureLoaded();
                string dir = Path.Combine(Path.Combine(ContentRegistry.ContentDir, "campaigns"), folder ?? "");
                string path = Path.Combine(dir, "drivers.txt");
                if (!File.Exists(path) && folder == "chile" && Directory.Exists(dir))
                {
                    File.WriteAllText(path, ChileanDefaults(), new System.Text.UTF8Encoding(false));
                    Debug.Log("[HCT15] drivers: wrote defaults to " + path);
                }
                if (File.Exists(path))
                {
                    names = new List<string>();
                    foreach (string raw in File.ReadAllLines(path))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                        names.Add(line);
                    }
                    Debug.Log("[HCT15] drivers: " + names.Count + " names from " + path);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[HCT15] drivers: " + folder + " unreadable: " + ex.Message); names = null; }
            _files[folder ?? ""] = names;
            return names;
        }

        private static string StripClone(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            int paren = id.IndexOf('(');
            return paren > 0 ? id.Substring(0, paren).Trim() : id;
        }

        /// <summary>One name per retail car, shuffled deterministically per campaign; keyed by Name, ItemId and prefab name.</summary>
        private static void EnsureAssigned(string folder, List<string> names)
        {
            if (_assigned != null && _assignedFor == folder) return;
            var map = new Dictionary<string, string>();
            var cars = new List<CarModel>();
            try { var main = CarModelDataList.Instance; if (main != null) cars.AddRange(main.CarModelsExceptIgnored); } catch (Exception) { }
            int seed = 101; foreach (char c in folder) seed = unchecked(seed * 31 + c);
            var rng = new System.Random(seed);
            var pool = new List<string>(names);
            for (int i = pool.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); string t = pool[i]; pool[i] = pool[j]; pool[j] = t; }
            for (int i = 0; i < cars.Count; i++)
            {
                CarModel car = cars[i];
                if (car == null) continue;
                string name = pool[i % pool.Count];
                if (i >= pool.Count) name += " " + (i / pool.Count + 1);   // more cars than names: "Name 2"
                if (!string.IsNullOrEmpty(car.Name)) map[car.Name] = name;
                if (!string.IsNullOrEmpty(car.ItemId)) map[car.ItemId] = name;
                if (car.ModelPrefab != null && !string.IsNullOrEmpty(car.ModelPrefab.name)) map[car.ModelPrefab.name] = name;
            }
            _assigned = map;
            _assignedFor = folder;
            Debug.Log("[HCT15] drivers: " + cars.Count + " car(s) named from campaigns\\" + folder + "\\drivers.txt (" + names.Count + " names)");
        }

        // Rival slots named one by one: "model#slot" -> driver. For a campaign whose grid is the mod's own few cars
        // (the retro campaign: three rival models for a whole grid) one name per MODEL would put the same name on every copy.
        private static readonly Dictionary<string, string> _slots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Called by EnemyCarManager for each rival it builds: the racer name it records. On a campaign that races
        /// the mod's own cars (StreetCars.CampaignEnemies gave the grid) every rival gets its own driver from
        /// drivers.txt, in the file's order by STRENGTH: the first name goes to the slot the donor stage authored
        /// fastest (SplineEnemyInfo.Speed), so the retro campaign's driver file's first line, RITCHIE, is the rival to beat.
        /// Elsewhere the model name is returned untouched and RacerName names by model, as before.
        /// </summary>
        public static string EnemyRacerName(string modelName, int index, List<SplineEnemyInfo> infos)
        {
            try
            {
                ModCampaign active = ModCampaigns.Active;
                if (active == null || active.Formula1 || StreetCars.LastCampaignGrid == 0) return modelName;
                List<string> names = For(active.Folder);
                if (names == null || names.Count == 0) return modelName;
                int rank = index;
                if (infos != null && index < infos.Count)
                {
                    rank = 0;
                    for (int k = 0; k < infos.Count; k++)
                        if (infos[k].Speed > infos[index].Speed || (infos[k].Speed == infos[index].Speed && k < index)) rank++;
                }
                string driver = names[rank % names.Count] + (rank >= names.Count ? " " + (rank / names.Count + 1) : "");
                string key = modelName + "#" + index;
                _slots[key] = driver;
                if (index == 0) Debug.Log("[HCT15] drivers: rivals named one by one from campaigns\\" + active.Folder + "\\drivers.txt, fastest slot first");
                return key;
            }
            catch (Exception) { return modelName; }
        }

        /// <summary>The label for a rival car on the results board / podium: a driver's name when the running campaign has one, else the fallback (the car name).</summary>
        public static string RacerName(string carId, string fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(carId)) return fallback;
                string slot;
                if (_slots.TryGetValue(carId, out slot)) return slot;
                string id = StripClone(carId);
                if (F1Campaign.IsGridCarId(id))
                {
                    string d = F1Campaign.DriverName(id);
                    if (!string.IsNullOrEmpty(d)) return d;
                    return fallback;
                }
                ModCampaign active = ModCampaigns.Active;
                if (active == null || active.Formula1) return fallback;
                List<string> names = For(active.Folder);
                if (names == null || names.Count == 0) return fallback;
                EnsureAssigned(active.Folder, names);
                string n;
                return _assigned.TryGetValue(id, out n) ? n : fallback;
            }
            catch (Exception) { return fallback; }
        }

        /// <summary>Forget the assignment (a new campaign, an edited file).</summary>
        public static void Refresh() { _assigned = null; _assignedFor = null; _files.Clear(); _slots.Clear(); }
    }
}
