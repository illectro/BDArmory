using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using BDArmory.Core;

namespace BDArmory.Competition
{

    public class BDAScoreClient
    {
        private BDAScoreService service;

        private string baseUrl = "https://bdascores.herokuapp.com";

        public string vesselPath = "";

        private string competitionHash = "";

        public bool pendingRequest = false;

        public CompetitionModel competition = null;

        public HeatModel activeHeat = null;

        public Dictionary<int, HeatModel> heats = new Dictionary<int, HeatModel>();

        public Dictionary<int, VesselModel> vessels = new Dictionary<int, VesselModel>();

        public Dictionary<int, PlayerModel> players = new Dictionary<int, PlayerModel>();


        public BDAScoreClient(BDAScoreService service, string vesselPath, string hash)
        {
            this.service = service;
            this.vesselPath = vesselPath + "/" + hash;
            this.competitionHash = hash;
        }

        public IEnumerator GetCompetition(string hash)
        {
            if (pendingRequest)
            {
                Debug.Log("[BDAScoreClient] Request already pending");
                yield break;
            }
            pendingRequest = true;

            string uri = string.Format("{0}/competitions/{1}.json", baseUrl, hash);
            Debug.Log(string.Format("[BDAScoreClient] GET {0}", uri));
            using (UnityWebRequest webRequest = UnityWebRequest.Get(uri))
            {
                yield return webRequest.SendWebRequest();
                if (!webRequest.isHttpError)
                {
                    ReceiveCompetition(webRequest.downloadHandler.text);
                }
                else
                {
                    Debug.Log(string.Format("[BDAScoreClient] Failed to get competition {0}: {1}", hash, webRequest.error));
                }
            }

            pendingRequest = false;
        }

        private void ReceiveCompetition(string response)
        {
            if (response == null || "".Equals(response))
            {
                Debug.Log(string.Format("[BDAScoreClient] Received empty competition response"));
                return;
            }
            CompetitionModel competition = JsonUtility.FromJson<CompetitionModel>(response);
            if (competition == null)
            {
                Debug.Log(string.Format("[BDAScoreClient] Failed to parse competition: {0}", response));
            }
            else
            {
                this.competition = competition;
                Debug.Log(string.Format("[BDAScoreClient] Competition: {0}", competition.ToString()));
            }
        }

        public IEnumerator GetHeats(string hash)
        {
            if (pendingRequest)
            {
                Debug.Log("[BDAScoreClient] Request already pending");
                yield break;
            }
            pendingRequest = true;

            string uri = string.Format("{0}/competitions/{1}/heats.csv", baseUrl, hash);
            Debug.Log(string.Format("[BDAScoreClient] GET {0}", uri));
            using (UnityWebRequest webRequest = UnityWebRequest.Get(uri))
            {
                yield return webRequest.SendWebRequest();
                if (!webRequest.isHttpError)
                {
                    ReceiveHeats(webRequest.downloadHandler.text);
                }
                else
                {
                    Debug.Log(string.Format("[BDAScoreClient] Failed to get heats for {0}: {1}", hash, webRequest.error));
                }
            }

            pendingRequest = false;
        }

        private void ReceiveHeats(string response)
        {
            if (response == null || "".Equals(response))
            {
                Debug.Log(string.Format("[BDAScoreClient] Received empty heat collection response"));
                return;
            }
            List<HeatModel> collection = HeatModel.FromCsv(response);
            heats.Clear();
            if (collection == null)
            {
                Debug.Log(string.Format("[BDAScoreClient] Failed to parse heat collection: {0}", response));
                return;
            }
            foreach (HeatModel heatModel in collection)
            {
                Debug.Log(string.Format("[BDAScoreClient] Heat: {0}", heatModel.ToString()));
                heats.Add(heatModel.id, heatModel);
            }
            Debug.Log(string.Format("[BDAScoreClient] Heats: {0}", heats.Count));
        }

        public IEnumerator GetPlayers(string hash)
        {
            if (pendingRequest)
            {
                Debug.Log("[BDAScoreClient] Request already pending");
                yield break;
            }
            pendingRequest = true;

            string uri = string.Format("{0}/competitions/{1}/players.csv", baseUrl, hash);
            Debug.Log(string.Format("[BDAScoreClient] GET {0}", uri));
            using (UnityWebRequest webRequest = UnityWebRequest.Get(uri))
            {
                yield return webRequest.SendWebRequest();
                if (!webRequest.isHttpError)
                {
                    ReceivePlayers(webRequest.downloadHandler.text);
                }
                else
                {
                    Debug.Log(string.Format("[BDAScoreClient] Failed to get players for {0}: {1}", hash, webRequest.error));
                }
            }

            pendingRequest = false;
        }

