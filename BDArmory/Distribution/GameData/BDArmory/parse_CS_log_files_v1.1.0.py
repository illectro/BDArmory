from pathlib import Path
import argparse

parser = argparse.ArgumentParser(description="Log-file parser for continuous spawning logs.", formatter_class=argparse.ArgumentDefaultsHelpFormatter)
parser.add_argument("logs", nargs='*', help="Log-files to parse. If none are given, all valid log-files are parsed.")
args = parser.parse_args()

log_dir = Path(__file__).parent / "Logs" if len(args.logs) == 0 else Path('.')
output_log_file = log_dir / "results.csv"

craft_data = []
competition_files = [Path(filename) for filename in args.logs if filename.endswith(".log")] if len(args.logs) > 0 else [filename for filename in Path.iterdir(log_dir) if filename.suffix in (".log", ".txt")]  # Pre-scan the files in case something changes (iterators don't like that).
for filename in competition_files:
	with open(log_dir / filename if len(args.logs) == 0 else filename, "r") as file_data:
		Craft_Name = ""
		Kills = 0
		Hits = 0
		Shots = 0
		Who_Shot_Me_Lines = []
		Who_Damaged_Me_Lines = []
		Clean_Kill_Lines = []  # Shots, rams and missiles all counted.
		for line in file_data:
			if not "VesselSpawner" in line:
				continue
			if " Name:" in line:
				craft_data.append(["bug", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])  # Name, clean kills,assists, deaths, hits, shots, damage, accuracy, score, hits/spawn, damage/spawn
				Craft_Name = line.split(" Name:")[-1].replace("\n", "")
				craft_data[-1][0] = Craft_Name
				Hits = 0
				Shots = 0
			if " DEATHCOUNT:" in line:  # Counts up deaths
				craft_data[-1][3] = int(line.split("DEATHCOUNT:")[-1].replace("\n", ""))
			if " CLEANKILL:" in line:  # Counts up clean kills
				Clean_Kill_Lines.extend(line.split(":  CLEANKILL:")[-1].replace("\n", "").split(", "))
			if " CLEANRAM:" in line:  # Counts up clean ram kills
				Clean_Kill_Lines.extend(line.split(":  CLEANRAM:")[-1].replace("\n", "").split(", "))
			if " CLEANMISSILEKILL:" in line:  # Counts up clean missile kills
				Clean_Kill_Lines.extend(line.split(":  CLEANMISSILEKILL:")[-1].replace("\n", "").split(", "))
			if " WHOSHOTME:" in line:  # Counds up assists
				Who_Shot_Me_Lines.extend(line.split(":  WHOSHOTME:")[-1].replace("\n", "").split(", ")[:craft_data[-1][3]])
			if " WHODAMAGEDMEWITHBULLETS:" in line:  # Counds up damage
				Who_Damaged_Me_Lines.extend(line.split(":  WHODAMAGEDMEWITHBULLETS:")[-1].replace("\n", "").split(", "))
			if " ACCURACY:" in line:  # Counts up hits
				for item in line.split(" ACCURACY:")[-1].replace("\n", "").split(","):
					Hits += int(item.split(":")[-1].split("/")[0])
				craft_data[-1][4] = Hits
			if " ACCURACY:" in line:  # Counts hp shots
				for item in line.split(" ACCURACY:")[-1].replace("\n", "").split(","):
					Shots += int(item.split(":")[-1].split("/")[1])
				craft_data[-1][5] = Shots

		for WHOSHOTME in Who_Shot_Me_Lines:  # Counts up assists
			# for shooter_round in WHOSHOTME.split(","):
			for shooter_hits in WHOSHOTME.split(";"):
				shooter = shooter_hits.split(":")[-1]
				for person_index in range(len(craft_data)):
					if shooter == craft_data[person_index][0]:
						craft_data[person_index][2] += 1
		for WHOSHOTME in Who_Damaged_Me_Lines:  # Counts up damage
			# for shooter_round in WHOSHOTME.split(","):
			for shooter_hits in WHOSHOTME.split(";"):
				shooter = shooter_hits.split(":")[-1]
				damage = float(shooter_hits.split(":")[-2])
				for person_index in range(len(craft_data)):
					if shooter == craft_data[person_index][0]:
						craft_data[person_index][6] += damage
		for kill_line in Clean_Kill_Lines:  # Counts up clean kills
			killer = kill_line.split(":")[1]
			for person_index in range(len(craft_data)):
				if killer == craft_data[person_index][0]:
					craft_data[person_index][1] += 1
		for i in range(len(craft_data)):
			craft_data[i][6] = int(craft_data[i][6])
			craft_data[i][7] = int(10000 * craft_data[i][4] / craft_data[i][5] if craft_data[i][5] > 0 else 0) / 100
			craft_data[i][8] = round(3 * craft_data[i][1] + 1 * craft_data[i][2] - 3 * craft_data[i][3] + .001 * craft_data[i][4], 8)
			craft_data[i][9] = int(100 * craft_data[i][4] / (1 + craft_data[i][3])) / 100
			craft_data[i][10] = int(craft_data[i][6] / (1 + craft_data[i][3]))

if len(craft_data) > 0:
	# Write results to console
	name_length = max([len(craft[0]) for craft in craft_data])
	print(f"Name{' '*(name_length-4)}\tKills\tAssists\tDeaths\tHits\tShots\tDamage\tAcc\tScore\tHits/Sp\tDmg/Sp")
	for item in sorted(craft_data, key=lambda item: item[0]):
		print(f"{item[0]}{' '*(name_length-len(item[0]))}\t" + '\t'.join(str(part) for part in item[1:]))

	# Write results to file
	with open(output_log_file, "w") as results_data:
		results_data.write("Name,Kills,Assists,Deaths,Hits,Shots,Damage,Acc,Score,Hits/Sp,Dmg/Sp\n")
		for item in sorted(craft_data, key=lambda item: item[0]):
			results_data.write(','.join(str(part) for part in item) + "\n")
else:
	print(f"No valid log files found.")
