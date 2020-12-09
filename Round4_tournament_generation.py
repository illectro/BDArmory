# Standard library imports
import argparse
import random
import json
import queue
from pathlib import Path
from typing import List

parser = argparse.ArgumentParser(description="A tournament file generator for season 2 round 4", formatter_class=argparse.ArgumentDefaultsHelpFormatter)
parser.add_argument('folder', type=str, help="The folder (under AutoSpawn) where the craft files are organised into teams.")
parser.add_argument('rounds', type=int, help="The number of rounds to generate.")
parser.add_argument('perTeam', type=int, help="The number of vessels per team per heat.")
args = parser.parse_args()

folder = Path(args.folder)
if not folder.exists():
    raise ValueError(f"The folder ({folder}) doesn't exist.")
teams = {f.name: [str(c.resolve()) for c in f.glob("*.craft")] for f in folder.iterdir() if f.is_dir()}  # Find all the teams
# teams = {f.name: [str(c.resolve()) for c in f.glob("*.craft")] for f in folder.iterdir() if f.is_dir() and f.name[0] != '_'}  # Find all the teams
# individuals = [str(c.resolve()) for f in folder.iterdir() if f.is_dir() and f.name[0] == '_' for c in f.glob("*.craft")]  # Find all the individuals
print(f"Found {len(teams)} teams:")
# print(f"Found {len(teams)} teams and {len(individuals)} of individuals:")
for team in teams:
    print(f" - {team} has {len(teams[team])} players,")
# print(f" - and {len(individuals)} individual players.")
print(f"Generating tournament.state file for {args.rounds} rounds with {len(teams)*(len(teams)-1)//2} heats per round (each team against another) and {args.perTeam} vessels per team per heat.")

tournamentHeader = json.dumps({
    "tournamentID": 4,  # Has to be a number
    "craftFiles": [craftFile for team in teams for craftFile in teams[team]] # + individuals
}, separators=(',', ':'))

roundConfig = {"latitude": -0.04762, "longitude": -74.8593, "altitude": 5000.0, "distance": 20.0, "absDistanceOrFactor": False, "easeInSpeed": 0.7, "killEverythingFirst": True, "assignTeams": False, "folder": "", "round": 2, "heat": 1, "completed": False}

teamQueues = {team: queue.Queue() for team in teams}
individualsQueue = queue.Queue()


def getTeamSelection(team: str, N: int) -> List[str]:
    global teamQueues, teams
    selection = []
    while len(selection) < N:
        while teamQueues[team].qsize() < 1:  # Not enough in the queue, extend it with randomised ordering of craft in the team.
            random.shuffle(teams[team])
            residue = []
            for craftFile in teams[team]:
                if craftFile not in selection:  # Avoid duplicates in the same heat.
                    teamQueues[team].put(craftFile)
                else:
                    residue.append(craftFile)
            for craftFile in residue:  # Add crafts that had already been selected to the queue last.
                teamQueues[team].put(craftFile)
        # if len(selection) < len(teams[team]):  # Still a craft in the queue that we haven't used yet.
        #     selection.append(teamQueues[team].get())
        # else:
        #     fillWithIndividuals(selection, N)
        selection.append(teamQueues[team].get())
    return selection


# def fillWithIndividuals(selection: List[str], N: int):
#     global individualsQueue, individuals
#     while len(selection) < N:
#         while individualsQueue.qsize() < 1:  # Not enough in the queue, extend it with randomised ordering of craft in the team.
#             random.shuffle(individuals)
#             residue = []
#             for craftFile in individuals:
#                 if craftFile not in selection:  # Avoid duplicates in the same heat.
#                     individualsQueue.put(craftFile)
#                 else:
#                     residue.append(craftFile)
#             for craftFile in residue:  # Add crafts that had already been selected to the queue last.
#                 individualsQueue.put(craftFile)
#         selection.append(individualsQueue.get())


with open('tournament.state', 'w') as f:
    f.write(tournamentHeader)

    teamNames = list(teams)
    for round in range(args.rounds):
        heats = []
        for i, team1 in enumerate(teamNames):
            for team2 in teamNames[i + 1:]:
                roundConfig.update({"craftFiles": getTeamSelection(team1, args.perTeam) + getTeamSelection(team2, args.perTeam)})
                heats.append({k: v for k, v in roundConfig.items()})

        for i in range(10): # Shuffle doesn't seem especially random, so do it 10 times.
            random.shuffle(heats)  # Shuffle the heat order to avoid watching the same team play too much in a row.
        for heat, heatConfig in enumerate(heats):
            heatConfig.update({"round": round, "heat": heat})

        # Write the round to the tournament.state file.
        for heat in heats:
            f.write('\n' + json.dumps(heat, separators=(',', ':')))
