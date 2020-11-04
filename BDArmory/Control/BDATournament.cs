using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using BDArmory.Core;
using BDArmory.UI;

namespace BDArmory.Control
{
    // A serializable configuration for loading and saving the tournament state.
    [Serializable]
    public class RoundConfig : VesselSpawner.SpawnConfig
    {
        public RoundConfig(int round, int heat, bool completed, VesselSpawner.SpawnConfig config) : base(config) { this.round = round; this.heat = heat; this.completed = completed; }
        public int round;
        public int heat;
        public bool completed;
    }

    [Serializable]
    public class TournamentState
    {
        public uint tournamentID;
        public List<string> craftFiles;
        [NonSerialized] public Dictionary<int, Dictionary<int, VesselSpawner.SpawnConfig>> rounds; // <Round, <Heat, Crafts>>
        [NonSerialized] public Dictionary<int, HashSet<int>> completed = new Dictionary<int, HashSet<int>>();

        /* Generate rounds and heats by shuffling the crafts list and breaking it into groups.
         * The last heat in a round will have fewer craft if the number of craft is not divisible by the number of vessels per heat.
         * The vessels per heat is limited to the number of available craft.
         */
        public void Generate(string folder, int numberOfRounds, int vesselsPerHeat)
        {
            tournamentID = (uint)DateTime.UtcNow.Subtract(new DateTime(2020, 1, 1)).TotalSeconds;
            craftFiles = Directory.GetFiles(Environment.CurrentDirectory + $"/AutoSpawn/{folder}").Where(f => f.EndsWith(".craft")).ToList();
            vesselsPerHeat = Mathf.Clamp(vesselsPerHeat, 1, craftFiles.Count);
            if (vesselsPerHeat == 1) vesselsPerHeat = craftFiles.Count; // Unlimited.
            rounds = new Dictionary<int, Dictionary<int, VesselSpawner.SpawnConfig>>();
            Debug.Log("[BDATournament]: Generating " + numberOfRounds + " rounds for tournament " + tournamentID + ", each with " + vesselsPerHeat + " vessels per heat.");
            for (int roundIndex = 0; roundIndex < numberOfRounds; ++roundIndex)
            {
                craftFiles.Shuffle();
                List<string> selectedFiles = craftFiles.Take(vesselsPerHeat).ToList();
                rounds.Add(rounds.Count, new Dictionary<int, VesselSpawner.SpawnConfig>());
                int heatIndex = 0;
                while (selectedFiles.Count > 0)
                {
                    rounds[roundIndex].Add(rounds[roundIndex].Count, new VesselSpawner.SpawnConfig(
                        BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                        BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                        BDArmorySettings.VESSEL_SPAWN_ALTITUDE,
                        BDArmorySettings.VESSEL_SPAWN_DISTANCE,
                        BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                        BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED,
                        true, // Kill everything first.
                        true, // Assign teams.
                        null, // No folder, we're going to specify the craft files.
                        selectedFiles.ToList() // Add a copy of the craft files list.
                    ));
                    selectedFiles = craftFiles.Skip(++heatIndex * vesselsPerHeat).Take(vesselsPerHeat).ToList();
                }
            }
        }