        private void ReceivePlayers(string response)
        {
            if (response == null || "".Equals(response))
            {
                Debug.Log(string.Format("[BDAScoreClient] Received empty player collection response"));
                return;
            }
            List<PlayerModel> collection = PlayerModel.FromCsv(response);
            players.Clear();
            if (collection == null)
            {
                Debug.Log(string.Format("[BDAScoreClient] Failed to parse player collection: {0}", response));
                return;
            }
            foreach (PlayerModel playerModel in collection)
            {
                Debug.Log(string.Format("[BDAScoreClient] Player {0}", playerModel.ToString()));
                if (!players.ContainsKey(playerModel.id))
                    players.Add(playerModel.id, playerModel);
                else
                    Debug.Log("[BDAScoreClient] Player " + playerModel.id + " already exists in the competition.");
            }
            Debug.Log(string.Format("[BDAScoreClient] Players: {0}", players.Count));
        }

        public IEnumerator GetVessels(string hash, HeatModel heat)
        {
            if (pendingRequest)
            {
                Debug.Log("[BDAScoreClient] Request already pending");
                yield break;
            }
            pendingRequest = true;

            string uri = string.Format("{0}/competitions/{1}/heats/{2}/vessels.csv", baseUrl, hash, heat.id);
            Debug.Log(string.Format("[BDAScoreClient] GET {0}", uri));
            using (UnityWebRequest webRequest = UnityWebRequest.Get(uri))
            {
                yield return webRequest.SendWebRequest();
                if (!webRequest.isHttpError)
                {
                    ReceiveVessels(webRequest.downloadHandler.text);
                }
                else
                {
                    Debug.Log(string.Format("[BDAScoreClient] Failed to get vessels {0}/{1}: {2}", hash, heat, webRequest.error));
                }
            }

            pendingRequest = false;
        }

        private void ReceiveVessels(string response)
        {
            if (response == null || "".Equals(response))
            {
                Debug.Log(string.Format("[BDAScoreClient] Received empty vessel collection response"));
                return;
            }
            List<VesselModel> collection = VesselModel.FromCsv(response);
            vessels.Clear();
            if (collection == null)
            {
                Debug.Log(string.Format("[BDAScoreClient] Failed to parse vessel collection: {0}", response));
                return;
            }
            foreach (VesselModel vesselModel in collection)
            {
                if (!vessels.ContainsKey(vesselModel.id)) // Skip duplicates.
                {
                    Debug.Log(string.Format("[BDAScoreClient] Vessel {0}", vesselModel.ToString()));
                    vessels.Add(vesselModel.id, vesselModel);
                }
                else
                {
                    Debug.Log("[BDAScoreClient]: Vessel " + vesselModel.ToString() + " is already in the vessel list, skipping.");
                }
            }
            Debug.Log(string.Format("[BDAScoreClient] Vessels: {0}", vessels.Count));
        }

        public IEnumerator PostRecords(string hash, int heat, List<RecordModel> records)
        {
            List<string> recordsJson = records.Select(e => e.ToJSON()).ToList();
            Debug.Log(string.Format("[BDAScoreClient] Prepare records for {0} players", records.Count()));
            string recordsJsonStr = string.Join(",", recordsJson);
            string requestBody = string.Format("{{\"records\":[{0}]}}", recordsJsonStr);

            byte[] rawBody = Encoding.UTF8.GetBytes(requestBody);
            string uri = string.Format("{0}/competitions/{1}/heats/{2}/records/batch.json?client_secret={3}", baseUrl, hash, heat, BDArmorySettings.REMOTE_CLIENT_SECRET);
            string uriWithoutSecret = string.Format("{0}/competitions/{1}/heats/{2}/records/batch.json?client_secret=****", baseUrl, hash, heat);
            Debug.Log(string.Format("[BDAScoreClient] POST {0}:\n{1}", uriWithoutSecret, requestBody));
            using (UnityWebRequest webRequest = new UnityWebRequest(uri))
            {
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.uploadHandler = new UploadHandlerRaw(rawBody);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.method = UnityWebRequest.kHttpVerbPOST;

                yield return webRequest.SendWebRequest();

                Debug.Log(string.Format("[BDAScoreClient] score reporting status: {0}", webRequest.downloadHandler.text));
                if (webRequest.isHttpError)
                {
                    Debug.Log(string.Format("[BDAScoreClient] Failed to post records: {0}", webRequest.error));
                }
            }
        }

