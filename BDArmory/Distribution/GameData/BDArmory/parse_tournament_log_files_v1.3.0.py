# Standard library imports
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser(description="Tournament log parser", formatter_class=argparse.ArgumentDefaultsHelpFormatter)
parser.add_argument('tournament', type=str, nargs='?', help="Tournament folder to parse")
args = parser.parse_args()
tournamentDir = Path(args.tournament) if args.tournament is not None else Path('')
tournamentData = {}


def CalculateAccuracy(hits, shots): return 100 * hits / shots if shots > 0 else 0


for round in sorted(roundDir for roundDir in tournamentDir.iterdir() if roundDir.is_dir()) if args.tournament is not None else (tournamentDir,):
	tournamentData[round.name] = {}
	for heat in sorted(round.glob("*.log")):
		with open(heat, "r") as logFile:
			tournamentData[round.name][heat.name] = {}
			for line in logFile:
				line = line.strip()
				if 'BDArmoryCompetition' not in line:
					continue  # Ignore irrelevant lines
				_, field = line.split(' ', 1)
				if field.startswith('ALIVE:'):
					state, craft = field.split(':', 1)
					tournamentData[round.name][heat.name][craft] = {'state': state}
				elif field.startswith('DEAD:'):
					state, order, time, craft = field.split(':', 3)
					tournamentData[round.name][heat.name][craft] = {'state': state, 'deathOrder': order, 'deathTime': time}
				elif field.startswith('MIA:'):
					state, craft = field.split(':', 1)
					tournamentData[round.name][heat.name][craft] = {'state': state}
				elif field.startswith('WHOSHOTWHO:'):
					_, craft, shooters = field.split(':', 2)
					data = shooters.split(':')
					tournamentData[round.name][heat.name][craft].update({'hitsBy': {player: int(hits) for player, hits in zip(data[1::2], data[::2])}})
				elif field.startswith('WHODAMAGEDWHOWITHBULLETS:'):
					_, craft, shooters = field.split(':', 2)
					data = shooters.split(':')
					tournamentData[round.name][heat.name][craft].update({'bulletDamageBy': {player: float(damage) for player, damage in zip(data[1::2], data[::2])}})
				elif field.startswith('WHOSHOTWHOWITHMISSILES:'):
					_, craft, shooters = field.split(':', 2)
					data = shooters.split(':')
					tournamentData[round.name][heat.name][craft].update({'missileHitsBy': {player: int(hits) for player, hits in zip(data[1::2], data[::2])}})
				elif field.startswith('WHODAMAGEDWHOWITHMISSILES:'):
					_, craft, shooters = field.split(':', 2)
					data = shooters.split(':')
					tournamentData[round.name][heat.name][craft].update({'missileDamageBy': {player: float(damage) for player, damage in zip(data[1::2], data[::2])}})
				elif field.startswith('WHORAMMEDWHO:'):
					_, craft, rammers = field.split(':', 2)
					data = rammers.split(':')
					tournamentData[round.name][heat.name][craft].update({'rammedPartsLostBy': {player: int(partsLost) for player, partsLost in zip(data[1::2], data[::2])}})
				# Ignore OTHERKILL for now.
				elif field.startswith('CLEANKILL:'):
					_, craft, killer = field.split(':', 2)
					tournamentData[round.name][heat.name][craft].update({'cleanKillBy': killer})
				elif field.startswith('CLEANMISSILEKILL:'):
					_, craft, killer = field.split(':', 2)
					tournamentData[round.name][heat.name][craft].update({'cleanMissileKillBy': killer})
				elif field.startswith('CLEANRAM:'):
					_, craft, killer = field.split(':', 2)
					tournamentData[round.name][heat.name][craft].update({'cleanRamKillBy': killer})
				elif field.startswith('ACCURACY:'):
					_, craft, accuracy = field.split(':', 2)
					hits, shots = accuracy.split('/')
					accuracy = CalculateAccuracy(int(hits), int(shots))
					tournamentData[round.name][heat.name][craft].update({'accuracy': accuracy, 'hits': int(hits), 'shots': int(shots)})
				# Ignore Tag mode for now.