        public bool SaveState(string stateFile)
        {
            try
            {
                List<string> strings = new List<string>();

                strings.Add(JsonUtility.ToJson(this));
                foreach (var round in rounds.Keys)
                    foreach (var heat in rounds[round].Keys)
                        strings.Add(JsonUtility.ToJson(new RoundConfig(round, heat, completed.ContainsKey(round) && completed[round].Contains(heat), rounds[round][heat])));

                File.WriteAllLines(Path.Combine(Environment.CurrentDirectory, stateFile), strings);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool LoadState(string stateFile)
        {
            try
            {
                if (!File.Exists(Path.Combine(Environment.CurrentDirectory, stateFile))) return false;
                var strings = File.ReadAllLines(Path.Combine(Environment.CurrentDirectory, stateFile));
                var data = JsonUtility.FromJson<TournamentState>(strings[0]);
                tournamentID = data.tournamentID;
                craftFiles = data.craftFiles;
                rounds = new Dictionary<int, Dictionary<int, VesselSpawner.SpawnConfig>>();
                completed = new Dictionary<int, HashSet<int>>();
                for (int i = 1; i < strings.Length; ++i)
                {
                    if (strings[i].Length > 0)
                    {
                        var roundConfig = JsonUtility.FromJson<RoundConfig>(strings[i]);
                        if (!rounds.ContainsKey(roundConfig.round)) rounds.Add(roundConfig.round, new Dictionary<int, VesselSpawner.SpawnConfig>());
                        rounds[roundConfig.round].Add(roundConfig.heat, new VesselSpawner.SpawnConfig(roundConfig.latitude, roundConfig.longitude, roundConfig.altitude, roundConfig.distance, roundConfig.absDistanceOrFactor, roundConfig.easeInSpeed, roundConfig.killEverythingFirst, roundConfig.assignTeams, roundConfig.folder, roundConfig.craftFiles));
                        if (roundConfig.completed)
                        {
                            if (!completed.ContainsKey(roundConfig.round)) completed.Add(roundConfig.round, new HashSet<int>());
                            completed[roundConfig.round].Add(roundConfig.heat);
                        }
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                return false;
            }
        }
    }

    public enum TournamentStatus { Stopped, Running, Waiting, Completed };

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class BDATournament : MonoBehaviour
    {
        public static BDATournament Instance;

        #region Flags and Variables
        TournamentState tournamentState;
        string stateFile = "GameData/BDArmory/tournament.state";
        string message;
        private Coroutine runTournamentCoroutine;
        public TournamentStatus tournamentStatus = TournamentStatus.Stopped;
        public uint tournamentID = 0;
        public int numberOfRounds = 0;
        public int currentRound = 0;
        public int numberOfHeats = 0;
        public int currentHeat = 0;
        public int vesselCount = 0;
        public int heatsRemaining = 0;
        bool competitionStarted = false;
        #endregion

        void Awake()
        {
            if (Instance)
                Destroy(Instance);
            Instance = this;
        }

        void Start()
        {
            StartCoroutine(LoadStateWhenReady());
        }

        IEnumerator LoadStateWhenReady()
        {
            while (BDACompetitionMode.Instance == null)
                yield return null;
            LoadTournamentState(); // Load the last state.
        }

        void OnDestroy()
        {
            StopTournament(); // Stop any running tournament.
            SaveTournamentState(); // Save the last state.
        }

        // Load tournament state from disk
        bool LoadTournamentState(string stateFile = "")
        {
            if (stateFile != "") this.stateFile = stateFile;
            tournamentState = new TournamentState();
            if (tournamentState.LoadState(this.stateFile))
            {
                message = "Tournament state loaded from " + this.stateFile;
                tournamentID = tournamentState.tournamentID;
                vesselCount = tournamentState.craftFiles.Count;
                numberOfRounds = tournamentState.rounds.Count;
                numberOfHeats = numberOfRounds > 0 ? tournamentState.rounds[0].Count : 0;
                heatsRemaining = tournamentState.rounds.Select(r => r.Value.Count).Sum() - tournamentState.completed.Select(c => c.Value.Count).Sum();
            }
            else
                message = "Failed to load tournament state.";
            Debug.Log("[BDATournament]: " + message);
            // if (BDACompetitionMode.Instance != null)
            //     BDACompetitionMode.Instance.competitionStatus.Add(message);
            tournamentStatus = heatsRemaining > 0 ? TournamentStatus.Stopped : TournamentStatus.Completed;
            return true;
        }

        // Save tournament state to disk
        bool SaveTournamentState()
        {
            if (tournamentState.SaveState(stateFile))
                message = "Tournament state saved to " + stateFile;
            else
                message = "Failed to save tournament state.";
            Debug.Log("[BDATournament]: " + message);
            // if (BDACompetitionMode.Instance != null)
            //     BDACompetitionMode.Instance.competitionStatus.Add(message);
            return true;
        }

        public void SetupTournament(string folder, int rounds, int vesselsPerHeat, string stateFile = "")
        {
            if (stateFile != "") this.stateFile = stateFile;
            tournamentState = new TournamentState();
            tournamentState.Generate(folder, rounds, vesselsPerHeat);
            tournamentID = tournamentState.tournamentID;
            vesselCount = tournamentState.craftFiles.Count;
            numberOfRounds = tournamentState.rounds.Count;
            numberOfHeats = numberOfRounds > 0 ? tournamentState.rounds[0].Count : 0;
            heatsRemaining = tournamentState.rounds.Select(r => r.Value.Count).Sum() - tournamentState.completed.Select(c => c.Value.Count).Sum();
            tournamentStatus = heatsRemaining > 0 ? TournamentStatus.Stopped : TournamentStatus.Completed;
            message = "Tournament generated for " + vesselCount + " craft found in AutoSpawn" + (folder == "" ? "" : "/" + folder);
            BDACompetitionMode.Instance.competitionStatus.Add(message);
            Debug.Log("[BDATournament]: " + message);
            SaveTournamentState();
        }

        public void RunTournament()
        {
            BDACompetitionMode.Instance.StopCompetition();
            VesselSpawner.Instance.CancelVesselSpawn();
            if (runTournamentCoroutine != null)
                StopCoroutine(runTournamentCoroutine);
            runTournamentCoroutine = StartCoroutine(RunTournamentCoroutine());
        }

        public void StopTournament()
        {
            if (runTournamentCoroutine != null)
            {
                StopCoroutine(runTournamentCoroutine);
                runTournamentCoroutine = null;
            }
            tournamentStatus = heatsRemaining > 0 ? TournamentStatus.Stopped : TournamentStatus.Completed;
        }

        IEnumerator RunTournamentCoroutine()
        {
            yield return new WaitForFixedUpdate();
            foreach (var roundIndex in tournamentState.rounds.Keys)
            {
                currentRound = roundIndex;
                foreach (var heatIndex in tournamentState.rounds[roundIndex].Keys)
                {
                    currentHeat = heatIndex;
                    if (tournamentState.completed.ContainsKey(roundIndex) && tournamentState.completed[roundIndex].Contains(heatIndex)) continue; // We've done that heat.

                    message = "Running heat " + heatIndex + " of round " + roundIndex + " of tournament " + tournamentState.tournamentID;
                    BDACompetitionMode.Instance.competitionStatus.Add(message);
                    Debug.Log("[BDATournament]: " + message);

                    int attempts = 0;
                    competitionStarted = false;
                    while (!competitionStarted && attempts++ < 3) // 3 attempts is plenty
                    {
                        tournamentStatus = TournamentStatus.Running;
                        yield return ExecuteHeat(roundIndex, heatIndex);
                        if (!competitionStarted)
                            switch (VesselSpawner.Instance.spawnFailureReason)
                            {
                                case VesselSpawner.SpawnFailureReason.None: // Successful spawning, but competition failed to start for some reason.
                                    BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + BDACompetitionMode.Instance.competitionStartFailureReason + ", trying again.");
                                    break;
                                case VesselSpawner.SpawnFailureReason.VesselLostParts: // Recoverable spawning failure.
                                case VesselSpawner.SpawnFailureReason.TimedOut: // Recoverable spawning failure.
                                    BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + VesselSpawner.Instance.spawnFailureReason + ", trying again.");
                                    break;
                                default: // Spawning is unrecoverable.
                                    BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + VesselSpawner.Instance.spawnFailureReason + ", aborting.");
                                    attempts = 3;
                                    break;
                            }
                    }
                    if (!competitionStarted)
                    {
                        Debug.Log("[BDATournament]: Failed to run heat, failure reasons: " + VesselSpawner.Instance.spawnFailureReason + ", " + BDACompetitionMode.Instance.competitionStartFailureReason);
                        tournamentStatus = TournamentStatus.Stopped;
                        yield break;
                    }

                    // Register the heat as completed.
                    if (!tournamentState.completed.ContainsKey(roundIndex)) tournamentState.completed.Add(roundIndex, new HashSet<int>());
                    tournamentState.completed[roundIndex].Add(heatIndex);
                    SaveTournamentState();
                    heatsRemaining = tournamentState.rounds.Select(r => r.Value.Count).Sum() - tournamentState.completed.Select(c => c.Value.Count).Sum();

                    if (heatsRemaining > 0)
                    {
                        // Wait 10s for any user action
                        tournamentStatus = TournamentStatus.Waiting;
                        double startTime = Planetarium.GetUniversalTime();
                        while ((Planetarium.GetUniversalTime() - startTime) < 10d)
                        {
                            BDACompetitionMode.Instance.competitionStatus.Add("Waiting " + (10d - (Planetarium.GetUniversalTime() - startTime)).ToString("0") + "s, then running next heat.");
                            yield return new WaitForSeconds(1);
                        }
                    }
                }
                message = "All heats in round " + roundIndex + " have been run.";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log("[BDATournament]: " + message);
            }
            message = "All rounds in tournament " + tournamentState.tournamentID + " have been run.";
            BDACompetitionMode.Instance.competitionStatus.Add(message);
            Debug.Log("[BDATournament]: " + message);
            tournamentStatus = TournamentStatus.Completed;
        }

        IEnumerator ExecuteHeat(int roundIndex, int heatIndex)
        {
            if (VesselSpawnerWindow.Instance.round4running) // FIXME Round 4
            {
                var team1Config = new VesselSpawner.SpawnConfig(tournamentState.rounds[roundIndex][heatIndex]);
                team1Config.craftFiles = team1Config.craftFiles.Take(team1Config.craftFiles.Count / 2).ToList();
                var team2Config = new VesselSpawner.SpawnConfig(tournamentState.rounds[roundIndex][heatIndex]);
                team2Config.craftFiles = team2Config.craftFiles.Skip(team2Config.craftFiles.Count / 2).ToList();
                team2Config.latitude += 8;
                VesselSpawner.Instance.TeamSpawn(new List<VesselSpawner.SpawnConfig> { team1Config, team2Config }, false);
            }
            else
                VesselSpawner.Instance.SpawnAllVesselsOnce(tournamentState.rounds[roundIndex][heatIndex]);
            while (VesselSpawner.Instance.vesselsSpawning)
                yield return new WaitForFixedUpdate();
            if (!VesselSpawner.Instance.vesselSpawnSuccess)
            {
                tournamentStatus = TournamentStatus.Stopped;
                yield break;
            }
            yield return new WaitForFixedUpdate();

            // NOTE: runs in separate coroutine
            BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE);
            yield return new WaitForFixedUpdate(); // Give the competition start a frame to get going.

            // start timer coroutine for the duration specified in settings UI
            var duration = Core.BDArmorySettings.COMPETITION_DURATION * 60f;
            message = "Starting " + (duration > 0 ? "a " + duration.ToString("F0") + "s" : "an unlimited") + " duration competition.";
            Debug.Log("[BDATournament]: " + message);
            BDACompetitionMode.Instance.competitionStatus.Add(message);
            while (BDACompetitionMode.Instance.competitionStarting)
                yield return new WaitForFixedUpdate(); // Wait for the competition to actually start.
            if (!BDACompetitionMode.Instance.competitionIsActive)
            {
                var message = "Competition failed to start.";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log("[BDATournament]: " + message);
                tournamentStatus = TournamentStatus.Stopped;
                yield break;
            }
            competitionStarted = true;
            while (BDACompetitionMode.Instance.competitionIsActive) // Wait for the competition to finish.
                yield return new WaitForSeconds(1);
        }
    }
}