        public IEnumerator GetCraftFiles(string hash, HeatModel model)
        {
            pendingRequest = true;
            // DO NOT DELETE THE DIRECTORY. Delete the craft files inside it.
            // This is much safer.
            if (Directory.Exists(vesselPath))
            {
                Debug.Log("[BDAScoreClient] Deleting existing craft in spawn directory " + vesselPath);
                DirectoryInfo info = new DirectoryInfo(vesselPath);
                FileInfo[] craftFiles = info.GetFiles("*.craft")
                    .Where(e => e.Extension == ".craft")
                    .ToArray();
                foreach (FileInfo file in craftFiles)
                {
                    File.Delete(file.FullName);
                }
            }
            else
            {
                Directory.CreateDirectory(vesselPath);
            }

            // already have the vessels in memory; just need to fetch the files
            foreach (VesselModel v in vessels.Values)
            {
                Debug.Log(string.Format("[BDAScoreClient] GET {0}", v.craft_url));
                using (UnityWebRequest webRequest = UnityWebRequest.Get(v.craft_url))
                {
                    yield return webRequest.SendWebRequest();
                    if (!webRequest.isHttpError)
                    {
                        byte[] rawBytes = webRequest.downloadHandler.data;
                        SaveCraftFile(v, rawBytes);
                    }
                    else
                    {
                        Debug.Log(string.Format("[BDAScoreClient] Failed to get craft for {0}: {1}", v.id, webRequest.error));
                    }
                }
            }
            pendingRequest = false;
        }

        private void SaveCraftFile(VesselModel vessel, byte[] bytes)
        {
            PlayerModel p = players[vessel.player_id];
            if (p == null)
            {
                Debug.Log(string.Format("[BDAScoreClient] Failed to save craft for vessel {0}, player {1}", vessel.id, vessel.player_id));
                return;
            }

            string vesselName = string.Format("{0}_{1}", p.name, vessel.name);
            string filename = string.Format("{0}/{1}.craft", vesselPath, vesselName);
            System.IO.File.WriteAllBytes(filename, bytes);

            // load the file and modify its vessel name to match the player
            string[] lines = File.ReadAllLines(filename);
            string pattern = ".*ship = (.+)";
            string[] modifiedLines = lines
                .Select(e => Regex.Replace(e, pattern, "ship = " + vesselName))
                .Where(e => !e.Contains("VESSELNAMING"))
                .ToArray();
            File.WriteAllLines(filename, modifiedLines);
            Debug.Log(string.Format("[BDAScoreClient] Saved craft for player {0}", vesselName));
        }

        public IEnumerator StartHeat(string hash, HeatModel heat)
        {
            if (pendingRequest)
            {
                Debug.Log("[BDAScoreClient] Request already pending");
                yield break;
            }
            pendingRequest = true;

            this.activeHeat = heat;
            UI.RemoteOrchestrationWindow.Instance.UpdateClientStatus();

            string uri = string.Format("{0}/competitions/{1}/heats/{2}/start", baseUrl, hash, heat.id);
            using (UnityWebRequest webRequest = new UnityWebRequest(uri))
            {
                yield return webRequest.SendWebRequest();
                if (!webRequest.isHttpError)
                {
                    Debug.Log(string.Format("[BDAScoreClient] Started heat {1} in {0}", hash, heat.order));
                }
                else
                {
                    Debug.Log(string.Format("[BDAScoreClient] Failed to start heat {1} in {0}: {2}", hash, heat.order, webRequest.error));
                }
            }

            pendingRequest = false;
        }

        public IEnumerator StopHeat(string hash, HeatModel heat)
        {
            if (pendingRequest)
            {
                Debug.Log("[BDAScoreClient] Request already pending");
                yield break;
            }
            pendingRequest = true;

            this.activeHeat = null;

            string uri = string.Format("{0}/competitions/{1}/heats/{2}/stop", baseUrl, hash, heat.id);
            using (UnityWebRequest webRequest = new UnityWebRequest(uri))
            {
                yield return webRequest.SendWebRequest();
                if (!webRequest.isHttpError)
                {
                    Debug.Log(string.Format("[BDAScoreClient] Stopped heat {1} in {0}", hash, heat.order));
                }
                else
                {
                    Debug.Log(string.Format("[BDAScoreClient] Failed to stop heat {1} in {0}: {2}", hash, heat.order, webRequest.error));
                }
            }

            pendingRequest = false;
        }

    }
}