with open(tournamentDir / 'results.json', 'w') as outFile:
	json.dump(tournamentData, outFile, indent=2)


craftNames = sorted(list(set(craft for round in tournamentData.values() for heat in round.values() for craft in heat.keys())))
summary = {
	craft: {
		'survivedCount': len([1 for round in tournamentData.values() for heat in round.values() if craft in heat and heat[craft]['state'] == 'ALIVE']),
		'deathCount': len([1 for round in tournamentData.values() for heat in round.values() if craft in heat and heat[craft]['state'] == 'DEAD']),
		'cleanKills': len([1 for round in tournamentData.values() for heat in round.values() for data in heat.values() if any((field in data and data[field] == craft) for field in ('cleanKillBy', 'cleanMissileKillBy', 'cleanRamKillBy'))]),
		'assists': len([1 for round in tournamentData.values() for heat in round.values() for data in heat.values() if data['state'] == 'DEAD' and any(field in data and craft in data[field] for field in ('hitsBy', 'missileHitsBy', 'rammedPartsLostBy')) and not any((field in data and data[field] == craft) for field in ('cleanKillBy', 'cleanMissileKillBy', 'cleanRamKillBy'))]),
		'hits': sum([heat[craft]['hits'] for round in tournamentData.values() for heat in round.values() if craft in heat and 'hits' in heat[craft]]),
		'bulletDamage': sum([data[field][craft] for round in tournamentData.values() for heat in round.values() for data in heat.values() for field in ('bulletDamageBy',) if field in data and craft in data[field]]),
		'missileHits': sum([data[field][craft] for round in tournamentData.values() for heat in round.values() for data in heat.values() for field in ('missileHitsBy',) if field in data and craft in data[field]]),
		'missileDamage': sum([data[field][craft] for round in tournamentData.values() for heat in round.values() for data in heat.values() for field in ('missileDamageBy',) if field in data and craft in data[field]]),
		'ramScore': sum([data[field][craft] for round in tournamentData.values() for heat in round.values() for data in heat.values() for field in ('rammedPartsLostBy',) if field in data and craft in data[field]]),
		'accuracy': CalculateAccuracy(sum([heat[craft]['hits'] for round in tournamentData.values() for heat in round.values() if craft in heat and 'hits' in heat[craft]]), sum([heat[craft]['shots'] for round in tournamentData.values() for heat in round.values() if craft in heat and 'shots' in heat[craft]])),
	}
	for craft in craftNames
}

for craft in summary.values():
	spawns = craft['survivedCount'] + craft['deathCount']
	craft.update({
		'damage/hit': craft['bulletDamage'] / craft['hits'] if craft['hits'] > 0 else 0,
		'hits/spawn': craft['hits'] / spawns if spawns > 0 else 0,
		'damage/spawn': craft['bulletDamage'] / spawns if spawns > 0 else 0,
	})

with open(tournamentDir / 'summary.json', 'w') as outFile:
	json.dump(summary, outFile, indent=2)

if len(summary) > 0:
	csv_summary = "craft," + ",".join(k for k in next(iter(summary.values())).keys()) + "\n"
	csv_summary += "\n".join(craft + "," + ",".join(str(v) for v in scores.values()) for craft, scores in summary.items())
	with open(tournamentDir / 'summary.csv', 'w') as outFile:
		outFile.write(csv_summary)

	# Write results to console
	name_length = max([len(craft) for craft in summary])
	print(f"Name{' '*(name_length-4)}\tSurvive\tDeaths\tKills\tAssists\tHits\tDamage\tMisHits\tMisDmg\tRam\tAcc%\tDmg/Hit\tHits/Sp\tDmg/Sp")
	for craft in sorted(summary):
		spawns = summary[craft]['survivedCount'] + summary[craft]['deathCount']
		print(
			f"{craft}{' '*(name_length-len(craft))}\t"
			+ '\t'.join(f'{score}' if isinstance(score, int) else f'{score:.0f}' if field in ('bulletDamage', 'missileDamage') else f'{score:.1f}' if field in ('damage/hit', 'hits/spawn', 'damage/spawn') else f'{score:.2f}' for field, score in summary[craft].items())
		)
else:
	print("No valid log files found.")
